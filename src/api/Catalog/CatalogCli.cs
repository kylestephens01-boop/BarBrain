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
///   dotnet BarBrain.Api.dll import products --file /data/whiskey-national.json
///   dotnet BarBrain.Api.dll import products --clear-attribute --source seed:whiskey-national --drink-ref bt-eagle-rare --key oak
///   dotnet BarBrain.Api.dll import openbrewerydb --file /data/obdb.csv
///   dotnet BarBrain.Api.dll import beerdb --dir /data/openbeer-us
///   dotnet BarBrain.Api.dll import ttb-sample --file seed/ttb-cola-sample.csv
///   dotnet BarBrain.Api.dll report --out seed-report.md
///   dotnet BarBrain.Api.dll eval recs [--out rec-eval.md]
///
/// "bundled" = attributes → styles → corridor (all offline, license-safe seeds).
/// "eval recs" = live-catalog Precision@10 (Sprint 7) — synthetic personas in
/// a rolled-back transaction; strictly read-only against production data.
/// </summary>
public static class CatalogCli
{
    public static bool IsCliInvocation(string[] args)
        => args.Length > 0 && args[0] is "import" or "report" or "eval";

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

        // eval recs: the real profile + rec pipeline (Sprint 7).
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddScoped<Palate.PalateProfileService>();
        builder.Services.AddScoped<Palate.MatchService>();
        builder.Services.AddScoped<Palate.RecommendationService>();
        builder.Services.AddScoped<Palate.LiveRecEvalService>();

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
                case ["import", "products", ..] when args.Contains("--clear-attribute"):
                    var cleared = await import.ClearAttributeOverrideAsync(
                        RequireOption(args, "--source"),
                        RequireOption(args, "--drink-ref"),
                        RequireOption(args, "--key"));
                    Console.WriteLine(cleared == ClearOverrideResult.Cleared
                        ? "Override cleared; dimension reverted to style-baseline inheritance."
                        : "No moderator override for that key; already at style baseline (no-op).");
                    break;
                case ["import", "products", ..]:
                    await import.ImportProductsAsync(RequireOption(args, "--file"));
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
                case ["eval", "recs", ..]:
                    var eval = services.GetRequiredService<Palate.LiveRecEvalService>();
                    return await eval.RunAsync(FindOption(args, "--out"));
                default:
                    Console.Error.WriteLine(
                        "Usage: import attributes|styles|corridor|bundled|demo-dupes" +
                        " | import products --file <json>" +
                        " | import products --clear-attribute --source <seed-tag> --drink-ref <ref> --key <attribute-key>" +
                        " | import openbrewerydb --file <csv> | import beerdb --dir <checkout>" +
                        " | import ttb-sample --file <csv> | report [--out <path>]" +
                        " | eval recs [--out <path>]");
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
