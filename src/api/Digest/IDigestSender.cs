namespace BarBrain.Api.Digest;

/// <summary>
/// Sends (or logs) the weekly digest (ADR-019). No SMTP provider exists yet
/// (HUMAN-CHECKLIST 6) — the default implementation LOGS, exactly like
/// <c>IVerificationEmailSender</c>. Swap in an SMTP sender when creds arrive.
///
/// <paramref name="deliver"/> is the CAN-SPAM guard, decided by the caller:
/// false means "compose but do not actually deliver" (no physical address
/// configured, or sending disabled). A real sender MUST honor it and log
/// instead of delivering; the logging sender ignores it (it never delivers).
/// </summary>
public interface IDigestSender
{
    Task SendAsync(string recipient, string subject, string htmlBody, bool deliver, CancellationToken ct = default);
}

public sealed class LoggingDigestSender(ILogger<LoggingDigestSender> logger) : IDigestSender
{
    public Task SendAsync(string recipient, string subject, string htmlBody, bool deliver, CancellationToken ct = default)
    {
        logger.LogInformation(
            "Weekly digest for {Recipient} — \"{Subject}\" ({Length} bytes, deliver={Deliver}; no SMTP configured — HUMAN-CHECKLIST 6)",
            recipient, subject, htmlBody.Length, deliver);
        return Task.CompletedTask;
    }
}
