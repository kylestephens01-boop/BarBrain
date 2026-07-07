namespace BarBrain.Shared.Contracts;

// ---- Palate matching (ADR-014, Sprint 4) ------------------------------------

/// <summary>
/// The "Your Matches" surface. One-way by design (ADR-014): a READ of who
/// matches your palate — no interaction of any kind. <c>Hidden</c> reflects the
/// caller's own hide-me state (when true, matching is off for them and the list
/// is empty in both directions). <c>DisplayMode</c> is the active
/// <c>match.display_mode</c> flag (eager|conservative) so the UI can explain
/// why a percentage is or isn't shown.
/// </summary>
public record MatchesResponse(
    IReadOnlyList<MatchDto> Matches,
    bool Hidden,
    string DisplayMode);

/// <summary>
/// One palate match. <c>MatchPct</c> is null when the display flag is
/// conservative and confidence hasn't reached med+ (an honest blank beats a
/// shaky number). <c>EarlyEstimate</c> is true when we DO show a percent but
/// confidence is still low (eager mode) — the UI labels it "early estimate".
/// </summary>
public record MatchDto(
    string Handle,
    int? MatchPct,
    string Confidence,        // low | med | high
    bool EarlyEstimate,
    IReadOnlyList<MatchLove> RecentLoves);

/// <summary>A drink one of your matches recently rated highly (social proof).</summary>
public record MatchLove(Guid DrinkId, string Name, string Category, decimal Value);

/// <summary>The caller's own matching preferences (hide-me + digest opt-out).</summary>
public record MatchSettingsResponse(bool HideFromMatches, bool DigestSubscribed);

public record HideFromMatchesRequest(bool Hidden);

public record DigestSubscriptionRequest(bool Subscribed);
