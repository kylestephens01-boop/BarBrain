using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BarBrain.Api.Tests.Integration;

/// <summary>
/// Boots the real API in-process for endpoint tests. Connection string and
/// migrate-on-startup are overridable so health/version tests can run with no
/// database, while pipeline tests point at the Testcontainers Postgres.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public string? ConnectionStringOverride { get; init; }
    public bool MigrateOnStartup { get; init; }

    /// <summary>Extra configuration keys (e.g. Auth:EnableMockExternal).</summary>
    public Dictionary<string, string?> Settings { get; init; } = [];

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Database:MigrateOnStartup", MigrateOnStartup ? "true" : "false");
        builder.UseSetting("Admin:Token", string.Empty); // Sprint 0 stub allows the demo flip

        if (ConnectionStringOverride is not null)
            builder.UseSetting("ConnectionStrings:Default", ConnectionStringOverride);

        foreach (var (key, value) in Settings)
            builder.UseSetting(key, value);
    }
}
