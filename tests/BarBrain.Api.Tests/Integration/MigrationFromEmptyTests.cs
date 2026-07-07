using BarBrain.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BarBrain.Api.Tests.Integration;

/// <summary>
/// Sprint 0 marquee acceptance: "Restore-from-empty migration test green in CI."
/// Migrates a brand-new empty database and proves the pgvector extension, both
/// foundation tables, and a row round-trip all land correctly.
/// </summary>
[Collection("postgres")]
public sealed class MigrationFromEmptyTests(PostgresFixture fixture)
{
    [SkippableFact]
    public async Task Migrate_from_empty_creates_pgvector_tables_and_roundtrips()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");

        var connectionString = await fixture.CreateEmptyDatabaseAsync("migrate_from_empty");
        await using var db = PostgresFixture.CreateContext(connectionString);

        // A truly empty DB: the full ordered chain is pending.
        var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
        Assert.Equal(3, pending.Count);
        Assert.EndsWith("InitialCreate", pending[0]);
        Assert.EndsWith("Sprint1Catalog", pending[1]);
        Assert.EndsWith("Sprint2Identity", pending[2]);

        await db.Database.MigrateAsync();

        var applied = (await db.Database.GetAppliedMigrationsAsync()).ToList();
        Assert.Equal(3, applied.Count);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());

        Assert.True(await ScalarBoolAsync(db,
            "SELECT EXISTS(SELECT 1 FROM pg_extension WHERE extname = 'vector') AS \"Value\""));
        Assert.True(await ScalarBoolAsync(db,
            "SELECT EXISTS(SELECT 1 FROM pg_extension WHERE extname = 'pg_trgm') AS \"Value\""));
        Assert.True(await TableExistsAsync(db, "settings"));
        Assert.True(await TableExistsAsync(db, "events"));
        foreach (var table in new[]
                 {
                     "users", "producers", "styles", "attribute_definitions",
                     "style_attributes", "drinks", "drink_attributes", "merge_queue",
                     "venues", "ratings", "user_logins", "user_claims", "user_tokens",
                 })
        {
            Assert.True(await TableExistsAsync(db, table), $"missing table {table}");
        }

        // Round-trip a row through each table.
        db.Settings.Add(new Setting { Key = "roundtrip.key", Value = "v", UpdatedAt = DateTimeOffset.UtcNow });
        db.Events.Add(new EventRecord
        {
            Name = "roundtrip_event",
            Properties = new Dictionary<string, string> { ["k"] = "v" },
            OccurredAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        Assert.Equal("v", (await db.Settings.FindAsync("roundtrip.key"))!.Value);
        Assert.Equal(1, await db.Events.CountAsync());
    }

    private static async Task<bool> ScalarBoolAsync(DbContext db, string sql)
        => await db.Database.SqlQueryRaw<bool>(sql).SingleAsync();

    private static Task<bool> TableExistsAsync(DbContext db, string table)
        => ScalarBoolAsync(db,
            $"SELECT EXISTS(SELECT 1 FROM information_schema.tables WHERE table_schema = 'public' AND table_name = '{table}') AS \"Value\"");
}
