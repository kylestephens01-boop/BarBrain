using BarBrain.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BarBrain.Api.Tests.Integration;

/// <summary>
/// Importer acceptance (Sprint 1): idempotent re-runs, full corridor vector
/// coverage (inherited or better), style taxonomy completeness, DB constraint
/// enforcement (ADR-026), and cosine sanity of the authored baselines.
/// </summary>
[Collection("postgres")]
public sealed class CatalogImportTests(PostgresFixture fixture) : IAsyncLifetime
{
    private string _connectionString = "";

    public async Task InitializeAsync()
    {
        if (!fixture.DockerAvailable)
            return;
        // Unique per test: xunit news up the class (and runs this) per test method.
        _connectionString = await fixture.CreateEmptyDatabaseAsync($"catalog_import_{Guid.NewGuid():N}");
        using var harness = new CatalogTestHarness(_connectionString);
        await harness.Db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [SkippableFact]
    public async Task Bundled_import_is_idempotent_and_covers_corridor_with_vectors()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        using var harness = new CatalogTestHarness(_connectionString);

        await harness.ImportBundledAsync();
        var producers = await harness.Db.Producers.CountAsync();
        var drinks = await harness.Db.Drinks.CountAsync();
        var styles = await harness.Db.Styles.CountAsync();
        var styleAttrs = await harness.Db.StyleAttributes.CountAsync();

        // Re-run: no dupes, no growth (idempotency acceptance).
        using var rerun = new CatalogTestHarness(_connectionString);
        await rerun.ImportBundledAsync();
        Assert.Equal(producers, await rerun.Db.Producers.CountAsync());
        Assert.Equal(drinks, await rerun.Db.Drinks.CountAsync());
        Assert.Equal(styles, await rerun.Db.Styles.CountAsync());
        Assert.Equal(styleAttrs, await rerun.Db.StyleAttributes.CountAsync());

        // Attribute vocabulary: exactly 8 dims per category (ADR-009).
        var dims = await rerun.Db.AttributeDefinitions.GroupBy(a => a.Category)
            .Select(g => new { g.Key, Count = g.Count() }).ToListAsync();
        Assert.Equal(3, dims.Count);
        Assert.All(dims, d => Assert.Equal(8, d.Count));

        // Style taxonomy: full BJCP list for beer, real trees elsewhere.
        Assert.True(await rerun.Db.Styles.CountAsync(s => s.Category == "beer") >= 130,
            "expected the full BJCP-derived beer style list");
        Assert.True(await rerun.Db.Styles.CountAsync(s => s.Category == "whiskey") >= 15);
        Assert.True(await rerun.Db.Styles.CountAsync(s => s.Category == "wine") >= 25);

        // Leaf styles carry complete baselines → baseline vectors present.
        var leafWithBaseline = await rerun.Db.Styles
            .CountAsync(s => s.CategoryVector != null && s.BridgeVector != null);
        Assert.True(leafWithBaseline >= 100, $"only {leafWithBaseline} styles have baseline vectors");

        // Corridor acceptance: every active corridor drink has a full vector
        // (inherited or better) and 8 attribute rows.
        var corridorDrinks = await rerun.Db.Drinks
            .Include(d => d.Attributes)
            .Where(d => d.Source == "seed:corridor" && d.Status == EntityStatus.Active)
            .ToListAsync();
        Assert.True(corridorDrinks.Count >= 50, $"corridor seed too small: {corridorDrinks.Count}");
        Assert.All(corridorDrinks, d =>
        {
            Assert.NotNull(d.CategoryVector);
            Assert.NotNull(d.BridgeVector);
            Assert.Equal(8, d.Attributes.Count);
            Assert.All(d.Attributes, a => Assert.Equal(AttributeValueSource.Inherited, a.Source));
        });

        // All three categories are represented (cross-category from day one).
        var categories = corridorDrinks.Select(d => d.Category).Distinct().ToHashSet();
        Assert.Superset(new HashSet<string> { "beer", "whiskey", "wine" }, categories);
        Assert.Equal(3, categories.Count);
    }

    [SkippableFact]
    public async Task Authored_baselines_pass_cosine_sanity()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        using var harness = new CatalogTestHarness(_connectionString);
        await harness.ImportBundledAsync();

        var stout = await harness.Db.Styles.SingleAsync(s => s.Code == "20C");   // Imperial Stout
        var porter = await harness.Db.Styles.SingleAsync(s => s.Code == "13C");  // English Porter
        var gose = await harness.Db.Styles.SingleAsync(s => s.Code == "23G");    // Gose

        var stoutPorter = Cosine(stout.CategoryVector!.ToArray(), porter.CategoryVector!.ToArray());
        var stoutGose = Cosine(stout.CategoryVector!.ToArray(), gose.CategoryVector!.ToArray());

        // A palate engine that thinks an imperial stout is closer to a gose
        // than to a porter is broken — this guards the authored numbers.
        Assert.True(stoutPorter > stoutGose + 0.2,
            $"cosine(stout, porter)={stoutPorter:0.00} should clearly exceed cosine(stout, gose)={stoutGose:0.00}");
    }

    [SkippableFact]
    public async Task Db_constraints_reject_invalid_rows()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        using var harness = new CatalogTestHarness(_connectionString);
        await harness.ImportBundledAsync();
        var producer = await harness.Db.Producers.FirstAsync(p => p.Status == EntityStatus.Active);

        // ADR-026: an ownerless (imported) row cannot be private.
        harness.Db.Drinks.Add(new Drink
        {
            ProducerId = producer.Id,
            Name = "Sneaky Private Import",
            NormalizedName = "sneaky private import",
            Category = DrinkCategory.Beer,
            Source = "seed:test",
            Visibility = Visibility.Private,
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => harness.Db.SaveChangesAsync());
        harness.Db.ChangeTracker.Clear();

        // Merge pairing: status=merged requires a redirect target (and vice versa).
        harness.Db.Drinks.Add(new Drink
        {
            ProducerId = producer.Id,
            Name = "Half Merged",
            NormalizedName = "half merged",
            Category = DrinkCategory.Beer,
            Source = "seed:test",
            Status = EntityStatus.Merged,
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => harness.Db.SaveChangesAsync());
        harness.Db.ChangeTracker.Clear();

        // Category vocabulary is closed.
        harness.Db.Drinks.Add(new Drink
        {
            ProducerId = producer.Id,
            Name = "Hard Seltzer",
            NormalizedName = "hard seltzer",
            Category = "seltzer",
            Source = "seed:test",
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => harness.Db.SaveChangesAsync());
        harness.Db.ChangeTracker.Clear();
    }

    private static double Cosine(float[] a, float[] b)
    {
        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            na += a[i] * a[i];
            nb += b[i] * b[i];
        }
        return dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }
}
