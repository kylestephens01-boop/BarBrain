using BarBrain.Api.Data;
using BarBrain.Api.Data.Entities;
using BarBrain.Api.Settings;
using BarBrain.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BarBrain.Api.Palate;

/// <summary>
/// Palate matching (ADR-014, Sprint 4). Blends the PRIMARY attribute-profile
/// similarity (cosine of preference vectors, ADR-025) with user-user
/// collaborative-filtering agreement (mean-centered Pearson over co-rated
/// drinks, ADR-007), DENSITY-WEIGHTED: the CF term is worth nothing below a
/// min-co-rated floor and grows with co-rating count, so attribute similarity
/// carries the score at today's sparse density and CF strengthens it only as
/// the co-rating matrix fills in. This is exactly ADR-025's deferred-CF upgrade
/// path activating — CF enters as a blend partner, never as a lone recommender.
///
/// The blend/Pearson/tier math lives in PURE statics so the synthetic-twins eval
/// (Gate C2) asserts it directly. Named matches are ONE-WAY (a read surface, no
/// interaction). Hide-me is enforced on READ, both directions, so it takes
/// effect immediately. Match-% display honors the <c>match.display_mode</c> flag.
/// </summary>
public sealed class MatchService(AppDbContext db, ISettingsService settings)
{
    // --- Flags (Hard Rule 10) -------------------------------------------------
    public const string NightlyEnabledFlag = "match.nightly_enabled";
    public const string NightlyHourFlag = "match.nightly_hour_utc";
    public const string MinCoRatedFlag = "match.min_corated";
    public const string ShrinkageFlag = "match.cf_shrinkage";
    public const string DensityKFlag = "match.density_k";
    public const string TopKFlag = "match.top_k_neighbors";
    public const string DisplayModeFlag = "match.display_mode";
    public const string ConfMedFlag = "match.confidence_med_corated";
    public const string ConfHighFlag = "match.confidence_high_corated";
    public const string MinDisplayScoreFlag = "match.min_display_score_pct";
    public const string RecentLoveMinFlag = "match.recent_love_min";
    public const string FeedNeighborsFlag = "match.feed_neighbor_count";

    public const int DefaultMinCoRated = 3;
    public const double DefaultShrinkage = 5;
    public const double DefaultDensityK = 10;
    public const int DefaultTopK = 50;
    public const int DefaultConfMed = 5;
    public const int DefaultConfHigh = 15;

    public const string DisplayEager = "eager";
    public const string DisplayConservative = "conservative";

    private const int RecentLovesPerMatch = 3;

    // ======================================================================
    //  Pure math (exercised directly by the Gate C2 eval)
    // ======================================================================

    public readonly record struct BlendParams(int MinCoRated, double Shrinkage, double DensityK);

    public readonly record struct Edge(
        double AttributeSimilarity, double? CoRatingAgreement, int CoRatedCount, double BlendedScore);

    /// <summary>
    /// The density-weighted blend for a single (user, neighbor, category) edge.
    /// <paramref name="coRatings"/> are the (thisUser, otherUser) rating pairs
    /// over drinks BOTH rated in the category.
    ///
    /// w = coRated/(coRated + K) once the min-co-rated floor is cleared, else 0.
    /// blended = (1−w)·attributeCosine + w·agreement. attributeCosine is clamped
    /// to [0,1]; agreement is the co-rated Pearson, significance-shrunk by
    /// n/(n+shrinkage) and clamped to [0,1] for the blend (anti-correlation adds
    /// nothing rather than actively subtracting). The RAW shrunk Pearson is
    /// returned for auditing.
    /// </summary>
    public static Edge ComputeEdge(
        float[]? preferenceA, float[]? preferenceB,
        IReadOnlyList<(double A, double B)> coRatings, BlendParams p)
    {
        var attr = preferenceA is not null && preferenceB is not null
            ? Math.Clamp(RecommendationService.Cosine(preferenceA, preferenceB), 0, 1)
            : 0.0;

        var n = coRatings.Count;
        double? agreement = null;
        var w = 0.0;
        if (n >= p.MinCoRated && p.MinCoRated > 0)
        {
            var shrunk = Pearson(coRatings) * (n / (n + p.Shrinkage));
            agreement = shrunk;
            w = p.DensityK <= 0 ? 1 : n / (n + p.DensityK);
        }

        var agreement01 = Math.Clamp(agreement ?? 0, 0, 1);
        var blended = Math.Clamp((1 - w) * attr + w * agreement01, 0, 1);
        return new Edge(attr, agreement, n, blended);
    }

    /// <summary>Pearson correlation over paired ratings, mean-centered per user.</summary>
    public static double Pearson(IReadOnlyList<(double A, double B)> pairs)
    {
        var n = pairs.Count;
        if (n == 0) return 0;
        double meanA = 0, meanB = 0;
        foreach (var (a, b) in pairs) { meanA += a; meanB += b; }
        meanA /= n; meanB /= n;

        double num = 0, denA = 0, denB = 0;
        foreach (var (a, b) in pairs)
        {
            var da = a - meanA;
            var dbv = b - meanB;
            num += da * dbv;
            denA += da * da;
            denB += dbv * dbv;
        }
        // A constant series has no correlation to report — treat as no signal.
        return denA < 1e-12 || denB < 1e-12 ? 0 : num / Math.Sqrt(denA * denB);
    }

    public static string ConfidenceTier(int coRated, int medThreshold, int highThreshold)
        => coRated >= highThreshold ? "high" : coRated >= medThreshold ? "med" : "low";

    // ======================================================================
    //  Nightly materialization (called by MatchNightlyService and the eval)
    // ======================================================================

    public sealed record BatchSummary(int Users, int Edges);

    /// <summary>
    /// Full rebuild of <c>user_match_neighbors</c> from current profiles +
    /// latest ratings. O(users²·categories); a nightly single-box job at MVP
    /// scale (ADR-004). Includes hidden users (hide-me is enforced on READ), so
    /// toggling it either way is immediate without waiting for the next run.
    /// </summary>
    public async Task<BatchSummary> ComputeAllAsync(CancellationToken ct = default)
    {
        var p = await LoadBlendParamsAsync(ct);
        var topK = await settings.GetIntAsync(TopKFlag, DefaultTopK, ct);

        var activated = await db.Users.AsNoTracking()
            .Where(u => u.ActivatedAt != null).Select(u => u.Id).ToListAsync(ct);
        var activatedSet = activated.ToHashSet();

        var profileRows = await db.UserPalateProfiles.AsNoTracking()
            .Where(p => p.PreferenceVector != null)
            .Select(p => new { p.UserId, p.Category, p.PreferenceVector })
            .ToListAsync(ct);

        var ratingRows = await db.Ratings.AsNoTracking()
            .Where(r => r.IsLatest)
            .Select(r => new { r.CreatedByUserId, r.DrinkId, r.Drink.Category, r.Value })
            .ToListAsync(ct);

        // (user, category) → drinkId → rating value.
        var ratingsByUserCat = ratingRows
            .Where(r => activatedSet.Contains(r.CreatedByUserId))
            .GroupBy(r => (r.CreatedByUserId, r.Category))
            .ToDictionary(g => g.Key, g => g.ToDictionary(r => r.DrinkId, r => (double)r.Value));

        // Per user, the neighbors we'll keep (top-K by blended across all cats).
        var edges = new List<UserMatchNeighbor>();
        var now = DateTimeOffset.UtcNow;

        foreach (var category in DrinkCategory.All)
        {
            var users = profileRows
                .Where(r => r.Category == category && activatedSet.Contains(r.UserId))
                .Select(r => new { r.UserId, Pref = r.PreferenceVector!.ToArray() })
                .ToList();

            for (var i = 0; i < users.Count; i++)
            for (var j = i + 1; j < users.Count; j++)
            {
                var a = users[i];
                var b = users[j];
                var coRatings = CoRatings(ratingsByUserCat, a.UserId, b.UserId, category);
                var edge = ComputeEdge(a.Pref, b.Pref, coRatings, p);
                if (edge.BlendedScore <= 0) continue;

                edges.Add(NewRow(a.UserId, b.UserId, category, edge, now));
                edges.Add(NewRow(b.UserId, a.UserId, category, edge, now));
            }
        }

        // Keep only each user's top-K neighbor edges (across categories).
        var kept = edges
            .GroupBy(e => e.UserId)
            .SelectMany(g => g.OrderByDescending(e => e.BlendedScore).Take(topK))
            .ToList();

        // Full rebuild: clear and rewrite. Cheap at this scale; avoids a diff.
        await db.UserMatchNeighbors.ExecuteDeleteAsync(ct);
        db.UserMatchNeighbors.AddRange(kept);
        await db.SaveChangesAsync(ct);

        return new BatchSummary(activated.Count, kept.Count);
    }

    private static List<(double, double)> CoRatings(
        Dictionary<(Guid, string), Dictionary<Guid, double>> byUserCat,
        Guid userA, Guid userB, string category)
    {
        if (!byUserCat.TryGetValue((userA, category), out var a) ||
            !byUserCat.TryGetValue((userB, category), out var b))
            return [];
        // Iterate the smaller map for the intersection.
        var (small, large) = a.Count <= b.Count ? (a, b) : (b, a);
        var swapped = a.Count > b.Count;
        var pairs = new List<(double, double)>(small.Count);
        foreach (var (drinkId, vSmall) in small)
            if (large.TryGetValue(drinkId, out var vLarge))
                pairs.Add(swapped ? (vLarge, vSmall) : (vSmall, vLarge));
        return pairs;
    }

    private static UserMatchNeighbor NewRow(
        Guid userId, Guid neighborId, string category, Edge edge, DateTimeOffset now) => new()
    {
        UserId = userId,
        NeighborUserId = neighborId,
        Category = category,
        AttributeSimilarity = edge.AttributeSimilarity,
        CoRatingAgreement = edge.CoRatingAgreement,
        CoRatedCount = edge.CoRatedCount,
        BlendedScore = edge.BlendedScore,
        ComputedAt = now,
    };

    // ======================================================================
    //  Read surfaces
    // ======================================================================

    /// <summary>"Your Matches": who matches the caller's palate, strongest first.</summary>
    public async Task<MatchesResponse> GetMatchesAsync(
        Guid userId, int take, CancellationToken ct = default)
    {
        var displayMode = await settings.GetStringAsync(DisplayModeFlag, DisplayEager, ct);
        displayMode = displayMode == DisplayConservative ? DisplayConservative : DisplayEager;

        // Hide-me, my side: I opted out → I see no one (symmetry).
        var iAmHidden = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId).Select(u => u.HideFromMatches).FirstOrDefaultAsync(ct);
        if (iAmHidden)
            return new MatchesResponse([], true, displayMode);

        var medThreshold = await settings.GetIntAsync(ConfMedFlag, DefaultConfMed, ct);
        var highThreshold = await settings.GetIntAsync(ConfHighFlag, DefaultConfHigh, ct);
        var minPct = await settings.GetIntAsync(MinDisplayScoreFlag, 1, ct);

        // Per-category edges to non-hidden neighbors, aggregated to one row per
        // neighbor. Hide-me, their side: exclude neighbors who opted out.
        var rows = await db.UserMatchNeighbors.AsNoTracking()
            .Where(m => m.UserId == userId && !m.Neighbor.HideFromMatches)
            .Select(m => new
            {
                m.NeighborUserId,
                Handle = m.Neighbor.UserName!,
                m.BlendedScore,
                m.CoRatedCount,
            })
            .ToListAsync(ct);

        var aggregated = rows
            .GroupBy(r => (r.NeighborUserId, r.Handle))
            .Select(g =>
            {
                // Evidence-weighted mean across categories: a category with more
                // co-rated drinks pulls harder, but attribute-only overlap
                // (weight 1) still counts.
                var weight = g.Sum(r => 1.0 + r.CoRatedCount);
                var score = g.Sum(r => r.BlendedScore * (1.0 + r.CoRatedCount)) / weight;
                var maxCoRated = g.Max(r => r.CoRatedCount);
                return new
                {
                    g.Key.NeighborUserId,
                    g.Key.Handle,
                    Score = score,
                    MaxCoRated = maxCoRated,
                };
            })
            .Where(x => (int)Math.Round(x.Score * 100) >= minPct)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Handle, StringComparer.Ordinal)
            .Take(take)
            .ToList();

        if (aggregated.Count == 0)
            return new MatchesResponse([], false, displayMode);

        var loves = await RecentLovesAsync(aggregated.Select(a => a.NeighborUserId).ToList(), ct);

        var matches = aggregated.Select(a =>
        {
            var confidence = ConfidenceTier(a.MaxCoRated, medThreshold, highThreshold);
            var rawPct = (int)Math.Clamp(Math.Round(a.Score * 100), 1, 99);
            var conservativeHides = displayMode == DisplayConservative && confidence == "low";
            int? pct = conservativeHides ? null : rawPct;
            var earlyEstimate = displayMode == DisplayEager && confidence == "low" && pct is not null;
            return new MatchDto(
                a.Handle, pct, confidence, earlyEstimate,
                loves.GetValueOrDefault(a.NeighborUserId, []));
        }).ToList();

        return new MatchesResponse(matches, false, displayMode);
    }

    /// <summary>Latest public high ratings per neighbor (social proof); newest first.</summary>
    private async Task<Dictionary<Guid, List<MatchLove>>> RecentLovesAsync(
        List<Guid> neighborIds, CancellationToken ct)
    {
        var likeMin = await GetDecimalAsync(RecentLoveMinFlag, 4.0m, ct);

        var raw = await db.Ratings.AsNoTracking()
            .Where(r => neighborIds.Contains(r.CreatedByUserId)
                && r.IsLatest && r.Visibility == Visibility.Public && r.Value >= likeMin)
            .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
            .Select(r => new
            {
                r.CreatedByUserId,
                r.DrinkId,
                r.Drink.Name,
                r.Drink.Category,
                r.Value,
            })
            .ToListAsync(ct);

        return raw
            .GroupBy(r => r.CreatedByUserId)
            .ToDictionary(
                g => g.Key,
                g => g.Take(RecentLovesPerMatch)
                    .Select(r => new MatchLove(r.DrinkId, r.Name, r.Category, r.Value))
                    .ToList());
    }

    /// <summary>
    /// Drinks the caller's strongest matches rated highly and the caller hasn't
    /// rated — feeds the "Loved by Your Matches" section and rec social proof.
    /// </summary>
    public async Task<IReadOnlyList<MatchLovedDrink>> LovedByMatchesAsync(
        Guid userId, CancellationToken ct = default)
    {
        var iAmHidden = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId).Select(u => u.HideFromMatches).FirstOrDefaultAsync(ct);
        if (iAmHidden) return [];

        var feedNeighbors = await settings.GetIntAsync(FeedNeighborsFlag, 25, ct);
        var likeMin = await GetDecimalAsync(RecentLoveMinFlag, 4.0m, ct);

        // Strongest neighbors (aggregate across categories), non-hidden.
        var neighborRows = await db.UserMatchNeighbors.AsNoTracking()
            .Where(m => m.UserId == userId && !m.Neighbor.HideFromMatches)
            .Select(m => new { m.NeighborUserId, Handle = m.Neighbor.UserName!, m.BlendedScore, m.CoRatedCount })
            .ToListAsync(ct);

        var neighbors = neighborRows
            .GroupBy(r => (r.NeighborUserId, r.Handle))
            .Select(g => new
            {
                g.Key.NeighborUserId,
                g.Key.Handle,
                Score = g.Sum(r => r.BlendedScore * (1.0 + r.CoRatedCount)) / g.Sum(r => 1.0 + r.CoRatedCount),
            })
            .OrderByDescending(n => n.Score)
            .Take(feedNeighbors)
            .ToList();
        if (neighbors.Count == 0) return [];

        var scoreById = neighbors.ToDictionary(n => n.NeighborUserId, n => n.Score);
        var handleById = neighbors.ToDictionary(n => n.NeighborUserId, n => n.Handle);
        var neighborIds = neighbors.Select(n => n.NeighborUserId).ToList();

        // Their highly-rated public drinks the caller hasn't rated.
        var loves = await db.Ratings.AsNoTracking()
            .Where(r => neighborIds.Contains(r.CreatedByUserId)
                && r.IsLatest && r.Visibility == Visibility.Public && r.Value >= likeMin
                && !db.Ratings.Any(mine => mine.CreatedByUserId == userId && mine.DrinkId == r.DrinkId))
            .Select(r => new { r.DrinkId, r.CreatedByUserId })
            .ToListAsync(ct);

        return loves
            .GroupBy(r => r.DrinkId)
            .Select(g =>
            {
                var top = g.OrderByDescending(r => scoreById.GetValueOrDefault(r.CreatedByUserId)).First();
                return new MatchLovedDrink(
                    g.Key,
                    g.Select(r => r.CreatedByUserId).Distinct().Count(),
                    handleById.GetValueOrDefault(top.CreatedByUserId, ""),
                    scoreById.GetValueOrDefault(top.CreatedByUserId));
            })
            .OrderByDescending(d => d.TopNeighborScore).ThenByDescending(d => d.MatchCount)
            .ToList();
    }

    private async Task<BlendParams> LoadBlendParamsAsync(CancellationToken ct)
    {
        var minCoRated = await settings.GetIntAsync(MinCoRatedFlag, DefaultMinCoRated, ct);
        var shrinkage = await GetDoubleAsync(ShrinkageFlag, DefaultShrinkage, ct);
        var densityK = await GetDoubleAsync(DensityKFlag, DefaultDensityK, ct);
        return new BlendParams(minCoRated, shrinkage, densityK);
    }

    private async Task<double> GetDoubleAsync(string key, double fallback, CancellationToken ct)
    {
        var raw = await settings.GetStringAsync(key, ct);
        return double.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;
    }

    private async Task<decimal> GetDecimalAsync(string key, decimal fallback, CancellationToken ct)
    {
        var raw = await settings.GetStringAsync(key, ct);
        return decimal.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;
    }
}

/// <summary>A drink loved by the caller's matches (feed + social proof).</summary>
public sealed record MatchLovedDrink(Guid DrinkId, int MatchCount, string SampleHandle, double TopNeighborScore);
