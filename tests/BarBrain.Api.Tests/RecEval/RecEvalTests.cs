using BarBrain.Api.Palate;

namespace BarBrain.Api.Tests.RecEval;

/// <summary>
/// Gate C1: the golden-set assertions. Thresholds are config (env-overridable
/// per the spec) and FAILURES BLOCK MERGE — rec quality is invisible to
/// screenshot review, so this suite is the review (ADR-027).
/// </summary>
[Collection("rec-eval")]
[Trait("Category", "RecEval")]
public sealed class RecEvalTests(RecEvalFixture fixture)
{
    // Thresholds in config: env vars override the spec defaults.
    private static double PrecisionMin =>
        double.TryParse(Environment.GetEnvironmentVariable("RECEVAL_PRECISION_MIN"), out var v) ? v : 0.70;
    private static double LooHitRateMin =>
        double.TryParse(Environment.GetEnvironmentVariable("RECEVAL_LOO_HITRATE_MIN"), out var v) ? v : 0.25;

    private void EnsureWorld()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; rec eval skipped.");
        Assert.True(fixture.SetupFailure is null, $"Eval world failed to build:\n{fixture.SetupFailure}");
    }

    [SkippableFact]
    public void Attribute_alignment_precision_at_10_meets_threshold()
    {
        EnsureWorld();
        var dense = fixture.Results.Where(r => r.Persona.Dense).ToList();
        Assert.NotEmpty(dense);

        var mean = dense.Average(r => r.Precision10);
        Assert.True(mean >= PrecisionMin,
            $"Mean precision@10 over dense personas = {mean:0.00}, needs ≥ {PrecisionMin:0.00}. " +
            $"Per persona: {string.Join(", ", dense.Select(r => $"{r.Persona.Handle}={r.Precision10:0.00}"))}");

        // No individual dense persona may fall off a cliff even if the mean holds.
        Assert.All(dense, r => Assert.True(r.Precision10 >= 0.5,
            $"{r.Persona.Handle} precision@10 = {r.Precision10:0.00} < 0.50"));
    }

    [SkippableFact]
    public void Leave_one_out_hit_rate_meets_threshold()
    {
        EnsureWorld();
        var withLoo = fixture.Results.Where(r => r.LooHit is not null).ToList();
        Assert.NotEmpty(withLoo);
        var hitRate = withLoo.Count(r => r.LooHit == true) / (double)withLoo.Count;
        Assert.True(hitRate >= LooHitRateMin,
            $"LOO hit-rate@10 = {hitRate:0.00}, needs ≥ {LooHitRateMin:0.00} " +
            $"({string.Join(", ", withLoo.Select(r => $"{r.Persona.Handle}={(r.LooHit == true ? "hit" : "miss")}"))})");
    }

    [SkippableFact]
    public void Wildcard_is_farther_than_alley_for_every_profiled_persona()
    {
        EnsureWorld();
        var comparable = fixture.Results
            .Where(r => !double.IsNaN(r.AlleyMeanDistance) && !double.IsNaN(r.WildcardMeanDistance))
            .ToList();
        Assert.NotEmpty(comparable);
        Assert.All(comparable, r => Assert.True(
            r.WildcardMeanDistance > r.AlleyMeanDistance,
            $"{r.Persona.Handle}: wildcard {r.WildcardMeanDistance:0.000} ≤ alley {r.AlleyMeanDistance:0.000} — section integrity broken"));
    }

    [SkippableFact]
    public void Feeds_are_deterministic_for_a_fixed_clock()
    {
        EnsureWorld();
        Assert.All(fixture.Results, r => Assert.True(r.Deterministic,
            $"{r.Persona.Handle}: two builds with identical inputs produced different feeds"));
    }

    [SkippableFact]
    public void Golden_persona_top_pick_matches_independent_brute_force()
    {
        EnsureWorld();
        Assert.NotNull(fixture.GoldenPersona);
        Assert.NotNull(fixture.GoldenExpectedTopPick);

        var alley = fixture.GoldenPersona!.Feed.Sections
            .First(s => s.Key == RecommendationService.SectionAlley).Items;
        Assert.NotEmpty(alley);
        Assert.Equal(fixture.GoldenExpectedTopPick!.Value, alley[0].DrinkId);
    }

    [SkippableFact]
    public void Cross_category_bridge_surfaces_smoky_beer_for_the_whiskey_only_peat_lover()
    {
        EnsureWorld();
        Assert.NotNull(fixture.BridgePersona);
        var feed = fixture.BridgePersona!.Feed;

        // The moat mechanic (ADR-027): a whiskey-only smoky palate must reach
        // into beer via the 6-dim bridge, visibly tagged.
        var crossBeer = feed.Sections.SelectMany(s => s.Items)
            .Where(i => i.CrossCategory && i.Category == "beer")
            .ToList();
        Assert.True(crossBeer.Count > 0,
            "No cross-category beer recs for the peat lover — the bridge is not functioning.");

        var byId = fixture.Catalog.ToDictionary(d => d.Id);
        var catalogBeerSmoke = fixture.Catalog
            .Where(d => d.Category == "beer")
            .Average(d => d.Vector[PeatLoverPersona.SmokeDim]);
        var pickSmoke = crossBeer.Average(i => byId[i.DrinkId].Vector[PeatLoverPersona.SmokeDim]);

        Assert.True(pickSmoke > catalogBeerSmoke + 0.15,
            $"Bridge beer picks average smoke {pickSmoke:0.00} vs catalog {catalogBeerSmoke:0.00} — " +
            "the bridge surfaced beer, but not SMOKY beer.");
        Assert.Contains(crossBeer, i => byId[i.DrinkId].Vector[PeatLoverPersona.SmokeDim] >= 0.6f);

        // And the "because" names the source palate.
        Assert.All(crossBeer, i => Assert.Contains("whiskey", i.Reason, StringComparison.OrdinalIgnoreCase));
        Assert.All(crossBeer, i => Assert.Equal("whiskey", i.SourceCategory));
    }

    [SkippableFact]
    public void Every_recommendation_carries_a_because()
    {
        EnsureWorld();
        // Hard product requirement (ADR-013/027) — not optional polish.
        Assert.All(fixture.Results.SelectMany(r => r.Feed.Sections).SelectMany(s => s.Items),
            item => Assert.False(string.IsNullOrWhiteSpace(item.Reason),
                $"Rec '{item.Name}' shipped without a reason"));
    }

    [SkippableFact]
    public void Feed_shape_adapts_to_confidence()
    {
        EnsureWorld();
        var cold = fixture.Results.Single(r => r.Persona.Handle == Personas.ColdHandle);
        var full = fixture.Results.First(r => r.Persona.Dense);

        Assert.Equal("cold", cold.Feed.Confidence);
        Assert.Equal("full", full.Feed.Confidence);

        RecCount(cold, RecommendationService.SectionAlley, out var coldAlley);
        RecCount(full, RecommendationService.SectionAlley, out var fullAlley);
        RecCount(cold, RecommendationService.SectionWildcard, out var coldWild);
        RecCount(full, RecommendationService.SectionWildcard, out var fullWild);

        Assert.True(coldAlley < fullAlley,
            $"Cold alley ({coldAlley}) should be smaller than full alley ({fullAlley})");
        Assert.True(coldWild > fullWild,
            $"Cold wildcard ({coldWild}) should exceed full wildcard ({fullWild})");

        // A 2-rating read should not wear a hard match number (same-category recs).
        Assert.All(cold.Feed.Sections.SelectMany(s => s.Items).Where(i => !i.CrossCategory),
            i => Assert.Null(i.MatchPct));
    }

    [SkippableFact]
    public void Matches_slot_exists_but_is_deferred_to_sprint_4()
    {
        EnsureWorld();
        var slot = fixture.Results[0].Feed.Sections
            .Single(s => s.Key == RecommendationService.SectionMatches);
        Assert.True(slot.ComingSoon);
        Assert.Empty(slot.Items);
    }

    private static void RecCount(RecEvalFixture.PersonaResult result, string section, out int count)
        => count = result.Feed.Sections.First(s => s.Key == section).Items.Count;
}
