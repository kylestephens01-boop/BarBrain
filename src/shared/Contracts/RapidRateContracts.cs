namespace BarBrain.Shared.Contracts;

/// <summary>
/// A rapid-rate browse row (Sprint 4.5): catalog summary + community rating
/// count + the caller's latest rating (star prefill / "your latest" caption).
/// Writes stay on POST /api/ratings — this contract is read-only.
/// </summary>
public record RapidRateItem(
    Guid Id,
    string Name,
    string ProducerName,
    string Category,
    string? StyleName,
    decimal? Abv,
    int PublicRatingCount,
    decimal? MyLatestValue);
