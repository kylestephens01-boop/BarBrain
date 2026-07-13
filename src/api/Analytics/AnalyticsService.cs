using BarBrain.Api.Data;
using BarBrain.Api.Settings;
using BarBrain.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BarBrain.Api.Analytics;

/// <summary>
/// The admin retention dashboard's numbers (Sprint 7, ADR-017). Everything is
/// computed from OUR tables — the events funnel plus the authoritative
/// activity tables (ratings, check-ins). "Active" = rated or checked in;
/// deliberately NOT page views, so the metric can't be flattered.
///
/// Every query here is documented verbatim in docs/ANALYTICS.md — keep the
/// two in sync when editing.
/// </summary>
public sealed class AnalyticsService(
    AppDbContext db,
    ISettingsService settings,
    TimeProvider clock)
{
    public const string D30KillPctFlag = "analytics.d30_kill_pct";
    public const string D30ExcellentPctFlag = "analytics.d30_excellent_pct";
    public const int DefaultD30KillPct = 3;       // PRD: "D30 well under 3%" = kill/pivot
    public const int DefaultD30ExcellentPct = 7;  // PRD: "7-8% excellent (accelerate)"

    public async Task<AdminAnalyticsResponse> BuildAsync(CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();

        var signupsTotal = await db.Users.CountAsync(u => u.ActivatedAt != null, ct);
        var signups7 = await db.Users.CountAsync(
            u => u.ActivatedAt != null && u.CreatedAt >= now.AddDays(-7), ct);
        var signups30 = await db.Users.CountAsync(
            u => u.ActivatedAt != null && u.CreatedAt >= now.AddDays(-30), ct);

        // Funnel from first-party events (they also survive account deletion,
        // scrubbed of the user link — totals stay honest).
        var signupEvents = await db.Events.CountAsync(e => e.Name == "signup", ct);
        var activationEvents = await db.Events.CountAsync(e => e.Name == "activation", ct);
        var activationRate = signupEvents == 0 ? 0 : 100.0 * activationEvents / signupEvents;

        var wau = await db.Ratings.Where(r => r.CreatedAt >= now.AddDays(-7))
            .Select(r => r.CreatedByUserId)
            .Union(db.Checkins.Where(c => c.CreatedAt >= now.AddDays(-7)).Select(c => c.UserId))
            .Distinct()
            .CountAsync(ct);

        var ratingsPerUser = await db.Database.SqlQuery<double>(
            $"""
            SELECT coalesce(avg(cnt), 0)::float AS "Value"
            FROM (SELECT count(*) AS cnt FROM ratings WHERE "IsLatest" GROUP BY "CreatedByUserId") s
            """).SingleAsync(ct);

        var multiCategoryPct = await db.Database.SqlQuery<double>(
            $"""
            WITH per_user AS (
                SELECT r."CreatedByUserId" AS uid, count(DISTINCT d."Category") AS cats
                FROM ratings r JOIN drinks d ON d."Id" = r."DrinkId"
                WHERE r."IsLatest"
                GROUP BY 1)
            SELECT (CASE WHEN count(*) = 0 THEN 0
                    ELSE 100.0 * count(*) FILTER (WHERE cats >= 2) / count(*) END)::float AS "Value"
            FROM per_user
            """).SingleAsync(ct);

        return new AdminAnalyticsResponse(
            now,
            signupsTotal, signups7, signups30,
            Math.Round(activationRate, 1),
            await RetentionAsync(1, now, ct),
            await RetentionAsync(7, now, ct),
            await RetentionAsync(30, now, ct),
            wau,
            await WeeklyAsync("ratings", "\"CreatedAt\"", now, ct),
            await WeeklyAsync("checkins", "\"CreatedAt\"", now, ct),
            Math.Round(ratingsPerUser, 1),
            Math.Round(multiCategoryPct, 1),
            await settings.GetIntAsync(D30KillPctFlag, DefaultD30KillPct, ct),
            await settings.GetIntAsync(D30ExcellentPctFlag, DefaultD30ExcellentPct, ct));
    }

    /// <summary>
    /// Day-N retention: of activated accounts at least N+1 days old, the share
    /// with any activity (rating OR check-in) on day N after their signup.
    /// </summary>
    private async Task<RetentionCohort> RetentionAsync(int day, DateTimeOffset now, CancellationToken ct)
    {
        var eligible = await db.Database.SqlQuery<int>(
            $"""
            SELECT count(*)::int AS "Value" FROM users u
            WHERE u."ActivatedAt" IS NOT NULL
              AND u."CreatedAt" <= {now} - make_interval(days => {day + 1})
            """).SingleAsync(ct);

        var retained = await db.Database.SqlQuery<int>(
            $"""
            SELECT count(DISTINCT u."Id")::int AS "Value"
            FROM users u
            JOIN (SELECT "CreatedByUserId" AS uid, "CreatedAt" AS at FROM ratings
                  UNION ALL
                  SELECT "UserId", "CreatedAt" FROM checkins) a
              ON a.uid = u."Id"
             AND a.at >= u."CreatedAt" + make_interval(days => {day})
             AND a.at <  u."CreatedAt" + make_interval(days => {day + 1})
            WHERE u."ActivatedAt" IS NOT NULL
              AND u."CreatedAt" <= {now} - make_interval(days => {day + 1})
            """).SingleAsync(ct);

        return new RetentionCohort(eligible, retained,
            eligible == 0 ? 0 : Math.Round(100.0 * retained / eligible, 1));
    }

    private async Task<List<WeeklyCount>> WeeklyAsync(
        string table, string column, DateTimeOffset now, CancellationToken ct)
    {
        // Last 8 ISO weeks, oldest first. Table/column are compile-time
        // constants from the two call sites — no injection surface.
#pragma warning disable EF1002
        var rows = await db.Database.SqlQueryRaw<WeeklyRow>(
            $$"""
            SELECT date_trunc('week', {{column}})::timestamptz AS "WeekStart", count(*)::int AS "Count"
            FROM {{table}}
            WHERE {{column}} >= {0} - interval '56 days'
            GROUP BY 1 ORDER BY 1
            """, now).ToListAsync(ct);
#pragma warning restore EF1002
        return rows.Select(r => new WeeklyCount(r.WeekStart.UtcDateTime, r.Count)).ToList();
    }

    private sealed class WeeklyRow
    {
        public DateTimeOffset WeekStart { get; set; }
        public int Count { get; set; }
    }
}
