using Pgvector;

namespace BarBrain.Api.Data.Entities;

/// <summary>
/// The canonical drink: (producer, product name, category) per ADR-008.
/// Package format and wine vintage are rating metadata (later sprints), not
/// identity. ABV is metadata + dedup signal, not identity.
///
/// A composite FK (StyleId, Category) → styles(Id, Category) makes it
/// impossible AT THE DATABASE for a beer to reference a wine style; the same
/// trick keeps merge redirects within a category (ADR-026 constraint posture).
/// </summary>
public class Drink
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid ProducerId { get; set; }
    public Producer Producer { get; set; } = null!;

    public required string Name { get; set; }
    /// <summary>Search/dedup key — see <c>NameNormalizer</c>. Trigram-indexed.</summary>
    public required string NormalizedName { get; set; }

    /// <summary>beer | whiskey | wine (CHECK-constrained).</summary>
    public required string Category { get; set; }

    public Guid? StyleId { get; set; }
    public Style? Style { get; set; }

    /// <summary>Percent ABV; metadata + dedup signal (ADR-008).</summary>
    public decimal? Abv { get; set; }

    public required string Source { get; set; }
    public string? SourceRef { get; set; }

    // --- Ownership + visibility (ADR-026) -----------------------------------
    public Guid? CreatedByUserId { get; set; }
    public User? CreatedBy { get; set; }
    public string Visibility { get; set; } = Entities.Visibility.Public;

    // --- Merge redirect (entity resolution) ---------------------------------
    public string Status { get; set; } = EntityStatus.Active;
    public Guid? MergedIntoDrinkId { get; set; }
    public Drink? MergedInto { get; set; }

    // --- Derived vectors (synced from drink_attributes; ADR-025) ------------
    // Relational drink_attributes is the auditable source of truth; these
    // columns exist for HNSW cosine similarity and are recomputed by
    // AttributeVectorService. Never hand-edit.
    public Vector? CategoryVector { get; set; }
    public Vector? BridgeVector { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<DrinkAttributeValue> Attributes { get; set; } = [];
}
