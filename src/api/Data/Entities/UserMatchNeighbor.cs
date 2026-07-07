namespace BarBrain.Api.Data.Entities;

/// <summary>
/// A materialized palate-match edge from <see cref="UserId"/> to
/// <see cref="NeighborUserId"/> in one category (Sprint 4, ADR-014/007/025).
/// Derived data, rebuilt by the nightly match batch; never hand-edited.
///
/// Two signals live on the row so the blend is auditable:
///  - <see cref="AttributeSimilarity"/>: cosine of the two preference vectors
///    (the PRIMARY signal today, ADR-025).
///  - <see cref="CoRatingAgreement"/>: mean-centered Pearson over the drinks
///    both users rated (ADR-007). Null below the min-co-rated floor — there is
///    no honest correlation to report yet.
/// The stored <see cref="BlendedScore"/> is the density-weighted combination
/// (ADR-014): attribute similarity dominates at low co-rating density and CF
/// grows in as <see cref="CoRatedCount"/> rises. Rows are written in BOTH
/// directions (the relation is symmetric) so "who matches me" is one indexed
/// read.
/// </summary>
public class UserMatchNeighbor
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public Guid NeighborUserId { get; set; }
    public User Neighbor { get; set; } = null!;

    /// <summary>beer | whiskey | wine (CHECK-constrained).</summary>
    public required string Category { get; set; }

    /// <summary>Cosine of the two preference vectors, [-1, 1] (usually [0, 1]).</summary>
    public double AttributeSimilarity { get; set; }

    /// <summary>Mean-centered Pearson over co-rated drinks; null below the floor.</summary>
    public double? CoRatingAgreement { get; set; }

    /// <summary>Drinks BOTH users have a latest rating for, in this category.</summary>
    public int CoRatedCount { get; set; }

    /// <summary>Density-weighted blend in [0, 1] — the number the % is built from.</summary>
    public double BlendedScore { get; set; }

    public DateTimeOffset ComputedAt { get; set; } = DateTimeOffset.UtcNow;
}
