namespace BarBrain.Api.Badges;

/// <summary>
/// THE weekly-streak definition (ADR-016: weekly streaks only, never volume/
/// frequency). Extracted from the Sprint 4 digest so the digest block and the
/// streak badges can never disagree — founder ruling 2026-07-10: badges reuse
/// the digest's existing definition.
///
/// Semantics: rolling 7-day buckets counted back from <paramref name="now"/>;
/// the streak is the number of consecutive buckets (starting at the current
/// one) each containing at least one rating. The spec's "calendar week" is
/// implemented as this rolling weekly bucket — noted in the Sprint 6 PR.
/// </summary>
public static class StreakMath
{
    public static int ConsecutiveWeeks(IEnumerable<DateTimeOffset> ratingTimes, DateTimeOffset now)
    {
        var weeksWithActivity = ratingTimes
            .Select(t => (int)((now - t).TotalDays / 7))
            .Where(w => w >= 0)
            .ToHashSet();
        var weeks = 0;
        while (weeksWithActivity.Contains(weeks)) weeks++;
        return weeks;
    }
}
