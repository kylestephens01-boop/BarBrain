using BarBrain.Api.Catalog.Import;
using BarBrain.Api.Data;
using BarBrain.Api.Settings;
using Microsoft.EntityFrameworkCore;

namespace BarBrain.Api.Catalog;

/// <summary>
/// CLI mode of the api executable (Sprint 1 spec: "importers runnable via
/// CLI"). Shares the app's configuration, DbContext, and services — the same
/// binary that serves HTTP runs the importers, so compose can exec them:
///
///   dotnet run --project src/api -- import bundled
///   dotnet BarBrain.Api.dll import openbrewerydb --file /data/obdb.csv
///   dotnet BarBrain.Api.dll import beerdb --dir /data/openbeer-us
///   dotnet BarBrain.Api.dll import ttb-sample --file seed/ttb-cola-sample.csv
///   dotnet BarBrain.Api.dll report --out seed-report.md
///
/// "bundled" = attributes → styles → corridor (all offline, license-safe seeds).
/// </summary>
public static class CatalogCli
{
    public static bool IsCliInvocation(string[] args)
        => args.Length > 0 && args[0] is "import" or "report";

    public static async Task<int> RunAsync(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        var connectionString = builder.Configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");
        builder.Services.AddDbContext<AppDbContext>(options =>
            options.UseBarBrainNpgsql(connectionString));
        builder.Services.AddMemoryCache();
        builder.Services.AddScoped<ISettingsService, SettingsService>();
        builder.Services.AddCatalogServices();

        using var host = builder.Build();
        await using var scope = host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("CatalogCli");

        try
        {
            // Importing into an unmigrated/stale DB must be impossible.
            var db = services.GetRequiredService<AppDbContext>();
            await db.Database.MigrateAsync();
            await SettingsSeeder.SeedAsync(db, AppContext.BaseDirectory, logger);

            var import = services.GetRequiredService<CatalogImportService>();
            var seedDir = Path.Combine(AppContext.BaseDirectory, "seed");

            switch (args)
            {
                case ["import", "attributes"]:
                    await import.ImportAttributesAsync(seedDir);
                    break;
                case ["import", "styles"]:
                    await import.ImportAttributesAsync(seedDir); // styles depend on the vocabulary
                    await import.ImportStylesAsync(seedDir);
                    break;
                case ["import", "corridor"]:
                    await import.ImportCorridorAsync(seedDir);
                    break;
                case ["import", "bundled"]:
                    await import.ImportAttributesAsync(seedDir);
                    await import.ImportStylesAsync(seedDir);
                    await import.ImportCorridorAsync(seedDir);
                    break;
                case ["import", "demo-dupes"]:
                    await import.ImportDemoDupesAsync();
                    break;
                case ["import", "openbrewerydb", ..]:
                    await import.ImportOpenBreweryDbAsync(RequireOption(args, "--file"));
                    break;
                case ["import", "beerdb", ..]:
                    await import.ImportBeerDbAsync(RequireOption(args, "--dir"));
                    break;
                case ["import", "ttb-sample", ..]:
                    await import.ImportTtbSampleAsync(RequireOption(args, "--file"));
                    break;
                case ["report", ..]:
                    var report = await import.BuildReportAsync();
                    Console.WriteLine(report);
                    if (FindOption(args, "--out") is { } outPath)
                        await File.WriteAllTextAsync(outPath, report);
                    break;
                default:
                    Console.Error.WriteLine(
                        "Usage: import attributes|styles|corridor|bundled|demo-dupes" +
                        " | import openbrewerydb --file <csv> | import beerdb --dir <checkout>" +
                        " | import ttb-sample --file <csv> | report [--out <path>]");
                    return 2;
            }

            return 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Catalog CLI failed.");
            return 1;
        }
    }

    private static string RequireOption(string[] args, string name)
        => FindOption(args, name)
           ?? throw new InvalidOperationException($"Missing required option {name} <value>.");

    private static string? FindOption(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}

public static class CatalogServiceCollectionExtensions
{
    public static IServiceCollection AddCatalogServices(this IServiceCollection services)
    {
        services.AddScoped<AttributeVectorService>();
        services.AddScoped<MergeService>();
        services.AddScoped<CatalogQueryService>();
        services.AddScoped<CatalogImportService>();
        return services;
    }
}
