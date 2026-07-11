using BarBrain.Api.Badges;

namespace BarBrain.Api.Tests;

/// <summary>
/// THE weekly-streak definition (Sprint 6): rolling 7-day buckets back from
/// now, consecutive from bucket 0. Shared by the digest and the streak badges
/// (founder ruling 2026-07-10) — these tests pin the semantics both rely on.
/// </summary>
public class StreakMathTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void No_ratings_is_zero()
        => Assert.Equal(0, StreakMath.ConsecutiveWeeks([], Now));

    [Fact]
    public void Rating_this_week_only_is_one()
        => Assert.Equal(1, StreakMath.ConsecutiveWeeks([Now.AddDays(-2)], Now));

    [Fact]
    public void Consecutive_weeks_count()
        => Assert.Equal(3, StreakMath.ConsecutiveWeeks(
            [Now.AddDays(-1), Now.AddDays(-8), Now.AddDays(-15)], Now));

    [Fact]
    public void Gap_breaks_the_streak()
        // Nothing in the current 7-day bucket → streak reads 0 even with history.
        => Assert.Equal(0, StreakMath.ConsecutiveWeeks([Now.AddDays(-8)], Now));

    [Fact]
    public void Older_activity_beyond_a_gap_does_not_count()
        // Bucket 0 and 2 active, bucket 1 empty → streak is 1.
        => Assert.Equal(1, StreakMath.ConsecutiveWeeks(
            [Now.AddDays(-1), Now.AddDays(-16)], Now));

    [Fact]
    public void Bucket_boundary_at_exactly_seven_days_rolls_to_the_next_bucket()
        // 7.0 days ago lands in bucket 1, not bucket 0.
        => Assert.Equal(0, StreakMath.ConsecutiveWeeks([Now.AddDays(-7)], Now));

    [Fact]
    public void Future_timestamps_are_ignored()
        => Assert.Equal(0, StreakMath.ConsecutiveWeeks([Now.AddDays(1)], Now));

    [Fact]
    public void Multiple_ratings_in_one_week_count_once()
        => Assert.Equal(1, StreakMath.ConsecutiveWeeks(
            [Now.AddDays(-1), Now.AddDays(-2), Now.AddDays(-3)], Now));
}
