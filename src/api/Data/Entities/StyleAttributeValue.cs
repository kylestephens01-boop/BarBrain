namespace BarBrain.Api.Data.Entities;

/// <summary>
/// A style's editorial baseline for one attribute (0–1). BarBrain-original
/// data (ADR-023); drinks inherit these when they lack their own value, with
/// provenance <c>inherited</c> (ADR-009).
/// </summary>
public class StyleAttributeValue
{
    public Guid StyleId { get; set; }
    public Style Style { get; set; } = null!;

    public required string AttributeKey { get; set; }
    public AttributeDefinition Attribute { get; set; } = null!;

    /// <summary>0–1 (CHECK-constrained); displayed 0–10.</summary>
    public float Value { get; set; }
}
