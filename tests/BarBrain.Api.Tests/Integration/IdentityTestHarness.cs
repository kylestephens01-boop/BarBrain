using System.Net.Http.Json;
using BarBrain.Api.Data;
using BarBrain.Api.Data.Entities;
using BarBrain.Shared.Contracts;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.EntityFrameworkCore;

namespace BarBrain.Api.Tests.Integration;

/// <summary>
/// Boots the REAL API (cookie auth included) against the shared Testcontainers
/// database and provides per-persona HttpClients — each with its own cookie
/// jar, because the whole point of the authz suite is user A's cookie
/// attacking user B's data.
/// </summary>
public sealed class IdentityTestHarness : IAsyncDisposable
{
    public ApiFactory Factory { get; }
    public string ConnectionString { get; }

    public IdentityTestHarness(string connectionString)
    {
        ConnectionString = connectionString;
        Factory = new ApiFactory
        {
            ConnectionStringOverride = connectionString,
            MigrateOnStartup = false, // shared DB is already migrated
            Settings = new Dictionary<string, string?>
            {
                ["Auth:EnableMockExternal"] = "true", // mock Google/Apple in tests
            },
        };
    }

    /// <summary>A browser-like client: cookie jar, NO auto-redirects (tests
    /// inspect Location headers), same-origin.</summary>
    public HttpClient CreateClient()
        => Factory.CreateDefaultClient(new CookieContainerHandler());

    public AppDbContext CreateDb() => PostgresFixture.CreateContext(ConnectionString);

    /// <summary>Adult DOB (well past 21) that encodes the persona for debugging.</summary>
    public static string AdultDob { get; } = "1990-04-17";

    public async Task<MeResponse> SignupAsync(
        HttpClient client, string handle, string? email = null, string dob = "1990-04-17")
    {
        var response = await client.PostAsJsonAsync("/api/auth/signup", new SignupRequest(
            email ?? $"{handle}@example.test", "correct-horse-battery", handle, dob, null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<MeResponse>())!;
    }

    /// <summary>Plants a public catalog drink to rate (producer + drink).</summary>
    public async Task<Guid> PlantDrinkAsync(string name = "Test Pils", string category = "beer")
    {
        await using var db = CreateDb();
        var producer = new Producer
        {
            Name = "Harness Brewing",
            NormalizedName = "harness brewing",
            Source = "test",
        };
        var drink = new Drink
        {
            Producer = producer,
            Name = name,
            NormalizedName = name.ToLowerInvariant(),
            Category = category,
            Source = "test",
        };
        db.Producers.Add(producer);
        db.Drinks.Add(drink);
        await db.SaveChangesAsync();
        return drink.Id;
    }

    public async Task CleanupIdentityDataAsync()
    {
        // Order matters (FKs). Catalog tables are left alone — other suites own them.
        // Sprint 6: check-ins RESTRICT venue deletes, so they go first — a
        // previous suite's leftover check-ins otherwise break THIS suite's
        // cleanup (CI caught it; ordering had been lucky before). Badges/
        // reports/flags would cascade, but explicit deletes keep suites
        // deterministic regardless of what ran before.
        await using var db = CreateDb();
        await db.Checkins.ExecuteDeleteAsync();
        await db.VenueMenuItems.ExecuteDeleteAsync();
        await db.UserBadges.ExecuteDeleteAsync();
        await db.Reports.ExecuteDeleteAsync();
        await db.AnomalyFlags.ExecuteDeleteAsync();
        await db.Ratings.ExecuteDeleteAsync();
        await db.Venues.ExecuteDeleteAsync();
        await db.Database.ExecuteSqlRawAsync("DELETE FROM user_logins; DELETE FROM user_tokens; DELETE FROM user_claims;");
        await db.Users.ExecuteDeleteAsync();
        await db.Events.ExecuteDeleteAsync();
    }

    /// <summary>
    /// Pin the Sprint 6 provenance-weighting flags for this suite (settings
    /// rows persist in the shared DB across suites — every suite that asserts
    /// aggregates pins what it needs, so ordering can't matter). Uses the
    /// admin API so THIS factory's settings cache is invalidated too.
    /// </summary>
    public async Task SetProvenanceFlagsAsync(HttpClient client, int minAgeDays, int minRatings)
    {
        (await client.PutAsJsonAsync("/api/admin/settings/ratings.public_min_account_age_days",
            new SettingUpdateRequest(minAgeDays.ToString()))).EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync("/api/admin/settings/ratings.public_min_rating_count",
            new SettingUpdateRequest(minRatings.ToString()))).EnsureSuccessStatusCode();
    }

    public async ValueTask DisposeAsync() => await Factory.DisposeAsync();
}
