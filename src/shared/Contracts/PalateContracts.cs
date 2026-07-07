namespace BarBrain.Shared.Contracts;

// ---- Feed (ADR-013/025/027) -------------------------------------------------

/// <summary>
/// The sectioned feed. <c>Confidence</c> ∈ cold|warm|full describes how much
/// the engine knows overall (drives the UI's framing); <c>NeedsOnboarding</c>
/// is true when no profile exists anywhere (the UI offers the quiz).
/// </summary>
public record FeedResponse(
    IReadOnlyList<FeedSection> Sections,
    string Confidence,
    int RatingsCount,
    IReadOnlyList<string> Categories,
    bool NeedsOnboarding);

/// <summary>ComingSoon marks the "Loved by your matches" slot (Sprint 4).</summary>
public record FeedSection(
    string Key,
    string Title,
    IReadOnlyList<RecDto> Items,
    bool ComingSoon = false);

/// <summary>
/// One recommendation. <c>Reason</c> is the human "because" (hard product
/// requirement, ADR-013/027); <c>ReasonAttributes</c> are the display names
/// inside it, for the UI's teal emphasis. <c>Tag</c>: cross_category |
/// new_territory | loved_by_matches | null. Cross-category items carry the
/// palate's <c>SourceCategory</c> (ADR-027 bridge).
///
/// Social proof (Sprint 4, ADR-014): <c>LovedByMatchCount</c> is how many of
/// the user's palate matches rated this highly, and <c>LovedByMatchHandle</c>
/// is one such match's handle — for the "loved by @handle + N others" line.
/// Null when no match loved it (or matching is off / cold).
/// </summary>
public record RecDto(
    Guid DrinkId,
    string Name,
    string ProducerName,
    string Category,
    string? StyleName,
    decimal? Abv,
    int? MatchPct,
    string Reason,
    IReadOnlyList<string> ReasonAttributes,
    bool CrossCategory,
    string? SourceCategory,
    string? Tag,
    int? LovedByMatchCount = null,
    string? LovedByMatchHandle = null);

// ---- Palate profile (radar) ---------------------------------------------------

public record PalateResponse(IReadOnlyList<CategoryPalate> Categories);

/// <summary>
/// A category's palate as the UI shows it: centroid attributes on the 0–10
/// display scale (ADR-009). <c>Confidence</c> ∈ cold|warm|full;
/// <c>RatingsToFull</c> feeds the "needs N more ratings" prompt.
/// </summary>
public record CategoryPalate(
    string Category,
    int RatingsCount,
    string Confidence,
    int RatingsToFull,
    IReadOnlyList<PalateAttribute> Attributes,
    DateTimeOffset ComputedAt);

public record PalateAttribute(string Key, string DisplayName, float Display);

// ---- Onboarding quiz ------------------------------------------------------------

public record InterestsRequest(IReadOnlyList<string> Categories);

public record InterestsResponse(IReadOnlyList<string> Categories);

public record QuizResponse(IReadOnlyList<QuizCategory> Categories);

public record QuizCategory(string Category, IReadOnlyList<QuizItem> Items);

/// <summary>
/// One quiz card. Beer/whiskey: Label = product, SubLabel = producer · style.
/// Wine: Label = varietal, SubLabel = the representative drink the rating
/// actually lands on ("e.g. Josh Cellars Cabernet Sauvignon").
/// </summary>
public record QuizItem(Guid DrinkId, string Label, string SubLabel);
