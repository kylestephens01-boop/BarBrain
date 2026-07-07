using BarBrain.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BarBrain.Api.Tests.Integration;

/// <summary>
/// ADR-026's second layer: even if every app check regresses, the DATABASE
/// refuses bad identity/rating rows. Each test bypasses the API and inserts
/// directly, expecting the named constraint to throw.
/// </summary>
[Collection("postgres")]
public sealed class Sprint2ConstraintTests(PostgresFixture fixture) : IAsyncLifetime
{
    private IdentityTestHarness _harness = null!;
    private Guid _userId;
    private Guid _drinkId;

    public async Task InitializeAsync()
    {
        if (!fixture.DockerAvailable) return;
        _harness = new IdentityTestHarness(fixture.ConnectionString);
        await _harness.CleanupIdentityDataAsync();
        var me = await _harness.SignupAsync(_harness.CreateClient(), "ck_user");
        _userId = me.Id;
        _drinkId = await _harness.PlantDrinkAsync("Constraint IPA");
    }

    public async Task DisposeAsync()
    {
        if (_harness is not null) await _harness.DisposeAsync();
    }

    private async Task<PostgresException> ExpectRejectionAsync(Func<Data.AppDbContext, Task> act)
    {
        await using var db = _harness.CreateDb();
        var ex = await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await act(db);
            await db.SaveChangesAsync();
        });
        var pg = Assert.IsType<PostgresException>(ex.InnerException ?? ex, exactMatch: false);
        return pg;
    }

    private Rating ValidRating() => new()
    {
        CreatedByUserId = _userId,
        DrinkId = _drinkId,
        Value = 3.5m,
        LocationContext = "untagged",
        IsLatest = false, // avoid tripping the latest unique in unrelated tests
    };

    [SkippableFact]
    public async Task Off_grid_rating_value_is_refused_by_the_database()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        var pg = await ExpectRejectionAsync(db =>
        {
            var r = ValidRating();
            r.Value = 3.3m;
            db.Ratings.Add(r);
            return Task.CompletedTask;
        });
        Assert.Equal("ck_ratings_value", pg.ConstraintName);
    }

    [SkippableFact]
    public async Task Unknown_visibility_vocabulary_is_refused()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        var pg = await ExpectRejectionAsync(db =>
        {
            var r = ValidRating();
            r.Visibility = "friends_only";
            db.Ratings.Add(r);
            return Task.CompletedTask;
        });
        Assert.Equal("ck_ratings_visibility", pg.ConstraintName);
    }

    [SkippableFact]
    public async Task Untagged_rating_with_a_venue_ref_is_refused()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        var pg = await ExpectRejectionAsync(async db =>
        {
            var homeBar = await db.Venues.SingleAsync(v => v.OwnerUserId == _userId);
            var r = ValidRating();
            r.VenueId = homeBar.Id; // untagged + venue ref = contradiction
            db.Ratings.Add(r);
        });
        Assert.Equal("ck_ratings_venue_pairing", pg.ConstraintName);
    }

    [SkippableFact]
    public async Task Two_latest_rows_per_user_and_drink_are_impossible()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        var pg = await ExpectRejectionAsync(db =>
        {
            var first = ValidRating();
            first.IsLatest = true;
            var second = ValidRating();
            second.IsLatest = true;
            db.Ratings.AddRange(first, second);
            return Task.CompletedTask;
        });
        Assert.Equal("ux_ratings_latest_per_user_drink", pg.ConstraintName);
    }

    [SkippableFact]
    public async Task A_public_home_bar_is_impossible()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        var pg = await ExpectRejectionAsync(db =>
        {
            db.Venues.Add(new Venue
            {
                Name = "Leaky Home Bar",
                VenueType = "home_bar",
                OwnerUserId = _userId,
                Visibility = "public", // Home Bars are private, always (ADR-015)
            });
            return Task.CompletedTask;
        });
        Assert.Equal("ck_venues_home_bar_private", pg.ConstraintName);
    }

    [SkippableFact]
    public async Task A_second_home_bar_per_user_is_impossible()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        var pg = await ExpectRejectionAsync(db =>
        {
            db.Venues.Add(new Venue
            {
                Name = "Home Bar 2",
                VenueType = "home_bar",
                OwnerUserId = _userId, // signup already created one
                Visibility = "private",
            });
            return Task.CompletedTask;
        });
        Assert.Equal("ux_venues_one_home_bar_per_user", pg.ConstraintName);
    }

    [SkippableFact]
    public async Task An_ownerless_private_venue_is_impossible()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        var pg = await ExpectRejectionAsync(db =>
        {
            db.Venues.Add(new Venue { Name = "Ghost Cellar", VenueType = "venue", Visibility = "private" });
            return Task.CompletedTask;
        });
        Assert.Equal("ck_venues_owner_visibility", pg.ConstraintName);
    }

    [SkippableFact]
    public async Task Activation_without_the_age_gate_is_impossible()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        var pg = await ExpectRejectionAsync(async db =>
        {
            // Bypass the app entirely: try to activate a user with no gate data.
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO users ("Id", "CreatedAt", "EmailConfirmed", "LockoutEnabled", "AccessFailedCount", "ActivatedAt")
                VALUES (gen_random_uuid(), now(), false, false, 0, now())
                """);
        });
        Assert.Equal("ck_users_activation_requires_gate", pg.ConstraintName);
    }

    [SkippableFact]
    public async Task Uppercase_handles_are_refused_at_the_database()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        var pg = await ExpectRejectionAsync(async db =>
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO users ("Id", "Handle", "CreatedAt", "EmailConfirmed", "LockoutEnabled", "AccessFailedCount")
                VALUES (gen_random_uuid(), 'MixedCase', now(), false, false, 0)
                """);
        });
        Assert.Equal("ck_users_handle_lowercase", pg.ConstraintName);
    }

    [SkippableFact]
    public async Task Implausible_birth_years_are_refused()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        var pg = await ExpectRejectionAsync(async db =>
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO users ("Id", "BirthYear", "CreatedAt", "EmailConfirmed", "LockoutEnabled", "AccessFailedCount")
                VALUES (gen_random_uuid(), 1850, now(), false, false, 0)
                """);
        });
        Assert.Equal("ck_users_birth_year", pg.ConstraintName);
    }
}
