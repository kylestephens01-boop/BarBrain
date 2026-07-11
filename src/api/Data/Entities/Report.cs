namespace BarBrain.Api.Data.Entities;

/// <summary>
/// A user report against public content (Sprint 6). Typed FK columns per
/// entity type — the merge_queue pattern (ADR-026): a CHECK guarantees exactly
/// the column matching <see cref="EntityType"/> is populated, and a partial
/// unique index allows at most one OPEN report per (reporter, entity) so the
/// report button can't be spammed into queue noise.
/// </summary>
public class Report
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>rating | venue | drink (CHECK-constrained).</summary>
    public required string EntityType { get; set; }

    public Guid? RatingId { get; set; }
    public Rating? Rating { get; set; }
    public Guid? VenueId { get; set; }
    public Venue? Venue { get; set; }
    public Guid? DrinkId { get; set; }
    public Drink? Drink { get; set; }

    public Guid ReporterUserId { get; set; }
    public User Reporter { get; set; } = null!;

    /// <summary>inaccurate | spam | offensive | other (CHECK-constrained).</summary>
    public required string Reason { get; set; }

    /// <summary>Optional free-text detail from the reporter.</summary>
    public string? Note { get; set; }

    public string Status { get; set; } = ReportStatus.Open;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DecidedAt { get; set; }

    /// <summary>Actor label (admin-token stub, mirroring merge_queue.DecidedBy).</summary>
    public string? DecidedBy { get; set; }
}
