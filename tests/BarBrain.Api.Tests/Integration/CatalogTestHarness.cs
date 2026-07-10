using BarBrain.Api.Catalog;
using BarBrain.Api.Catalog.Import;
using BarBrain.Api.Data;
using BarBrain.Api.Settings;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace BarBrain.Api.Tests.Integration;

/// <summary>
/// Wires the catalog services against a test database the same way the app
/// does, and locates the bundled seed directory from the repo checkout.
/// </summary>
public sealed class CatalogTestHarness : IDisposable
{
    public AppDbContext Db { get; }
    public ISettingsService Settings { get; }
    public AttributeVectorService Vectors { get; }
    public MergeService Merges { get; }
    public CatalogImportService Import { get; }
    public CatalogQueryService Queries { get; }

    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    public CatalogTestHarness(string connectionString)
    {
        Db = PostgresFixture.CreateContext(connectionString);
        Settings = new SettingsService(Db, _cache);
        Vectors = new AttributeVectorService(Db, Settings);
        Merges = new MergeService(Db, Settings, NullLogger<MergeService>.Instance);
        Import = new CatalogImportService(Db, Vectors, Merges, Settings, NullLogger<CatalogImportService>.Instance);
        Queries = new CatalogQueryService(Db);
    }

    /// <summary>src/api/seed in the repo checkout (works locally and in CI).</summary>
    public static string SeedDir { get; } = LocateSeedDir();

    public async Task ImportBundledAsync()
    {
        await Import.ImportAttributesAsync(SeedDir);
        await Import.ImportStylesAsync(SeedDir);
        await Import.ImportCorridorAsync(SeedDir);
    }

    private static string LocateSeedDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "BarBrain.slnx")))
                return Path.Combine(dir.FullName, "src", "api", "seed");
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not locate repo root (BarBrain.slnx) from test base directory.");
    }

    public void Dispose()
    {
        Db.Dispose();
        _cache.Dispose();
    }
}
