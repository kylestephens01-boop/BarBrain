using System.Text.RegularExpressions;

namespace BarBrain.Api.Monitoring;

/// <summary>
/// Scrubs PII out of anything headed for the error tracker (Sprint 7).
/// Error events are OPERATIONAL data — they carry no userId on purpose and
/// must never smuggle an email address or credential through an exception
/// message or path.
/// </summary>
public static partial class PiiScrubber
{
    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}")]
    private static partial Regex Email();

    // key=value / key: value forms for credential-ish keys. Redacts to end of
    // line ON PURPOSE: "Authorization: Bearer abc" is two tokens, and losing
    // trailing words beats leaking half a credential.
    [GeneratedRegex(@"(?im)\b(password|passphrase|token|secret|authorization|cookie)\b\s*[=:].*$")]
    private static partial Regex Credential();

    public static string Scrub(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var scrubbed = Email().Replace(text, "[email]");
        scrubbed = Credential().Replace(scrubbed, "$1=[redacted]");
        return scrubbed;
    }
}
