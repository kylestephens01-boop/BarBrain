namespace BarBrain.Api.Data.Entities;

/// <summary>
/// A DB-backed feature flag / setting (ADR-006). All phase-dependent behavior
/// reads from this table via <c>ISettingsService</c>; values are stored as
/// strings and interpreted by typed accessors.
/// </summary>
public class Setting
{
    /// <summary>Dotted key, e.g. "home.banner_text". Primary key.</summary>
    public required string Key { get; set; }

    /// <summary>Raw string value; typed accessors parse this.</summary>
    public required string Value { get; set; }

    /// <summary>Human-readable purpose, shown in the admin surface.</summary>
    public string? Description { get; set; }

    /// <summary>Last write time (UTC).</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
