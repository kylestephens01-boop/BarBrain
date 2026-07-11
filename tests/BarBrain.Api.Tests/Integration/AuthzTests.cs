using System.Net;
using System.Net.Http.Json;
using BarBrain.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BarBrain.Api.Tests.Integration;

/// <summary>
/// THE SPRINT 2 SECURITY SPINE (founder directive): user A must not be able to
/// read or modify user B's private ratings or PII through any endpoint. These
/// run against the real HTTP pipeline — cookie auth, endpoint filters, and the
/// ADR-026 ownership/visibility filtering in RatingService — because the
/// phone-review model cannot eyeball access-control bugs; CI must catch them.
/// </summary>
[Collection("postgres")]
public sealed class AuthzTests(PostgresFixture fixture) : IAsyncLifetime
{
    private IdentityTestHarness _harness = null!;
    private HttpClient _alice = null!;   // owner
    private HttpClient _mallory = null!; // attacker
    private HttpClient _anon = null!;    // signed out
    private Guid _drinkId;
    private RatingDto _alicePrivate = null!;
    private RatingDto _alicePublic = null!;

    // xUnit runs InitializeAsync PER TEST (fresh class instance each time), so
    // this wipes identity data and rebuilds the two-persona world every test.
    public async Task InitializeAsync()
    {
        if (!fixture.DockerAvailable) return; // tests Skip themselves

        _harness = new IdentityTestHarness(fixture.ConnectionString);
        await _harness.CleanupIdentityDataAsync();

        _alice = _harness.CreateClient();
        _mallory = _harness.CreateClient();
        _anon = _harness.CreateClient();

        await _harness.SignupAsync(_alice, "authz_alice");
        await _harness.SignupAsync(_mallory, "authz_mallory");

        // This suite attacks OWNERSHIP/VISIBILITY, not provenance weighting —
        // disable the Sprint 6 eligibility floors so fresh personas count in
        // aggregates (ProvenanceWeightingTests owns eligibility behavior).
        await _harness.SetProvenanceFlagsAsync(_alice, minAgeDays: 0, minRatings: 0);

        _drinkId = await _harness.PlantDrinkAsync("Authz Test Ale");
        var secondDrink = await _harness.PlantDrinkAsync("Authz Test Stout");

        _alicePrivate = await RateAsync(_alice, _drinkId, 4.5m, "private", "my secret cellar note");
        _alicePublic = await RateAsync(_alice, secondDrink, 3.0m, "public", "public note");
    }

    public async Task DisposeAsync()
    {
        if (_harness is not null) await _harness.DisposeAsync();
    }

    private static async Task<RatingDto> RateAsync(
        HttpClient client, Guid drinkId, decimal value, string visibility, string? note)
    {
        var response = await client.PostAsJsonAsync("/api/ratings",
            new RateRequest(drinkId, value, note, visibility, "home_bar"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RatingDto>())!;
    }

    // ---- READ paths ---------------------------------------------------------

    [SkippableFact]
    public async Task Private_rating_is_absent_from_the_public_drink_endpoint()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        var body = await _anon.GetFromJsonAsync<DrinkRatingsResponse>(
            $"/api/catalog/drinks/{_drinkId}/ratings");
        Assert.NotNull(body);
        Assert.Equal(0, body!.PublicCount);
        Assert.Null(body.Average);
        Assert.Empty(body.Recent);

        // …while the owner still sees it in their own journal.
        var journal = await _alice.GetFromJsonAsync<PagedResult<RatingDto>>("/api/ratings/mine");
        Assert.Contains(journal!.Items, r => r.Id == _alicePrivate.Id);
    }

    [SkippableFact]
    public async Task Attacker_journal_never_contains_the_victims_ratings()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        var journal = await _mallory.GetFromJsonAsync<PagedResult<RatingDto>>("/api/ratings/mine");
        Assert.NotNull(journal);
        Assert.DoesNotContain(journal!.Items, r => r.Id == _alicePrivate.Id || r.Id == _alicePublic.Id);
    }

    [SkippableFact]
    public async Task Attacker_cannot_read_victims_per_drink_history()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        var mine = await _mallory.GetFromJsonAsync<List<RatingDto>>(
            $"/api/ratings/mine/drink/{_drinkId}");
        Assert.NotNull(mine);
        Assert.Empty(mine!); // Alice's private rating on this drink never appears
    }

    [SkippableFact]
    public async Task Public_rating_payload_carries_no_pii()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        // The one place strangers see rating data: handle + stars + note only.
        var raw = await _anon.GetStringAsync(
            $"/api/catalog/drinks/{_alicePublic.DrinkId}/ratings");
        Assert.Contains("authz_alice", raw); // pseudonymous handle IS expected
        Assert.DoesNotContain("email", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("birth", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@example.test", raw);
        Assert.DoesNotContain("userId", raw);
        Assert.DoesNotContain("location", raw, StringComparison.OrdinalIgnoreCase); // Home Bar must not leak
    }

    [SkippableFact]
    public async Task Me_only_ever_returns_the_callers_own_identity()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        var me = await _mallory.GetFromJsonAsync<MeResponse>("/api/auth/me");
        Assert.Equal("authz_mallory", me!.Handle);
        Assert.Equal("authz_mallory@example.test", me.Email);
    }

    // ---- WRITE paths --------------------------------------------------------

    [SkippableFact]
    public async Task Attacker_cannot_modify_victims_rating_and_gets_404_not_403()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        var response = await _mallory.PatchAsJsonAsync(
            $"/api/ratings/{_alicePrivate.Id}", new RatingUpdateRequest("defaced", "public"));
        // 404, not 403: the row's existence must not leak.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        await using var db = _harness.CreateDb();
        var row = await db.Ratings.AsNoTracking().SingleAsync(r => r.Id == _alicePrivate.Id);
        Assert.Equal("private", row.Visibility);
        Assert.Equal("my secret cellar note", row.Note);
    }

    [SkippableFact]
    public async Task Attacker_cannot_delete_victims_rating()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        var response = await _mallory.DeleteAsync($"/api/ratings/{_alicePrivate.Id}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        await using var db = _harness.CreateDb();
        Assert.True(await db.Ratings.AnyAsync(r => r.Id == _alicePrivate.Id));
    }

    [SkippableFact]
    public async Task Attacker_cannot_rate_into_someone_elses_home_bar()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        await using var db = _harness.CreateDb();
        var aliceHomeBar = await db.Venues.AsNoTracking()
            .SingleAsync(v => v.Owner!.UserName == "authz_alice" && v.VenueType == "home_bar");

        // Passing Alice's Home Bar id under the 'venue' context must 404.
        var response = await _mallory.PostAsJsonAsync("/api/ratings",
            new RateRequest(_drinkId, 3.0m, null, "public", "venue", aliceHomeBar.Id));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        // And 'home_bar' context always lands in the CALLER'S own Home Bar.
        var own = await RateAsync(_mallory, _drinkId, 3.0m, "public", null);
        Assert.Equal("Home Bar", own.VenueName);
        var row = await db.Ratings.AsNoTracking().SingleAsync(r => r.Id == own.Id);
        Assert.NotEqual(aliceHomeBar.Id, row.VenueId);
    }

    // ---- Anonymous ----------------------------------------------------------

    [SkippableFact]
    public async Task Anonymous_callers_get_401_from_every_rating_write_and_journal_read()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        Assert.Equal(HttpStatusCode.Unauthorized, (await _anon.PostAsJsonAsync("/api/ratings",
            new RateRequest(_drinkId, 3.0m, null, null, "untagged"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _anon.GetAsync("/api/ratings/mine")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _anon.GetAsync($"/api/ratings/mine/drink/{_drinkId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _anon.PatchAsJsonAsync(
            $"/api/ratings/{_alicePrivate.Id}", new RatingUpdateRequest(null, "public"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _anon.DeleteAsync($"/api/ratings/{_alicePrivate.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _anon.GetAsync("/api/auth/me")).StatusCode);
    }

    [SkippableFact]
    public async Task Visibility_flip_to_private_removes_it_from_the_drink_page()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        // Gate B's phone check, as CI: public rating visible → flip private → gone.
        var drink = await _harness.PlantDrinkAsync("Authz Flip Lager");
        var rating = await RateAsync(_alice, drink, 5.0m, "public", "great");

        var before = await _anon.GetFromJsonAsync<DrinkRatingsResponse>($"/api/catalog/drinks/{drink}/ratings");
        Assert.Equal(1, before!.PublicCount);

        var patch = await _alice.PatchAsJsonAsync($"/api/ratings/{rating.Id}", new RatingUpdateRequest(null, "private"));
        patch.EnsureSuccessStatusCode();

        var after = await _anon.GetFromJsonAsync<DrinkRatingsResponse>($"/api/catalog/drinks/{drink}/ratings");
        Assert.Equal(0, after!.PublicCount);
        Assert.Empty(after.Recent);
    }
}
