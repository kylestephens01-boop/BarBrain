using BarBrain.Api.Palate;

namespace BarBrain.Api.Tests.RecEval;

/// <summary>
/// A synthetic persona: a KNOWN ground-truth preference direction plus a
/// deterministic noisy-rating generator. The engine never sees the preference
/// vector — only the ratings — so recovering it is the thing under test.
/// </summary>
public record Persona(
    string Handle,
    string Category,
    float[] Preference,
    bool Dense,
    int Seed,
    float Noise,
    string[] InterestCategories)
{
    /// <summary>Half-step ratings over the persona's own category.</summary>
    public virtual List<(Guid DrinkId, decimal Value)> GenerateRatings(
        IReadOnlyList<RecEvalFixture.SynDrink> catalog)
    {
        var mine = catalog.Where(d => d.Category == Category)
            .OrderByDescending(d => RecommendationService.Cosine(Preference, d.Vector))
            .ToList();
        var affinities = mine.ToDictionary(d => d.Id,
            d => RecommendationService.Cosine(Preference, d.Vector));
        var min = affinities.Values.Min();
        var max = affinities.Values.Max();

        // Loved + hated + a mid spread: profile math needs contrast.
        var chosen = Dense
            ? mine.Take(12)
                .Concat(mine.Skip(mine.Count / 3).Where((_, i) => i % 3 == 0).Take(16))
                .Concat(mine.TakeLast(12))
                .ToList()
            : mine.Take(4).Concat(mine.TakeLast(4)).ToList();

        var rng = new Random(Seed);
        var ratings = new List<(Guid, decimal)>();
        foreach (var drink in chosen.DistinctBy(d => d.Id))
        {
            var normalized = (affinities[drink.Id] - min) / Math.Max(1e-9, max - min);
            var value = 1 + 4 * normalized + Noise * Gaussian(rng);
            ratings.Add((drink.Id, RoundHalf(value)));
        }
        return ratings;
    }

    protected static decimal RoundHalf(double value)
        => Math.Clamp(Math.Round((decimal)value * 2, MidpointRounding.AwayFromZero) / 2, 1.0m, 5.0m);

    private static double Gaussian(Random rng)
    {
        var u1 = 1.0 - rng.NextDouble();
        var u2 = rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
    }
}

/// <summary>
/// The moat persona (ADR-027): rates ONLY whiskey, and only on the smoke dim
/// (bridge index 3) — loves peat, pans the un-smoky. If the bridge works,
/// smoky BEER shows up in their feed.
/// </summary>
public sealed record PeatLoverPersona() : Persona(
    Personas.BridgeHandle, "whiskey", [0.3f, 0.4f, 0.5f, 1f, 0.1f, 0.1f, 0.3f, 0.3f],
    Dense: false, Seed: 41, Noise: 0f, InterestCategories: ["beer", "whiskey"])
{
    public const int SmokeDim = 3;

    public override List<(Guid DrinkId, decimal Value)> GenerateRatings(
        IReadOnlyList<RecEvalFixture.SynDrink> catalog)
        => catalog.Where(d => d.Category == "whiskey")
            .Where(d => d.Vector[SmokeDim] is >= 0.7f or <= 0.2f)
            .OrderBy(d => d.Id)
            .Take(24)
            .Select(d => (d.Id, d.Vector[SmokeDim] >= 0.7f ? 5.0m : 1.5m))
            .ToList();
}

/// <summary>
/// Noiseless golden persona: exact preference direction, clean extremes. The
/// engine's #1 Alley pick must equal an independent brute-force argmax.
/// </summary>
public sealed record GoldenHopheadPersona() : Persona(
    Personas.GoldenHandle, "beer", [0f, 0f, 0f, 0f, 0f, 0f, 1f, 0f],
    Dense: false, Seed: 42, Noise: 0f, InterestCategories: ["beer"])
{
    public override List<(Guid DrinkId, decimal Value)> GenerateRatings(
        IReadOnlyList<RecEvalFixture.SynDrink> catalog)
    {
        var mine = catalog.Where(d => d.Category == "beer")
            .OrderByDescending(d => RecommendationService.Cosine(Preference, d.Vector))
            .ToList();
        return mine.Take(10).Select(d => (d.Id, 5.0m))
            .Concat(mine.TakeLast(10).Select(d => (d.Id, 1.0m)))
            .ToList();
    }
}

/// <summary>A 2-rating cold-start persona: the confidence-adaptive shape check.</summary>
public sealed record ColdCuriousPersona() : Persona(
    Personas.ColdHandle, "beer", [0.2f, 0.3f, 0.3f, 0.1f, 0.5f, 0.3f, 0.6f, 0.3f],
    Dense: false, Seed: 43, Noise: 0f, InterestCategories: ["beer"])
{
    public override List<(Guid DrinkId, decimal Value)> GenerateRatings(
        IReadOnlyList<RecEvalFixture.SynDrink> catalog)
        => catalog.Where(d => d.Category == "beer")
            .OrderByDescending(d => RecommendationService.Cosine(Preference, d.Vector))
            .Take(2)
            .Select(d => (d.Id, 4.5m))
            .ToList();
}

public static class Personas
{
    public const string BridgeHandle = "eval_peat_lover";
    public const string GoldenHandle = "eval_golden_hophead";
    public const string ColdHandle = "eval_cold_curious";

    // Beer dims:    [sweet, bitter, body, smoke, fruit, acid, hops, malt]
    // Whiskey dims: [sweet, tannin, body, smoke, fruit, acid, spice, grain]
    // Wine dims:    [sweet, tannin, body, smoke?, fruit, acid, ...]
    public static readonly IReadOnlyList<Persona> All =
    [
        new("eval_hophead", "beer", [0.1f, 0.9f, 0.4f, 0.1f, 0.5f, 0.2f, 1f, 0.2f], true, 1, 0.25f, ["beer"]),
        new("eval_malt_sweet", "beer", [0.9f, 0.1f, 0.7f, 0.2f, 0.3f, 0.1f, 0.1f, 1f], true, 2, 0.25f, ["beer"]),
        new("eval_roasty_stout", "beer", [0.5f, 0.4f, 0.9f, 1f, 0.1f, 0.1f, 0.2f, 0.8f], true, 3, 0.25f, ["beer"]),
        new("eval_sour_fan", "beer", [0.4f, 0.1f, 0.3f, 0.05f, 0.8f, 1f, 0.1f, 0.1f], false, 4, 0.25f, ["beer"]),
        new("eval_crisp_lager", "beer", [0.2f, 0.3f, 0.15f, 0.05f, 0.2f, 0.2f, 0.3f, 0.4f], false, 5, 0.25f, ["beer"]),
        new("eval_oaky_bourbon", "whiskey", [0.8f, 0.9f, 0.8f, 0.3f, 0.3f, 0.1f, 0.5f, 0.6f], true, 6, 0.25f, ["whiskey"]),
        new("eval_rye_spice", "whiskey", [0.3f, 0.6f, 0.5f, 0.2f, 0.4f, 0.3f, 0.9f, 0.4f], false, 7, 0.25f, ["whiskey"]),
        new PeatLoverPersona(),
        new("eval_dry_red", "wine", [0.1f, 1f, 0.8f, 0.2f, 0.4f, 0.4f, 0.3f, 0.6f], true, 8, 0.25f, ["wine"]),
        new("eval_sweet_white", "wine", [1f, 0.05f, 0.4f, 0f, 0.8f, 0.3f, 0.2f, 0.1f], true, 9, 0.25f, ["wine"]),
        new("eval_tart_fruit", "wine", [0.4f, 0.1f, 0.3f, 0.1f, 0.9f, 1f, 0.2f, 0.1f], false, 10, 0.25f, ["wine"]),
        new("eval_big_tannin", "wine", [0.2f, 0.95f, 0.9f, 0.3f, 0.3f, 0.3f, 0.4f, 0.5f], false, 11, 0.25f, ["wine"]),
        new GoldenHopheadPersona(),
        new ColdCuriousPersona(),
    ];
}
