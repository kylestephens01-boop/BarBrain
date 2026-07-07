namespace BarBrain.Api.Auth;

/// <summary>
/// Sends the email-verification link (ADR-011 soft verification). No SMTP
/// provider exists yet (HUMAN-CHECKLIST) — the default implementation logs the
/// link so dev/CI flows work end to end; swap in an SMTP sender when creds
/// arrive. Copy stays within BRAND.md voice: no urgency framing.
/// </summary>
public interface IVerificationEmailSender
{
    Task SendAsync(string email, string verifyUrl, CancellationToken ct = default);
}

public sealed class LoggingVerificationEmailSender(ILogger<LoggingVerificationEmailSender> logger)
    : IVerificationEmailSender
{
    public Task SendAsync(string email, string verifyUrl, CancellationToken ct = default)
    {
        // The address is PII-adjacent but this is our own server log, short-
        // retention, and required to actually deliver the flow in dev.
        logger.LogInformation("Verification link for {Email}: {Url} (no SMTP configured — HUMAN-CHECKLIST)", email, verifyUrl);
        return Task.CompletedTask;
    }
}
