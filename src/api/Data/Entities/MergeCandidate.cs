namespace BarBrain.Api.Data.Entities;

/// <summary>
/// A row in the merge queue: a suspected duplicate pair awaiting a moderator
/// decision. Typed FK column pairs (producer/drink) replace a polymorphic id
/// so referential integrity is real (ADR-026); a CHECK constraint guarantees
/// exactly the pair matching <see cref="EntityType"/> is populated.
/// Convention: SOURCE is the row that gets merged away (redirected), TARGET
/// is the canonical survivor.
/// </summary>
public class MergeCandidate
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>producer | drink | venue (CHECK-constrained).</summary>
    public required string EntityType { get; set; }

    public Guid? SourceProducerId { get; set; }
    public Producer? SourceProducer { get; set; }
    public Guid? TargetProducerId { get; set; }
    public Producer? TargetProducer { get; set; }

    public Guid? SourceDrinkId { get; set; }
    public Drink? SourceDrink { get; set; }
    public Guid? TargetDrinkId { get; set; }
    public Drink? TargetDrink { get; set; }

    public Guid? SourceVenueId { get; set; }
    public Venue? SourceVenue { get; set; }
    public Guid? TargetVenueId { get; set; }
    public Venue? TargetVenue { get; set; }

    /// <summary>0–1 similarity confidence from the candidate generator.</summary>
    public float Similarity { get; set; }

    /// <summary>Human-readable evidence, e.g. "trgm 0.82; abv Δ0.1; same city".</summary>
    public string? Reason { get; set; }

    public string Status { get; set; } = MergeStatus.Pending;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DecidedAt { get; set; }

    /// <summary>Actor label while auth is stubbed; becomes a user FK in Sprint 2.</summary>
    public string? DecidedBy { get; set; }
}
