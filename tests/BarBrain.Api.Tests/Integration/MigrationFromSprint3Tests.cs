using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BarBrain.Api.Tests.Integration;

/// <summary>
/// Definition of done: migrations apply cleanly to the PREVIOUS sprint's
/// schema. Replays the world as Sprint 3 left it — TWO activated users (the
/// case that catches the digest-token backfill: a scalar default would give
/// both the empty guid and violate the new unique index) — then upgrades to
/// head and proves the Sprint 4 columns/tables arrived without disturbing the
/// existing rows.
/// </summary>
[Collection("postgres")]
public sealed class MigrationFromSprint3Tests(PostgresFixture fixture)
{
    private const string Sprint3Migration = "20260707015932_Sprint3Palate";

    [SkippableFact]
    public async Task Upgrade_from_sprint3_schema_is_clean_and_backfills_unique_tokens()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");

        var connectionString = await fixture.CreateEmptyDatabaseAsync("migrate_from_sprint3");
        await using var db = PostgresFixture.CreateContext(connectionString);

        // Stop the world at the end of Sprint 3.
        await db.GetService<IMigrator>().MigrateAsync(Sprint3Migration);
        Assert.False(await TableExistsAsync(db, "user_match_neighbors"));
        Assert.False(await ColumnExistsAsync(db, "users", "HideFromMatches"));
        Assert.False(await ColumnExistsAsync(db, "users", "DigestUnsubscribeToken"));

        // Two Sprint 3-era users. Raw SQL because the current model has Sprint 4
        // columns the Sprint 3 schema doesn't yet have.
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO users ("Id", "Handle", "NormalizedUserName", "BirthYear", "AttestedAt", "ActivatedAt", "CreatedAt")
            VALUES ('00000000-0000-0000-0000-000000000001', 'alice', 'ALICE', 1990, now(), now(), now()),
                   ('00000000-0000-0000-0000-000000000002', 'bob',   'BOB',   1991, now(), now(), now());
            """);

        // Upgrade to head.
        await db.Database.MigrateAsync();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());

        // Sprint 4 arrived.
        Assert.True(await TableExistsAsync(db, "user_match_neighbors"));
        Assert.True(await ColumnExistsAsync(db, "users", "HideFromMatches"));
        Assert.True(await ColumnExistsAsync(db, "users", "DigestUnsubscribeToken"));

        // Existing rows survive; hide-me defaulted off; the backfill gave each
        // user a DISTINCT non-empty unsubscribe token (the unique index held).
        var users = await db.Users.AsNoTracking().OrderBy(u => u.UserName).ToListAsync();
        Assert.Equal(2, users.Count);
        Assert.All(users, u => Assert.False(u.HideFromMatches));
        Assert.All(users, u => Assert.Null(u.DigestUnsubscribedAt));
        Assert.All(users, u => Assert.NotEqual(Guid.Empty, u.DigestUnsubscribeToken));
        Assert.NotEqual(users[0].DigestUnsubscribeToken, users[1].DigestUnsubscribeToken);
        Assert.Equal("alice", users[0].UserName);
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
