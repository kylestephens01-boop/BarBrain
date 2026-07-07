using System.Net;
using System.Net.Http.Json;
using BarBrain.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BarBrain.Api.Tests.Integration;

/// <summary>
/// The core loop's business rules (ADR-012): half-step values, append-only
/// re-rating with a single DB-enforced latest, latest-drives-aggregate,
/// delete-promotes-previous, journal history + filters, verification grace.
/// </summary>
[Collection("postgres")]
public sealed class RatingRulesTests(PostgresFixture fixture) : IAsyncLifetime
{
    private IdentityTestHarness _harness = null!;
    private HttpClient _client = null!;
    private Guid _drinkId;

    public async Task InitializeAsync()
    {
        if (!fixture.DockerAvailable) return;
        _harness = new IdentityTestHarness(fixture.ConnectionString);
        await _harness.CleanupIdentityDataAsync();
        _client = _harness.CreateClient();
        await _harness.SignupAsync(_client, "rules_user");
        _drinkId = await _harness.PlantDrinkAsync("Rules Pale Ale");
    }

    public async Task DisposeAsync()
    {
        if (_harness is not null) await _harness.DisposeAsync();
    }

    private Task<HttpResponseMessage> RateAsync(decimal value, string? note = null,
        string visibility = "public", string location = "home_bar")
        => _client.PostAsJsonAsync("/api/ratings",
            new RateRequest(_drinkId, value, note, visibility, location));

    [SkippableTheory]
    [InlineData(3.3)]  // not a half step
    [InlineData(0.5)]  // below floor
    [InlineData(5.5)]  // above ceiling
    [InlineData(0)]
    public async Task Off_grid_values_are_rejected(double bad)
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        var response = await RateAsync((decimal)bad);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_value", (await response.Content.ReadFromJsonAsync<ApiError>())!.Code);
    }

    [SkippableFact]
    public async Task Re_rating_appends_history_and_the_latest_drives_the_aggregate()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        (await RateAsync(2.0m, "first impressions")).EnsureSuccessStatusCode();
        (await RateAsync(4.5m, "it grew on me")).EnsureSuccessStatusCode();

        // Journal keeps BOTH rows, newest first (spec: history kept).
        var journal = await _client.GetFromJsonAsync<PagedResult<RatingDto>>("/api/ratings/mine");
        Assert.Equal(2, journal!.Total);
        Assert.Equal(4.5m, journal.Items[0].Value);
        Assert.True(journal.Items[0].IsLatest);
        Assert.Equal(2.0m, journal.Items[1].Value);
        Assert.False(journal.Items[1].IsLatest);

        // The drink page aggregate uses ONLY the latest (4.5, not the 3.25 mean).
        var pub = await _client.GetFromJsonAsync<DrinkRatingsResponse>($"/api/catalog/drinks/{_drinkId}/ratings");
        Assert.Equal(1, pub!.PublicCount);
        Assert.Equal(4.5m, pub.Average);
    }

    [SkippableFact]
    public async Task Deleting_the_latest_promotes_the_previous_rating()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        (await RateAsync(2.0m)).EnsureSuccessStatusCode();
        var latest = await (await RateAsync(5.0m)).Content.ReadFromJsonAsync<RatingDto>();

        (await _client.DeleteAsync($"/api/ratings/{latest!.Id}")).EnsureSuccessStatusCode();

        var journal = await _client.GetFromJsonAsync<PagedResult<RatingDto>>("/api/ratings/mine");
        var remaining = Assert.Single(journal!.Items);
        Assert.Equal(2.0m, remaining.Value);
        Assert.True(remaining.IsLatest); // stepped back up

        var pub = await _client.GetFromJsonAsync<DrinkRatingsResponse>($"/api/catalog/drinks/{_drinkId}/ratings");
        Assert.Equal(2.0m, pub!.Average);
    }

    [SkippableFact]
    public async Task Note_and_visibility_edit_in_place_without_new_history()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        var rating = await (await RateAsync(3.5m, "draft note")).Content.ReadFromJsonAsync<RatingDto>();

        var patch = await _client.PatchAsJsonAsync($"/api/ratings/{rating!.Id}",
            new RatingUpdateRequest("final note", "private"));
        patch.EnsureSuccessStatusCode();
        var updated = await patch.Content.ReadFromJsonAsync<RatingDto>();
        Assert.Equal("final note", updated!.Note);
        Assert.Equal("private", updated.Visibility);

        var journal = await _client.GetFromJsonAsync<PagedResult<RatingDto>>("/api/ratings/mine");
        Assert.Equal(1, journal!.Total); // edit ≠ append
    }

    [SkippableFact]
    public async Task Location_contexts_resolve_correctly()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        var atHome = await (await RateAsync(4.0m)).Content.ReadFromJsonAsync<RatingDto>();
        Assert.Equal("home_bar", atHome!.LocationContext);
        Assert.Equal("Home Bar", atHome.VenueName);

        var untagged = await (await RateAsync(4.0m, location: "untagged")).Content.ReadFromJsonAsync<RatingDto>();
        Assert.Equal("untagged", untagged!.LocationContext);
        Assert.Null(untagged.VenueName);

        // 'venue' needs a venue id (real venues are Sprint 5; contract complete now).
        var noVenue = await RateAsync(4.0m, location: "venue");
        Assert.Equal(HttpStatusCode.BadRequest, noVenue.StatusCode);
        Assert.Equal("venue_required", (await noVenue.Content.ReadFromJsonAsync<ApiError>())!.Code);

        var nonsense = await RateAsync(4.0m, location: "spaceship");
        Assert.Equal(HttpStatusCode.BadRequest, nonsense.StatusCode);
    }

    [SkippableFact]
    public async Task Journal_category_filter_works()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        var whiskeyId = await _harness.PlantDrinkAsync("Rules Rye", "whiskey");
        (await RateAsync(4.0m)).EnsureSuccessStatusCode();
        (await _client.PostAsJsonAsync("/api/ratings",
            new RateRequest(whiskeyId, 3.0m, null, "public", "home_bar"))).EnsureSuccessStatusCode();

        var beerOnly = await _client.GetFromJsonAsync<PagedResult<RatingDto>>("/api/ratings/mine?category=beer");
        Assert.All(beerOnly!.Items, r => Assert.Equal("beer", r.Category));
        Assert.Equal(1, beerOnly.Total);
    }

    [SkippableFact]
    public async Task First_rating_and_rating_events_are_recorded()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        (await RateAsync(4.0m)).EnsureSuccessStatusCode();
        (await RateAsync(4.5m)).EnsureSuccessStatusCode();

        await using var db = _harness.CreateDb();
        Assert.Equal(1, await db.Events.CountAsync(e => e.Name == "first_rating"));
        Assert.Equal(2, await db.Events.CountAsync(e => e.Name == "rating"));
    }

    [SkippableFact]
    public async Task Rating_locks_after_the_verification_grace_window_lapses()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        // Flag-driven grace (Hard Rule 10). Shrink it to zero → immediate lock.
        var flip = await _client.PutAsJsonAsync("/api/admin/settings/auth.verification_grace_days",
            new SettingUpdateRequest("0"));
        flip.EnsureSuccessStatusCode();
        try
        {
            var blocked = await RateAsync(4.0m);
            Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
            Assert.Equal("verification_required",
                (await blocked.Content.ReadFromJsonAsync<ApiError>())!.Code);

            // Verified users are never blocked.
            await using (var db = _harness.CreateDb())
            {
                await db.Users.Where(u => u.UserName == "rules_user")
                    .ExecuteUpdateAsync(s => s.SetProperty(u => u.EmailConfirmed, true));
            }
            (await RateAsync(4.0m)).EnsureSuccessStatusCode();
        }
        finally
        {
            (await _client.PutAsJsonAsync("/api/admin/settings/auth.verification_grace_days",
                new SettingUpdateRequest("7"))).EnsureSuccessStatusCode();
        }
    }
}
