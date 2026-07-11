using BarBrain.Api.Data;
using BarBrain.Api.Data.Entities;
using BarBrain.Api.Settings;
using BarBrain.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BarBrain.Api.Moderation;

/// <summary>
/// Nightly anomaly scan (Sprint 6): surfaces statistical evidence for HUMAN
/// review — never an automatic action. Two detectors, both flag-tunable:
///
/// rating_zscore_outlier — users whose latest public ratings systematically
/// deviate from each drink's public mean (review-bombing / shill patterns).
/// The user's mean deviation is z-scored against all scanned users.
///
/// rapid_fire — an implausible burst of rating WRITES in a short window
/// (bot/script patterns). This is write-abuse detection, not consumption
/// tracking (ADR-016 governs incentives; nothing here rewards or surfaces
/// anything to users).
///
/// The pure math lives in statics for no-Docker unit tests.
/// </summary>
public sealed class AnomalyScanService(
    AppDbContext db,
    ISettingsService settings,
    TimeProvider clock)
{
    public const string ZScoreThresholdFlag = "anomaly.zscore_threshold";
    public const string ZScoreMinRatingsFlag = "anomaly.zscore_min_ratings";
    public const string RapidFireWindowMinutesFlag = "anomaly.rapid_fire_window_minutes";
    public const string RapidFireCountFlag = "anomaly.rapid_fire_count";

    public const double DefaultZScoreThreshold = 3.0;
    public const int DefaultZScoreMinRatings = 8;
    public const int DefaultRapidFireWindowMinutes = 10;
    public const int DefaultRapidFireCount = 25;

    public sealed record ScanSummary(int ZScoreFlags, int RapidFireFlags);

    public async Task<ScanSummary> ScanAsync(CancellationToken ct = default)
    {
        var zFlags = await ScanZScoreOutliersAsync(ct);
        var rapidFlags = await ScanRapidFireAsync(ct);
        return new ScanSummary(zFlags, rapidFlags);
    }

    // ======================================================================
    //  Pure math (unit-tested without a database)
    // ======================================================================

    /// <summary>Per-user mean deviation from drink means → z-scores across users.</summary>
    public static Dictionary<Guid, (double MeanDeviation, double ZScore, int Count)> ZScores(
        IReadOnlyList<(Guid UserId, Guid DrinkId, double Value)> ratings, int minRatingsPerUser)
    {
        var drinkMeans = ratings.GroupBy(r => r.DrinkId)
            .ToDictionary(g => g.Key, g => g.Average(r => r.Value));

        var perUser = ratings
            .GroupBy(r => r.UserId)
            .Where(g => g.Count() >= minRatingsPerUser)
            .ToDictionary(
                g => g.Key,
                g => (Mean: g.Average(r => r.Value - drinkMeans[r.DrinkId]), Count: g.Count()));
        if (perUser.Count < 3) return []; // no population to compare against

        var deviations = perUser.Values.Select(v => v.Mean).ToList();
        var mean = deviations.Average();
        var variance = deviations.Sum(d => (d - mean) * (d - mean)) / deviations.Count;
        var std = Math.Sqrt(variance);
        if (std < 1e-9) return [];

        return perUser.ToDictionary(
            kv => kv.Key,
            kv => (kv.Value.Mean, (kv.Value.Mean - mean) / std, kv.Value.Count));
    }

    /// <summary>Largest count of timestamps inside any sliding window.</summary>
    public static int MaxInWindow(IReadOnlyList<DateTimeOffset> times, TimeSpan window)
    {
        if (times.Count == 0) return 0;
        var sorted = times.OrderBy(t => t).ToList();
        int best = 1, start = 0;
        for (var end = 0; end < sorted.Count; end++)
        {
            while (sorted[end] - sorted[start] > window) start++;
            best = Math.Max(best, end - start + 1);
        }
        return best;
    }

    // ======================================================================
    //  Detectors
    // ======================================================================

    private async Task<int> ScanZScoreOutliersAsync(CancellationToken ct)
    {
        var threshold = await GetDoubleAsync(ZScoreThresholdFlag, DefaultZScoreThreshold, ct);
        var minRatings = await settings.GetIntAsync(ZScoreMinRatingsFlag, DefaultZScoreMinRatings, ct);

        var rows = await db.Ratings.AsNoTracking()
            .Where(r => r.IsLatest && r.Visibility == Visibility.Public && r.HiddenAt == null)
            .Select(r => new { r.CreatedByUserId, r.DrinkId, r.Value })
            .ToListAsync(ct);

        var scores = ZScores(
            rows.Select(r => (r.CreatedByUserId, r.DrinkId, (double)r.Value)).ToList(), minRatings);

        var flagged = 0;
        foreach (var (userId, (meanDev, z, count)) in scores.Where(kv => Math.Abs(kv.Value.ZScore) >= threshold))
        {
            await UpsertFlagAsync(userId, AnomalyKind.RatingZScoreOutlier,
                $"mean deviation {meanDev:+0.00;-0.00} stars from drink averages over {count} drinks (z={z:0.0})",
                Math.Abs(z), ct);
            flagged++;
        }
        return flagged;
    }

    private async Task<int> ScanRapidFireAsync(CancellationToken ct)
    {
        var windowMinutes = await settings.GetIntAsync(RapidFireWindowMinutesFlag, DefaultRapidFireWindowMinutes, ct);
        var maxCount = await settings.GetIntAsync(RapidFireCountFlag, DefaultRapidFireCount, ct);
        var window = TimeSpan.FromMinutes(Math.Max(1, windowMinutes));
        var since = clock.GetUtcNow().AddHours(-24);

        var rows = await db.Ratings.AsNoTracking()
            .Where(r => r.CreatedAt >= since)
            .Select(r => new { r.CreatedByUserId, r.CreatedAt })
            .ToListAsync(ct);

        var flagged = 0;
        foreach (var group in rows.GroupBy(r => r.CreatedByUserId))
        {
            var peak = MaxInWindow(group.Select(r => r.CreatedAt).ToList(), window);
            if (peak < maxCount) continue;
            await UpsertFlagAsync(group.Key, AnomalyKind.RapidFire,
                $"{peak} rating writes within {window.TotalMinutes:0} minutes (last 24h)",
                peak, ct);
            flagged++;
        }
        return flagged;
    }

    /// <summary>Refresh the open flag in place, or open a new one (one OPEN per user+kind).</summary>
    private async Task UpsertFlagAsync(
        Guid userId, string kind, string evidence, double score, CancellationToken ct)
    {
        var open = await db.AnomalyFlags.FirstOrDefaultAsync(
            f => f.UserId == userId && f.Kind == kind && f.Status == AnomalyStatus.Open, ct);
        if (open is not null)
        {
            open.Evidence = evidence;
            open.Score = score;
        }
        else
        {
            db.AnomalyFlags.Add(new AnomalyFlag
            {
                UserId = userId,
                Kind = kind,
                Evidence = evidence,
                Score = score,
                CreatedAt = clock.GetUtcNow(),
            });
        }
        await db.SaveChangesAsync(ct);
    }

    // ======================================================================
    //  Admin queue
    // ======================================================================

    public async Task<IReadOnlyList<AnomalyFlagDto>> ListAsync(string? status, CancellationToken ct = default)
    {
        var wanted = string.IsNullOrWhiteSpace(status) ? AnomalyStatus.Open : status.ToLowerInvariant();
        return await db.AnomalyFlags.AsNoTracking()
            .Where(f => f.Status == wanted)
            .OrderByDescending(f => f.Score)
            .Take(200)
            .Select(f => new AnomalyFlagDto(
                f.Id, f.UserId, f.User.UserName ?? "?", f.Kind, f.Evidence, f.Score, f.Status,
                f.User.ShadowLimitedAt != null, f.User.BannedAt != null, f.CreatedAt))
            .ToListAsync(ct);
    }

    /// <summary>Clear a flag (reviewed, no action warranted).</summary>
    public async Task<bool> ClearAsync(
        Guid flagId, string actor, ModerationService moderation, CancellationToken ct = default)
    {
        var flag = await db.AnomalyFlags.FirstOrDefaultAsync(
            f => f.Id == flagId && f.Status == AnomalyStatus.Open, ct);
        if (flag is null) return false;

        flag.Status = AnomalyStatus.Cleared;
        flag.DecidedAt = clock.GetUtcNow();
        flag.DecidedBy = actor;
        moderation.Audit(actor, ModerationActionKind.AnomalyCleared, "user", flag.UserId,
            ("flagId", flag.Id.ToString()), ("kind", flag.Kind));
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Mark a flag actioned (a shadow-limit/ban was taken off it).</summary>
    public async Task MarkActionedAsync(Guid flagId, string actor, CancellationToken ct = default)
    {
        var flag = await db.AnomalyFlags.FirstOrDefaultAsync(
            f => f.Id == flagId && f.Status == AnomalyStatus.Open, ct);
        if (flag is null) return;
        flag.Status = AnomalyStatus.Actioned;
        flag.DecidedAt = clock.GetUtcNow();
        flag.DecidedBy = actor;
        await db.SaveChangesAsync(ct);
    }

    private async Task<double> GetDoubleAsync(string key, double fallback, CancellationToken ct)
    {
        var raw = await settings.GetStringAsync(key, ct);
        return double.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;
    }
}
