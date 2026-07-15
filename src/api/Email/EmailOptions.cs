namespace BarBrain.Api.Email;

/// <summary>
/// Transactional-email configuration (HUMAN-CHECKLIST 6), bound from the
/// <c>Email</c> section. In compose these arrive as environment variables
/// (<c>Email__Smtp__Host</c> etc.) mapped from <c>SMTP_*</c> / <c>EMAIL_FROM</c>
/// in <c>infra/.env</c> — see docker-compose.yml. An empty <see cref="Smtp"/>
/// host means "no provider": every email path logs instead of sending, which
/// is the dev/CI default.
/// </summary>
public sealed class EmailOptions
{
    public const string Section = "Email";

    /// <summary>Sender address; must be on a domain verified with the SMTP provider.</summary>
    public string From { get; set; } = "";

    /// <summary>Display name shown next to <see cref="From"/>.</summary>
    public string FromName { get; set; } = "BarBrain";

    public SmtpSettings Smtp { get; set; } = new();

    public sealed class SmtpSettings
    {
        public string Host { get; set; } = "";
        /// <summary>465 (implicit TLS, the Resend default) or 587 (STARTTLS).</summary>
        public int Port { get; set; } = 465;
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
    }
}
