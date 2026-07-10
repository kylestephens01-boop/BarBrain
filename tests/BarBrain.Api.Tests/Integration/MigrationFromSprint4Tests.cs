using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BarBrain.Api.Tests.Integration;

/// <summary>
/// Definition of done: migrations apply cleanly to the PREVIOUS sprint's
/// schema. Replays the world as Sprint 4 left it — one user WITH a Home Bar
/// (the normal activation path) and one WITHOUT (the backfill case) — then
/// upgrades to head and proves the Sprint 5 venue model arrived, the
/// NormalizedName backfill ran, and every user ends with exactly one Home Bar.
/// </summary>
[Collection("postgres")]
public sealed class MigrationFromSprint4Tests(PostgresFixture fixture)
{
    private const string Sprint4Migration = "20260707034428_Sprint4Matching";

    [SkippableFact]
    public async Task Upgrade_from_sprint4_schema_backfills_home_bars_and_normalized_names()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");

        var connectionString = await fixture.CreateEmptyDatabaseAsync("migrate_from_sprint4");
        await using var db = PostgresFixture.CreateContext(connectionString);

        // Stop the world at the end of Sprint 4.
        await db.GetService<IMigrator>().MigrateAsync(Sprint4Migration);
        Assert.False(await TableExistsAsync(db, "venue_menu_items"));
        Assert.False(await TableExistsAsync(db, "checkins"));
        Assert.False(await ColumnExistsAsync(db, "venues", "Tier"));
        Assert.False(await ColumnExistsAsync(db, "venues", "NormalizedName"));

        // Sprint 4-era rows, raw SQL (the current model has Sprint 5 columns).
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO users ("Id", "Handle", "NormalizedUserName", "BirthYear", "AttestedAt", "ActivatedAt", "CreatedAt", "DigestUnsubscribeToken")
            VALUES ('00000000-0000-0000-0000-000000000001', 'hasbar', 'HASBAR', 1990, now(), now(), now(), gen_random_uuid()),
                   ('00000000-0000-0000-0000-000000000002', 'nobar',  'NOBAR',  1991, now(), now(), now(), gen_random_uuid());
            INSERT INTO venues ("Id", "Name", "VenueType", "OwnerUserId", "Visibility", "CreatedAt")
            VALUES ('00000000-0000-0000-0000-00000000000a', 'My Cellar', 'home_bar',
                    '00000000-0000-0000-0000-000000000001', 'private', now());
            """);

        // Upgrade to head.
        await db.Database.MigrateAsync();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());

        // Sprint 5 arrived.
        Assert.True(await TableExistsAsync(db, "venue_menu_items"));
        Assert.True(await TableExistsAsync(db, "checkins"));
        Assert.True(await ColumnExistsAsync(db, "venues", "Tier"));

        var venues = await db.Venues.AsNoTracking().OrderBy(v => v.CreatedAt).ToListAsync();

        // Exactly one Home Bar per user: the existing one untouched, the
        // missing one backfilled — both active, private, tierless, geo-less.
        Assert.Equal(2, venues.Count);
        var existing = venues.Single(v => v.OwnerUserId == Guid.Parse("00000000-0000-0000-0000-000000000001"));
        var backfilled = venues.Single(v => v.OwnerUserId == Guid.Parse("00000000-0000-0000-0000-000000000002"));

        Assert.Equal("My Cellar", existing.Name);
        Assert.Equal("my cellar", existing.NormalizedName); // data op 1
        Assert.Equal("Home Bar", backfilled.Name);          // data op 2
        Assert.All(venues, v =>
        {
            Assert.Equal("home_bar", v.VenueType);
            Assert.Equal("active", v.Status);
            Assert.Null(v.Tier);
            Assert.Null(v.Latitude);
            Assert.Equal("private", v.Visibility);
        });
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
