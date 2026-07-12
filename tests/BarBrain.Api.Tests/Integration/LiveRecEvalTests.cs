using BarBrain.Api.Palate;
using BarBrain.Api.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace BarBrain.Api.Tests.Integration;

/// <summary>
/// Sprint 7 acceptance: the live-catalog eval verb. It must produce a single
/// comparable Precision@10 number against a real (bundled-seed) catalog and —
/// the reason it exists as a separate verb at all — leave the database
/// EXACTLY as it found it: every synthetic persona row rolls back.
/// </summary>
[Collection("postgres")]
public sealed class LiveRecEvalTests(PostgresFixture fixture) : IAsyncLifetime
{
    private string _connectionString = "";

    public async Task InitializeAsync()
    {
        if (!fixture.DockerAvailable) return;
        _connectionString = await fixture.CreateEmptyDatabaseAsync($"live_eval_{Guid.NewGuid():N}");
        using var harness = new CatalogTestHarness(_connectionString);
        await harness.Db.Database.MigrateAsync();
        // A real catalog: bundled corridor + the whiskey-national batch gives
        // two categories comfortably past the eval's minimum size.
        await harness.ImportBundledAsync();
        await harness.Import.ImportProductsAsync(
            Path.Combine(CatalogTestHarness.SeedDir, "whiskey-national.json"));
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [SkippableFact]
    public async Task Eval_prints_a_precision_number_and_leaves_no_rows_behind()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");

        var outPath = Path.Combine(Path.GetTempPath(), $"rec-eval-{Guid.NewGuid():N}.md");
        try
        {
            int exit;
            await using (var db = PostgresFixture.CreateContext(_connectionString))
            {
                using var cache = new MemoryCache(new MemoryCacheOptions());
                var settings = new SettingsService(db, cache);
                var clock = TimeProvider.System;
                var profiles = new PalateProfileService(db, clock);
                var recs = new RecommendationService(db, settings, clock, new MatchService(db, settings));
                var eval = new LiveRecEvalService(db, profiles, recs, clock,
                    NullLogger<LiveRecEvalService>.Instance);

                exit = await eval.RunAsync(outPath);
            }

            Assert.Equal(0, exit);

            // A single comparable number, in range.
            var report = await File.ReadAllTextAsync(outPath);
            var line = report.Split('\n').Single(l => l.StartsWith("Live Precision@10:"));
            var value = double.Parse(line["Live Precision@10:".Length..].Trim(),
                System.Globalization.CultureInfo.InvariantCulture);
            Assert.InRange(value, 0.0, 1.0);

            // READ-ONLY: no persona users, ratings, profiles, or interests survive.
            await using var check = PostgresFixture.CreateContext(_connectionString);
            Assert.Equal(0, await check.Users.CountAsync());
            Assert.Equal(0, await check.Ratings.CountAsync());
            Assert.Equal(0, await check.UserPalateProfiles.CountAsync());
            Assert.Equal(0, await check.UserCategoryInterests.CountAsync());
            Assert.Equal(0, await check.Events.CountAsync());
        }
        finally
        {
            if (File.Exists(outPath)) File.Delete(outPath);
        }
    }
}
