using BarBrain.Api.Auth;
using BarBrain.Api.Digest;
using BarBrain.Api.Privacy;

namespace BarBrain.Api.Email;

/// <summary>
/// Chooses the email backend once, for all three send paths (verification,
/// account deletion, digest/alerts): SMTP when <c>Email:Smtp:Host</c> is
/// configured, the logging stubs otherwise (dev/CI). Same config-gated
/// pattern as the OAuth providers in <see cref="AuthRegistration"/>.
/// </summary>
public static class EmailRegistration
{
    public static void AddBarBrainEmail(this WebApplicationBuilder builder)
    {
        var options = builder.Configuration.GetSection(EmailOptions.Section).Get<EmailOptions>()
            ?? new EmailOptions();
        builder.Services.AddSingleton(options);

        if (string.IsNullOrWhiteSpace(options.Smtp.Host))
        {
            AddLoggingSenders(builder.Services);
            return;
        }

        if (string.IsNullOrWhiteSpace(options.From))
        {
            // Half-configured email must never take the SITE down: the
            // 2026-07-15 deploy crash-looped the api exactly here (SMTP_HOST
            // present, EMAIL_FROM missing). Prod degrades to log-only senders
            // and shouts in the logs; dev/CI still fail fast so the gap is
            // caught before it ships.
            if (!builder.Environment.IsProduction())
                throw new InvalidOperationException("Email:From is required when Email:Smtp:Host is set.");

            AddLoggingSenders(builder.Services);
            builder.Services.AddHostedService<EmailMisconfigurationAlert>();
            return;
        }

        builder.Services.AddSingleton<SmtpEmailClient>();
        builder.Services.AddSingleton<IVerificationEmailSender, SmtpVerificationEmailSender>();
        builder.Services.AddSingleton<IAccountEmailSender, SmtpAccountEmailSender>();
        builder.Services.AddSingleton<IDigestSender, SmtpDigestSender>();
    }

    private static void AddLoggingSenders(IServiceCollection services)
    {
        services.AddSingleton<IVerificationEmailSender, LoggingVerificationEmailSender>();
        services.AddSingleton<IAccountEmailSender, LoggingAccountEmailSender>();
        services.AddSingleton<IDigestSender, LoggingDigestSender>();
    }
}

/// <summary>
/// One loud, greppable startup error when prod boots with email half-
/// configured (see the degrade path in <see cref="EmailRegistration"/>).
/// </summary>
internal sealed class EmailMisconfigurationAlert(ILogger<EmailMisconfigurationAlert> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogError(
            "Email:Smtp:Host is set but Email:From is empty — transactional email is DISABLED (log-only) "
            + "until EMAIL_FROM is set in infra/.env (see RUNBOOK 'Transactional email').");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
