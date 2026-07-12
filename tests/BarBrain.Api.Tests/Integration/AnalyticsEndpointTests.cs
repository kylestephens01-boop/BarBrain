using System.Net;
using System.Net.Http.Json;
using BarBrain.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BarBrain.Api.Tests.Integration;

/// <summary>
/// Sprint 7 acceptance: the retention dashboard renders cohorts from real
/// data. Signups, WAU, and D1 retention are pinned with planted rows; the
/// endpoint sits behind the admin token (the matrix suite covers the
/// perimeter; this covers the numbers).
/// </summary>
[Collection("postgres")]
public sealed class AnalyticsEndpointTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const string AdminToken = "analytics-test-token";

    private IdentityTestHarness _harness = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        if (!fixture.DockerAvailable) return;
        _harness = new IdentityTestHarness(fixture.ConnectionString);
        await _harness.CleanupIdentityDataAsync();

        // Before the factory's first use, so the setting lands in config.
        _harness.Factory.Settings["Admin:Token"] = AdminToken;
        _client = _harness.CreateClient();
    }

    public async Task DisposeAsync()
    {
        if (_harness is not null) await _harness.DisposeAsync();
    }

    private HttpRequestMessage AdminGet() =>
        new(HttpMethod.Get, "/api/admin/analytics")
        {
            Headers = { { "X-Admin-Token", AdminToken } },
        };

    [SkippableFact]
    public async Task Dashboard_computes_signups_wau_and_d1_retention_from_real_rows()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");

        // Two signups; one rates today (WAU member), the other stays idle.
        var active = await _harness.SignupAsync(_client, "cohort_active");
        var idleClient = _harness.CreateClient();
        await _harness.SignupAsync(idleClient, "cohort_idle");

        var drinkId = await _harness.PlantDrinkAsync("Cohort Cream Ale");
        (await _client.PostAsJsonAsync("/api/ratings",
            new RateRequest(drinkId, 4.0m, null, "public", "home_bar"))).EnsureSuccessStatusCode();

        // Back-date so the D1 window is fully in the past AND contains the
        // rating: signup 2.5 days ago (eligible: >= 2d old), rating at
        // signup + 1.5d (inside the [1d, 2d) day-1 window).
        await using (var db = _harness.CreateDb())
        {
            var signupAt = DateTimeOffset.UtcNow.AddDays(-2.5);
            await db.Users.Where(u => u.Id == active.Id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(u => u.CreatedAt, signupAt)
                    .SetProperty(u => u.AttestedAt, signupAt)
                    .SetProperty(u => u.ActivatedAt, signupAt));
            await db.Ratings.Where(r => r.CreatedByUserId == active.Id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.CreatedAt, signupAt.AddDays(1.5))
                    .SetProperty(r => r.UpdatedAt, signupAt.AddDays(1.5)));
        }

        using var response = await _client.SendAsync(AdminGet());
        response.EnsureSuccessStatusCode();
        var data = (await response.Content.ReadFromJsonAsync<AdminAnalyticsResponse>())!;

        Assert.Equal(2, data.SignupsTotal);
        Assert.Equal(2, data.Signups7d);
        Assert.Equal(100.0, data.ActivationRatePct); // signup==activation on every path today
        Assert.Equal(1, data.Wau);                    // only the rater is active

        // D1: idle user is too young to be eligible (signed up just now);
        // the back-dated user is eligible AND retained.
        Assert.Equal(1, data.D1.Eligible);
        Assert.Equal(1, data.D1.Retained);
        Assert.Equal(100.0, data.D1.Pct);

        // D30: nobody is 31 days old — an empty cohort reads 0, not an error.
        Assert.Equal(0, data.D30.Eligible);
        Assert.Equal(0.0, data.D30.Pct);

        // Weekly pace includes the back-dated rating; thresholds ride along.
        Assert.True(data.RatingsPerWeek.Sum(w => w.Count) >= 1);
        Assert.Equal(3, data.D30KillPct);
        Assert.Equal(7, data.D30ExcellentPct);
        Assert.Equal(1.0, data.RatingsPerActiveUser);
    }

    [SkippableFact]
    public async Task Dashboard_is_admin_gated()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        using var bare = new HttpRequestMessage(HttpMethod.Get, "/api/admin/analytics");
        using var response = await _client.SendAsync(bare);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
