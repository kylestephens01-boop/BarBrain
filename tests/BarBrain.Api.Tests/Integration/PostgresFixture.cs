using BarBrain.Api.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace BarBrain.Api.Tests.Integration;

/// <summary>
/// Spins up a real Postgres 16 + pgvector container for integration tests, and
/// migrates a shared "main" database once. If Docker isn't reachable,
/// <see cref="DockerAvailable"/> stays false and dependent tests skip (so
/// `dotnet test` is green on machines without Docker; CI always has it).
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    // Built inside InitializeAsync: this Testcontainers version probes Docker
    // during Build(), so construction itself must be guarded.
    private PostgreSqlContainer? _container;

    public bool DockerAvailable { get; private set; }

    /// <summary>Connection string to the shared, already-migrated database.</summary>
    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        try
        {
            _container = new PostgreSqlBuilder("pgvector/pgvector:pg16")
                .WithDatabase("barbrain")
                .WithUsername("barbrain")
                .WithPassword("barbrain")
                .Build();

            await _container.StartAsync();
            ConnectionString = _container.GetConnectionString();

            // Migrate the shared DB once for service/endpoint tests. The
            // migrate-from-empty test uses its own fresh database instead.
            await using var db = CreateContext(ConnectionString);
            await db.Database.MigrateAsync();

            DockerAvailable = true;
        }
        catch (Exception)
        {
            // Docker daemon not available — tests guard on DockerAvailable.
            DockerAvailable = false;
        }
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }

    public static AppDbContext CreateContext(string connectionString)
    {
        // Same provider config as the app (incl. EnableDynamicJson for the
        // events jsonb column) so tests exercise the real mapping behavior.
        var builder = new DbContextOptionsBuilder<AppDbContext>();
        builder.UseBarBrainNpgsql(connectionString, enableRetry: false);
        return new AppDbContext(builder.Options);
    }

    /// <summary>
    /// Creates a brand-new empty database on the container and returns its
    /// connection string — used to prove migrate-from-empty in isolation.
    /// </summary>
    public async Task<string> CreateEmptyDatabaseAsync(string name)
    {
        await using var admin = new NpgsqlConnection(ConnectionString);
        await admin.OpenAsync();
        await using (var cmd = admin.CreateCommand())
        {
            cmd.CommandText = $"CREATE DATABASE \"{name}\"";
            await cmd.ExecuteNonQueryAsync();
        }

        return new NpgsqlConnectionStringBuilder(ConnectionString) { Database = name }.ConnectionString;
    }
}

[CollectionDefinition("postgres")]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>;
