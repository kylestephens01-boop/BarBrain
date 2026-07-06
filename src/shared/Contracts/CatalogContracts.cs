namespace BarBrain.Shared.Contracts;

/// <summary>A search hit or browse row. Score is null outside search.</summary>
public record DrinkSummary(
    Guid Id,
    string Name,
    string ProducerName,
    string Category,
    string? StyleName,
    decimal? Abv,
    double? Score);

public record PagedResult<T>(int Page, int PageSize, int Total, IReadOnlyList<T> Items);

public record ProducerRef(Guid Id, string Name, string? City, string? Region);

public record StyleRef(Guid Id, string Name, string? Code);

/// <summary>One attribute on a drink, with provenance (ADR-009). Value is 0–1; Display is 0–10.</summary>
public record DrinkAttributeDto(
    string Key,
    string DisplayName,
    float Value,
    float Display,
    string Source,
    float Confidence);

/// <summary>
/// Drink detail incl. attribute vector + provenance. When a merged (redirect)
/// id was requested, <c>RedirectedFromMerged</c> is true and <c>RequestedId</c>
/// carries the original id; the body is the canonical drink.
/// </summary>
public record DrinkDetail(
    Guid Id,
    string Name,
    string Category,
    ProducerRef Producer,
    StyleRef? Style,
    decimal? Abv,
    string Source,
    string? SourceRef,
    bool RedirectedFromMerged,
    Guid? RequestedId,
    IReadOnlyList<DrinkAttributeDto> Attributes,
    float[]? CategoryVector,
    float[]? BridgeVector,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>A node in a per-category style tree (ADR-023: numbers only, no prose).</summary>
public record StyleNode(
    Guid Id,
    string Name,
    string? Code,
    string Category,
    decimal? AbvMin, decimal? AbvMax,
    short? IbuMin, short? IbuMax,
    decimal? SrmMin, decimal? SrmMax,
    decimal? OgMin, decimal? OgMax,
    decimal? FgMin, decimal? FgMax,
    bool HasBaseline,
    IReadOnlyList<StyleNode> Children);

/// <summary>An entity in a merge pair, denormalized for the admin queue UI.</summary>
public record MergeEntityRef(Guid Id, string Name, string Detail);

public record MergeCandidateDto(
    Guid Id,
    string EntityType,
    MergeEntityRef Source,
    MergeEntityRef Target,
    float Similarity,
    string? Reason,
    string Status,
    DateTimeOffset CreatedAt);
