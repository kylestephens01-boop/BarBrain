namespace BarBrain.Api.Auth;

/// <summary>
/// The 21+ gate (ADR-010, Hard Rules 2/7). The full date of birth exists ONLY
/// as method parameters here — it is computed against and discarded. Callers
/// persist <c>dob.Year</c> + an attestation timestamp, nothing else.
/// </summary>
public static class AgeGate
{
    public const int MinimumAge = 21;

    /// <summary>Strict yyyy-MM-dd, the wire format of every signup path.</summary>
    public static bool TryParseDateOfBirth(string? value, out DateOnly dateOfBirth)
        => DateOnly.TryParseExact(value, "yyyy-MM-dd", out dateOfBirth);

    /// <summary>
    /// True when the person is at least 21 on <paramref name="today"/>.
    /// "Today" is UTC — the one place a timezone could matter is the calendar
    /// day of a 21st birthday itself, and UTC is the least-surprising tiebreak
    /// for a server-side gate.
    /// </summary>
    public static bool IsOfAge(DateOnly dateOfBirth, DateOnly today)
        => dateOfBirth.AddYears(MinimumAge) <= today;

    /// <summary>Sanity window for the persisted birth year (matches the DB CHECK).</summary>
    public static bool IsPlausibleBirthYear(int year) => year is >= 1900 and <= 2100;
}
