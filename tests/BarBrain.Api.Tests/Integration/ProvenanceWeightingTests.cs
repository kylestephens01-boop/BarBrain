using System.Net.Http.Json;
using BarBrain.Api.Data.Entities;
using BarBrain.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BarBrain.Api.Tests.Integration;

/// <summary>
/// Sprint 6 acceptance: a 0-day-old account's ratings are PROVABLY excluded
/// from the public average, and included after graduating both thresholds
/// (≥7 days AND ≥5 latest ratings, config). Graduation is a read-time filter,
/// so the "time travel" is the account aging past the cutoff — simulated by
/// backdating users.CreatedAt, which is exactly the compared column.
/// </summary>
[Collection("postgres")]
public sealed class ProvenanceWeightingTests(PostgresFixture fixture) : IAsyncLifetime
{
    private IdentityTestHarness _harness = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        if (!fixture.DockerAvailable) return;
        _harness = new IdentityTestHarness(fixture.ConnectionString);
        await CleanupAsync();
        _client = _harness.CreateClient();
        await _harness.SignupAsync(_client, "fresh_account");
        // Pin the defaults explicitly: settings rows persist in the shared DB
        // and other suites disable these floors — ordering must not matter.
        await _harness.SetProvenanceFlagsAsync(_client, minAgeDays: 7, minRatings: 5);
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
        await db.ModerationActions.ExecuteDeleteAsync();
        await db.Checkins.ExecuteDeleteAsync();
        await _harness.CleanupIdentityDataAsync();
    }

    private async Task<DrinkRatingsResponse> PublicRatingsAsync(Guid drinkId)
        => (await _client.GetFromJsonAsync<DrinkRatingsResponse>(
            $"/api/catalog/drinks/{drinkId}/ratings"))!;

    [SkippableFact]
    public async Task Young_account_is_excluded_from_the_average_then_included_after_graduation()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");

        var target = await _harness.PlantDrinkAsync("Provenance Pils");
        var rate = await _client.PostAsJsonAsync("/api/ratings",
            new RateRequest(target, 5.0m, null, "public", "home_bar"));
        rate.EnsureSuccessStatusCode();

        // 0-day-old account: the rating is VISIBLE as social content but
        // carries no weight — count 0, no average.
        var before = await PublicRatingsAsync(target);
        Assert.Equal(0, before.PublicCount);
        Assert.Null(before.Average);
        Assert.Single(before.Recent);
        Assert.Equal("fresh_account", before.Recent[0].Handle);

        // Rate 4 more drinks → 5 latest ratings (threshold two).
        for (var i = 0; i < 4; i++)
        {
            var other = await _harness.PlantDrinkAsync($"Filler Beer {i}");
            var r = await _client.PostAsJsonAsync("/api/ratings",
                new RateRequest(other, 3.0m, null, "public", "home_bar"));
            r.EnsureSuccessStatusCode();
        }

        // Still too young: rating count alone doesn't graduate.
        var stillYoung = await PublicRatingsAsync(target);
        Assert.Equal(0, stillYoung.PublicCount);
        Assert.Null(stillYoung.Average);

        // Time travel: age the account past the 7-day cutoff.
        await using (var db = _harness.CreateDb())
        {
            await db.Users.Where(u => u.UserName == "fresh_account")
                .ExecuteUpdateAsync(s => s.SetProperty(
                    u => u.CreatedAt, DateTimeOffset.UtcNow.AddDays(-8)));
        }

        // Graduated: both thresholds met → counted, no recompute job needed.
        var after = await PublicRatingsAsync(target);
        Assert.Equal(1, after.PublicCount);
        Assert.Equal(5.0m, after.Average);
    }

    [SkippableFact]
    public async Task Thresholds_are_config_flags()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");

        var target = await _harness.PlantDrinkAsync("Flagged Porter");
        var rate = await _client.PostAsJsonAsync("/api/ratings",
            new RateRequest(target, 4.0m, null, "public", "home_bar"));
        rate.EnsureSuccessStatusCode();

        // Drop both thresholds via the ADMIN API — it invalidates the settings
        // cache in-process, exactly how an operator flips flags (Hard Rule 10:
        // phase-dependent thresholds are config, not code).
        (await _client.PutAsJsonAsync("/api/admin/settings/ratings.public_min_account_age_days",
            new SettingUpdateRequest("0"))).EnsureSuccessStatusCode();
        (await _client.PutAsJsonAsync("/api/admin/settings/ratings.public_min_rating_count",
            new SettingUpdateRequest("1"))).EnsureSuccessStatusCode();

        try
        {
            var response = await PublicRatingsAsync(target);
            Assert.Equal(1, response.PublicCount);
            Assert.Equal(4.0m, response.Average);
        }
        finally
        {
            (await _client.PutAsJsonAsync("/api/admin/settings/ratings.public_min_account_age_days",
                new SettingUpdateRequest("7"))).EnsureSuccessStatusCode();
            (await _client.PutAsJsonAsync("/api/admin/settings/ratings.public_min_rating_count",
                new SettingUpdateRequest("5"))).EnsureSuccessStatusCode();
        }
    }
}
