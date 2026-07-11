using BarBrain.Api.Data;
using BarBrain.Api.Data.Entities;
using BarBrain.Api.Settings;
using Microsoft.EntityFrameworkCore;

namespace BarBrain.Api.Catalog;

/// <summary>
/// Entity resolution (Sprint 1 spec): pg_trgm similarity generates merge
/// candidates into the queue; a moderator approves or rejects. Approving
/// turns the SOURCE row into a redirect (status='merged') pointing at the
/// TARGET; old IDs keep resolving (see CatalogQueryService redirects).
/// Rejected pairs are remembered and never re-proposed.
/// </summary>
public sealed class MergeService(AppDbContext db, ISettingsService settings, ILogger<MergeService> logger)
{
    /// <summary>Config flags (Hard Rule 10): similarity thresholds in percent.</summary>
    public const string ProducerThresholdFlag = "catalog.merge_threshold_producer_pct";
    public const string DrinkThresholdFlag = "catalog.merge_threshold_drink_pct";
    public const string VenueThresholdFlag = "catalog.merge_threshold_venue_pct";
    public const string VenueRadiusFlag = "venues.dedupe_radius_m";

    private sealed class CandidateRow
    {
        public Guid SourceId { get; set; }
        public Guid TargetId { get; set; }
        public float Sim { get; set; }
        public bool SameProducer { get; set; }
        public decimal? AbvDelta { get; set; }
    }

    /// <summary>
    /// Scans active producers for suspected duplicates. Pair order is
    /// canonical — newer row is the SOURCE (merges away), older row the
    /// TARGET (survives) — so symmetric duplicates can't arise.
    /// </summary>
    public async Task<int> GenerateProducerCandidatesAsync(CancellationToken ct = default)
    {
        var threshold = await settings.GetIntAsync(ProducerThresholdFlag, 55, ct) / 100f;

        var rows = await db.Database.SqlQuery<CandidateRow>($"""
            SELECT a."Id" AS "SourceId", b."Id" AS "TargetId",
                   similarity(a."NormalizedName", b."NormalizedName") AS "Sim",
                   FALSE AS "SameProducer", NULL::numeric AS "AbvDelta"
            FROM producers a
            JOIN producers b
              ON (a."CreatedAt", a."Id") > (b."CreatedAt", b."Id")
             AND a."NormalizedName" % b."NormalizedName"
            WHERE a."Status" = 'active' AND b."Status" = 'active'
              AND similarity(a."NormalizedName", b."NormalizedName") >= {threshold}
              AND NOT EXISTS (
                    SELECT 1 FROM merge_queue m
                    WHERE (m."SourceProducerId" = a."Id" AND m."TargetProducerId" = b."Id")
                       OR (m."SourceProducerId" = b."Id" AND m."TargetProducerId" = a."Id"))
            """).ToListAsync(ct);

        foreach (var row in rows)
        {
            db.MergeQueue.Add(new MergeCandidate
            {
                EntityType = MergeEntityType.Producer,
                SourceProducerId = row.SourceId,
                TargetProducerId = row.TargetId,
                Similarity = row.Sim,
                Reason = $"name similarity {row.Sim:0.00}",
            });
        }

        await db.SaveChangesAsync(ct);
        return rows.Count;
    }

    /// <summary>
    /// Scans active drinks for suspected duplicates: same category, similar
    /// names, same (or similar-named) producer, and ABV within 0.5 when both
    /// known (ABV is a dedup signal per ADR-008).
    /// </summary>
    public async Task<int> GenerateDrinkCandidatesAsync(CancellationToken ct = default)
    {
        var threshold = await settings.GetIntAsync(DrinkThresholdFlag, 50, ct) / 100f;

        var rows = await db.Database.SqlQuery<CandidateRow>($"""
            SELECT a."Id" AS "SourceId", b."Id" AS "TargetId",
                   similarity(a."NormalizedName", b."NormalizedName") AS "Sim",
                   a."ProducerId" = b."ProducerId" AS "SameProducer",
                   CASE WHEN a."Abv" IS NULL OR b."Abv" IS NULL THEN NULL
                        ELSE abs(a."Abv" - b."Abv") END AS "AbvDelta"
            FROM drinks a
            JOIN drinks b
              ON a."Category" = b."Category"
             AND (a."CreatedAt", a."Id") > (b."CreatedAt", b."Id")
             AND a."NormalizedName" % b."NormalizedName"
            JOIN producers pa ON pa."Id" = a."ProducerId"
            JOIN producers pb ON pb."Id" = b."ProducerId"
            WHERE a."Status" = 'active' AND b."Status" = 'active'
              AND similarity(a."NormalizedName", b."NormalizedName") >= {threshold}
              AND (a."ProducerId" = b."ProducerId"
                   OR similarity(pa."NormalizedName", pb."NormalizedName") >= 0.6)
              AND (a."Abv" IS NULL OR b."Abv" IS NULL OR abs(a."Abv" - b."Abv") <= 0.5)
              AND NOT EXISTS (
                    SELECT 1 FROM merge_queue m
                    WHERE (m."SourceDrinkId" = a."Id" AND m."TargetDrinkId" = b."Id")
                       OR (m."SourceDrinkId" = b."Id" AND m."TargetDrinkId" = a."Id"))
            """).ToListAsync(ct);

        foreach (var row in rows)
        {
            var reason = $"name similarity {row.Sim:0.00}"
                + (row.SameProducer ? "; same producer" : "; similar producer")
                + (row.AbvDelta is { } d ? $"; abv Δ{d:0.0}" : "");
            db.MergeQueue.Add(new MergeCandidate
            {
                EntityType = MergeEntityType.Drink,
                SourceDrinkId = row.SourceId,
                TargetDrinkId = row.TargetId,
                Similarity = row.Sim,
                Reason = reason,
            });
        }

        await db.SaveChangesAsync(ct);
        return rows.Count;
    }

    /// <summary>
    /// Venue dedupe (Sprint 5 spec): name-trigram similarity gated by geo
    /// proximity — a name-similar pair only queues when the venues sit within
    /// the configured radius of each other (or either side lacks geo, where
    /// distance can't clear them). Public venue-type venues only; Home Bars
    /// never participate. Pass <paramref name="onlyVenueId"/> to scan one
    /// venue against the rest (the wiki add-venue flow).
    /// </summary>
    public async Task<int> GenerateVenueCandidatesAsync(
        Guid? onlyVenueId = null, CancellationToken ct = default)
    {
        var threshold = await settings.GetIntAsync(VenueThresholdFlag, 55, ct) / 100f;
        var radiusM = await settings.GetIntAsync(VenueRadiusFlag, 200, ct);

        // Haversine via earth radius 6371km; venues are corridor-scale so the
        // spherical approximation is far below the dedupe radius tolerance.
        var rows = await db.Database.SqlQuery<CandidateRow>($"""
            SELECT a."Id" AS "SourceId", b."Id" AS "TargetId",
                   similarity(a."NormalizedName", b."NormalizedName") AS "Sim",
                   FALSE AS "SameProducer", NULL::numeric AS "AbvDelta"
            FROM venues a
            JOIN venues b
              ON (a."CreatedAt", a."Id") > (b."CreatedAt", b."Id")
             AND a."NormalizedName" % b."NormalizedName"
            WHERE a."VenueType" = 'venue' AND b."VenueType" = 'venue'
              AND a."Status" = 'active' AND b."Status" = 'active'
              AND ({onlyVenueId}::uuid IS NULL OR a."Id" = {onlyVenueId} OR b."Id" = {onlyVenueId})
              AND similarity(a."NormalizedName", b."NormalizedName") >= {threshold}
              AND (a."Latitude" IS NULL OR b."Latitude" IS NULL
                   OR 6371000 * 2 * asin(sqrt(
                        pow(sin(radians(b."Latitude" - a."Latitude") / 2), 2)
                        + cos(radians(a."Latitude")) * cos(radians(b."Latitude"))
                          * pow(sin(radians(b."Longitude" - a."Longitude") / 2), 2)))
                      <= {radiusM})
              AND NOT EXISTS (
                    SELECT 1 FROM merge_queue m
                    WHERE (m."SourceVenueId" = a."Id" AND m."TargetVenueId" = b."Id")
                       OR (m."SourceVenueId" = b."Id" AND m."TargetVenueId" = a."Id"))
            """).ToListAsync(ct);

        foreach (var row in rows)
        {
            db.MergeQueue.Add(new MergeCandidate
            {
                EntityType = MergeEntityType.Venue,
                SourceVenueId = row.SourceId,
                TargetVenueId = row.TargetId,
                Similarity = row.Sim,
                Reason = $"name similarity {row.Sim:0.00}; within {radiusM}m or geo unknown",
            });
        }

        await db.SaveChangesAsync(ct);
        return rows.Count;
    }

    /// <summary>
    /// Approves a candidate: SOURCE becomes a redirect to TARGET. Producer
    /// merges repoint the source's drinks to the target producer; a drink that
    /// would collide with the target's canonical identity stays on the merged
    /// (redirecting) producer and a drink-level candidate is enqueued instead —
    /// the queue converges over successive decisions rather than failing the
    /// whole merge. Drink merges copy attribute rows the target lacks.
    /// Other pending candidates whose source is the now-merged row are
    /// auto-rejected as superseded.
    /// </summary>
    public async Task<MergeDecisionOutcome> ApproveAsync(Guid candidateId, string actor, CancellationToken ct = default)
    {
        var candidate = await db.MergeQueue.FirstOrDefaultAsync(m => m.Id == candidateId, ct);
        if (candidate is null)
            return MergeDecisionOutcome.NotFound;
        if (candidate.Status != MergeStatus.Pending)
            return MergeDecisionOutcome.AlreadyDecided;

        // The provider retries via execution strategy; wrap the multi-step
        // merge in a single transaction inside it.
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            if (candidate.EntityType == MergeEntityType.Producer)
                await ApproveProducerMergeAsync(candidate, ct);
            else if (candidate.EntityType == MergeEntityType.Drink)
                await ApproveDrinkMergeAsync(candidate, ct);
            else
                await ApproveVenueMergeAsync(candidate, ct);

            candidate.Status = MergeStatus.Approved;
            candidate.DecidedAt = DateTimeOffset.UtcNow;
            candidate.DecidedBy = actor;

            await SupersedePendingForMergedSourceAsync(candidate, actor, ct);

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });

        return MergeDecisionOutcome.Done;
    }

    public async Task<MergeDecisionOutcome> RejectAsync(Guid candidateId, string actor, CancellationToken ct = default)
    {
        var candidate = await db.MergeQueue.FirstOrDefaultAsync(m => m.Id == candidateId, ct);
        if (candidate is null)
            return MergeDecisionOutcome.NotFound;
        if (candidate.Status != MergeStatus.Pending)
            return MergeDecisionOutcome.AlreadyDecided;

        candidate.Status = MergeStatus.Rejected;
        candidate.DecidedAt = DateTimeOffset.UtcNow;
        candidate.DecidedBy = actor;
        await db.SaveChangesAsync(ct);
        return MergeDecisionOutcome.Done;
    }

    private async Task ApproveProducerMergeAsync(MergeCandidate candidate, CancellationToken ct)
    {
        var source = await db.Producers.Include(p => p.Drinks)
            .FirstAsync(p => p.Id == candidate.SourceProducerId, ct);
        var target = await db.Producers
            .FirstAsync(p => p.Id == candidate.TargetProducerId, ct);
        EnsureActive(source, target);

        var targetKeys = await db.Drinks
            .Where(d => d.ProducerId == target.Id && d.Status == EntityStatus.Active)
            .Select(d => new { d.Category, d.NormalizedName })
            .ToListAsync(ct);
        var taken = targetKeys.Select(k => (k.Category, k.NormalizedName)).ToHashSet();

        foreach (var drink in source.Drinks.Where(d => d.Status == EntityStatus.Active))
        {
            if (taken.Contains((drink.Category, drink.NormalizedName)))
            {
                // Canonical-identity collision: don't repoint; queue a drink merge.
                var conflict = await db.Drinks.FirstAsync(d =>
                    d.ProducerId == target.Id
                    && d.Category == drink.Category
                    && d.NormalizedName == drink.NormalizedName
                    && d.Status == EntityStatus.Active, ct);
                var pairExists = await db.MergeQueue.AnyAsync(m =>
                    (m.SourceDrinkId == drink.Id && m.TargetDrinkId == conflict.Id)
                    || (m.SourceDrinkId == conflict.Id && m.TargetDrinkId == drink.Id), ct);
                if (!pairExists)
                {
                    db.MergeQueue.Add(new MergeCandidate
                    {
                        EntityType = MergeEntityType.Drink,
                        SourceDrinkId = drink.Id,
                        TargetDrinkId = conflict.Id,
                        Similarity = 1f,
                        Reason = "canonical-identity collision during producer merge",
                    });
                }
                logger.LogInformation(
                    "Producer merge {Source}→{Target}: drink {Drink} collides; queued drink merge.",
                    source.Id, target.Id, drink.Id);
                continue;
            }

            drink.ProducerId = target.Id;
            drink.UpdatedAt = DateTimeOffset.UtcNow;
            taken.Add((drink.Category, drink.NormalizedName));
        }

        source.Status = EntityStatus.Merged;
        source.MergedIntoProducerId = target.Id;
        source.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private async Task ApproveDrinkMergeAsync(MergeCandidate candidate, CancellationToken ct)
    {
        var source = await db.Drinks.Include(d => d.Attributes)
            .FirstAsync(d => d.Id == candidate.SourceDrinkId, ct);
        var target = await db.Drinks.Include(d => d.Attributes)
            .FirstAsync(d => d.Id == candidate.TargetDrinkId, ct);
        EnsureActive(source, target);

        // Enrich the survivor with any dims it lacks (keep its own values).
        var targetKeys = target.Attributes.Select(a => a.AttributeKey).ToHashSet();
        foreach (var attr in source.Attributes.Where(a => !targetKeys.Contains(a.AttributeKey)))
        {
            target.Attributes.Add(new DrinkAttributeValue
            {
                DrinkId = target.Id,
                AttributeKey = attr.AttributeKey,
                Value = attr.Value,
                Source = attr.Source,
                Confidence = attr.Confidence,
            });
        }

        target.Abv ??= source.Abv;
        target.StyleId ??= source.StyleId;
        target.UpdatedAt = DateTimeOffset.UtcNow;

        source.Status = EntityStatus.Merged;
        source.MergedIntoDrinkId = target.Id;
        source.UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Venue merge (Sprint 5 spec: "merge preserves menus"): menu items move
    /// to the survivor unless it already lists the drink (its own row wins);
    /// check-ins and venue-tagged ratings repoint so history follows the one
    /// real-world place; the source becomes a redirect.
    /// </summary>
    private async Task ApproveVenueMergeAsync(MergeCandidate candidate, CancellationToken ct)
    {
        var source = await db.Venues.FirstAsync(v => v.Id == candidate.SourceVenueId, ct);
        var target = await db.Venues.FirstAsync(v => v.Id == candidate.TargetVenueId, ct);
        EnsureActive(source, target);

        var targetDrinkIds = await db.VenueMenuItems
            .Where(mi => mi.VenueId == target.Id)
            .Select(mi => mi.DrinkId)
            .ToListAsync(ct);
        var taken = targetDrinkIds.ToHashSet();

        var sourceItems = await db.VenueMenuItems
            .Where(mi => mi.VenueId == source.Id)
            .ToListAsync(ct);
        foreach (var item in sourceItems)
        {
            if (taken.Add(item.DrinkId))
            {
                item.VenueId = target.Id;
                item.UpdatedAt = DateTimeOffset.UtcNow;
            }
            else
            {
                db.VenueMenuItems.Remove(item); // duplicate listing; survivor's row wins
            }
        }

        // The survivor keeps its own fields; it only absorbs what it lacks.
        // Geo moves as a pair (ck_venues_geo_pairing).
        if (target.Latitude is null && source.Latitude is not null)
        {
            target.Latitude = source.Latitude;
            target.Longitude = source.Longitude;
        }
        target.Address ??= source.Address;
        target.Hours ??= source.Hours;

        await db.Checkins
            .Where(c => c.VenueId == source.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.VenueId, target.Id), ct);
        await db.Ratings
            .Where(r => r.VenueId == source.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.VenueId, target.Id), ct);

        source.Status = EntityStatus.Merged;
        source.MergedIntoVenueId = target.Id;
    }

    private async Task SupersedePendingForMergedSourceAsync(
        MergeCandidate approved, string actor, CancellationToken ct)
    {
        var mergedProducerId = approved.SourceProducerId;
        var mergedDrinkId = approved.SourceDrinkId;
        var mergedVenueId = approved.SourceVenueId;

        var stale = await db.MergeQueue.Where(m =>
                m.Id != approved.Id
                && m.Status == MergeStatus.Pending
                && (mergedProducerId != null
                        ? (m.SourceProducerId == mergedProducerId || m.TargetProducerId == mergedProducerId)
                        : mergedDrinkId != null
                            ? (m.SourceDrinkId == mergedDrinkId || m.TargetDrinkId == mergedDrinkId)
                            : (m.SourceVenueId == mergedVenueId || m.TargetVenueId == mergedVenueId)))
            .ToListAsync(ct);

        foreach (var candidate in stale)
        {
            candidate.Status = MergeStatus.Rejected;
            candidate.DecidedAt = DateTimeOffset.UtcNow;
            candidate.DecidedBy = actor;
            var reason = $"{candidate.Reason}; superseded by merge {approved.Id}";
            candidate.Reason = reason.Length <= 256 ? reason : reason[..256];
        }
    }

    private static void EnsureActive(object source, object target)
    {
        var sourceStatus = source switch
        {
            Producer p => p.Status,
            Drink d => d.Status,
            Venue v => v.Status,
            _ => throw new InvalidOperationException("Unexpected entity."),
        };
        var targetStatus = target switch
        {
            Producer p => p.Status,
            Drink d => d.Status,
            Venue v => v.Status,
            _ => throw new InvalidOperationException("Unexpected entity."),
        };
        if (sourceStatus != EntityStatus.Active || targetStatus != EntityStatus.Active)
            throw new InvalidOperationException(
                "Merge candidate references a non-active row; it should have been superseded.");
    }
}

public enum MergeDecisionOutcome
{
    Done,
    NotFound,
    AlreadyDecided,
}
