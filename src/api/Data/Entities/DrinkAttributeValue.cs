namespace BarBrain.Api.Data.Entities;

/// <summary>
/// A drink's value for one attribute, with provenance and confidence
/// (ADR-009). Style-baseline inheritance MATERIALIZES here as rows with
/// <c>Source = "inherited"</c> so the vector build reads one table and the
/// provenance is inspectable per-dimension.
/// </summary>
public class DrinkAttributeValue
{
    public Guid DrinkId { get; set; }
    public Drink Drink { get; set; } = null!;

    public required string AttributeKey { get; set; }
    public AttributeDefinition Attribute { get; set; } = null!;

    /// <summary>0–1 (CHECK-constrained); displayed 0–10.</summary>
    public float Value { get; set; }

    /// <summary>inherited | manufacturer | crowd | llm | moderator (CHECK-constrained).</summary>
    public required string Source { get; set; }

    /// <summary>0–1 (CHECK-constrained). Inherited values are low-confidence.</summary>
    public float Confidence { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
