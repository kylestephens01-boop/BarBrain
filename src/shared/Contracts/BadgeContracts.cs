namespace BarBrain.Shared.Contracts;

// ---- Badges (Sprint 6, ADR-016) ----------------------------------------------

/// <summary>One badge as the gallery shows it: definition + the caller's state.</summary>
public record BadgeDto(
    string Slug,
    string Name,
    string Description,
    string Icon,
    string DisplayGroup,
    int Threshold,
    bool Earned,
    DateTimeOffset? AwardedAt);

/// <summary>
/// The profile Badges tab: every active badge (earned + locked) plus the
/// streak card. Streak is the ADR-016-sanctioned weekly discovery streak —
/// the same definition the digest uses.
/// </summary>
public record BadgeGalleryResponse(
    IReadOnlyList<BadgeDto> Badges,
    int StreakWeeks,
    int DistinctDrinksThisWeek);

/// <summary>Unseen awards for the toast; POST /api/badges/seen acknowledges them.</summary>
public record UnseenBadgesResponse(IReadOnlyList<BadgeDto> Badges);
