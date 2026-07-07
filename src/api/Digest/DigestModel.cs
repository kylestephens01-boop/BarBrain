namespace BarBrain.Api.Digest;

/// <summary>
/// A composed weekly digest for one user (ADR-019). Every block is nullable /
/// possibly-empty: a block is only present when its config flag is on AND there
/// is something to say. <see cref="HasContent"/> is false when the whole thing
/// would be hollow — the sender skips those so no one gets an empty email.
///
/// Copy generated from this model stays inside BRAND.md + ADR-016: breadth and
/// discovery framing only, never consumption volume/frequency, no "drink more".
/// </summary>
public sealed record DigestModel(
    string Handle,
    string UnsubscribeUrl,
    string? PhysicalAddress,
    DigestRecap? Recap,
    DigestStreak? Streak,
    IReadOnlyList<DigestPick> TopPicks,
    DigestMatchHook? MatchHook)
{
    public bool HasContent =>
        Recap is not null || Streak is not null || TopPicks.Count > 0 || MatchHook is not null;
}

/// <summary>This week's activity, breadth-framed (drinks explored, categories touched).</summary>
public sealed record DigestRecap(int DrinksRated, int Categories);

/// <summary>
/// Weekly-distinct-drink discovery streak (ADR-016 — the ONLY sanctioned streak:
/// consecutive weeks with ≥1 rating, plus distinct drinks this week). Never a
/// count of servings, never "days in a row", never volume.
/// </summary>
public sealed record DigestStreak(int Weeks, int DistinctDrinksThisWeek);

/// <summary>One "top pick" — the lead recommendation from a feed section.</summary>
public sealed record DigestPick(string SectionTitle, string DrinkName, string ProducerName, string Reason);

/// <summary>
/// A palate-match hook (ADR-014). Either "a strong palate match was found"
/// (with a handle) or "your match tried X" (handle + a drink they loved).
/// </summary>
public sealed record DigestMatchHook(int MatchCount, string MatchHandle, string? MatchDrinkName);
