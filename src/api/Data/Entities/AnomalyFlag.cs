namespace BarBrain.Api.Data.Entities;

/// <summary>
/// A data-quality anomaly surfaced for HUMAN review (Sprint 6): the nightly
/// scan flags statistical outliers and rapid-fire write patterns. A flag is
/// evidence, never an automatic action — a moderator decides. At most one OPEN
/// flag per (user, kind); re-scans refresh the open flag's evidence in place.
///
/// This is abuse DETECTION, not an incentive — ADR-016 governs rewards and is
/// untouched by monitoring for data-quality attacks.
/// </summary>
public class AnomalyFlag
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    /// <summary>rating_zscore_outlier | rapid_fire (CHECK-constrained).</summary>
    public required string Kind { get; set; }

    /// <summary>Human-readable evidence, e.g. "mean deviation -1.8 stars over 12 drinks (z=3.4)".</summary>
    public required string Evidence { get; set; }

    /// <summary>Severity score for queue ordering (higher = more anomalous).</summary>
    public double Score { get; set; }

    public string Status { get; set; } = AnomalyStatus.Open;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DecidedAt { get; set; }
    public string? DecidedBy { get; set; }
}
