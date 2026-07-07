namespace BarBrain.Api.Data.Entities;

/// <summary>
/// A category the user claimed at the onboarding interest gate (Sprint 3).
/// Only claimed categories get quizzed and fed; the flags are additive —
/// rating outside a claimed category simply works and grows a profile there.
/// </summary>
public class UserCategoryInterest
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    /// <summary>beer | whiskey | wine (CHECK-constrained).</summary>
    public required string Category { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
