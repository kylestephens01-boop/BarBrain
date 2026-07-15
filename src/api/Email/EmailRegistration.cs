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
            builder.Services.AddSingleton<IVerificationEmailSender, LoggingVerificationEmailSender>();
            builder.Services.AddSingleton<IAccountEmailSender, LoggingAccountEmailSender>();
            builder.Services.AddSingleton<IDigestSender, LoggingDigestSender>();
            return;
        }

        if (string.IsNullOrWhiteSpace(options.From))
            throw new InvalidOperationException("Email:From is required when Email:Smtp:Host is set.");

        builder.Services.AddSingleton<SmtpEmailClient>();
        builder.Services.AddSingleton<IVerificationEmailSender, SmtpVerificationEmailSender>();
        builder.Services.AddSingleton<IAccountEmailSender, SmtpAccountEmailSender>();
        builder.Services.AddSingleton<IDigestSender, SmtpDigestSender>();
    }
}
