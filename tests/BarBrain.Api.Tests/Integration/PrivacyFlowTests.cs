using System.Net;
using System.Net.Http.Json;
using BarBrain.Api.Badges;
using BarBrain.Api.Data.Entities;
using BarBrain.Api.Privacy;
using BarBrain.Shared.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace BarBrain.Api.Tests.Integration;

/// <summary>
/// Sprint 7 acceptance: privacy self-serve (ADR-018). Export downloads valid
/// JSON with the owner's data only (badges included, per the Sprint 7 spec);
/// both deletion paths work end to end with DB state verified; the grace
/// period holds; cancel undoes the request.
/// </summary>
[Collection("postgres")]
public sealed class PrivacyFlowTests(PostgresFixture fixture) : IAsyncLifetime
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
        await db.Checkins.ExecuteDeleteAsync();
        await db.VenueMenuItems.ExecuteDeleteAsync();
        await _harness.CleanupIdentityDataAsync();
    }

    private async Task SeedBadgeDefinitionsAsync()
    {
        await using var db = _harness.CreateDb();
        var apiProjectDir = Path.GetDirectoryName(CatalogTestHarness.SeedDir)!;
        await BadgeSeeder.SeedAsync(db, apiProjectDir, NullLogger.Instance);
    }

    private async Task RateAsync(HttpClient client, Guid drinkId, string visibility = "public", string? note = null)
    {
        var response = await client.PostAsJsonAsync("/api/ratings", new RateRequest(
            drinkId, 4.0m, note, visibility, "home_bar"));
        response.EnsureSuccessStatusCode();
    }

    private async Task SetGraceDaysAsync(int days)
        => (await _client.PutAsJsonAsync($"/api/admin/settings/{AccountDataService.GraceDaysFlag}",
            new SettingUpdateRequest(days.ToString()))).EnsureSuccessStatusCode();

    private async Task<int> ExecuteDueDeletionsAsync()
    {
        using var scope = _harness.Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AccountDataService>()
            .ExecuteDueDeletionsAsync();
    }

    // ======================================================================

    [SkippableFact]
    public async Task Export_is_valid_json_with_own_data_only_including_badges()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");

        var me = await _harness.SignupAsync(_client, "export_owner");
        var stranger = _harness.CreateClient();
        await _harness.SignupAsync(stranger, "export_stranger");

        var drinkId = await _harness.PlantDrinkAsync("Export Ale");
        await RateAsync(_client, drinkId, note: "my private thoughts");
        await RateAsync(stranger, await _harness.PlantDrinkAsync("Stranger Stout"));

        var response = await _client.GetAsync("/api/account/export");
        response.EnsureSuccessStatusCode();
        Assert.Contains("attachment", response.Content.Headers.ContentDisposition?.DispositionType
            ?? response.Headers.GetValues("Content-Disposition").First());

        var export = await response.Content.ReadFromJsonAsync<AccountExport>();
        Assert.NotNull(export);
        Assert.Equal(me.Id, export!.Profile.Id);
        Assert.Equal("export_owner", export.Profile.Handle);
        Assert.NotNull(export.Profile.Email);

        // Rating history with the note; the first-rating badge; no stranger data.
        var rating = Assert.Single(export.Ratings);
        Assert.Equal("Export Ale", rating.Drink);
        Assert.Equal("my private thoughts", rating.Note);
        Assert.Contains(export.Badges, b => b.Slug == "first-taste");
        Assert.DoesNotContain(export.Ratings, r => r.Drink == "Stranger Stout");
    }

    [SkippableFact]
    public async Task Deletion_requires_password_grace_holds_and_cancel_undoes()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");

        await _harness.SignupAsync(_client, "grace_user");
        await SetGraceDaysAsync(AccountDataService.DefaultGraceDays);

        // Wrong password → refused; nothing scheduled.
        var wrong = await _client.PostAsJsonAsync("/api/account/delete",
            new DeletionRequest("anonymize", "not-my-password"));
        Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _client.GetAsync("/api/account/deletion")).StatusCode);

        // Correct password schedules; the status endpoint reports it.
        var ok = await _client.PostAsJsonAsync("/api/account/delete",
            new DeletionRequest("anonymize", "correct-horse-battery"));
        ok.EnsureSuccessStatusCode();
        var status = await ok.Content.ReadFromJsonAsync<DeletionStatusResponse>();
        Assert.Equal("anonymize", status!.Mode);
        Assert.Equal(status.RequestedAt.AddDays(AccountDataService.DefaultGraceDays), status.EffectiveAt);

        // A second request while pending is refused (mode changes go via cancel).
        var dup = await _client.PostAsJsonAsync("/api/account/delete",
            new DeletionRequest("delete", "correct-horse-battery"));
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);

        // Grace period: nothing executes yet.
        Assert.Equal(0, await ExecuteDueDeletionsAsync());

        // Cancel → clean slate; the account is untouched.
        (await _client.PostAsync("/api/account/delete/cancel", null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NoContent, (await _client.GetAsync("/api/account/deletion")).StatusCode);
        await using var db = _harness.CreateDb();
        Assert.NotNull(await db.Users.SingleOrDefaultAsync(u => u.UserName == "grace_user"));
    }

    [SkippableFact]
    public async Task Full_delete_removes_data_releases_shared_rows_and_scrubs_events()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");

        var me = await _harness.SignupAsync(_client, "full_delete_user");
        var drinkId = await _harness.PlantDrinkAsync("Doomed Lager");
        await RateAsync(_client, drinkId);

        // Simulate a wiki contribution: the user authored a public catalog drink.
        await using (var db = _harness.CreateDb())
        {
            await db.Drinks.Where(d => d.Id == drinkId)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.CreatedByUserId, me.Id));
        }

        await SetGraceDaysAsync(0);
        (await _client.PostAsJsonAsync("/api/account/delete",
            new DeletionRequest("delete", "correct-horse-battery"))).EnsureSuccessStatusCode();

        Assert.Equal(1, await ExecuteDueDeletionsAsync());

        await using var check = _harness.CreateDb();
        // User row, ratings, and the Home Bar are gone.
        Assert.Null(await check.Users.SingleOrDefaultAsync(u => u.Id == me.Id));
        Assert.False(await check.Ratings.AnyAsync(r => r.CreatedByUserId == me.Id));
        Assert.False(await check.Venues.AnyAsync(v => v.OwnerUserId == me.Id));

        // The shared public drink survives, ownerless — other users' data is never collateral.
        var drink = await check.Drinks.SingleAsync(d => d.Id == drinkId);
        Assert.Null(drink.CreatedByUserId);

        // First-party events no longer reference the user (jsonb key scrubbed).
        var uid = me.Id.ToString();
        var events = await check.Events.AsNoTracking().ToListAsync();
        Assert.DoesNotContain(events, e =>
            e.Properties is not null && e.Properties.TryGetValue("userId", out var v) && v == uid);
        Assert.Contains(events, e => e.Name == "account_deleted");
    }

    [SkippableFact]
    public async Task Anonymize_keeps_public_contributions_and_purges_pii()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");

        var me = await _harness.SignupAsync(_client, "anon_user");
        var publicDrink = await _harness.PlantDrinkAsync("Kept Kolsch");
        var privateDrink = await _harness.PlantDrinkAsync("Secret Saison");
        await RateAsync(_client, publicDrink, "public", note: "a public review");
        await RateAsync(_client, privateDrink, "private");

        await SetGraceDaysAsync(0);
        (await _client.PostAsJsonAsync("/api/account/delete",
            new DeletionRequest("anonymize", "correct-horse-battery"))).EnsureSuccessStatusCode();

        Assert.Equal(1, await ExecuteDueDeletionsAsync());

        await using var check = _harness.CreateDb();
        var user = await check.Users.SingleAsync(u => u.Id == me.Id);

        // PII purged; the row can never sign in again.
        Assert.StartsWith("anonymous_", user.UserName);
        Assert.Null(user.Email);
        Assert.Null(user.PasswordHash);
        Assert.Null(user.BirthYear);
        Assert.Null(user.AttestedAt);
        Assert.Null(user.ActivatedAt);
        Assert.True(user.HideFromMatches);
        Assert.Null(user.DeletionRequestedAt);

        // Public contribution stays, reassigned to the anonymous row; the
        // private rating (and the Home Bar it pointed at) is gone.
        var kept = await check.Ratings.SingleAsync(r => r.CreatedByUserId == me.Id);
        Assert.Equal(publicDrink, kept.DrinkId);
        Assert.Equal("a public review", kept.Note);
        Assert.True(kept.IsLatest);
        Assert.Equal("untagged", kept.LocationContext);
        Assert.Null(kept.VenueId);
        Assert.False(await check.Venues.AnyAsync(v => v.OwnerUserId == me.Id));

        // Sign-in with the old credentials is dead.
        var login = await _harness.CreateClient().PostAsJsonAsync("/api/auth/login",
            new LoginRequest("anon_user@example.test", "correct-horse-battery"));
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }
}
