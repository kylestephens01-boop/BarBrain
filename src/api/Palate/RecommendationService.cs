using BarBrain.Api.Data;
using BarBrain.Api.Data.Entities;
using BarBrain.Api.Settings;
using BarBrain.Shared.Contracts;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace BarBrain.Api.Palate;

/// <summary>
/// Content-based sectioned feed (ADR-013/025/027). pgvector HNSW cosine over
/// per-drink attribute vectors is the PRIMARY mechanism; no CF anywhere.
///
/// Sections are distance bands over the candidate pool: Up Your Alley =
/// nearest, Stretch a Little = the adjacent band, Wildcard = far-band picks
/// sampled with a per-(user, day) deterministic seed. An MMR-lite pass keeps
/// sections from collapsing into near-duplicates. Section shape is
/// CONFIDENCE-ADAPTIVE (flags): a 2-rating user gets a smaller Alley and more
/// Wildcards than a 40-rating user.
///
/// Cross-category bridge recs (ADR-027, the moat mechanic): each profiled
/// source category nominates drinks in the user's OTHER categories via the
/// 6-dim bridge; they carry a cross-category tag and a source-aware reason.
///
/// EVERY item carries its "because" (hard product requirement). Copy stays
/// inside BRAND.md: no volume, no frequency, no intoxication framing.
/// </summary>
public sealed class RecommendationService(
    AppDbContext db,
    ISettingsService settings,
    TimeProvider clock,
    MatchService matches)
{
    // --- Flags (Hard Rule 10) -------------------------------------------------
    public const string PoolSizeFlag = "feed.pool_size";
    public const string AlleyBandFlag = "feed.alley_band";
    public const string StretchBandFlag = "feed.stretch_band";
    public const string SectionSizeFlag = "feed.section_size";
    public const string ColdSectionSizeFlag = "feed.cold_section_size";
    public const string WildcardFullFlag = "feed.wildcard_count_full";
    public const string WildcardColdFlag = "feed.wildcard_count_cold";
    public const string PopularityWeightFlag = "feed.popularity_weight_pct";
    public const string MmrLambdaFlag = "feed.mmr_lambda_pct";
    public const string BridgeEnabledFlag = "feed.bridge_enabled";
    public const string BridgeCountFlag = "feed.bridge_count";
    public const string MatchesSectionSizeFlag = "feed.matches_section_size";
    public const string CfWeightFlag = "feed.cf_weight_pct";

    public const string SectionAlley = "up_your_alley";
    public const string SectionStretch = "stretch_a_little";
    public const string SectionWildcard = "wildcard";
    public const string SectionMatches = "loved_by_your_matches"; // Sprint 4

    private sealed record Candidate(
        Guid DrinkId,
        string Name,
        string ProducerName,
        string Category,
        string? StyleName,
        decimal? Abv,
        double Distance,       // cosine distance to the scoring vector
        float[]? Vector,       // 8-dim category vector (same-category candidates)
        double Popularity,     // smoothed [0,1)
        bool CrossCategory,
        string? SourceCategory)
    {
        public double Score { get; init; }
    }

    public async Task<FeedResponse> BuildFeedAsync(
        Guid userId, string? categoryFilter, CancellationToken ct = default)
    {
        var profiles = await db.UserPalateProfiles.AsNoTracking()
            .Where(p => p.UserId == userId).ToListAsync(ct);
        var interests = await db.UserCategoryInterests.AsNoTracking()
            .Where(i => i.UserId == userId).Select(i => i.Category).ToListAsync(ct);

        var categories = ResolveCategories(categoryFilter, profiles, interests);
        var totalRatings = profiles.Sum(p => p.RatingsCount);

        var warm = await settings.GetIntAsync(PalateProfileService.WarmRatingsFlag, PalateProfileService.DefaultWarmRatings, ct);
        var full = await settings.GetIntAsync(PalateProfileService.FullRatingsFlag, PalateProfileService.DefaultFullRatings, ct);
        var confidence = totalRatings >= full ? "full" : totalRatings >= warm ? "warm" : "cold";

        var poolSize = await settings.GetIntAsync(PoolSizeFlag, 300, ct);
        var alleyBand = await settings.GetIntAsync(AlleyBandFlag, 30, ct);
        var stretchBand = await settings.GetIntAsync(StretchBandFlag, 90, ct);
        var sectionSize = await settings.GetIntAsync(SectionSizeFlag, 8, ct);
        var coldSectionSize = await settings.GetIntAsync(ColdSectionSizeFlag, 4, ct);
        var wildcardCount = confidence == "full"
            ? await settings.GetIntAsync(WildcardFullFlag, 3, ct)
            : await settings.GetIntAsync(WildcardColdFlag, 6, ct);
        var popularityWeight = await settings.GetIntAsync(PopularityWeightFlag, 10, ct) / 100.0;
        var mmrLambda = await settings.GetIntAsync(MmrLambdaFlag, 75, ct) / 100.0;
        var cfWeight = await settings.GetIntAsync(CfWeightFlag, 20, ct) / 100.0;

        var pickSize = confidence == "cold" ? coldSectionSize : sectionSize;

        var popularity = await LoadPopularityAsync(ct);
        var attributeNames = await LoadAttributeNamesAsync(ct);

        // Matches (ADR-014): drinks the user's palate matches loved. Powers the
        // hybrid CF blend, the social-proof line, and the 4th feed section.
        // Fallback ladder: a COLD user gets pure content (no CF), a warm/full
        // user gets the blend — matching needs some ratings to mean anything.
        var matchLoved = confidence == "cold"
            ? new Dictionary<Guid, MatchLovedDrink>()
            : (await matches.LovedByMatchesAsync(userId, ct)).ToDictionary(m => m.DrinkId);

        // --- Candidate pools, per category ------------------------------------
        var scored = new List<Candidate>();
        var popular = new List<Candidate>();
        foreach (var category in categories)
        {
            var profile = profiles.FirstOrDefault(p => p.Category == category);
            if (profile?.PreferenceVector is { } pv)
                scored.AddRange(await NearestCandidatesAsync(userId, category, pv, poolSize, popularity, popularityWeight, ct));
            else
                popular.AddRange(await PopularCandidatesAsync(userId, category, poolSize / 3, popularity, ct));
        }

        // Hybrid: nudge same-category scores toward drinks the user's matches
        // loved (density-weighted — cfWeight × the strongest matching neighbor's
        // score). Band assignment stays distance-based (attribute geometry is
        // the spine); CF only reorders WITHIN a band via MMR, so a user with no
        // matches sees an unchanged content-only feed (the golden eval relies
        // on this).
        if (cfWeight > 0 && matchLoved.Count > 0)
            scored = scored
                .Select(c => matchLoved.TryGetValue(c.DrinkId, out var ml)
                    ? c with { Score = c.Score + cfWeight * ml.TopNeighborScore }
                    : c)
                .ToList();

        var ranked = scored.OrderBy(c => c.Distance).ThenBy(c => c.DrinkId).ToList();
        var alleyPool = ranked.Take(alleyBand).ToList();
        var stretchPool = ranked.Skip(alleyBand).Take(stretchBand - alleyBand).ToList();
        var farPool = ranked.Skip(stretchBand).ToList();

        // Cold categories back-fill from popularity so sections never render empty.
        alleyPool.AddRange(popular.OrderByDescending(c => c.Popularity).ThenBy(c => c.DrinkId).Take(alleyBand));
        stretchPool.AddRange(popular.OrderByDescending(c => c.Popularity).ThenBy(c => c.DrinkId)
            .Skip(alleyBand).Take(stretchBand - alleyBand));
        farPool.AddRange(popular.Skip(stretchBand));

        // --- Section picks ------------------------------------------------------
        var picked = new HashSet<Guid>();
        var alley = MmrPick(alleyPool, pickSize, mmrLambda, picked);
        var stretch = MmrPick(stretchPool, pickSize, mmrLambda, picked);

        // Wildcard: deterministic per (user, UTC day) — stable feed within a
        // day, fresh exploration tomorrow. Popularity nudges the sample.
        // Small catalogs (pre-bulk-seed) may not HAVE a far band; the section
        // must still render, so the pool falls back band by band toward the
        // near candidates rather than going empty.
        var daySeed = HashCode.Combine(userId, clock.GetUtcNow().UtcDateTime.DayOfYear, clock.GetUtcNow().Year);
        var wildcardPool = farPool.Where(c => !picked.Contains(c.DrinkId)).ToList();
        if (wildcardPool.Count < wildcardCount)
        {
            var seen = wildcardPool.Select(c => c.DrinkId).ToHashSet();
            wildcardPool.AddRange(stretchPool.Concat(alleyPool)
                .Where(c => !picked.Contains(c.DrinkId) && seen.Add(c.DrinkId)));
        }
        wildcardPool = wildcardPool
            .OrderByDescending(c => SeededScore(daySeed, c.DrinkId) * (1 + c.Popularity))
            .ToList();
        var wildcard = new List<Candidate>();
        foreach (var candidate in wildcardPool)
        {
            if (wildcard.Count >= wildcardCount) break;
            if (picked.Add(candidate.DrinkId)) wildcard.Add(candidate);
        }

        // --- Cross-category bridge injection (ADR-027) --------------------------
        if (await settings.GetBoolAsync(BridgeEnabledFlag, true, ct))
        {
            var bridgeCount = await settings.GetIntAsync(BridgeCountFlag, 2, ct);
            var bridgePicks = await BridgeCandidatesAsync(
                userId, categories, profiles, bridgeCount, picked, ct);

            // Bridge picks live in Stretch (adjacent exploration by design);
            // overflow spills into Wildcard. They REPLACE tail picks rather
            // than growing the section.
            foreach (var pick in bridgePicks)
            {
                if (!picked.Add(pick.DrinkId)) continue;
                if (stretch.Count >= pickSize && stretch.Count > 0) stretch.RemoveAt(stretch.Count - 1);
                stretch.Add(pick);
            }
        }

        // 4th section (ADR-013/014): drinks the user's palate matches loved,
        // that the user hasn't rated. No longer a stub — but still empty (and
        // NOT "coming soon") when the user has no computed matches yet.
        var matchesSectionSize = await settings.GetIntAsync(MatchesSectionSizeFlag, 8, ct);
        var matchesSection = await BuildMatchesSectionAsync(matchLoved, matchesSectionSize, picked, ct);

        var sections = new List<FeedSection>
        {
            new(SectionAlley, "Up your alley", alley.Select(c => ToDto(c, SectionAlley, confidence, attributeNames, profiles, matchLoved)).ToList()),
            new(SectionStretch, "Stretch a little", stretch.Select(c => ToDto(c, SectionStretch, confidence, attributeNames, profiles, matchLoved)).ToList()),
            new(SectionWildcard, "Wildcard", wildcard.Select(c => ToDto(c, SectionWildcard, confidence, attributeNames, profiles, matchLoved)).ToList()),
            new(SectionMatches, "Loved by your matches", matchesSection),
        };

        return new FeedResponse(sections, confidence, totalRatings,
            categories.ToList(), profiles.Count == 0);
    }

    // --- Queries ---------------------------------------------------------------

    private async Task<List<Candidate>> NearestCandidatesAsync(
        Guid userId, string category, Vector preference, int poolSize,
        Dictionary<Guid, double> popularity, double popularityWeight, CancellationToken ct)
    {
        var rows = await db.Drinks.AsNoTracking()
            .Where(d => d.Category == category
                && d.Status == EntityStatus.Active
                && d.Visibility == Visibility.Public && d.HiddenAt == null
                && d.CategoryVector != null
                && !db.Ratings.Any(r => r.CreatedByUserId == userId && r.DrinkId == d.Id))
            .OrderBy(d => d.CategoryVector!.CosineDistance(preference))
            .ThenBy(d => d.Id)
            .Take(poolSize)
            .Select(d => new
            {
                d.Id, d.Name, ProducerName = d.Producer.Name, d.Category,
                StyleName = d.Style != null ? d.Style.Name : null, d.Abv,
                Distance = d.CategoryVector!.CosineDistance(preference),
                d.CategoryVector,
            })
            .ToListAsync(ct);

        return rows.Select(r =>
        {
            var pop = popularity.GetValueOrDefault(r.Id);
            return new Candidate(r.Id, r.Name, r.ProducerName, r.Category, r.StyleName,
                r.Abv, r.Distance, r.CategoryVector!.ToArray(), pop, false, null)
            {
                Score = (1 - r.Distance) + popularityWeight * pop,
            };
        }).ToList();
    }

    private async Task<List<Candidate>> PopularCandidatesAsync(
        Guid userId, string category, int take, Dictionary<Guid, double> popularity, CancellationToken ct)
    {
        var rows = await db.Drinks.AsNoTracking()
            .Where(d => d.Category == category
                && d.Status == EntityStatus.Active
                && d.Visibility == Visibility.Public && d.HiddenAt == null
                && d.CategoryVector != null
                && !db.Ratings.Any(r => r.CreatedByUserId == userId && r.DrinkId == d.Id))
            .OrderBy(d => d.Id) // deterministic; popularity sorts below
            .Take(take * 4)
            .Select(d => new
            {
                d.Id, d.Name, ProducerName = d.Producer.Name, d.Category,
                StyleName = d.Style != null ? d.Style.Name : null, d.Abv,
            })
            .ToListAsync(ct);

        return rows
            .Select(r => new Candidate(r.Id, r.Name, r.ProducerName, r.Category, r.StyleName,
                r.Abv, Distance: 1, Vector: null, popularity.GetValueOrDefault(r.Id), false, null)
            {
                Score = popularity.GetValueOrDefault(r.Id),
            })
            .OrderByDescending(c => c.Popularity).ThenBy(c => c.DrinkId)
            .Take(take)
            .ToList();
    }

    private async Task<List<Candidate>> BridgeCandidatesAsync(
        Guid userId, IReadOnlyList<string> categories, List<UserPalateProfile> profiles,
        int perPair, IReadOnlySet<Guid> alreadyPicked, CancellationToken ct)
    {
        var picks = new List<Candidate>();
        foreach (var source in profiles.Where(p => p.BridgeVector is not null && p.RatingsCount > 0))
        {
            foreach (var target in categories.Where(c => c != source.Category))
            {
                var rows = await db.Drinks.AsNoTracking()
                    .Where(d => d.Category == target
                        && d.Status == EntityStatus.Active
                        && d.Visibility == Visibility.Public && d.HiddenAt == null
                        && d.BridgeVector != null
                        && !db.Ratings.Any(r => r.CreatedByUserId == userId && r.DrinkId == d.Id))
                    .OrderBy(d => d.BridgeVector!.CosineDistance(source.BridgeVector!))
                    .ThenBy(d => d.Id)
                    .Take(perPair * 3)
                    .Select(d => new
                    {
                        d.Id, d.Name, ProducerName = d.Producer.Name, d.Category,
                        StyleName = d.Style != null ? d.Style.Name : null, d.Abv,
                        Distance = d.BridgeVector!.CosineDistance(source.BridgeVector!),
                        d.BridgeVector,
                    })
                    .ToListAsync(ct);

                picks.AddRange(rows
                    .Where(r => !alreadyPicked.Contains(r.Id))
                    .Take(perPair)
                    .Select(r => new Candidate(r.Id, r.Name, r.ProducerName, r.Category,
                        r.StyleName, r.Abv, r.Distance, r.BridgeVector!.ToArray(), 0,
                        CrossCategory: true, SourceCategory: source.Category)
                    {
                        Score = 1 - r.Distance,
                    }));
            }
        }
        return picks;
    }

    /// <summary>
    /// "Loved by your matches": drinks the caller's palate matches rated highly
    /// and the caller hasn't tried (ADR-013/014). Ordered by the strongest
    /// matching neighbor first, then by how many matches loved it. Skips drinks
    /// already placed in another section. Every item carries its "because"
    /// (hard product requirement) — here the because IS the social proof.
    /// </summary>
    private async Task<List<RecDto>> BuildMatchesSectionAsync(
        Dictionary<Guid, MatchLovedDrink> matchLoved, int size,
        IReadOnlySet<Guid> picked, CancellationToken ct)
    {
        var ordered = matchLoved.Values
            .Where(m => !picked.Contains(m.DrinkId))
            .OrderByDescending(m => m.TopNeighborScore).ThenByDescending(m => m.MatchCount)
            .ThenBy(m => m.DrinkId)
            .Take(size)
            .ToList();
        if (ordered.Count == 0) return [];

        var ids = ordered.Select(m => m.DrinkId).ToList();
        var drinks = await db.Drinks.AsNoTracking()
            .Where(d => ids.Contains(d.Id))
            .Select(d => new
            {
                d.Id, d.Name, ProducerName = d.Producer.Name, d.Category,
                StyleName = d.Style != null ? d.Style.Name : null, d.Abv,
            })
            .ToListAsync(ct);
        var byId = drinks.ToDictionary(d => d.Id);

        var items = new List<RecDto>();
        foreach (var m in ordered)
        {
            if (!byId.TryGetValue(m.DrinkId, out var d)) continue;
            var others = m.MatchCount - 1;
            var reason = others switch
            {
                <= 0 => $"@{m.SampleHandle}, a strong palate match, rated this highly.",
                1 => $"@{m.SampleHandle} and one other palate match rated this highly.",
                _ => $"@{m.SampleHandle} and {others} other palate matches rated this highly.",
            };
            items.Add(new RecDto(
                d.Id, d.Name, d.ProducerName, d.Category, d.StyleName, d.Abv,
                MatchPct: null, reason, ReasonAttributes: [], CrossCategory: false,
                SourceCategory: null, Tag: "loved_by_matches",
                LovedByMatchCount: m.MatchCount, LovedByMatchHandle: m.SampleHandle));
        }
        return items;
    }

    private async Task<Dictionary<Guid, double>> LoadPopularityAsync(CancellationToken ct)
    {
        // Smoothed count of latest PUBLIC ratings → [0,1). Fine at MVP scale;
        // becomes a materialized rollup when it shows up in a profile trace.
        // Public aggregate → the Sprint 6 moderation filters apply.
        var counts = await db.Ratings.AsNoTracking()
            .Where(r => r.IsLatest && r.Visibility == Visibility.Public
                && r.HiddenAt == null
                && r.CreatedBy.ShadowLimitedAt == null && r.CreatedBy.BannedAt == null)
            .GroupBy(r => r.DrinkId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(ct);
        return counts.ToDictionary(x => x.Key, x => x.Count / (x.Count + 5.0));
    }

    private async Task<Dictionary<string, string[]>> LoadAttributeNamesAsync(CancellationToken ct)
    {
        var defs = await db.AttributeDefinitions.AsNoTracking().ToListAsync(ct);
        return defs.GroupBy(d => d.Category).ToDictionary(
            g => g.Key,
            g =>
            {
                var names = new string[VectorDims.Category];
                foreach (var def in g) names[def.DimIndex] = def.DisplayName;
                return names;
            });
    }

    // --- Selection helpers (pure; exercised directly by the eval suite) ---------

    /// <summary>
    /// MMR-lite: greedy pick maximizing λ·score − (1−λ)·max-similarity-to-picked,
    /// so a section doesn't fill with eight near-identical IPAs. Similarity is
    /// only computed between same-category candidates (their vectors share a
    /// geometry); cross-category pairs get no penalty.
    /// </summary>
    public static List<TCandidate> MmrPickCore<TCandidate>(
        IReadOnlyList<TCandidate> pool,
        int count,
        double lambda,
        Func<TCandidate, double> score,
        Func<TCandidate, TCandidate, double> similarity,
        Func<TCandidate, Guid> id,
        ISet<Guid> alreadyPicked)
    {
        var picks = new List<TCandidate>();
        var remaining = pool.Where(c => !alreadyPicked.Contains(id(c))).ToList();
        while (picks.Count < count && remaining.Count > 0)
        {
            TCandidate? best = default;
            var bestValue = double.MinValue;
            foreach (var candidate in remaining)
            {
                var penalty = picks.Count == 0 ? 0 : picks.Max(p => similarity(candidate, p));
                var value = lambda * score(candidate) - (1 - lambda) * penalty;
                if (value > bestValue)
                {
                    bestValue = value;
                    best = candidate;
                }
            }
            picks.Add(best!);
            remaining.Remove(best!);
            alreadyPicked.Add(id(best!));
        }
        return picks;
    }

    private static List<Candidate> MmrPick(
        IReadOnlyList<Candidate> pool, int count, double lambda, ISet<Guid> alreadyPicked)
        => MmrPickCore(pool, count, lambda,
            c => c.Score,
            (a, b) => a.Category == b.Category && a.Vector is not null && b.Vector is not null
                ? Cosine(a.Vector, b.Vector) : 0,
            c => c.DrinkId,
            alreadyPicked);

    public static double Cosine(float[] a, float[] b)
    {
        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            na += a[i] * a[i];
            nb += b[i] * b[i];
        }
        return na < 1e-12 || nb < 1e-12 ? 0 : dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }

    /// <summary>Deterministic pseudo-random in [0,1) from (seed, id).</summary>
    public static double SeededScore(int seed, Guid id)
    {
        var h = (uint)HashCode.Combine(seed, id);
        return h / (double)uint.MaxValue;
    }

    // --- "Because" (hard product requirement) ------------------------------------

    private RecDto ToDto(
        Candidate c, string section, string confidence,
        Dictionary<string, string[]> attributeNames, List<UserPalateProfile> profiles,
        Dictionary<Guid, MatchLovedDrink> matchLoved)
    {
        var names = attributeNames.GetValueOrDefault(c.Category) ?? [];
        var profile = profiles.FirstOrDefault(p => p.Category == c.Category);
        string reason;
        List<string> reasonAttributes = [];
        string? tag = null;

        if (c.CrossCategory && c.SourceCategory is not null)
        {
            var sourceProfile = profiles.FirstOrDefault(p => p.Category == c.SourceCategory);
            var bridgeNames = BridgeAttributeNames(names);
            var top = TopDims(sourceProfile?.BridgeVector?.ToArray(), c.Vector, bridgeNames, 1);
            reasonAttributes = top;
            var attr = top.Count > 0 ? top[0].ToLowerInvariant() : "the same shape";
            reason = $"Your {c.SourceCategory} ratings run {attr} — this {c.Category} does too.";
            tag = "cross_category";
        }
        else if (section == SectionWildcard)
        {
            var top = c.Vector is null ? [] : TopDims(c.Vector, c.Vector, names, 1);
            reasonAttributes = top;
            reason = top.Count > 0
                ? $"New territory: {top[0].ToLowerInvariant()}-forward, unlike your usual — that's the idea."
                : "New territory, on purpose — tell us if it lands.";
            tag = "new_territory";
        }
        else if (profile?.PreferenceVector is { } pv && c.Vector is not null)
        {
            var top = TopDims(pv.ToArray(), c.Vector, names, 2);
            reasonAttributes = top;
            reason = top.Count switch
            {
                >= 2 => $"Because your {c.Category} ratings lean {top[0].ToLowerInvariant()} and {top[1].ToLowerInvariant()} — this fits that shape.",
                1 => $"Because your {c.Category} ratings lean {top[0].ToLowerInvariant()} — this fits that shape.",
                _ => $"Close to the shape of your {c.Category} ratings.",
            };
        }
        else
        {
            reason = "A well-rated pick while your palate takes shape.";
        }

        int? matchPct = c.Vector is not null && !double.IsNaN(c.Distance) && c.Distance <= 1
            ? (int)Math.Clamp(Math.Round((1 - c.Distance) * 100), 1, 99)
            : null;
        // Cold reads get no hard number — an early guess shouldn't wear a percent.
        if (confidence == "cold" && !c.CrossCategory) matchPct = null;

        // Social proof (ADR-014): if the user's matches also loved this, say so.
        int? lovedCount = null;
        string? lovedHandle = null;
        if (matchLoved.TryGetValue(c.DrinkId, out var ml))
        {
            lovedCount = ml.MatchCount;
            lovedHandle = ml.SampleHandle;
        }

        return new RecDto(c.DrinkId, c.Name, c.ProducerName, c.Category, c.StyleName,
            c.Abv, matchPct, reason, reasonAttributes, c.CrossCategory, c.SourceCategory, tag,
            lovedCount, lovedHandle);
    }

    /// <summary>Top contributing dims: weight[i] × value[i], positive weights only.</summary>
    public static List<string> TopDims(float[]? weights, float[]? values, string[] names, int take)
    {
        if (weights is null || values is null || names.Length == 0)
            return [];
        return Enumerable.Range(0, Math.Min(Math.Min(weights.Length, values.Length), names.Length))
            .Where(i => weights[i] > 0 && names[i] is not null)
            .OrderByDescending(i => weights[i] * values[i])
            .Take(take)
            .Select(i => names[i])
            .ToList();
    }

    /// <summary>Bridge dims 0–5 carry the first six category display names (ADR-009 geometry).</summary>
    private static string[] BridgeAttributeNames(string[] categoryNames)
        => categoryNames.Length >= VectorDims.Bridge ? categoryNames[..VectorDims.Bridge] : categoryNames;

    private static IReadOnlyList<string> ResolveCategories(
        string? filter, List<UserPalateProfile> profiles, List<string> interests)
    {
        if (filter is not null && DrinkCategory.IsValid(filter))
            return [filter];
        var mine = interests.Union(profiles.Select(p => p.Category)).ToList();
        return mine.Count > 0 ? mine : DrinkCategory.All;
    }
}
