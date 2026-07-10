using BarBrain.Api.Data;
using BarBrain.Api.Data.Entities;
using BarBrain.Api.Palate;
using BarBrain.Api.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Pgvector;
using Testcontainers.PostgreSql;

namespace BarBrain.Api.Tests.RecEval;

/// <summary>
/// The rec-quality golden set (Gate C1, ADR-027): a SYNTHETIC catalog with
/// known attribute vectors + fixed personas with known preference vectors,
/// run through the REAL profile computation and the REAL feed engine against
/// a real Postgres+pgvector. Verifiable regardless of seed state — rec
/// quality is invisible to screenshot review, so these numbers block merge.
///
/// All the heavy lifting happens ONCE here; the tests assert on the computed
/// results. DisposeAsync always writes TestResults/rec-eval-report.md (the CI
/// artifact), pass or fail.
/// </summary>
public sealed class RecEvalFixture : IAsyncLifetime
{
    // Deterministic world (the eval MUST be reproducible).
    public const int CatalogSeed = 20260707;
    public static readonly DateTimeOffset FixedNow = new(2026, 7, 7, 12, 0, 0, TimeSpan.Zero);
    public const int RandomDrinksPerCategory = 110;

    public bool DockerAvailable { get; private set; }
    public string? SetupFailure { get; private set; }

    private PostgreSqlContainer? _container;
    private MemoryCache? _cache;
    public string ConnectionString { get; private set; } = "";

    public sealed record SynDrink(Guid Id, string Name, string Category, float[] Vector);
    public sealed record PersonaResult(
        Persona Persona,
        Guid UserId,
        float[]? ProfilePreference,
        Shared.Contracts.FeedResponse Feed,
        double Precision10,
        double AlleyMeanDistance,
        double WildcardMeanDistance,
        bool? LooHit,
        bool Deterministic);

    public List<SynDrink> Catalog { get; } = [];
    public List<PersonaResult> Results { get; } = [];
    public PersonaResult? BridgePersona { get; private set; }
    public PersonaResult? GoldenPersona { get; private set; }
    public Guid? GoldenExpectedTopPick { get; private set; }

    public async Task InitializeAsync()
    {
        try
        {
            _container = new PostgreSqlBuilder("pgvector/pgvector:pg16")
                .WithDatabase("barbrain_receval")
                .WithUsername("barbrain")
                .WithPassword("barbrain")
                .Build();
            await _container.StartAsync();
            ConnectionString = _container.GetConnectionString();
            DockerAvailable = true;
        }
        catch
        {
            DockerAvailable = false;
            return; // tests skip
        }

        try
        {
            await using (var db = Integration.PostgresFixture.CreateContext(ConnectionString))
                await db.Database.MigrateAsync();

            await SeedAttributeDefinitionsAsync();
            await SeedSyntheticCatalogAsync();
            await RunPersonasAsync();
        }
        catch (Exception ex)
        {
            // Surface the real failure through the tests instead of an opaque
            // fixture crash.
            SetupFailure = ex.ToString();
        }
    }

    public async Task DisposeAsync()
    {
        try
        {
            if (DockerAvailable)
                RecEvalReport.Write(this);
        }
        finally
        {
            _cache?.Dispose();
            if (_container is not null)
                await _container.DisposeAsync();
        }
    }

    private AppDbContext Db() => Integration.PostgresFixture.CreateContext(ConnectionString);

    private (AppDbContext Db, PalateProfileService Profiles, RecommendationService Recs) Services()
    {
        var db = Db();
        _cache ??= new MemoryCache(new MemoryCacheOptions());
        var settings = new SettingsService(db, _cache);
        var clock = new FixedTimeProvider(FixedNow);
        var matchService = new MatchService(db, settings);
        return (db, new PalateProfileService(db, clock),
            new RecommendationService(db, settings, clock, matchService));
    }

    // --- World building ---------------------------------------------------------

    private async Task SeedAttributeDefinitionsAsync()
    {
        // Real vocabulary via the real importer (same geometry as production).
        await using var db = Db();
        _cache ??= new MemoryCache(new MemoryCacheOptions());
        var settings = new SettingsService(db, _cache);
        var vectors = new Api.Catalog.AttributeVectorService(db, settings);
        var merges = new Api.Catalog.MergeService(db, settings,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Api.Catalog.MergeService>.Instance);
        var import = new Api.Catalog.Import.CatalogImportService(db, vectors, merges, settings,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Api.Catalog.Import.CatalogImportService>.Instance);
        await import.ImportAttributesAsync(Integration.CatalogTestHarness.SeedDir);
    }

    private async Task SeedSyntheticCatalogAsync()
    {
        await using var db = Db();
        var rng = new Random(CatalogSeed);

        foreach (var category in DrinkCategory.All)
        {
            var producer = new Producer
            {
                Name = $"Synthetic {category} Works",
                NormalizedName = $"synthetic {category} works",
                Source = "receval",
            };
            db.Producers.Add(producer);

            var drinks = new List<Drink>();

            // 8 archetypes: one dominant dim each, the rest at a low floor —
            // known landmarks the golden assertions can reason about.
            for (var dim = 0; dim < VectorDims.Category; dim++)
            {
                var v = Enumerable.Repeat(0.2f, VectorDims.Category).ToArray();
                v[dim] = 0.95f;
                drinks.Add(NewDrink(producer, category, $"SYN {category} archetype d{dim}", v));
            }

            // Seeded random fillers spanning the space.
            for (var i = 0; i < RandomDrinksPerCategory; i++)
            {
                var v = new float[VectorDims.Category];
                for (var d = 0; d < VectorDims.Category; d++)
                    v[d] = (float)Math.Round(rng.NextDouble(), 3);
                drinks.Add(NewDrink(producer, category, $"SYN {category} {i:D3}", v));
            }

            db.Drinks.AddRange(drinks);
            Catalog.AddRange(drinks.Select(d =>
                new SynDrink(d.Id, d.Name, category, d.CategoryVector!.ToArray())));
        }

        await db.SaveChangesAsync();
    }

    private static Drink NewDrink(Producer producer, string category, string name, float[] vector)
        => new()
        {
            Producer = producer,
            Name = name,
            NormalizedName = name.ToLowerInvariant(),
            Category = category,
            Source = "receval",
            CategoryVector = new Vector(vector),
            BridgeVector = new Vector(vector[..VectorDims.Bridge]),
        };

    // --- Personas ----------------------------------------------------------------

    private async Task RunPersonasAsync()
    {
        foreach (var persona in Personas.All)
        {
            var userId = await CreateUserAsync(persona);
            await GenerateRatingsAsync(persona, userId);
            await RecomputeAsync(userId);

            var feed = await BuildFeedAsync(userId);
            var feedAgain = await BuildFeedAsync(userId);
            var deterministic = FeedSignature(feed) == FeedSignature(feedAgain);

            var profile = await LoadPreferenceAsync(userId, persona.Category);
            var result = new PersonaResult(
                persona, userId, profile, feed,
                Precision10(persona, feed),
                SectionMeanDistance(feed, RecommendationService.SectionAlley, profile),
                SectionMeanDistance(feed, RecommendationService.SectionWildcard, profile),
                LooHit: null,
                deterministic);

            if (persona.Dense)
                result = result with { LooHit = await LeaveOneOutAsync(persona, userId) };

            Results.Add(result);
            if (persona.Handle == Personas.BridgeHandle) BridgePersona = result;
            if (persona.Handle == Personas.GoldenHandle)
            {
                GoldenPersona = result;
                GoldenExpectedTopPick = await BruteForceTopPickAsync(userId, persona, profile!);
            }
        }
    }

    private async Task<Guid> CreateUserAsync(Persona persona)
    {
        await using var db = Db();
        var user = new User { UserName = persona.Handle, Email = $"{persona.Handle}@receval.test" };
        db.Users.Add(user);
        foreach (var category in persona.InterestCategories)
            db.UserCategoryInterests.Add(new UserCategoryInterest { UserId = user.Id, Category = category });
        await db.SaveChangesAsync();
        return user.Id;
    }

    private async Task GenerateRatingsAsync(Persona persona, Guid userId)
    {
        await using var db = Db();
        var rows = persona.GenerateRatings(Catalog);
        foreach (var (drinkId, value) in rows)
        {
            db.Ratings.Add(new Rating
            {
                CreatedByUserId = userId,
                DrinkId = drinkId,
                Value = value,
                Visibility = Visibility.Public,
                LocationContext = LocationContext.Untagged,
                IsLatest = true,
                CreatedAt = FixedNow,
                UpdatedAt = FixedNow,
            });
        }
        await db.SaveChangesAsync();
    }

    private async Task RecomputeAsync(Guid userId)
    {
        var (db, profiles, _) = Services();
        await using (db)
            await profiles.RecomputeAllForUserAsync(userId);
    }

    private async Task<Shared.Contracts.FeedResponse> BuildFeedAsync(Guid userId)
    {
        var (db, _, recs) = Services();
        await using (db)
            return await recs.BuildFeedAsync(userId, null);
    }

    private async Task<float[]?> LoadPreferenceAsync(Guid userId, string category)
    {
        await using var db = Db();
        var profile = await db.UserPalateProfiles.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId && p.Category == category);
        return profile?.PreferenceVector?.ToArray();
    }

    // --- Metrics ------------------------------------------------------------------

    /// <summary>
    /// Attribute-alignment precision@10: fraction of the persona's top-10
    /// Up-Your-Alley recs that sit in the persona's TRUE top-quartile of the
    /// catalog (by cosine to the persona's ground-truth preference). Random
    /// ordering scores ~0.25.
    /// </summary>
    private double Precision10(Persona persona, Shared.Contracts.FeedResponse feed)
    {
        var truth = Catalog
            .Where(d => d.Category == persona.Category)
            .OrderByDescending(d => RecommendationService.Cosine(persona.Preference, d.Vector))
            .ToList();
        var topQuartile = truth.Take(truth.Count / 4).Select(d => d.Id).ToHashSet();

        var alley = feed.Sections.First(s => s.Key == RecommendationService.SectionAlley)
            .Items.Where(i => i.Category == persona.Category).Take(10).ToList();
        if (alley.Count == 0) return 0;
        return alley.Count(i => topQuartile.Contains(i.DrinkId)) / (double)alley.Count;
    }

    /// <summary>Mean engine-space distance of a section's same-category picks.</summary>
    private double SectionMeanDistance(
        Shared.Contracts.FeedResponse feed, string sectionKey, float[]? profilePreference)
    {
        if (profilePreference is null) return double.NaN;
        var byId = Catalog.ToDictionary(d => d.Id);
        var distances = feed.Sections.First(s => s.Key == sectionKey).Items
            .Where(i => !i.CrossCategory && byId.ContainsKey(i.DrinkId))
            .Select(i => 1 - RecommendationService.Cosine(profilePreference, byId[i.DrinkId].Vector))
            .ToList();
        return distances.Count == 0 ? double.NaN : distances.Average();
    }

    /// <summary>
    /// Leave-one-out: hide the persona's best-loved drink, recompute, and ask
    /// whether the engine finds it again in the first 10 of Alley+Stretch.
    /// </summary>
    private async Task<bool> LeaveOneOutAsync(Persona persona, Guid userId)
    {
        await using var db = Db();
        var byId = Catalog.ToDictionary(d => d.Id);
        var held = await db.Ratings
            .Where(r => r.CreatedByUserId == userId)
            .ToListAsync();
        var target = held
            .OrderByDescending(r => r.Value)
            .ThenByDescending(r => RecommendationService.Cosine(persona.Preference, byId[r.DrinkId].Vector))
            .First();

        db.Ratings.Remove(target);
        await db.SaveChangesAsync();
        await RecomputeAsync(userId);

        var feed = await BuildFeedAsync(userId);
        var top10 = feed.Sections
            .Where(s => s.Key is RecommendationService.SectionAlley or RecommendationService.SectionStretch)
            .SelectMany(s => s.Items)
            .Take(10)
            .Select(i => i.DrinkId)
            .ToHashSet();
        var hit = top10.Contains(target.DrinkId);

        // Restore the world for later metrics.
        db.Ratings.Add(new Rating
        {
            CreatedByUserId = userId,
            DrinkId = target.DrinkId,
            Value = target.Value,
            Visibility = target.Visibility,
            LocationContext = target.LocationContext,
            IsLatest = true,
            CreatedAt = FixedNow,
            UpdatedAt = FixedNow,
        });
        await db.SaveChangesAsync();
        await RecomputeAsync(userId);
        return hit;
    }

    /// <summary>
    /// Independent recomputation of what the engine's FIRST Alley pick must
    /// be: MMR's first selection maximizes λ·score with no diversity penalty,
    /// so it is exactly argmax((1−distance) + popWeight·popularity) over
    /// unrated same-category drinks.
    /// </summary>
    private async Task<Guid> BruteForceTopPickAsync(Guid userId, Persona persona, float[] profilePreference)
    {
        await using var db = Db();
        var rated = (await db.Ratings.Where(r => r.CreatedByUserId == userId)
            .Select(r => r.DrinkId).ToListAsync()).ToHashSet();
        var popularity = (await db.Ratings
            .Where(r => r.IsLatest && r.Visibility == Visibility.Public)
            .GroupBy(r => r.DrinkId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync())
            .ToDictionary(x => x.Key, x => x.Count / (x.Count + 5.0));

        // Mirror the engine's selection exactly: the Alley pool is the top 30
        // by cosine DISTANCE (flag default), and MMR's first pick is the
        // strict argmax of score within that pool, in pool order.
        var pool = Catalog
            .Where(d => d.Category == persona.Category && !rated.Contains(d.Id))
            .OrderBy(d => 1 - RecommendationService.Cosine(profilePreference, d.Vector))
            .ThenBy(d => d.Id)
            .Take(30)
            .ToList();

        var best = pool[0].Id;
        var bestScore = double.MinValue;
        foreach (var drink in pool)
        {
            var score = RecommendationService.Cosine(profilePreference, drink.Vector)
                + 0.10 * popularity.GetValueOrDefault(drink.Id);
            if (score > bestScore)
            {
                bestScore = score;
                best = drink.Id;
            }
        }
        return best;
    }

    private static string FeedSignature(Shared.Contracts.FeedResponse feed)
        => string.Join("|", feed.Sections.Select(s =>
            s.Key + ":" + string.Join(",", s.Items.Select(i => i.DrinkId))));
}

/// <summary>Frozen clock — the wildcard's per-day seed must not move mid-eval.</summary>
public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

[CollectionDefinition("rec-eval")]
public sealed class RecEvalCollection : ICollectionFixture<RecEvalFixture>;
