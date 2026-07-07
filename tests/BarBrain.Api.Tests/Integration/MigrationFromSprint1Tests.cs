using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BarBrain.Api.Tests.Integration;

/// <summary>
/// Definition of done: migrations apply cleanly to the PREVIOUS sprint's
/// schema. Replays the world as Sprint 1 left it — including a users-stub row
/// with a handle, which is exactly what the additive Identity extension must
/// not disturb — then upgrades to head.
/// </summary>
[Collection("postgres")]
public sealed class MigrationFromSprint1Tests(PostgresFixture fixture)
{
    private const string Sprint1Migration = "20260706220913_Sprint1Catalog";

    [SkippableFact]
    public async Task Upgrade_from_sprint1_schema_is_clean_and_preserves_data()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");

        var connectionString = await fixture.CreateEmptyDatabaseAsync("migrate_from_sprint1");
        await using var db = PostgresFixture.CreateContext(connectionString);

        // Stop the world at the end of Sprint 1.
        await db.GetService<IMigrator>().MigrateAsync(Sprint1Migration);
        Assert.False(await TableExistsAsync(db, "ratings"));
        Assert.False(await ColumnExistsAsync(db, "users", "BirthYear"));

        // Data as the Sprint 1 app shaped it: a users-stub row + catalog rows.
        // Raw SQL because the CURRENT entity model has Sprint 2 columns.
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO users ("Id", "Handle", "CreatedAt")
            VALUES ('00000000-0000-0000-0000-000000000001', 'stub_survivor', now());
            INSERT INTO producers ("Id", "Name", "NormalizedName", "Source", "Visibility", "Status", "CreatedAt", "UpdatedAt")
            VALUES ('00000000-0000-0000-0000-000000000002', 'Survivor Brewing', 'survivor brewing', 'test', 'public', 'active', now(), now());
            INSERT INTO drinks ("Id", "ProducerId", "Name", "NormalizedName", "Category", "Source", "Visibility", "Status", "CreatedAt", "UpdatedAt")
            VALUES ('00000000-0000-0000-0000-000000000003', '00000000-0000-0000-0000-000000000002', 'Survivor Ale', 'survivor ale', 'beer', 'test', 'public', 'active', now(), now());
            """);

        // Upgrade to head.
        await db.Database.MigrateAsync();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());

        // Sprint 2 arrived…
        Assert.True(await TableExistsAsync(db, "ratings"));
        Assert.True(await TableExistsAsync(db, "venues"));
        Assert.True(await TableExistsAsync(db, "user_logins"));
        Assert.True(await ColumnExistsAsync(db, "users", "BirthYear"));

        // …and the Sprint 1 rows are intact and readable through the NEW model.
        var user = await db.Users.AsNoTracking()
            .SingleAsync(u => u.Id == Guid.Parse("00000000-0000-0000-0000-000000000001"));
        Assert.Equal("stub_survivor", user.UserName); // Handle column, untouched
        Assert.Null(user.BirthYear);
        Assert.Null(user.ActivatedAt);
        Assert.Equal("Survivor Ale",
            (await db.Drinks.AsNoTracking().SingleAsync(d => d.NormalizedName == "survivor ale")).Name);
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
