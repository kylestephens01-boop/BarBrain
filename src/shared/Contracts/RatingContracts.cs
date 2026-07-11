namespace BarBrain.Shared.Contracts;

/// <summary>
/// Create a rating (append-only; ADR-012). Value is 1.0–5.0 in half steps.
/// Visibility defaults to public-pseudonymous; "private" is the per-rating
/// toggle. LocationContext: home_bar | venue | untagged (ADR-015). home_bar
/// always resolves server-side to the CALLER'S own Home Bar; VenueId matters
/// only for the 'venue' context (real venues arrive in Sprint 5).
/// </summary>
public record RateRequest(
    Guid DrinkId,
    decimal Value,
    string? Note,
    string? Visibility,
    string LocationContext,
    Guid? VenueId = null,
    string? Origin = null,     // user (default) | quiz — quiz ratings are real ratings (Sprint 3)
    string? RecSection = null); // the FeedSection.Key when rating from a rec card (Sprint 6 badges)

/// <summary>In-place edit of note/visibility. A changed VALUE is a new rating.</summary>
public record RatingUpdateRequest(string? Note, string? Visibility);

/// <summary>A rating as its owner sees it (journal, prefill).</summary>
public record RatingDto(
    Guid Id,
    Guid DrinkId,
    string DrinkName,
    string Category,
    string? StyleName,
    decimal Value,
    string? Note,
    string Visibility,
    string LocationContext,
    string? VenueName,
    bool IsLatest,
    DateTimeOffset CreatedAt,
    string Origin = "user");

/// <summary>A rating as strangers see it: pseudonymous handle, no location.
/// Id exists so the row can be REPORTED (Sprint 6) — it exposes nothing new.</summary>
public record PublicRatingDto(
    Guid Id,
    string Handle,
    decimal Value,
    string? Note,
    DateTimeOffset CreatedAt);

/// <summary>Drink-page rating data: aggregate over LATEST public ratings only.</summary>
public record DrinkRatingsResponse(
    int PublicCount,
    decimal? Average,
    IReadOnlyList<PublicRatingDto> Recent);
