using BarBrain.Api.Data;
using BarBrain.Api.Data.Entities;
using BarBrain.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BarBrain.Api.Badges;

/// <summary>
/// The badge evaluator (Sprint 6). Definitions are DATA (badge_definitions,
/// seeded from badges.json); this class implements the metric vocabulary —
/// every metric a distinct-entity or weekly-streak count, never volume or
/// frequency (ADR-016, Hard Rule 4).
///
/// Evaluation is idempotent and race-safe: awards are permanent, the DB-unique
/// (user, badge) index refuses duplicates, and re-running is always harmless —
/// so domain services call <see cref="EvaluateAsync"/> inline after writes
/// (instant toasts) and the nightly job re-evaluates everyone (heals anything
/// missed, catches streak rollovers that happen without a write).
/// </summary>
public sealed class BadgeService(AppDbContext db, TimeProvider clock, ILogger<BadgeService> logger)
{
    /// <summary>Metric subsets per trigger, so inline hooks only pay for what a write can change.</summary>
    public static readonly string[] RatingMetrics =
    [
        BadgeMetric.DistinctStylesRated, BadgeMetric.DistinctCategoriesRated,
        BadgeMetric.WildcardDistinctDrinks, BadgeMetric.WeeklyStreakWeeks,
    ];
    public static readonly string[] CheckinMetrics = [BadgeMetric.DistinctVenuesCheckedIn];
    public static readonly string[] ContributionMetrics = [BadgeMetric.WikiContributions, BadgeMetric.MenuConfirms];
    public static readonly string[] MergeMetrics = [BadgeMetric.AcceptedMergeContributions];

    /// <summary>
    /// Award every active, not-yet-earned badge whose metric now meets its
    /// threshold. <paramref name="metrics"/> narrows the check (inline hooks);
    /// null evaluates everything (nightly). Never throws into the caller's
    /// write path — a badge must not break a rating.
    /// </summary>
    public async Task<int> EvaluateAsync(
        Guid userId, IReadOnlyCollection<string>? metrics = null, CancellationToken ct = default)
    {
        try
        {
            return await EvaluateCoreAsync(userId, metrics, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Badge evaluation failed for user {UserId}", userId);
            return 0;
        }
    }

    private async Task<int> EvaluateCoreAsync(
        Guid userId, IReadOnlyCollection<string>? metrics, CancellationToken ct)
    {
        var definitions = await db.BadgeDefinitions.AsNoTracking()
            .Where(b => b.Active)
            .ToListAsync(ct);
        if (metrics is not null)
            definitions = definitions.Where(b => metrics.Contains(b.Metric)).ToList();

        var earned = await db.UserBadges.AsNoTracking()
            .Where(ub => ub.UserId == userId)
            .Select(ub => ub.BadgeSlug)
            .ToHashSetAsync(ct);
        var candidates = definitions.Where(d => !earned.Contains(d.Slug)).ToList();
        if (candidates.Count == 0) return 0;

        var values = new Dictionary<string, int>();
        foreach (var metric in candidates.Select(c => c.Metric).Distinct())
            values[metric] = await ComputeMetricAsync(userId, metric, ct);

        var now = clock.GetUtcNow();
        var awardedCount = 0;
        foreach (var def in candidates.Where(d => values[d.Metric] >= d.Threshold))
        {
            db.UserBadges.Add(new UserBadge { UserId = userId, BadgeSlug = def.Slug, AwardedAt = now });
            db.Events.Add(new EventRecord
            {
                Name = "badge_awarded",
                OccurredAt = now,
                Properties = new Dictionary<string, string>
                {
                    ["userId"] = userId.ToString(),
                    ["badge"] = def.Slug,
                },
            });
            awardedCount++;
        }

        if (awardedCount > 0)
        {
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Concurrent evaluation awarded the same badge first — the
                // unique index did its job; the award exists either way.
                db.ChangeTracker.Clear();
                return 0;
            }
        }
        return awardedCount;
    }

    /// <summary>Current metric value. Every metric reads the authoritative domain tables.</summary>
    public async Task<int> ComputeMetricAsync(Guid userId, string metric, CancellationToken ct = default)
        => metric switch
        {
            BadgeMetric.DistinctStylesRated => await db.Ratings.AsNoTracking()
                .Where(r => r.CreatedByUserId == userId && r.IsLatest && r.Drink.StyleId != null)
                .Select(r => r.Drink.StyleId)
                .Distinct()
                .CountAsync(ct),

            BadgeMetric.DistinctCategoriesRated => await db.Ratings.AsNoTracking()
                .Where(r => r.CreatedByUserId == userId && r.IsLatest)
                .Select(r => r.Drink.Category)
                .Distinct()
                .CountAsync(ct),

            // ALL history rows, not just latest: Wildcard credit survives a
            // later organic re-rating (founder ruling: tried = rated from the
            // Wildcard section; forward-only via RecSection).
            BadgeMetric.WildcardDistinctDrinks => await db.Ratings.AsNoTracking()
                .Where(r => r.CreatedByUserId == userId && r.RecSection == RecSection.Wildcard)
                .Select(r => r.DrinkId)
                .Distinct()
                .CountAsync(ct),

            // Check-ins only ever target public venues (CheckinService), so
            // Home Bar exclusion is structural; the filter is belt-and-braces.
            BadgeMetric.DistinctVenuesCheckedIn => await db.Checkins.AsNoTracking()
                .Where(c => c.UserId == userId && c.Venue.VenueType == VenueType.Venue)
                .Select(c => c.VenueId)
                .Distinct()
                .CountAsync(ct),

            // Founder ruling: "first drink added" = first wiki contribution
            // (venue OR menu listing) — there is no user drink-add path.
            BadgeMetric.WikiContributions =>
                await db.Venues.AsNoTracking()
                    .CountAsync(v => v.CreatedByUserId == userId && v.VenueType == VenueType.Venue, ct)
                + await db.VenueMenuItems.AsNoTracking()
                    .CountAsync(mi => mi.CreatedByUserId == userId, ct),

            BadgeMetric.MenuConfirms => await MenuConfirmsAsync(userId, ct),

            BadgeMetric.AcceptedMergeContributions => await db.MergeQueue.AsNoTracking()
                .CountAsync(m => m.Status == MergeStatus.Approved && (
                    (m.SourceVenue != null && m.SourceVenue.CreatedByUserId == userId)
                    || (m.TargetVenue != null && m.TargetVenue.CreatedByUserId == userId)
                    || (m.SourceDrink != null && m.SourceDrink.CreatedByUserId == userId)
                    || (m.TargetDrink != null && m.TargetDrink.CreatedByUserId == userId)
                    || (m.SourceProducer != null && m.SourceProducer.CreatedByUserId == userId)
                    || (m.TargetProducer != null && m.TargetProducer.CreatedByUserId == userId)), ct),

            BadgeMetric.WeeklyStreakWeeks => StreakMath.ConsecutiveWeeks(
                await db.Ratings.AsNoTracking()
                    .Where(r => r.CreatedByUserId == userId)
                    .Select(r => r.CreatedAt)
                    .ToListAsync(ct),
                clock.GetUtcNow()),

            _ => 0,
        };

    /// <summary>
    /// Distinct menu listings the user confirmed, from menu_item_confirmed
    /// events (LastConfirmedAt keeps no per-user history). The name filter
    /// keeps the fetch small; jsonb properties are filtered in memory to stay
    /// off provider-specific JSON translation.
    /// </summary>
    private async Task<int> MenuConfirmsAsync(Guid userId, CancellationToken ct)
    {
        var uid = userId.ToString();
        var props = await db.Events.AsNoTracking()
            .Where(e => e.Name == "menu_item_confirmed")
            .Select(e => e.Properties)
            .ToListAsync(ct);
        return props
            .OfType<Dictionary<string, string>>()
            .Where(p => p.GetValueOrDefault("userId") == uid)
            .Select(p => (p.GetValueOrDefault("venueId"), p.GetValueOrDefault("drinkId")))
            .Distinct()
            .Count();
    }

    // ======================================================================
    //  Read surfaces
    // ======================================================================

    /// <summary>The profile Badges tab: all active badges + earned state + streak card.</summary>
    public async Task<BadgeGalleryResponse> GalleryAsync(Guid userId, CancellationToken ct = default)
    {
        var earned = await db.UserBadges.AsNoTracking()
            .Where(ub => ub.UserId == userId)
            .ToDictionaryAsync(ub => ub.BadgeSlug, ub => ub.AwardedAt, ct);

        var badges = (await db.BadgeDefinitions.AsNoTracking()
                .Where(b => b.Active)
                .OrderBy(b => b.SortOrder)
                .ToListAsync(ct))
            .Select(b => new BadgeDto(
                b.Slug, b.Name, b.Description, b.Icon, b.DisplayGroup, b.Threshold,
                earned.ContainsKey(b.Slug),
                earned.TryGetValue(b.Slug, out var at) ? at : null))
            .ToList();

        var now = clock.GetUtcNow();
        var ratingTimes = await db.Ratings.AsNoTracking()
            .Where(r => r.CreatedByUserId == userId)
            .Select(r => new { r.CreatedAt, r.DrinkId })
            .ToListAsync(ct);
        var streak = StreakMath.ConsecutiveWeeks(ratingTimes.Select(r => r.CreatedAt), now);
        var distinctThisWeek = ratingTimes
            .Where(r => (now - r.CreatedAt).TotalDays < 7)
            .Select(r => r.DrinkId).Distinct().Count();

        return new BadgeGalleryResponse(badges, streak, distinctThisWeek);
    }

    /// <summary>Unseen awards, oldest first (toast order).</summary>
    public async Task<UnseenBadgesResponse> UnseenAsync(Guid userId, CancellationToken ct = default)
    {
        var rows = await db.UserBadges.AsNoTracking()
            .Where(ub => ub.UserId == userId && ub.SeenAt == null && ub.Badge.Active)
            .OrderBy(ub => ub.AwardedAt)
            .Select(ub => new BadgeDto(
                ub.Badge.Slug, ub.Badge.Name, ub.Badge.Description, ub.Badge.Icon,
                ub.Badge.DisplayGroup, ub.Badge.Threshold, true, ub.AwardedAt))
            .ToListAsync(ct);
        return new UnseenBadgesResponse(rows);
    }

    /// <summary>Acknowledge every unseen award (the toast was shown).</summary>
    public async Task MarkSeenAsync(Guid userId, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        await db.UserBadges
            .Where(ub => ub.UserId == userId && ub.SeenAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(ub => ub.SeenAt, now), ct);
    }
}
