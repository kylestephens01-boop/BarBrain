namespace BarBrain.Api.Catalog.Import;

/// <summary>
/// JSON shapes for the bundled seed files in <c>src/api/seed/</c>. These are
/// BarBrain-authored data (ADR-023/024): the style seed uses BJCP only as a
/// factual reference (names/codes/numeric ranges); attribute baselines are our
/// own editorial values, 0–1.
/// </summary>
public sealed class AttributeSeed
{
    public required string Key { get; set; }
    public required string Category { get; set; }
    public required short DimIndex { get; set; }
    public short? BridgeIndex { get; set; }
    public required string DisplayName { get; set; }
    public string? Description { get; set; }
}

public sealed class StyleSeedFile
{
    public required string Category { get; set; }
    public required string Source { get; set; }
    public required List<StyleSeed> Styles { get; set; }
}

public sealed class StyleSeed
{
    /// <summary>Style code (unique per category). Parents are grouping nodes.</summary>
    public required string Code { get; set; }
    public required string Name { get; set; }
    /// <summary>Parent style CODE, or null for a root node.</summary>
    public string? Parent { get; set; }

    /// <summary>[min, max] ranges; null when not applicable to the style/category.</summary>
    public decimal[]? Abv { get; set; }
    public short[]? Ibu { get; set; }
    public decimal[]? Srm { get; set; }
    public decimal[]? Og { get; set; }
    public decimal[]? Fg { get; set; }

    /// <summary>
    /// Baseline attribute values keyed by SHORT key (e.g. "sweetness"); the
    /// importer prefixes the category. Grouping nodes may omit this.
    /// </summary>
    public Dictionary<string, float>? Attributes { get; set; }
}

public sealed class CorridorSeedFile
{
    public required string Source { get; set; }
    public required List<CorridorProducerSeed> Producers { get; set; }
}

public sealed class CorridorProducerSeed
{
    /// <summary>Stable SourceRef for idempotent re-import.</summary>
    public required string Ref { get; set; }
    public required string Name { get; set; }
    public string? Type { get; set; }
    public string? City { get; set; }
    public string? Region { get; set; }
    public string? Country { get; set; }
    public List<CorridorDrinkSeed> Drinks { get; set; } = [];
}

public sealed class CorridorDrinkSeed
{
    public required string Ref { get; set; }
    public required string Name { get; set; }
    public required string Category { get; set; }
    /// <summary>Style code (preferred) or exact style name within the category.</summary>
    public string? Style { get; set; }
    public decimal? Abv { get; set; }
}
