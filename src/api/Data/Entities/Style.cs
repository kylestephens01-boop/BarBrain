using Pgvector;

namespace BarBrain.Api.Data.Entities;

/// <summary>
/// A style in a per-category taxonomy tree (ADR-023 — licensing-safe).
/// Carries ONLY: name, style/category code, structural numeric ranges, and
/// BarBrain-original attribute baselines (via <see cref="StyleAttributeValue"/>).
/// There is DELIBERATELY no description column: guideline prose (BJCP etc.) is
/// copyright-incompatible. Rich text may be added later under explicit
/// permission as an additive migration — do not add it before then.
/// </summary>
public class Style
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>beer | whiskey | wine (CHECK-constrained).</summary>
    public required string Category { get; set; }

    /// <summary>
    /// Style code — BJCP code for beer (e.g. "21A"), BarBrain codes elsewhere
    /// (e.g. "WH-BRB"). Unique within a category when present.
    /// </summary>
    public string? Code { get; set; }

    public required string Name { get; set; }
    public required string NormalizedName { get; set; }

    public Guid? ParentStyleId { get; set; }
    public Style? Parent { get; set; }
    public List<Style> Children { get; set; } = [];

    // --- Structural numeric parameters (facts; nullable where N/A) ----------
    public decimal? AbvMin { get; set; }
    public decimal? AbvMax { get; set; }
    public short? IbuMin { get; set; }
    public short? IbuMax { get; set; }
    public decimal? SrmMin { get; set; }
    public decimal? SrmMax { get; set; }
    public decimal? OgMin { get; set; }
    public decimal? OgMax { get; set; }
    public decimal? FgMin { get; set; }
    public decimal? FgMax { get; set; }

    public required string Source { get; set; }

    // --- Derived baseline vectors (synced from style_attributes; ADR-025) ---
    public Vector? CategoryVector { get; set; }
    public Vector? BridgeVector { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<StyleAttributeValue> Attributes { get; set; } = [];
    public List<Drink> Drinks { get; set; } = [];
}
