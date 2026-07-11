namespace BarBrain.Api.Data.Entities;

/// <summary>
/// A badge definition (Sprint 6): config data, not code. Rows are upserted at
/// startup from <c>seed/badges.json</c> — the file is the source of truth, so
/// launching a new badge on an existing metric is a config edit, no deploy.
/// Criteria DSL: <see cref="Metric"/> (closed vocabulary, every metric a
/// distinct-entity or weekly-streak count per ADR-016) + <see cref="Threshold"/>.
///
/// HARD RULES: names/descriptions are user-facing copy — the BRAND.md
/// prohibited-language list binds them (no volume/intoxication framing, ever).
/// </summary>
public class BadgeDefinition
{
    /// <summary>Stable machine id, e.g. "style-sampler". Never renamed.</summary>
    public required string Slug { get; set; }

    public required string Name { get; set; }
    public required string Description { get; set; }

    /// <summary>Icon name from the app's Icon component set.</summary>
    public required string Icon { get; set; }

    /// <summary>Display group for the gallery (breadth | exploration | venues | contribution | streak).</summary>
    public required string DisplayGroup { get; set; }

    /// <summary>Criteria metric (see <see cref="BadgeMetric"/>).</summary>
    public required string Metric { get; set; }

    /// <summary>Award when the metric reaches this value (≥ 1).</summary>
    public int Threshold { get; set; }

    /// <summary>Inactive badges are neither shown nor awarded (config kill switch).</summary>
    public bool Active { get; set; } = true;

    public int SortOrder { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
