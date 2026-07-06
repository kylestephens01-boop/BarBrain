namespace BarBrain.Api.Data.Entities;

/// <summary>
/// BarBrain's own flavor-attribute vocabulary (ADR-009/023): exactly 8 dims
/// per category, each pinned to a stable vector position (<see cref="DimIndex"/>).
/// Six of the eight map onto the cross-category bridge (<see cref="BridgeIndex"/>:
/// 0 sweetness, 1 bitterness/tannin, 2 body, 3 smoke, 4 fruit, 5 acidity);
/// the remaining two are category-specific. Values are stored 0–1 and
/// displayed 0–10 (ADR-009).
///
/// DimIndex/BridgeIndex are LOAD-BEARING: vectors are built in this order.
/// Never renumber an existing key — that silently scrambles every stored
/// vector. Adding a 9th dim is a schema change (vector(8) → vector(9)), not a
/// seed edit.
/// </summary>
public class AttributeDefinition
{
    /// <summary>Dotted key, e.g. "beer.sweetness". Primary key.</summary>
    public required string Key { get; set; }

    public required string Category { get; set; }

    /// <summary>Position (0–7) in the category vector.</summary>
    public required short DimIndex { get; set; }

    /// <summary>Position (0–5) in the bridge vector, or null if category-specific.</summary>
    public short? BridgeIndex { get; set; }

    public required string DisplayName { get; set; }

    /// <summary>BarBrain-authored explanation (ours — safe to display).</summary>
    public string? Description { get; set; }
}
