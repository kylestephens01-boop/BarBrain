using System.Net;
using System.Net.Http.Json;
using BarBrain.Api.Badges;
using BarBrain.Api.Data.Entities;
using BarBrain.Shared.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace BarBrain.Api.Tests.Integration;

/// <summary>
/// Sprint 6 acceptance: the badge framework end to end. Definitions come from
/// badges.json (seeded here exactly as startup does); awards land instantly
/// via the inline hooks; the unseen/seen loop drives the toast; the gallery
/// shows earned + locked. Every metric is a distinct-count (ADR-016).
/// </summary>
[Collection("postgres")]
public sealed class BadgeFlowTests(PostgresFixture fixture) : IAsyncLifetime
{
    private IdentityTestHarness _harness = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        if (!fixture.DockerAvailable) return;
        _harness = new IdentityTestHarness(fixture.ConnectionString);
        await CleanupAsync();
        await SeedBadgeDefinitionsAsync();
        _client = _harness.CreateClient();
        await _harness.SignupAsync(_client, "badge_user");
    }

    public async Task DisposeAsync()
    {
        if (_harness is not null) await _harness.DisposeAsync();
    }

    private async Task CleanupAsync()
    {
        await using var db = _harness.CreateDb();
        await db.UserBadges.ExecuteDeleteAsync();
        await db.Reports.ExecuteDeleteAsync();
        await db.AnomalyFlags.ExecuteDeleteAsync();
        await db.ModerationActions.ExecuteDeleteAsync();
        await db.Checkins.ExecuteDeleteAsync();
        await db.VenueMenuItems.ExecuteDeleteAsync();
        await db.MergeQueue.Where(m => m.EntityType == MergeEntityType.Venue).ExecuteDeleteAsync();
        await _harness.CleanupIdentityDataAsync();
    }

    /// <summary>Startup seeds badges from badges.json; the harness skips startup, so replay it.</summary>
    private async Task SeedBadgeDefinitionsAsync()
    {
        await using var db = _harness.CreateDb();
        var apiProjectDir = Path.GetDirectoryName(CatalogTestHarness.SeedDir)!;
        await BadgeSeeder.SeedAsync(db, apiProjectDir, NullLogger.Instance);
    }

    private async Task<Guid> PlantStyledDrinkAsync(string name, string styleName, string category = "beer")
    {
        await using var db = _harness.CreateDb();
        var producer = await db.Producers.FirstOrDefaultAsync(p => p.NormalizedName == "badge harness brewing")
            ?? new Producer { Name = "Badge Harness Brewing", NormalizedName = "badge harness brewing", Source = "test" };
        var style = await db.Styles.FirstOrDefaultAsync(
                s => s.NormalizedName == styleName.ToLowerInvariant() && s.Category == category)
            ?? new Style { Category = category, Name = styleName, NormalizedName = styleName.ToLowerInvariant(), Source = "test" };
        var drink = new Drink
        {
            Producer = producer,
            Name = name,
            NormalizedName = name.ToLowerInvariant(),
            Category = category,
            Style = style,
            Source = "test",
        };
        db.Drinks.Add(drink);
        await db.SaveChangesAsync();
        return drink.Id;
    }

    private async Task RateAsync(Guid drinkId, decimal value = 4.0m, string? recSection = null)
    {
        var response = await _client.PostAsJsonAsync("/api/ratings", new RateRequest(
            drinkId, value, null, "public", "home_bar", RecSection: recSection));
        response.EnsureSuccessStatusCode();
    }

    private async Task<BadgeGalleryResponse> GalleryAsync()
        => (await _client.GetFromJsonAsync<BadgeGalleryResponse>("/api/badges"))!;

    private async Task<UnseenBadgesResponse> UnseenAsync()
        => (await _client.GetFromJsonAsync<UnseenBadgesResponse>("/api/badges/unseen"))!;

    // ======================================================================

    [SkippableFact]
    public async Task Rating_five_styles_awards_breadth_badges_with_toast_flow()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");

        // Rate 5 drinks across 5 distinct styles.
        for (var i = 0; i < 5; i++)
        {
            var drinkId = await PlantStyledDrinkAsync($"Styled Beer {i}", $"Test Style {i}");
            await RateAsync(drinkId);
        }

        // Instant awards: first rating (First Taste) + 5 styles (Style Sampler).
        var unseen = await UnseenAsync();
        var slugs = unseen.Badges.Select(b => b.Slug).ToList();
        Assert.Contains("first-taste", slugs);
        Assert.Contains("style-sampler", slugs);
        Assert.DoesNotContain("style-scholar", slugs); // threshold 15 not met

        // Acknowledge → the toast queue drains and stays drained.
        var seen = await _client.PostAsync("/api/badges/seen", null);
        seen.EnsureSuccessStatusCode();
        Assert.Empty((await UnseenAsync()).Badges);

        // The gallery shows earned + locked, streak card included.
        var gallery = await GalleryAsync();
        Assert.Equal(15, gallery.Badges.Count);
        Assert.True(gallery.Badges.Single(b => b.Slug == "style-sampler").Earned);
        Assert.False(gallery.Badges.Single(b => b.Slug == "style-scholar").Earned);
        Assert.Equal(1, gallery.StreakWeeks);
        Assert.Equal(5, gallery.DistinctDrinksThisWeek);

        // Awards are permanent and idempotent: another rating re-evaluates
        // without duplicating (DB-unique) — count stays stable.
        var again = await PlantStyledDrinkAsync("Styled Beer again", "Test Style 0");
        await RateAsync(again);
        await using var db = _harness.CreateDb();
        Assert.Equal(1, await db.UserBadges.CountAsync(ub => ub.BadgeSlug == "style-sampler"));
    }

    [SkippableFact]
    public async Task Wildcard_rec_section_powers_exploration_badge_and_is_validated()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");

        var drinkId = await PlantStyledDrinkAsync("Wildcard Pick", "Wild Style");

        // Unknown section key → client bug, refused.
        var bad = await _client.PostAsJsonAsync("/api/ratings", new RateRequest(
            drinkId, 4.0m, null, "public", "home_bar", RecSection: "not_a_section"));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        // Rated from the Wildcard section → the exploration badge, instantly.
        await RateAsync(drinkId, recSection: "wildcard");
        var unseen = await UnseenAsync();
        Assert.Contains("wild-card", unseen.Badges.Select(b => b.Slug));

        // Only DISTINCT wildcard drinks count (ADR-016): re-rating the same
        // drink from Wildcard moves nothing toward Open Mind (threshold 5).
        await RateAsync(drinkId, value: 3.5m, recSection: "wildcard");
        await using var scope = _harness.Factory.Services.CreateAsyncScope();
        var badges = scope.ServiceProvider.GetRequiredService<BadgeService>();
        await using var db = _harness.CreateDb();
        var userId = (await db.Users.SingleAsync(u => u.UserName == "badge_user")).Id;
        Assert.Equal(1, await badges.ComputeMetricAsync(userId, BadgeMetric.WildcardDistinctDrinks));
    }

    [SkippableFact]
    public async Task Checkins_at_three_distinct_venues_award_out_and_about()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");

        for (var i = 0; i < 3; i++)
        {
            var add = await _client.PostAsJsonAsync("/api/venues",
                new VenueCreateRequest($"Badge Bar {i}", null, null, null, null));
            add.EnsureSuccessStatusCode();
            var venue = (await add.Content.ReadFromJsonAsync<VenueDto>())!;
            var checkin = await _client.PostAsJsonAsync("/api/checkins", new CheckinRequest(venue.Id));
            checkin.EnsureSuccessStatusCode();

            // Re-checking in at the same venue adds nothing (distinct count).
            var again = await _client.PostAsJsonAsync("/api/checkins", new CheckinRequest(venue.Id));
            again.EnsureSuccessStatusCode();
        }

        var unseen = await UnseenAsync();
        var slugs = unseen.Badges.Select(b => b.Slug).ToList();
        Assert.Contains("out-and-about", slugs);      // 3 distinct venues
        Assert.Contains("pioneer", slugs);            // first wiki contribution (venue add)
        Assert.DoesNotContain("corridor-cartographer", slugs); // 10 not reached
    }

    [SkippableFact]
    public async Task Weekly_streak_badge_uses_the_digest_definition()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");

        var drinkA = await PlantStyledDrinkAsync("Streak Beer A", "Streak Style A");
        var drinkB = await PlantStyledDrinkAsync("Streak Beer B", "Streak Style B");
        await RateAsync(drinkA);
        await RateAsync(drinkB);

        await using var db = _harness.CreateDb();
        var userId = (await db.Users.SingleAsync(u => u.UserName == "badge_user")).Id;

        // Move one rating into last week's bucket → 2 consecutive weeks.
        var older = await db.Ratings.OrderBy(r => r.CreatedAt).FirstAsync();
        older.CreatedAt = DateTimeOffset.UtcNow.AddDays(-8);
        await db.SaveChangesAsync();

        // The threshold crossed WITHOUT a new write — the nightly's job. Run
        // the same evaluation it runs.
        await using var scope = _harness.Factory.Services.CreateAsyncScope();
        var badges = scope.ServiceProvider.GetRequiredService<BadgeService>();
        var awarded = await badges.EvaluateAsync(userId);
        Assert.True(awarded >= 1);

        var gallery = await GalleryAsync();
        Assert.True(gallery.Badges.Single(b => b.Slug == "two-week-wanderer").Earned);
        Assert.Equal(2, gallery.StreakWeeks);
        Assert.False(gallery.Badges.Single(b => b.Slug == "steady-explorer").Earned);
    }

    [SkippableFact]
    public async Task Badges_endpoints_require_auth()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");

        var anonymous = _harness.CreateClient();
        var response = await anonymous.GetAsync("/api/badges");
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }
}
