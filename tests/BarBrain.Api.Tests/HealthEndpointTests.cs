using System.Net;
using System.Net.Http.Json;
using BarBrain.Api.Tests.Integration;
using BarBrain.Shared.Contracts;

namespace BarBrain.Api.Tests;

/// <summary>
/// Health/version need no database, so these run everywhere (no Docker required)
/// — a fast guard that the app boots and the diagnostics contract holds.
/// </summary>
public sealed class HealthEndpointTests
{
    [Fact]
    public async Task Health_returns_ok_with_version_and_sha()
    {
        await using var api = new ApiFactory(); // MigrateOnStartup defaults false → no DB
        var client = api.CreateClient();

        var response = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var health = await response.Content.ReadFromJsonAsync<HealthResponse>();
        Assert.NotNull(health);
        Assert.Equal("ok", health!.Status);
        Assert.False(string.IsNullOrWhiteSpace(health.Version));
        Assert.False(string.IsNullOrWhiteSpace(health.Sha));
    }

    [Fact]
    public async Task Version_returns_version_and_sha()
    {
        await using var api = new ApiFactory();
        var client = api.CreateClient();

        var version = await client.GetFromJsonAsync<VersionResponse>("/version");
        Assert.NotNull(version);
        Assert.False(string.IsNullOrWhiteSpace(version!.Version));
        Assert.False(string.IsNullOrWhiteSpace(version.Sha));
    }
}
