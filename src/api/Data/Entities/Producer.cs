namespace BarBrain.Api.Data.Entities;

/// <summary>
/// A drink producer: brewery, distillery, winery (or several at once — e.g.
/// Cedar Ridge brews and distills, so producers carry no category; drinks do).
/// Canonical identity is fuzzy by nature; duplicates are resolved through the
/// merge queue, and merged rows remain as redirects (ADR-008 dedup posture).
/// </summary>
public class Producer
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public required string Name { get; set; }

    /// <summary>Search/dedup key — see <c>NameNormalizer</c>. Trigram-indexed.</summary>
    public required string NormalizedName { get; set; }

    /// <summary>brewery | distillery | winery | cidery | meadery | multi | other.</summary>
    public string? ProducerType { get; set; }

    public string? Country { get; set; }
    /// <summary>State/province.</summary>
    public string? Region { get; set; }
    public string? City { get; set; }

    /// <summary>Provenance: which pipeline created this row (e.g. "seed:openbrewerydb").</summary>
    public required string Source { get; set; }
    /// <summary>Stable id in the upstream dataset, for idempotent re-import.</summary>
    public string? SourceRef { get; set; }

    // --- Ownership + visibility (ADR-026) — imported rows have no owner and
    // are constrained public; user submissions arrive in Sprint 2+. ----------
    public Guid? CreatedByUserId { get; set; }
    public User? CreatedBy { get; set; }
    public string Visibility { get; set; } = Entities.Visibility.Public;

    // --- Merge redirect (entity resolution) ---------------------------------
    public string Status { get; set; } = EntityStatus.Active;
    public Guid? MergedIntoProducerId { get; set; }
    public Producer? MergedInto { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<Drink> Drinks { get; set; } = [];
}
