using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BarBrain.Api.Tests.Integration;

/// <summary>
/// Definition of done: migrations apply cleanly to the PREVIOUS sprint's
/// schema. Stops the world at Sprint 5, plants a user + rating, upgrades to
/// head, and proves the Sprint 6 surface arrived — all additive: the new
/// tables exist, the new columns exist and are NULL on old rows, and the old
/// row survives untouched.
/// </summary>
[Collection("postgres")]
public sealed class MigrationFromSprint5Tests(PostgresFixture fixture)
{
    private const string Sprint5Migration = "20260710200645_Sprint5Venues";

    [SkippableFact]
    public async Task Upgrade_from_sprint5_schema_is_purely_additive()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");

        var connectionString = await fixture.CreateEmptyDatabaseAsync("migrate_from_sprint5");
        await using var db = PostgresFixture.CreateContext(connectionString);

        // Stop the world at the end of Sprint 5.
        await db.GetService<IMigrator>().MigrateAsync(Sprint5Migration);
        Assert.False(await TableExistsAsync(db, "badge_definitions"));
        Assert.False(await TableExistsAsync(db, "user_badges"));
        Assert.False(await TableExistsAsync(db, "reports"));
        Assert.False(await TableExistsAsync(db, "anomaly_flags"));
        Assert.False(await TableExistsAsync(db, "moderation_actions"));
        Assert.False(await ColumnExistsAsync(db, "ratings", "RecSection"));
        Assert.False(await ColumnExistsAsync(db, "ratings", "HiddenAt"));
        Assert.False(await ColumnExistsAsync(db, "users", "ShadowLimitedAt"));

        // Sprint 5-era rows, raw SQL (the current model has Sprint 6 columns).
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO users ("Id", "Handle", "NormalizedUserName", "BirthYear", "AttestedAt", "ActivatedAt", "CreatedAt", "DigestUnsubscribeToken")
            VALUES ('00000000-0000-0000-0000-000000000001', 'oldster', 'OLDSTER', 1988, now(), now(), now(), gen_random_uuid());
            INSERT INTO producers ("Id", "Name", "NormalizedName", "Source", "Visibility", "Status", "CreatedAt", "UpdatedAt")
            VALUES ('00000000-0000-0000-0000-00000000000b', 'Old Brewing', 'old brewing', 'test', 'public', 'active', now(), now());
            INSERT INTO drinks ("Id", "ProducerId", "Name", "NormalizedName", "Category", "Source", "Visibility", "Status", "CreatedAt", "UpdatedAt")
            VALUES ('00000000-0000-0000-0000-00000000000c', '00000000-0000-0000-0000-00000000000b', 'Old Ale', 'old ale', 'beer', 'test', 'public', 'active', now(), now());
            INSERT INTO ratings ("Id", "CreatedByUserId", "DrinkId", "Value", "Visibility", "LocationContext", "Origin", "IsLatest", "CreatedAt", "UpdatedAt")
            VALUES ('00000000-0000-0000-0000-00000000000d', '00000000-0000-0000-0000-000000000001', '00000000-0000-0000-0000-00000000000c', 4.0, 'public', 'untagged', 'user', true, now(), now());
            """);

        // Upgrade to head.
        await db.Database.MigrateAsync();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());

        // Sprint 6 arrived, additively.
        Assert.True(await TableExistsAsync(db, "badge_definitions"));
        Assert.True(await TableExistsAsync(db, "user_badges"));
        Assert.True(await TableExistsAsync(db, "reports"));
        Assert.True(await TableExistsAsync(db, "anomaly_flags"));
        Assert.True(await TableExistsAsync(db, "moderation_actions"));
        Assert.True(await ColumnExistsAsync(db, "ratings", "RecSection"));
        Assert.True(await ColumnExistsAsync(db, "venues", "HiddenAt"));
        Assert.True(await ColumnExistsAsync(db, "drinks", "HiddenAt"));
        Assert.True(await ColumnExistsAsync(db, "users", "BannedAt"));
        Assert.True(await ColumnExistsAsync(db, "users", "ModerationNote"));

        // The old rating survives with every new column at its neutral value.
        var rating = await db.Ratings.AsNoTracking().SingleAsync();
        Assert.Equal(4.0m, rating.Value);
        Assert.Null(rating.RecSection);
        Assert.Null(rating.HiddenAt);
        Assert.Null(rating.HiddenBy);

        var user = await db.Users.AsNoTracking().SingleAsync();
        Assert.Null(user.ShadowLimitedAt);
        Assert.Null(user.BannedAt);
    }

    private static Task<bool> TableExistsAsync(DbContext db, string table)
        => ScalarBoolAsync(db,
            $"SELECT EXISTS(SELECT 1 FROM information_schema.tables WHERE table_schema = 'public' AND table_name = '{table}') AS \"Value\"");

    private static Task<bool> ColumnExistsAsync(DbContext db, string table, string column)
        => ScalarBoolAsync(db,
            $"SELECT EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = '{table}' AND column_name = '{column}') AS \"Value\"");

    private static async Task<bool> ScalarBoolAsync(DbContext db, string sql)
        => await db.Database.SqlQueryRaw<bool>(sql).SingleAsync();
}
