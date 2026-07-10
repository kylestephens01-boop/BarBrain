using BarBrain.Api.Data;
using BarBrain.Api.Data.Entities;
using BarBrain.Api.Palate;
using BarBrain.Api.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Pgvector;
using Testcontainers.PostgreSql;

namespace BarBrain.Api.Tests.MatchEval;

/// <summary>
/// The matching eval world (Gate C2, ADR-014). Matching cannot be human-reviewed
/// with one real user, so a synthetic population of KNOWN palate twins is the
/// acceptance gate: plant pairs of users with near-identical palates among
/// decoys, run the REAL profile job and the REAL match batch against real
/// Postgres+pgvector, and assert the matcher finds each twin's partner, that
/// density-weighting behaves, that hide-me removes a user both directions, and
/// that the match-% flag toggles display. A sparsity simulation ("what will
/// launch month feel like") is written as a CI artifact.
///
/// Mirrors the Sprint 3 RecEval fixture: all heavy lifting once here; the tests
/// assert on the captured results. A report is always written on dispose.
/// </summary>
public sealed class MatchEvalFixture : IAsyncLifetime
{
    public const int CatalogSeed = 20260708;
    public const int DrinksPerCategory = 90;
    public const string Category = DrinkCategory.Beer;

    // Twin pairs share a ground-truth preference direction. Dense pairs also
    // co-rate the same drinks (attribute + CF agreement); the sparse pair shares
    // the palate but rates DISJOINT drinks (attribute-only, low density).
    public const int DensePairs = 6;
    public const int SparsePairIndex = DensePairs; // one extra, attribute-only

    public bool DockerAvailable { get; private set; }
    public string? SetupFailure { get; private set; }

    private PostgreSqlContainer? _container;
    private MemoryCache? _cache;
    public string ConnectionString { get; private set; } = "";

    public sealed record SynDrink(Guid Id, float[] Vector);
    public sealed record TwinUser(string Handle, Guid UserId, int PairIndex, bool Dense);

    public List<SynDrink> Catalog { get; } = [];
    public List<TwinUser> Twins { get; } = [];
    /// <summary>UserId → the handle of its top-1 match (or null if none).</summary>
    public Dictionary<Guid, string?> TopMatchHandle { get; } = [];
    public List<SparsityPoint> Sparsity { get; } = [];

    public sealed record SparsityPoint(
        int Users, int WithAnyMatch, int WithMedConfidence, double MedFraction, double AnyFraction);

    public async Task InitializeAsync()
    {
        try
        {
            _container = new PostgreSqlBuilder("pgvector/pgvector:pg16")
                .WithDatabase("barbrain_matcheval")
                .WithUsername("barbrain").WithPassword("barbrain").Build();
            await _container.StartAsync();
            ConnectionString = _container.GetConnectionString();
            DockerAvailable = true;
        }
        catch
        {
            DockerAvailable = false;
            return;
        }

        try
        {
            await using (var db = Integration.PostgresFixture.CreateContext(ConnectionString))
                await db.Database.MigrateAsync();

            await SeedAttributeDefinitionsAsync(ConnectionString);
            await SeedCatalogAsync(ConnectionString, Catalog);
            await BuildTwinWorldAsync();
            await CaptureTopMatchesAsync();

            // Sparsity simulation — isolated databases so the twin world stays clean.
            Sparsity.Add(await SimulateSparsityAsync(50, 5001));
            Sparsity.Add(await SimulateSparsityAsync(500, 5002));
        }
        catch (Exception ex)
        {
            SetupFailure = ex.ToString();
        }
    }

    public async Task DisposeAsync()
    {
        try { if (DockerAvailable) MatchEvalReport.Write(this); }
        finally
        {
            _cache?.Dispose();
            if (_container is not null) await _container.DisposeAsync();
        }
    }

    // --- service wiring ---------------------------------------------------------

    private AppDbContext Db(string cs) => Integration.PostgresFixture.CreateContext(cs);

    public SettingsService Settings(AppDbContext db)
    {
        _cache ??= new MemoryCache(new MemoryCacheOptions());
        return new SettingsService(db, _cache);
    }

    public MatchService MatchService(AppDbContext db) => new(db, Settings(db));

    // --- world building ---------------------------------------------------------

    private async Task SeedAttributeDefinitionsAsync(string cs)
    {
        await using var db = Db(cs);
        var settings = Settings(db);
        var vectors = new Api.Catalog.AttributeVectorService(db, settings);
        var merges = new Api.Catalog.MergeService(db, settings,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Api.Catalog.MergeService>.Instance);
        var import = new Api.Catalog.Import.CatalogImportService(db, vectors, merges, settings,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Api.Catalog.Import.CatalogImportService>.Instance);
        await import.ImportAttributesAsync(Integration.CatalogTestHarness.SeedDir);
    }

    private static async Task SeedCatalogAsync(string cs, List<SynDrink> into)
    {
        await using var db = Integration.PostgresFixture.CreateContext(cs);
        var rng = new Random(CatalogSeed);
        var producer = new Producer
        {
            Name = "Synthetic Match Works", NormalizedName = "synthetic match works", Source = "matcheval",
        };
        db.Producers.Add(producer);

        var drinks = new List<Drink>();
        for (var dim = 0; dim < VectorDims.Category; dim++)
        {
            var v = Enumerable.Repeat(0.2f, VectorDims.Category).ToArray();
            v[dim] = 0.95f;
            drinks.Add(NewDrink(producer, $"MATCH archetype d{dim}", v));
        }
        for (var i = 0; i < DrinksPerCategory; i++)
        {
            var v = new float[VectorDims.Category];
            for (var d = 0; d < VectorDims.Category; d++)
                v[d] = (float)Math.Round(rng.NextDouble(), 3);
            drinks.Add(NewDrink(producer, $"MATCH {i:D3}", v));
        }
        db.Drinks.AddRange(drinks);
        await db.SaveChangesAsync();
        into.AddRange(drinks.Select(d => new SynDrink(d.Id, d.CategoryVector!.ToArray())));
    }

    private static Drink NewDrink(Producer producer, string name, float[] vector) => new()
    {
        Producer = producer, Name = name, NormalizedName = name.ToLowerInvariant(),
        Category = Category, Source = "matcheval",
        CategoryVector = new Vector(vector),
        BridgeVector = new Vector(vector[..VectorDims.Bridge]),
    };

    private async Task BuildTwinWorldAsync()
    {
        // Each pair's preference emphasizes a distinct attribute dim → the pairs
        // are well separated, so a twin's nearest palate really is its partner.
        for (var pair = 0; pair <= SparsePairIndex; pair++)
        {
            var dense = pair < DensePairs;
            var pref = OneHotPreference(pair);
            var ranked = Catalog
                .OrderByDescending(d => RecommendationService.Cosine(pref, d.Vector))
                .ToList();

            // Dense twins rate the SAME drinks (high overlap → co-rating agreement).
            // Sparse twins split the ranked list into disjoint halves (0 co-rated).
            var (aDrinks, bDrinks) = dense
                ? (LovedHated(ranked, 0, 8), LovedHated(ranked, 0, 8))
                : (LovedHated(ranked, 0, 6), LovedHated(ranked, 6, 6));

            var a = await CreateUserAsync($"twin_{pair}a");
            var b = await CreateUserAsync($"twin_{pair}b");
            await AddRatingsAsync(a, aDrinks, seed: pair * 2);
            await AddRatingsAsync(b, bDrinks, seed: pair * 2 + 1);
            await RecomputeAsync(a);
            await RecomputeAsync(b);
            Twins.Add(new TwinUser($"twin_{pair}a", a, pair, dense));
            Twins.Add(new TwinUser($"twin_{pair}b", b, pair, dense));
        }

        // A few decoys with random preferences — no one should match them well.
        var rng = new Random(999);
        for (var i = 0; i < 3; i++)
        {
            var pref = new float[VectorDims.Category];
            for (var d = 0; d < VectorDims.Category; d++) pref[d] = (float)rng.NextDouble();
            var ranked = Catalog.OrderByDescending(d => RecommendationService.Cosine(pref, d.Vector)).ToList();
            var u = await CreateUserAsync($"decoy_{i}");
            await AddRatingsAsync(u, LovedHated(ranked, 0, 6), seed: 700 + i);
            await RecomputeAsync(u);
        }

        await using var db = Db(ConnectionString);
        await MatchService(db).ComputeAllAsync();
    }

    private static float[] OneHotPreference(int dim)
    {
        var v = Enumerable.Repeat(0.15f, VectorDims.Category).ToArray();
        v[dim % VectorDims.Category] = 1f;
        return v;
    }

    /// <summary>Top <paramref name="n"/> loved (5) + next-window hated (1) from a ranked list.</summary>
    private static List<(Guid Id, decimal Value)> LovedHated(List<SynDrink> ranked, int offset, int n)
    {
        var loved = ranked.Skip(offset).Take(n).Select(d => (d.Id, 5.0m));
        var hated = ranked.AsEnumerable().Reverse().Skip(offset).Take(n).Select(d => (d.Id, 1.0m));
        return loved.Concat(hated).DistinctBy(x => x.Item1).ToList();
    }

    private async Task<Guid> CreateUserAsync(string handle)
    {
        await using var db = Db(ConnectionString);
        var user = new User
        {
            UserName = handle, Email = $"{handle}@matcheval.test",
            // Activated (matchable) — the DB gate requires birth year + attestation.
            BirthYear = 1990, AttestedAt = DateTimeOffset.UtcNow,
            ActivatedAt = DateTimeOffset.UtcNow,
        };
        db.Users.Add(user);
        db.UserCategoryInterests.Add(new UserCategoryInterest { UserId = user.Id, Category = Category });
        await db.SaveChangesAsync();
        return user.Id;
    }

    private async Task AddRatingsAsync(Guid userId, List<(Guid Id, decimal Value)> ratings, int seed)
    {
        await using var db = Db(ConnectionString);
        var now = new DateTimeOffset(2026, 7, 8, 12, 0, 0, TimeSpan.Zero);
        foreach (var (drinkId, value) in ratings)
            db.Ratings.Add(new Rating
            {
                CreatedByUserId = userId, DrinkId = drinkId, Value = value,
                Visibility = Visibility.Public, LocationContext = LocationContext.Untagged,
                IsLatest = true, CreatedAt = now, UpdatedAt = now,
            });
        await db.SaveChangesAsync();
    }

    private async Task RecomputeAsync(Guid userId)
    {
        await using var db = Db(ConnectionString);
        await new PalateProfileService(db, new FixedClock()).RecomputeAllForUserAsync(userId);
    }

    private async Task CaptureTopMatchesAsync()
    {
        await using var db = Db(ConnectionString);
        var matchService = MatchService(db);
        foreach (var twin in Twins)
        {
            var result = await matchService.GetMatchesAsync(twin.UserId, 25);
            TopMatchHandle[twin.UserId] = result.Matches.FirstOrDefault()?.Handle;
        }
    }

    // --- sparsity simulation ----------------------------------------------------

    private async Task<SparsityPoint> SimulateSparsityAsync(int userCount, int seed)
    {
        var dbName = $"matcheval_sparsity_{userCount}";
        var cs = await CreateDatabaseAsync(dbName);
        await using (var db = Integration.PostgresFixture.CreateContext(cs))
            await db.Database.MigrateAsync();

        await SeedAttributeDefinitionsAsync(cs);
        var catalog = new List<SynDrink>();
        await SeedCatalogAsync(cs, catalog);

        var rng = new Random(seed);
        var userIds = new List<Guid>();
        // Sparse, realistic early users: a handful of ratings each near a random
        // palate direction — mostly non-overlapping, like a real launch month.
        for (var i = 0; i < userCount; i++)
        {
            var pref = new float[VectorDims.Category];
            for (var d = 0; d < VectorDims.Category; d++) pref[d] = (float)rng.NextDouble();
            var ranked = catalog.OrderByDescending(d => RecommendationService.Cosine(pref, d.Vector)).ToList();
            var take = 4 + rng.Next(6); // 4–9 ratings
            var offset = rng.Next(Math.Max(1, catalog.Count - take * 2));
            var picks = ranked.Skip(offset).Take(take).Select(d => (d.Id, (decimal)(rng.Next(2) == 0 ? 5.0 : 2.0))).ToList();

            Guid userId;
            await using (var db = Integration.PostgresFixture.CreateContext(cs))
            {
                var user = new User
                {
                    UserName = $"s{userCount}_{i}", Email = $"s{userCount}_{i}@matcheval.test",
                    BirthYear = 1990, AttestedAt = DateTimeOffset.UtcNow,
                    ActivatedAt = DateTimeOffset.UtcNow,
                };
                db.Users.Add(user);
                db.UserCategoryInterests.Add(new UserCategoryInterest { UserId = user.Id, Category = Category });
                var now = new DateTimeOffset(2026, 7, 8, 12, 0, 0, TimeSpan.Zero);
                foreach (var (drinkId, value) in picks)
                    db.Ratings.Add(new Rating
                    {
                        CreatedByUserId = user.Id, DrinkId = drinkId, Value = value,
                        Visibility = Visibility.Public, LocationContext = LocationContext.Untagged,
                        IsLatest = true, CreatedAt = now, UpdatedAt = now,
                    });
                await db.SaveChangesAsync();
                userId = user.Id;
            }
            userIds.Add(userId);
            await using (var db = Integration.PostgresFixture.CreateContext(cs))
                await new PalateProfileService(db, new FixedClock()).RecomputeAllForUserAsync(userId);
        }

        await using (var db = Integration.PostgresFixture.CreateContext(cs))
            await MatchService(db).ComputeAllAsync();

        // For each user: do they have any match, and any at med+ confidence?
        var medThreshold = Api.Palate.MatchService.DefaultConfMed;
        int withAny = 0, withMed = 0;
        await using (var db = Integration.PostgresFixture.CreateContext(cs))
        {
            var byUser = (await db.UserMatchNeighbors.AsNoTracking().ToListAsync())
                .GroupBy(m => m.UserId);
            foreach (var g in byUser)
            {
                withAny++;
                if (g.Max(m => m.CoRatedCount) >= medThreshold) withMed++;
            }
        }

        return new SparsityPoint(
            userCount, withAny, withMed,
            userCount == 0 ? 0 : withMed / (double)userCount,
            userCount == 0 ? 0 : withAny / (double)userCount);
    }

    private async Task<string> CreateDatabaseAsync(string name)
    {
        await using var admin = new Npgsql.NpgsqlConnection(ConnectionString);
        await admin.OpenAsync();
        await using var cmd = admin.CreateCommand();
        cmd.CommandText = $"CREATE DATABASE \"{name}\"";
        await cmd.ExecuteNonQueryAsync();
        return new Npgsql.NpgsqlConnectionStringBuilder(ConnectionString) { Database = name }.ConnectionString;
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 7, 8, 12, 0, 0, TimeSpan.Zero);
    }
}

[CollectionDefinition("match-eval")]
public sealed class MatchEvalCollection : ICollectionFixture<MatchEvalFixture>;
