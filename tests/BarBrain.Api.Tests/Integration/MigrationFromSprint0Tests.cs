using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BarBrain.Api.Tests.Integration;

/// <summary>
/// Sprint 1 acceptance: migrations apply cleanly to the PREVIOUS sprint's
/// schema, not just to an empty database. Replays Sprint 0 (InitialCreate)
/// exactly, plants Sprint 0 data, then upgrades to head and proves the data
/// survived and the catalog tables arrived.
/// </summary>
[Collection("postgres")]
public sealed class MigrationFromSprint0Tests(PostgresFixture fixture)
{
    private const string Sprint0Migration = "20260618042846_InitialCreate";

    [SkippableFact]
    public async Task Upgrade_from_sprint0_schema_is_clean_and_preserves_data()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");

        var connectionString = await fixture.CreateEmptyDatabaseAsync("migrate_from_sprint0");
        await using var db = PostgresFixture.CreateContext(connectionString);

        // Stop the world at the end of Sprint 0.
        await db.GetService<IMigrator>().MigrateAsync(Sprint0Migration);
        Assert.False(await TableExistsAsync(db, "drinks"));

        // Data written by the Sprint 0 app…
        db.Settings.Add(new Data.Entities.Setting
        {
            Key = "upgrade.survivor",
            Value = "yes",
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        // …must survive the Sprint 1 upgrade.
        await db.Database.MigrateAsync();

        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.True(await TableExistsAsync(db, "drinks"));
        Assert.True(await TableExistsAsync(db, "merge_queue"));
        Assert.Equal("yes", (await db.Settings.FindAsync("upgrade.survivor"))!.Value);
    }

    private static async Task<bool> TableExistsAsync(DbContext db, string table)
    {
        // Callers pass compile-time constant table names only.
        var sql = "SELECT EXISTS(SELECT 1 FROM information_schema.tables WHERE table_schema = 'public' AND table_name = "
            + $"'{table}') AS \"Value\"";
        return await db.Database.SqlQueryRaw<bool>(sql).SingleAsync();
    }
}
