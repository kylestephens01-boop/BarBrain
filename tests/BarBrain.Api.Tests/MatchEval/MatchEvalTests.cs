using BarBrain.Api.Data.Entities;
using BarBrain.Api.Palate;
using Microsoft.EntityFrameworkCore;

namespace BarBrain.Api.Tests.MatchEval;

/// <summary>
/// Gate C2: the matching acceptance gate (ADR-014). Matching is eval-only this
/// sprint — these CI assertions ARE the review (no human feel-review until real
/// users exist). Failures block merge.
/// </summary>
[Collection("match-eval")]
[Trait("Category", "MatchEval")]
public sealed class MatchEvalTests(MatchEvalFixture fixture)
{
    private static double TwinHitRateMin =>
        double.TryParse(Environment.GetEnvironmentVariable("MATCHEVAL_TWIN_HITRATE_MIN"), out var v) ? v : 0.90;

    private void EnsureWorld()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; match eval skipped.");
        Assert.True(fixture.SetupFailure is null, $"Match eval world failed to build:\n{fixture.SetupFailure}");
    }

    [SkippableFact]
    public void Planted_twins_are_found_as_top_match()
    {
        EnsureWorld();
        Assert.NotEmpty(fixture.Twins);

        var hits = fixture.Twins.Count(t =>
            fixture.TopMatchHandle.GetValueOrDefault(t.UserId) == PartnerHandle(t.Handle));
        var rate = hits / (double)fixture.Twins.Count;

        Assert.True(rate >= TwinHitRateMin,
            $"Twin top-1 hit-rate = {rate:0.00} ({hits}/{fixture.Twins.Count}), needs ≥ {TwinHitRateMin:0.00}. " +
            string.Join(", ", fixture.Twins.Select(t =>
                $"{t.Handle}→{fixture.TopMatchHandle.GetValueOrDefault(t.UserId) ?? "∅"}")));
    }

    [Fact]
    public void Density_weighting_math_lets_attribute_similarity_dominate_when_sparse()
    {
        // Pure math — no world needed, so this runs everywhere (incl. no Docker).
        var p = new MatchService.BlendParams(
            MatchService.DefaultMinCoRated, MatchService.DefaultShrinkage, MatchService.DefaultDensityK);
        // A MODERATE attribute cosine (~0.4), so a strong/weak CF signal can move
        // the blend visibly up or down — the whole point of density weighting.
        var prefA = new float[] { 1, 0, 0, 0, 0, 0, 0, 0 };
        var prefB = new float[] { 0.4f, 0.9f, 0, 0, 0, 0, 0, 0 };
        var attr = Math.Clamp(RecommendationService.Cosine(prefA, prefB), 0, 1);

        // No co-ratings → the CF term is weightless: blended IS attribute similarity.
        var sparse = MatchService.ComputeEdge(prefA, prefB, [], p);
        Assert.Null(sparse.CoRatingAgreement);
        Assert.Equal(attr, sparse.BlendedScore, 9);

        // Below the floor (2 < 3) still counts as no signal.
        var belowFloor = MatchService.ComputeEdge(prefA, prefB, [(5, 5), (1, 1)], p);
        Assert.Null(belowFloor.CoRatingAgreement);
        Assert.Equal(attr, belowFloor.BlendedScore, 9);

        // Dense + strong agreement → CF pulls the score UP from attribute-only.
        // (Pearson 1.0 shrinks to 20/(20+5)=0.8 at n=20 — significance weighting.)
        var agree = Enumerable.Range(0, 20).Select(i => ((double)(i % 5 + 1), (double)(i % 5 + 1))).ToList();
        var dense = MatchService.ComputeEdge(prefA, prefB, agree, p);
        Assert.NotNull(dense.CoRatingAgreement);
        Assert.True(dense.CoRatingAgreement > 0.75, $"expected strong (shrunk) agreement, got {dense.CoRatingAgreement}");
        Assert.True(dense.BlendedScore > attr + 0.05,
            $"dense blended {dense.BlendedScore:0.000} should exceed attribute-only {attr:0.000}");

        // Dense + DISAGREEMENT → CF pulls the score DOWN from attribute-only.
        var disagree = Enumerable.Range(0, 20).Select(i => ((double)(i % 5 + 1), (double)(5 - i % 5))).ToList();
        var conflict = MatchService.ComputeEdge(prefA, prefB, disagree, p);
        Assert.True(conflict.BlendedScore < attr,
            $"conflicting co-ratings {conflict.BlendedScore:0.000} should fall below attribute-only {attr:0.000}");
    }

    [SkippableFact]
    public async Task Density_weighting_holds_end_to_end_for_seeded_pairs()
    {
        EnsureWorld();
        await using var db = Integration.PostgresFixture.CreateContext(fixture.ConnectionString);

        // Sparse pair: shares the palate, rates disjoint drinks → attribute-only.
        var sparse = await EdgeAsync(db, $"twin_{MatchEvalFixture.SparsePairIndex}a", $"twin_{MatchEvalFixture.SparsePairIndex}b");
        Assert.True(sparse.CoRatedCount < MatchService.DefaultMinCoRated,
            $"expected the sparse pair to share few drinks, got {sparse.CoRatedCount}");
        Assert.Null(sparse.CoRatingAgreement);
        Assert.Equal(sparse.AttributeSimilarity, sparse.BlendedScore, 6);

        // Dense pair 0: heavy overlap → CF is engaged (agreement present, count high).
        var dense = await EdgeAsync(db, "twin_0a", "twin_0b");
        Assert.True(dense.CoRatedCount >= MatchService.DefaultMinCoRated,
            $"expected the dense pair to co-rate many drinks, got {dense.CoRatedCount}");
        Assert.NotNull(dense.CoRatingAgreement);
    }

    [SkippableFact]
    public async Task Hide_me_removes_a_user_from_matches_in_both_directions()
    {
        EnsureWorld();
        var a = fixture.Twins.First(t => t.Handle == "twin_0a");
        var b = fixture.Twins.First(t => t.Handle == "twin_0b");

        await using var db = Integration.PostgresFixture.CreateContext(fixture.ConnectionString);
        var svc = fixture.MatchService(db);

        // Baseline: they see each other.
        Assert.Contains((await svc.GetMatchesAsync(a.UserId, 25)).Matches, m => m.Handle == "twin_0b");

        try
        {
            await db.Users.Where(u => u.Id == b.UserId)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.HideFromMatches, true));

            // Direction 1: the hidden user disappears from the partner's list.
            var partnerView = await svc.GetMatchesAsync(a.UserId, 25);
            Assert.DoesNotContain(partnerView.Matches, m => m.Handle == "twin_0b");

            // Direction 2: the hidden user themselves sees no one (and is flagged).
            var selfView = await svc.GetMatchesAsync(b.UserId, 25);
            Assert.True(selfView.Hidden);
            Assert.Empty(selfView.Matches);
        }
        finally
        {
            await db.Users.Where(u => u.Id == b.UserId)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.HideFromMatches, false));
        }
    }

    [SkippableFact]
    public async Task Match_percent_display_flag_toggles_the_percentage()
    {
        EnsureWorld();
        // The sparse pair matches strongly on attributes but has low confidence
        // (0 co-rated), which is exactly where the two display modes diverge.
        var a = fixture.Twins.First(t => t.Handle == $"twin_{MatchEvalFixture.SparsePairIndex}a");
        var partner = $"twin_{MatchEvalFixture.SparsePairIndex}b";

        await using var db = Integration.PostgresFixture.CreateContext(fixture.ConnectionString);
        var settings = fixture.Settings(db);
        var svc = fixture.MatchService(db);

        try
        {
            await settings.SetAsync(MatchService.DisplayModeFlag, MatchService.DisplayEager);
            var eager = (await svc.GetMatchesAsync(a.UserId, 25)).Matches.First(m => m.Handle == partner);
            Assert.Equal("low", eager.Confidence);
            Assert.NotNull(eager.MatchPct);
            Assert.True(eager.EarlyEstimate, "eager mode should label a low-confidence % as an early estimate");

            await settings.SetAsync(MatchService.DisplayModeFlag, MatchService.DisplayConservative);
            var conservative = (await svc.GetMatchesAsync(a.UserId, 25)).Matches.First(m => m.Handle == partner);
            Assert.Null(conservative.MatchPct);
            Assert.False(conservative.EarlyEstimate);
        }
        finally
        {
            await settings.SetAsync(MatchService.DisplayModeFlag, MatchService.DisplayEager);
        }
    }

    [SkippableFact]
    public void Sparsity_simulation_is_reported_for_the_founder()
    {
        EnsureWorld();
        // The report artifact is the deliverable ("what will launch month feel
        // like"); here we just assert it ran and produced sane fractions.
        Assert.Contains(fixture.Sparsity, s => s.Users == 50);
        Assert.Contains(fixture.Sparsity, s => s.Users == 500);
        Assert.All(fixture.Sparsity, s =>
        {
            Assert.InRange(s.MedFraction, 0.0, 1.0);
            Assert.InRange(s.AnyFraction, 0.0, 1.0);
        });
    }

    private async Task<UserMatchNeighbor> EdgeAsync(
        Microsoft.EntityFrameworkCore.DbContext db, string fromHandle, string toHandle)
    {
        var context = (Api.Data.AppDbContext)db;
        var from = fixture.Twins.First(t => t.Handle == fromHandle).UserId;
        var to = fixture.Twins.First(t => t.Handle == toHandle).UserId;
        return await context.UserMatchNeighbors.AsNoTracking()
            .SingleAsync(m => m.UserId == from && m.NeighborUserId == to && m.Category == MatchEvalFixture.Category);
    }

    private static string PartnerHandle(string handle)
        => handle.EndsWith('a') ? handle[..^1] + "b" : handle[..^1] + "a";
}
