namespace BarBrain.Api.Privacy;

/// <summary>
/// Sends the account-deletion confirmation email (Sprint 7, ADR-018). Same
/// posture as <see cref="Auth.IVerificationEmailSender"/>: EmailRegistration
/// picks SMTP when Email:Smtp:Host is configured, this logging implementation
/// otherwise (dev/CI).
/// </summary>
public interface IAccountEmailSender
{
    Task SendDeletionScheduledAsync(
        string email, string mode, DateTimeOffset effectiveAt, CancellationToken ct = default);
}

public sealed class LoggingAccountEmailSender(ILogger<LoggingAccountEmailSender> logger)
    : IAccountEmailSender
{
    public Task SendDeletionScheduledAsync(
        string email, string mode, DateTimeOffset effectiveAt, CancellationToken ct = default)
    {
        logger.LogInformation(
            "Deletion scheduled for {Email}: mode={Mode}, effective {EffectiveAt:yyyy-MM-dd}. " +
            "Sign in and cancel from your profile if this wasn't you. (no SMTP configured — HUMAN-CHECKLIST)",
            email, mode, effectiveAt);
        return Task.CompletedTask;
    }
}
