using System.Net;
using System.Net.Sockets;
using System.Text;
using BarBrain.Api.Auth;
using BarBrain.Api.Digest;
using BarBrain.Api.Email;
using BarBrain.Api.Privacy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace BarBrain.Api.Tests.Integration;

/// <summary>
/// Proves the transactional-email wiring end to end without a provider:
/// the env-var naming convention (SMTP_* → Email__* → Email: section) binds,
/// EmailRegistration picks SMTP vs logging senders off that config, and
/// <see cref="SmtpEmailClient"/> completes a real SMTP conversation against a
/// loopback test server. No Docker/database needed — boots like the health
/// tests.
/// </summary>
public sealed class EmailWiringTests
{
    [Fact]
    public void Compose_style_environment_variables_bind_to_EmailOptions()
    {
        // docker-compose.yml maps SMTP_* from infra/.env to these exact keys.
        const string prefix = "BBTEST_EMAIL_";
        Environment.SetEnvironmentVariable($"{prefix}Email__Smtp__Host", "smtp.resend.com");
        Environment.SetEnvironmentVariable($"{prefix}Email__Smtp__Port", "465");
        Environment.SetEnvironmentVariable($"{prefix}Email__Smtp__Username", "resend");
        Environment.SetEnvironmentVariable($"{prefix}Email__Smtp__Password", "re_test_key");
        Environment.SetEnvironmentVariable($"{prefix}Email__From", "noreply@barbrain.co");
        try
        {
            var config = new ConfigurationBuilder().AddEnvironmentVariables(prefix).Build();
            var options = config.GetSection(EmailOptions.Section).Get<EmailOptions>();

            Assert.NotNull(options);
            Assert.Equal("smtp.resend.com", options.Smtp.Host);
            Assert.Equal(465, options.Smtp.Port);
            Assert.Equal("resend", options.Smtp.Username);
            Assert.Equal("re_test_key", options.Smtp.Password);
            Assert.Equal("noreply@barbrain.co", options.From);
            Assert.Equal("BarBrain", options.FromName); // default, no env var
        }
        finally
        {
            foreach (var key in new[] { "Email__Smtp__Host", "Email__Smtp__Port", "Email__Smtp__Username", "Email__Smtp__Password", "Email__From" })
                Environment.SetEnvironmentVariable(prefix + key, null);
        }
    }

    [Fact]
    public void No_smtp_host_registers_the_logging_senders()
    {
        using var factory = new ApiFactory();

        Assert.IsType<LoggingVerificationEmailSender>(factory.Services.GetRequiredService<IVerificationEmailSender>());
        Assert.IsType<LoggingAccountEmailSender>(factory.Services.GetRequiredService<IAccountEmailSender>());
        Assert.IsType<LoggingDigestSender>(factory.Services.GetRequiredService<IDigestSender>());
    }

    [Fact]
    public void Configured_smtp_host_registers_the_smtp_senders()
    {
        using var factory = new ApiFactory
        {
            Settings =
            {
                ["Email:Smtp:Host"] = "smtp.resend.com",
                ["Email:From"] = "noreply@barbrain.co",
            },
        };

        Assert.IsType<SmtpVerificationEmailSender>(factory.Services.GetRequiredService<IVerificationEmailSender>());
        Assert.IsType<SmtpAccountEmailSender>(factory.Services.GetRequiredService<IAccountEmailSender>());
        Assert.IsType<SmtpDigestSender>(factory.Services.GetRequiredService<IDigestSender>());
    }

    [Fact]
    public void Smtp_host_without_a_from_address_fails_startup_outside_production()
    {
        using var factory = new ApiFactory { Settings = { ["Email:Smtp:Host"] = "smtp.resend.com" } };

        var ex = Assert.ThrowsAny<Exception>(() => factory.Services);
        for (Exception? e = ex; e is not null; e = e.InnerException)
            if (e.Message.Contains("Email:From")) return;
        Assert.Fail($"expected an Email:From configuration error, got: {ex.Message}");
    }

    [Fact]
    public void Smtp_host_without_a_from_address_in_production_degrades_to_log_only()
    {
        // Half-configured email must not crash-loop the prod api (2026-07-15
        // outage): boot succeeds, senders fall back to log-only.
        using var factory = new ApiFactory
        {
            EnvironmentOverride = "Production",
            Settings = { ["Email:Smtp:Host"] = "smtp.resend.com" },
        };

        Assert.IsType<LoggingVerificationEmailSender>(factory.Services.GetRequiredService<IVerificationEmailSender>());
        Assert.IsType<LoggingAccountEmailSender>(factory.Services.GetRequiredService<IAccountEmailSender>());
        Assert.IsType<LoggingDigestSender>(factory.Services.GetRequiredService<IDigestSender>());
    }

    [Fact]
    public async Task SmtpEmailClient_delivers_through_a_real_smtp_conversation()
    {
        using var server = new FakeSmtpServer();
        var client = NewClient(server.Port);

        await client.SendAsync("kyle.stephens01@gmail.com", "BarBrain test email",
            "Plain-text body with a link: https://dev.barbrain.co/verify", isHtml: false);

        var data = await server.WaitForMessageAsync();
        Assert.Contains("MAIL FROM:<noreply@barbrain.co>", server.Commands);
        Assert.Contains("RCPT TO:<kyle.stephens01@gmail.com>", server.Commands);
        Assert.Contains("Subject: BarBrain test email", data);
        Assert.Contains("noreply@barbrain.co", data);
        Assert.Contains("https://dev.barbrain.co/verify", data);
    }

    [Fact]
    public async Task SmtpDigestSender_honors_the_can_spam_guard()
    {
        // Nothing listens on this client's port — a send attempt would throw.
        var digest = new SmtpDigestSender(NewClient(port: 9), NullLogger<SmtpDigestSender>.Instance);

        await digest.SendAsync("someone@example.test", "Weekly digest", "<p>hi</p>", deliver: false);
    }

    private static SmtpEmailClient NewClient(int port) => new(
        new EmailOptions
        {
            From = "noreply@barbrain.co",
            // Not 465, so the client negotiates STARTTLS only if offered —
            // the fake server offers none and stays plaintext.
            Smtp = new EmailOptions.SmtpSettings { Host = "127.0.0.1", Port = port },
        },
        NullLogger<SmtpEmailClient>.Instance);

    /// <summary>
    /// Minimal single-connection SMTP server: enough of RFC 5321 (greeting,
    /// EHLO with no extensions, MAIL/RCPT/DATA/QUIT) for MailKit to hand over
    /// one message in plaintext.
    /// </summary>
    private sealed class FakeSmtpServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly Task<string> _session;
        public readonly List<string> Commands = [];

        public int Port { get; }

        public FakeSmtpServer()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _session = Task.Run(RunSessionAsync);
        }

        public async Task<string> WaitForMessageAsync() =>
            await _session.WaitAsync(TimeSpan.FromSeconds(15));

        private async Task<string> RunSessionAsync()
        {
            using var socket = await _listener.AcceptTcpClientAsync();
            using var stream = socket.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII);
            using var writer = new StreamWriter(stream, Encoding.ASCII) { AutoFlush = true, NewLine = "\r\n" };

            var data = new StringBuilder();
            await writer.WriteLineAsync("220 fake.test ready");
            while (await reader.ReadLineAsync() is { } line)
            {
                Commands.Add(line);
                if (line.StartsWith("EHLO") || line.StartsWith("HELO"))
                    await writer.WriteLineAsync("250 fake.test"); // no extensions → no STARTTLS/AUTH
                else if (line.StartsWith("MAIL FROM") || line.StartsWith("RCPT TO"))
                    await writer.WriteLineAsync("250 OK");
                else if (line == "DATA")
                {
                    await writer.WriteLineAsync("354 end with <CRLF>.<CRLF>");
                    while (await reader.ReadLineAsync() is { } body && body != ".")
                        data.AppendLine(body);
                    await writer.WriteLineAsync("250 OK queued");
                }
                else if (line == "QUIT")
                {
                    await writer.WriteLineAsync("221 bye");
                    break;
                }
                else
                    await writer.WriteLineAsync("250 OK");
            }
            return data.ToString();
        }

        public void Dispose() => _listener.Stop();
    }
}
