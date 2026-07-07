using BarBrain.Api.Data;
using BarBrain.Api.Data.Entities;
using BarBrain.Api.Digest;
using BarBrain.Api.Palate;
using BarBrain.Api.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Pgvector;

namespace BarBrain.Api.Tests.Integration;

/// <summary>
/// The weekly digest end to end (ADR-019): compose → render → send for
/// subscribed users, block flags honored, unsubscribe respected, and the
/// CAN-SPAM guard (no physical address ⇒ log-only, never delivered). Each test
/// builds its own isolated database so the digest's "all subscribed users" scan
/// isn't polluted by other suites.
/// </summary>
[Collection("postgres")]
public sealed class DigestServiceTests(PostgresFixture fixture)
{
    private sealed class CapturingSender : IDigestSender
    {
        public readonly List<(string Recipient, string Subject, string Html, bool Deliver)> Sent = [];
        public Task SendAsync(string recipient, string subject, string html, bool deliver, CancellationToken ct = default)
        {
            Sent.Add((recipient, subject, html, deliver));
            return Task.CompletedTask;
        }
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 7, 8, 12, 0, 0, TimeSpan.Zero);
    }

    [SkippableFact]
    public async Task Enabled_with_address_delivers_a_footer_compliant_digest_with_content()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        var (cs, sender, settings) = await BuildWorldAsync();

        await settings.SetAsync(DigestService.EnabledFlag, "true");
        await settings.SetAsync(DigestService.PhysicalAddressFlag, "BarBrain LLC, 123 Register Agent Way, Des Moines, IA");
        await settings.SetAsync(DigestService.BaseUrlFlag, "https://dev.barbrain.co");

        var summary = await RunAsync(cs, sender, settings);

        Assert.True(summary.Ran);
        Assert.True(summary.Composed >= 2, $"expected both users composed, got {summary.Composed}");
        Assert.Equal(summary.Composed, summary.Delivered);
        Assert.All(sender.Sent, s => Assert.True(s.Deliver));

        var alice = sender.Sent.Single(s => s.Recipient == "alice@digest.test");
        Assert.Contains("Register Agent Way", alice.Html);          // CAN-SPAM physical address
        Assert.Contains("/api/digest/unsubscribe?token=", alice.Html); // CAN-SPAM unsubscribe, under /api/*
        Assert.Contains("palate match", alice.Html);                // match hook block present

        // Events instrumented (ADR-017).
        await using var db = PostgresFixture.CreateContext(cs);
        Assert.Equal(summary.Delivered, await db.Events.CountAsync(e => e.Name == "digest_sent"));
    }

    [SkippableFact]
    public async Task Block_flag_off_drops_that_block()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        var (cs, sender, settings) = await BuildWorldAsync();

        await settings.SetAsync(DigestService.EnabledFlag, "true");
        await settings.SetAsync(DigestService.PhysicalAddressFlag, "123 Test St");
        await settings.SetAsync(DigestComposer.BlockMatchHookFlag, "false");

        await RunAsync(cs, sender, settings);
        Assert.All(sender.Sent, s => Assert.DoesNotContain("palate match", s.Html));
    }

    [SkippableFact]
    public async Task No_physical_address_forces_log_only_never_delivered()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        var (cs, sender, settings) = await BuildWorldAsync();

        await settings.SetAsync(DigestService.EnabledFlag, "true");
        await settings.SetAsync(DigestService.PhysicalAddressFlag, ""); // the CAN-SPAM guard

        var summary = await RunAsync(cs, sender, settings);

        Assert.True(summary.Composed >= 2);
        Assert.Equal(0, summary.Delivered);
        Assert.Equal(summary.Composed, summary.LoggedOnly);
        Assert.All(sender.Sent, s => Assert.False(s.Deliver));
    }

    [SkippableFact]
    public async Task Disabled_flag_skips_the_run_entirely()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        var (cs, sender, settings) = await BuildWorldAsync();
        await settings.SetAsync(DigestService.EnabledFlag, "false");

        var summary = await RunAsync(cs, sender, settings);
        Assert.False(summary.Ran);
        Assert.Empty(sender.Sent);
    }

    [SkippableFact]
    public async Task Unsubscribed_user_is_skipped()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        var (cs, sender, settings) = await BuildWorldAsync();

        await settings.SetAsync(DigestService.EnabledFlag, "true");
        await settings.SetAsync(DigestService.PhysicalAddressFlag, "123 Test St");

        await using (var db = PostgresFixture.CreateContext(cs))
            await db.Users.Where(u => u.Email == "alice@digest.test")
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.DigestUnsubscribedAt, new FixedClock().GetUtcNow()));

        await RunAsync(cs, sender, settings);
        Assert.DoesNotContain(sender.Sent, s => s.Recipient == "alice@digest.test");
        Assert.Contains(sender.Sent, s => s.Recipient == "bob@digest.test");
    }

    // --- world building --------------------------------------------------------

    private async Task<DigestService.RunSummary> RunAsync(
        string cs, CapturingSender sender, ISettingsService settings)
    {
        await using var db = PostgresFixture.CreateContext(cs);
        var clock = new FixedClock();
        var matches = new MatchService(db, settings);
        var recs = new RecommendationService(db, settings, clock, matches);
        var composer = new DigestComposer(db, settings, recs, matches, clock);
        var service = new DigestService(db, composer, sender, settings, clock, NullLogger<DigestService>.Instance);
        return await service.RunOnceAsync(respectEnabledFlag: true);
    }

    private async Task<(string Cs, CapturingSender Sender, ISettingsService Settings)> BuildWorldAsync()
    {
        var cs = await fixture.CreateEmptyDatabaseAsync($"digest_{Guid.NewGuid():N}");
        await using (var db = PostgresFixture.CreateContext(cs))
            await db.Database.MigrateAsync();

        var cache = new MemoryCache(new MemoryCacheOptions());

        // Attribute definitions (feed reasons need display names) + a small
        // synthetic beer catalog with vectors.
        await using (var db = PostgresFixture.CreateContext(cs))
        {
            var settings0 = new SettingsService(db, cache);
            var import = new Api.Catalog.Import.CatalogImportService(
                db,
                new Api.Catalog.AttributeVectorService(db, settings0),
                new Api.Catalog.MergeService(db, settings0, NullLogger<Api.Catalog.MergeService>.Instance),
                NullLogger<Api.Catalog.Import.CatalogImportService>.Instance);
            await import.ImportAttributesAsync(CatalogTestHarness.SeedDir);

            var producer = new Producer
            {
                Name = "Digest Works", NormalizedName = "digest works", Source = "digesttest",
            };
            db.Producers.Add(producer);
            for (var i = 0; i < 16; i++)
            {
                var v = new float[VectorDims.Category];
                v[i % VectorDims.Category] = 0.9f;
                for (var d = 0; d < VectorDims.Category; d++) if (v[d] == 0) v[d] = 0.2f;
                db.Drinks.Add(new Drink
                {
                    Producer = producer, Name = $"Digest Beer {i:D2}",
                    NormalizedName = $"digest beer {i:d2}", Category = DrinkCategory.Beer,
                    Source = "digesttest", CategoryVector = new Vector(v),
                    BridgeVector = new Vector(v[..VectorDims.Bridge]),
                });
            }
            await db.SaveChangesAsync();
        }

        // Two similar, activated, subscribed users who co-rate the same drinks.
        Guid alice, bob;
        await using (var db = PostgresFixture.CreateContext(cs))
        {
            var drinkIds = await db.Drinks.OrderBy(d => d.Name).Select(d => d.Id).ToListAsync();
            alice = await AddUserAsync(db, "alice", drinkIds);
            bob = await AddUserAsync(db, "bob", drinkIds);
        }

        var clock = new FixedClock();
        foreach (var id in new[] { alice, bob })
            await using (var db = PostgresFixture.CreateContext(cs))
                await new PalateProfileService(db, clock).RecomputeAllForUserAsync(id);

        await using (var db = PostgresFixture.CreateContext(cs))
            await new MatchService(db, new SettingsService(db, cache)).ComputeAllAsync();

        return (cs, new CapturingSender(), new SettingsService(PostgresFixture.CreateContext(cs), cache));
    }

    private static async Task<Guid> AddUserAsync(AppDbContext db, string handle, List<Guid> drinkIds)
    {
        var now = new DateTimeOffset(2026, 7, 8, 12, 0, 0, TimeSpan.Zero);
        var user = new User
        {
            UserName = handle, Email = $"{handle}@digest.test", ActivatedAt = now,
        };
        db.Users.Add(user);
        db.UserCategoryInterests.Add(new UserCategoryInterest { UserId = user.Id, Category = DrinkCategory.Beer });
        // Same drinks, same shape → strong match; loved + hated for contrast.
        for (var i = 0; i < drinkIds.Count; i++)
            db.Ratings.Add(new Rating
            {
                CreatedByUserId = user.Id, DrinkId = drinkIds[i],
                Value = i < drinkIds.Count / 2 ? 5.0m : 2.0m,
                Visibility = Visibility.Public, LocationContext = LocationContext.Untagged,
                IsLatest = true, CreatedAt = now, UpdatedAt = now,
            });
        await db.SaveChangesAsync();
        return user.Id;
    }
}
