using System.Text;
using BarBrain.Api.Data;
using BarBrain.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Pgvector;

namespace BarBrain.Api.Palate;

/// <summary>
/// Live-catalog rec-quality eval (Sprint 7, founder-scoped 2026-07-10). Runs
/// the REAL profile + recommendation pipeline for synthetic golden-set
/// personas against the LIVE catalog and prints a single comparable
/// Precision@10 number.
///
/// READ-ONLY BY CONSTRUCTION: every synthetic row (persona users, interests,
/// ratings, recomputed profiles) is written inside one database transaction
/// that is ALWAYS rolled back — there is no commit path. That constraint is
/// the whole reason the CI fixture harness can't simply be pointed at the
/// VPS (Sprint 5 design note).
///
/// Metric parity with the fixture (tests/RecEval): personas are the same
/// dominant-dimension archetypes; Precision@10 is the fraction of the top-10
/// Up-Your-Alley recs inside the persona's true top-quartile by cosine to
/// the ground-truth preference. One deliberate divergence: the live quartile
/// is computed over UNRATED eligible drinks (the fixture's catalog is big
/// enough not to care; small live categories would be unfairly penalized for
/// drinks the persona itself consumed). Fixture and live numbers are
/// compared, not equated — Gate C1 fixture baseline was 0.71.
/// </summary>
public sealed class LiveRecEvalService(
    AppDbContext db,
    PalateProfileService profiles,
    RecommendationService recs,
    TimeProvider clock,
    ILogger<LiveRecEvalService> logger)
{
    /// <summary>Categories need at least this many vectorized drinks to evaluate.</summary>
    public const int MinDrinksPerCategory = 20;

    private const int LovedRatings = 5;
    private const int MidRatings = 5;
    private const int LowRatings = 5;

    private sealed record EligibleDrink(Guid Id, string Category, float[] Vector);
    private sealed record PersonaScore(string Handle, string Category, double Precision10, int AlleyCount);

    public async Task<int> RunAsync(string? outPath = null, CancellationToken ct = default)
    {
        var eligible = await db.Drinks.AsNoTracking()
            .Where(d => d.Status == EntityStatus.Active
                        && d.Visibility == Visibility.Public
                        && d.HiddenAt == null
                        && d.CategoryVector != null)
            .Select(d => new { d.Id, d.Category, d.CategoryVector })
            .ToListAsync(ct);

        var byCategory = eligible
            .Select(d => new EligibleDrink(d.Id, d.Category, d.CategoryVector!.ToArray()))
            .GroupBy(d => d.Category)
            .ToDictionary(g => g.Key, g => g.ToList());

        var report = new StringBuilder();
        report.AppendLine($"# Live-catalog rec eval — {clock.GetUtcNow():yyyy-MM-dd HH:mm} UTC");
        report.AppendLine($"Eligible drinks: {eligible.Count} " +
            $"({string.Join(", ", byCategory.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key} {kv.Value.Count}"))})");

        var scores = new List<PersonaScore>();

        // The rollback-only transaction. The retrying execution strategy is
        // active on this context, so the transaction lives inside ExecuteAsync;
        // there is deliberately NO commit call anywhere in this block.
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            try
            {
                foreach (var (category, drinks) in byCategory.OrderBy(kv => kv.Key))
                {
                    if (drinks.Count < MinDrinksPerCategory)
                    {
                        report.AppendLine(
                            $"- {category}: SKIPPED ({drinks.Count} drinks < {MinDrinksPerCategory} minimum)");
                        continue;
                    }

                    for (var dim = 0; dim < VectorDims.Category; dim++)
                        scores.Add(await RunPersonaAsync(category, dim, drinks, ct));
                }
            }
            finally
            {
                // Synthetic rows never survive — success or failure.
                await tx.RollbackAsync(CancellationToken.None);
            }
        });

        if (scores.Count == 0)
        {
            report.AppendLine("No category met the minimum catalog size; no number produced.");
            Console.WriteLine(report.ToString());
            return 2;
        }

        foreach (var group in scores.GroupBy(s => s.Category).OrderBy(g => g.Key))
            report.AppendLine($"- {group.Key}: Precision@10 = {group.Average(s => s.Precision10):F2} " +
                $"({group.Count()} personas)");

        var overall = scores.Average(s => s.Precision10);
        report.AppendLine();
        report.AppendLine($"Live Precision@10: {overall:F2}");
        report.AppendLine("(Gate C1 fixture baseline: 0.71 — different catalog, compare the trend, don't equate.)");

        Console.WriteLine(report.ToString());
        if (outPath is not null)
            await File.WriteAllTextAsync(outPath, report.ToString(), ct);

        logger.LogInformation("Live rec eval finished: {Personas} personas, Precision@10 {Score:F2} (rolled back)",
            scores.Count, overall);
        return 0;
    }

    private async Task<PersonaScore> RunPersonaAsync(
        string category, int dim, List<EligibleDrink> drinks, CancellationToken ct)
    {
        // Same archetype construction as the fixture: one dominant dimension,
        // the rest at a low floor. Noiseless → deterministic.
        var preference = Enumerable.Repeat(0.2f, VectorDims.Category).ToArray();
        preference[dim] = 0.95f;

        var handle = $"eval_{category}_d{dim}";
        var user = new User { UserName = handle, Email = $"{handle}@eval.invalid" };
        db.Users.Add(user);
        db.UserCategoryInterests.Add(new UserCategoryInterest { UserId = user.Id, Category = category });

        // Deterministic rating set: love the closest drinks, shrug at the
        // middle, dislike the farthest — the shape the profile learner expects.
        var ranked = drinks
            .OrderByDescending(d => RecommendationService.Cosine(preference, d.Vector))
            .ThenBy(d => d.Id)
            .ToList();
        var now = clock.GetUtcNow();
        var rated = new HashSet<Guid>();

        void Rate(IEnumerable<EligibleDrink> picks, decimal value)
        {
            foreach (var pick in picks)
            {
                if (!rated.Add(pick.Id)) continue;
                db.Ratings.Add(new Rating
                {
                    CreatedByUserId = user.Id,
                    DrinkId = pick.Id,
                    Value = value,
                    Visibility = Visibility.Public,
                    LocationContext = LocationContext.Untagged,
                    IsLatest = true,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
            }
        }

        Rate(ranked.Take(LovedRatings), 5.0m);
        Rate(ranked.Skip(ranked.Count / 2).Take(MidRatings), 3.0m);
        Rate(ranked.TakeLast(LowRatings), 1.5m);
        await db.SaveChangesAsync(ct);

        // The REAL pipeline: profile recompute, then the sectioned feed.
        await profiles.RecomputeAllForUserAsync(user.Id, ct);
        var feed = await recs.BuildFeedAsync(user.Id, null, ct);

        // Ground truth over what the engine could actually recommend.
        var truth = ranked.Where(d => !rated.Contains(d.Id)).ToList();
        var topQuartile = truth.Take(truth.Count / 4).Select(d => d.Id).ToHashSet();

        var alley = feed.Sections.First(s => s.Key == RecommendationService.SectionAlley)
            .Items.Where(i => i.Category == category).Take(10).ToList();

        var precision = alley.Count == 0
            ? 0
            : alley.Count(i => topQuartile.Contains(i.DrinkId)) / (double)alley.Count;

        return new PersonaScore(handle, category, precision, alley.Count);
    }
}
