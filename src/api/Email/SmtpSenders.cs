using BarBrain.Api.Auth;
using BarBrain.Api.Digest;
using BarBrain.Api.Privacy;

namespace BarBrain.Api.Email;

/// <summary>
/// SMTP-backed implementations of the three email interfaces, registered by
/// <see cref="EmailRegistration"/> when <c>Email:Smtp:Host</c> is set. Copy
/// follows BRAND.md voice: calm, no urgency framing.
/// </summary>
public sealed class SmtpVerificationEmailSender(SmtpEmailClient smtp) : IVerificationEmailSender
{
    public Task SendAsync(string email, string verifyUrl, CancellationToken ct = default) =>
        smtp.SendAsync(email, "Confirm your BarBrain email",
            $"""
            Confirm this email address for your BarBrain account whenever you get a chance:

            {verifyUrl}

            If you didn't sign up for BarBrain, you can ignore this message.
            """, isHtml: false, ct);
}

public sealed class SmtpAccountEmailSender(SmtpEmailClient smtp) : IAccountEmailSender
{
    public Task SendDeletionScheduledAsync(
        string email, string mode, DateTimeOffset effectiveAt, CancellationToken ct = default) =>
        smtp.SendAsync(email, "Your BarBrain account deletion is scheduled",
            $"""
            Deletion of your BarBrain account is scheduled ({mode}), effective {effectiveAt:yyyy-MM-dd}.

            If this wasn't you, sign in and cancel it from your profile before that date.
            """, isHtml: false, ct);
}

public sealed class SmtpDigestSender(SmtpEmailClient smtp, ILogger<SmtpDigestSender> logger) : IDigestSender
{
    public Task SendAsync(string recipient, string subject, string htmlBody, bool deliver, CancellationToken ct = default)
    {
        // CAN-SPAM guard (IDigestSender contract): deliver=false means the
        // caller composed but delivery is not allowed — log, never send.
        if (!deliver)
        {
            logger.LogInformation(
                "Digest for {Recipient} — \"{Subject}\" ({Length} bytes) composed but NOT delivered (deliver=false)",
                recipient, subject, htmlBody.Length);
            return Task.CompletedTask;
        }
        return smtp.SendAsync(recipient, subject, htmlBody, isHtml: true, ct);
    }
}
