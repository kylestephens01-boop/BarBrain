using BarBrain.Api.Data;
using BarBrain.Api.Palate;
using BarBrain.Api.Settings;
using Microsoft.EntityFrameworkCore;

namespace BarBrain.Api.Digest;

/// <summary>
/// Builds a <see cref="DigestModel"/> for one user (ADR-019). Each block is
/// gated by its own config flag (Hard Rule 10) and only appears when there's
/// real content. Reuses the live feed engine for top picks and the match engine
/// for the hook, so the digest never drifts from what the app shows.
/// </summary>
public sealed class DigestComposer(
    AppDbContext db,
    ISettingsService settings,
    RecommendationService recommendations,
    MatchService matches,
    TimeProvider clock)
{
    public const string BlockRecapFlag = "digest.block_recap";
    public const string BlockStreakFlag = "digest.block_streak";
    public const string BlockTopPicksFlag = "digest.block_top_picks";
    public const string BlockMatchHookFlag = "digest.block_match_hook";

    public async Task<DigestModel> ComposeAsync(
        Guid userId, string handle, string unsubscribeUrl, string? physicalAddress,
        CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();

        var recapOn = await settings.GetBoolAsync(BlockRecapFlag, true, ct);
        var streakOn = await settings.GetBoolAsync(BlockStreakFlag, true, ct);
        var picksOn = await settings.GetBoolAsync(BlockTopPicksFlag, true, ct);
        var hookOn = await settings.GetBoolAsync(BlockMatchHookFlag, true, ct);

        DigestRecap? recap = null;
        DigestStreak? streak = null;
        if (recapOn || streakOn)
        {
            var history = await db.Ratings.AsNoTracking()
                .Where(r => r.CreatedByUserId == userId)
                .Select(r => new { r.CreatedAt, r.DrinkId, r.Drink.Category })
                .ToListAsync(ct);

            // Rolling 7-day buckets from now; bucket 0 is "this week".
            var thisWeek = history.Where(r => (now - r.CreatedAt).TotalDays < 7).ToList();

            if (recapOn && thisWeek.Count > 0)
                recap = new DigestRecap(
                    thisWeek.Select(r => r.DrinkId).Distinct().Count(),
                    thisWeek.Select(r => r.Category).Distinct().Count());

            if (streakOn)
            {
                // THE weekly streak (ADR-016: weekly only — never per-serving
                // or per-day). Shared with the streak badges via StreakMath so
                // the digest and the gallery can never disagree (Sprint 6).
                var weeks = Badges.StreakMath.ConsecutiveWeeks(
                    history.Select(r => r.CreatedAt), now);
                if (weeks >= 2)
                    streak = new DigestStreak(
                        weeks, thisWeek.Select(r => r.DrinkId).Distinct().Count());
            }
        }

        var picks = new List<DigestPick>();
        if (picksOn)
        {
            var feed = await recommendations.BuildFeedAsync(userId, null, ct);
            foreach (var section in feed.Sections)
            {
                var lead = section.Items.FirstOrDefault();
                if (lead is not null)
                    picks.Add(new DigestPick(section.Title, lead.Name, lead.ProducerName, lead.Reason));
            }
        }

        DigestMatchHook? hook = null;
        if (hookOn)
        {
            var matchResult = await matches.GetMatchesAsync(userId, 5, ct);
            var top = matchResult.Matches.FirstOrDefault();
            if (top is not null)
                hook = new DigestMatchHook(
                    matchResult.Matches.Count, top.Handle,
                    top.RecentLoves.FirstOrDefault()?.Name);
        }

        return new DigestModel(handle, unsubscribeUrl, physicalAddress, recap, streak, picks, hook);
    }
}
