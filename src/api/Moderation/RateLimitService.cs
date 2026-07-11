using BarBrain.Api.Data;
using BarBrain.Api.Settings;
using Microsoft.EntityFrameworkCore;

namespace BarBrain.Api.Moderation;

/// <summary>
/// Per-endpoint, per-account rate limits from config flags (Sprint 6
/// hardening; Hard Rule 10 — thresholds are flags, not code). Counts the
/// caller's own DOMAIN rows in a trailing window — the same pattern the
/// Sprint 5 wiki limits use (venues.add_per_day, venues.menu_edits_per_day,
/// which stay where they are in VenueService).
///
/// These are abuse controls on WRITES, invisible in normal use — deliberately
/// generous defaults; they are unrelated to (and must never become)
/// consumption limiting or prompting (ADR-016 governs incentives).
/// </summary>
public sealed class RateLimitService(AppDbContext db, ISettingsService settings, TimeProvider clock)
{
    public const string RatingsPerHourFlag = "limits.ratings_per_hour";
    public const string ReportsPerDayFlag = "limits.reports_per_day";

    public const int DefaultRatingsPerHour = 120;
    public const int DefaultReportsPerDay = 20;

    /// <summary>Null when allowed; a human message when limited.</summary>
    public async Task<string?> CheckRatingsAsync(Guid userId, CancellationToken ct = default)
    {
        var limit = await settings.GetIntAsync(RatingsPerHourFlag, DefaultRatingsPerHour, ct);
        if (limit <= 0) return null; // 0 disables the limit (config kill switch)
        var since = clock.GetUtcNow().AddHours(-1);
        var count = await db.Ratings.AsNoTracking()
            .CountAsync(r => r.CreatedByUserId == userId && r.CreatedAt >= since, ct);
        return count >= limit
            ? "That's a lot of updates at once — give it a little while and try again."
            : null;
    }

    public async Task<string?> CheckReportsAsync(Guid userId, CancellationToken ct = default)
    {
        var limit = await settings.GetIntAsync(ReportsPerDayFlag, DefaultReportsPerDay, ct);
        if (limit <= 0) return null;
        var since = clock.GetUtcNow().AddHours(-24);
        var count = await db.Reports.AsNoTracking()
            .CountAsync(r => r.ReporterUserId == userId && r.CreatedAt >= since, ct);
        return count >= limit
            ? "You've filed a lot of reports today — try again tomorrow."
            : null;
    }
}
