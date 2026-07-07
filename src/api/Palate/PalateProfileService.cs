using BarBrain.Api.Data;
using BarBrain.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Pgvector;

namespace BarBrain.Api.Palate;

/// <summary>
/// Computes per-(user, category) palate profiles from the append-only ratings
/// history — the LATEST rating per drink only (ADR-012). Recomputed after
/// every rating write and by the nightly batch; profiles are derived data.
///
/// The math lives in the PURE <see cref="Compute"/> so the golden-set eval
/// suite can assert it without a database. Profiles compute from the FIRST
/// rating; confidence is a TIER (flags), not a floor — the feed decides how
/// much to trust a low-count profile (spec amendment, July 2026).
/// </summary>
public sealed class PalateProfileService(AppDbContext db, TimeProvider clock)
{
    /// <summary>Ratings in a category before the profile is "warming" (flag, Hard Rule 10).</summary>
    public const string WarmRatingsFlag = "palate.warm_confidence_ratings";
    /// <summary>Ratings before full confidence (the spec's old min-5, as a tier).</summary>
    public const string FullRatingsFlag = "palate.full_confidence_ratings";
    public const int DefaultWarmRatings = 3;
    public const int DefaultFullRatings = 5;

    public sealed record RatedVector(float Value, Vector Category, Vector? Bridge);

    public sealed record ComputedPalate(
        Vector? Preference, Vector? Centroid, Vector? Bridge, float Mean, int Count);

    public async Task RecomputeAsync(Guid userId, string category, CancellationToken ct = default)
    {
        var rows = await db.Ratings.AsNoTracking()
            .Where(r => r.CreatedByUserId == userId && r.IsLatest
                && r.Drink.Category == category
                && r.Drink.CategoryVector != null)
            .Select(r => new RatedVector(
                (float)r.Value, r.Drink.CategoryVector!, r.Drink.BridgeVector))
            .ToListAsync(ct);

        var existing = await db.UserPalateProfiles
            .FirstOrDefaultAsync(p => p.UserId == userId && p.Category == category, ct);

        if (rows.Count == 0)
        {
            // Last vectored rating in the category disappeared — so does the profile.
            if (existing is not null)
            {
                db.UserPalateProfiles.Remove(existing);
                await db.SaveChangesAsync(ct);
            }
            return;
        }

        var computed = Compute(rows);
        var profile = existing ?? new UserPalateProfile { UserId = userId, Category = category };
        profile.PreferenceVector = computed.Preference;
        profile.CentroidVector = computed.Centroid;
        profile.BridgeVector = computed.Bridge;
        profile.RatingsCount = computed.Count;
        profile.UserMean = computed.Mean;
        profile.ComputedAt = clock.GetUtcNow();
        if (existing is null)
            db.UserPalateProfiles.Add(profile);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Two rating writes racing the same fresh profile: the loser's
            // insert hits the PK. The winner's row is equivalent (both are
            // recomputed from the same ratings table) — drop ours.
            db.ChangeTracker.Clear();
        }
    }

    public async Task RecomputeAllForUserAsync(Guid userId, CancellationToken ct = default)
    {
        foreach (var category in DrinkCategory.All)
            await RecomputeAsync(userId, category, ct);
    }

    /// <summary>
    /// The palate math, pure and deterministic.
    ///
    /// Preference (drives recs): drink vectors weighted by (rating − user
    /// mean), L2-normalized — points from "what you rate" toward "what you
    /// like"; dims can be negative. A user whose ratings are all identical
    /// carries no contrast, so the weight falls back to (rating − 3.0), and a
    /// user who rated everything exactly 3.0 falls back to uniform weights
    /// (their centroid is the only signal).
    ///
    /// Centroid (drives the radar): rating-weighted average of LIKED drinks'
    /// attributes (rating ≥ max(mean, 3)), absolute 0–1.
    ///
    /// Bridge: the preference computation on the 6 shared dims (ADR-027).
    /// </summary>
    public static ComputedPalate Compute(IReadOnlyList<RatedVector> rows)
    {
        var count = rows.Count;
        var mean = rows.Average(r => r.Value);

        var weights = rows.Select(r => r.Value - mean).ToArray();
        if (weights.All(w => Math.Abs(w) < 1e-4f))
            weights = rows.Select(r => r.Value - 3f).ToArray();
        if (weights.All(w => Math.Abs(w) < 1e-4f))
            weights = Enumerable.Repeat(1f, count).ToArray();

        var preference = WeightedSum(rows.Select(r => r.Category.ToArray()).ToList(), weights, VectorDims.Category);

        var bridgeRows = rows.Where(r => r.Bridge is not null).ToList();
        Vector? bridge = null;
        if (bridgeRows.Count > 0)
        {
            var bridgeWeights = new float[bridgeRows.Count];
            var bi = 0;
            for (var i = 0; i < rows.Count; i++)
                if (rows[i].Bridge is not null)
                    bridgeWeights[bi++] = weights[i];
            bridge = WeightedSum(bridgeRows.Select(r => r.Bridge!.ToArray()).ToList(), bridgeWeights, VectorDims.Bridge);
        }

        // Centroid over liked drinks (all drinks when nothing clears the bar).
        var likeBar = Math.Max(mean, 3f) - 0.01f;
        var liked = rows.Where(r => r.Value >= likeBar).ToList();
        if (liked.Count == 0) liked = rows.ToList();
        var centroid = new float[VectorDims.Category];
        var weightTotal = liked.Sum(r => r.Value);
        foreach (var row in liked)
        {
            var v = row.Category.ToArray();
            for (var i = 0; i < VectorDims.Category; i++)
                centroid[i] += row.Value * v[i];
        }
        for (var i = 0; i < VectorDims.Category; i++)
            centroid[i] = Math.Clamp(centroid[i] / weightTotal, 0f, 1f);

        return new ComputedPalate(preference, new Vector(centroid), bridge, mean, count);
    }

    private static Vector? WeightedSum(List<float[]> vectors, float[] weights, int dims)
    {
        var sum = new float[dims];
        for (var r = 0; r < vectors.Count; r++)
            for (var i = 0; i < dims; i++)
                sum[i] += weights[r] * vectors[r][i];

        var norm = MathF.Sqrt(sum.Sum(x => x * x));
        if (norm < 1e-6f)
            return null; // no direction — the feed falls back to popularity
        for (var i = 0; i < dims; i++)
            sum[i] /= norm;
        return new Vector(sum);
    }
}
