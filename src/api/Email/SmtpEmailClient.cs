using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace BarBrain.Api.Email;

/// <summary>
/// The one place that actually talks SMTP (MailKit). All three email paths
/// (verification, account deletion, digest/alerts) funnel through here so
/// provider concerns — TLS mode, auth, the From identity — live in exactly
/// one spot. One connection per send: at VPS scale (ADR-004) that's simpler
/// and safer than pooling.
/// </summary>
public sealed class SmtpEmailClient(EmailOptions options, ILogger<SmtpEmailClient> logger)
{
    public async Task SendAsync(string to, string subject, string body, bool isHtml, CancellationToken ct = default)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(options.FromName, options.From));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;
        message.Body = new TextPart(isHtml ? "html" : "plain") { Text = body };

        // 465 = implicit TLS (Resend's default). Anything else negotiates
        // STARTTLS when the server offers it, which also lets the loopback
        // test server run plaintext.
        var security = options.Smtp.Port == 465
            ? SecureSocketOptions.SslOnConnect
            : SecureSocketOptions.StartTlsWhenAvailable;

        using var client = new SmtpClient();
        await client.ConnectAsync(options.Smtp.Host, options.Smtp.Port, security, ct);
        if (!string.IsNullOrEmpty(options.Smtp.Username))
            await client.AuthenticateAsync(options.Smtp.Username, options.Smtp.Password, ct);
        await client.SendAsync(message, ct);
        await client.DisconnectAsync(quit: true, ct);

        logger.LogInformation("Email sent to {To}: \"{Subject}\"", to, subject);
    }
}
