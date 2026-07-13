using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BarBrain.Api.Tests.Integration;

/// <summary>
/// Definition of done: migrations apply cleanly to the PREVIOUS sprint's
/// schema. Stops the world at Sprint 6, plants a user, upgrades to head, and
/// proves Sprint 7 arrived additively: the deletion columns exist, are NULL
/// on the old row, and their CHECK constraints hold.
/// </summary>
[Collection("postgres")]
public sealed class MigrationFromSprint6Tests(PostgresFixture fixture)
{
    private const string Sprint6Migration = "20260711011835_Sprint6GamificationModeration";

    [SkippableFact]
    public async Task Upgrade_from_sprint6_schema_is_purely_additive()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");

        var connectionString = await fixture.CreateEmptyDatabaseAsync("migrate_from_sprint6");
        await using var db = PostgresFixture.CreateContext(connectionString);

        // Stop the world at the end of Sprint 6.
        await db.GetService<IMigrator>().MigrateAsync(Sprint6Migration);
        Assert.False(await ColumnExistsAsync(db, "users", "DeletionRequestedAt"));
        Assert.False(await ColumnExistsAsync(db, "users", "DeletionMode"));

        // A Sprint 6-era row, raw SQL (the current model has Sprint 7 columns).
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO users ("Id", "Handle", "NormalizedUserName", "BirthYear", "AttestedAt", "ActivatedAt", "CreatedAt", "DigestUnsubscribeToken")
            VALUES ('00000000-0000-0000-0000-000000000001', 'oldster', 'OLDSTER', 1988, now(), now(), now(), gen_random_uuid());
            """);

        // Upgrade to head.
        await db.Database.MigrateAsync();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());

        // Sprint 7 arrived, additively: columns exist, NULL on the old row.
        Assert.True(await ColumnExistsAsync(db, "users", "DeletionRequestedAt"));
        Assert.True(await ColumnExistsAsync(db, "users", "DeletionMode"));
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.UserName == "oldster");
        Assert.Null(user.DeletionRequestedAt);
        Assert.Null(user.DeletionMode);

        // The pairing CHECK is live: a mode without a timestamp is refused.
        await Assert.ThrowsAsync<Microsoft.EntityFrameworkCore.DbUpdateException>(async () =>
        {
            await db.Database.ExecuteSqlRawAsync(
                """UPDATE users SET "DeletionMode" = 'delete' WHERE "Handle" = 'oldster'""");
        });
    }

    private static async Task<bool> ColumnExistsAsync(DbContext db, string table, string column)
    {
        // Table/column names are compile-time constants from this test —
        // no injection surface.
#pragma warning disable EF1002
        return await db.Database.SqlQueryRaw<bool>(
            $"""
            SELECT EXISTS(SELECT 1 FROM information_schema.columns
                          WHERE table_schema = 'public' AND table_name = '{table}'
                            AND column_name = '{column}') AS "Value"
            """).SingleAsync();
#pragma warning restore EF1002
    }
}
