using System.Net;
using System.Net.Http.Json;
using BarBrain.Shared.Contracts;

namespace BarBrain.Api.Tests.Integration;

/// <summary>
/// End-to-end proof of the Sprint 0 feature-flag pipeline acceptance criterion:
/// flipping a flag via the admin API changes what the home config returns,
/// with no redeploy. Also exercises the events write endpoint.
/// </summary>
[Collection("postgres")]
public sealed class FlagPipelineTests(PostgresFixture fixture)
{
    private ApiFactory CreateApi() => new()
    {
        ConnectionStringOverride = fixture.ConnectionString,
        MigrateOnStartup = true, // idempotent; also runs the flag seeder
    };

    [SkippableFact]
    public async Task Flipping_banner_flag_changes_home_config_without_redeploy()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");

        await using var api = CreateApi();
        var client = api.CreateClient();

        var newBanner = $"Flipped at {Guid.NewGuid():N}";
        var put = await client.PutAsJsonAsync(
            "/api/admin/settings/home.banner_text",
            new SettingUpdateRequest(newBanner));
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        var config = await client.GetFromJsonAsync<HomeConfig>("/api/config/home");
        Assert.NotNull(config);
        Assert.Equal(newBanner, config!.BannerText);
    }

    [SkippableFact]
    public async Task Health_and_version_report_build_metadata()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");

        await using var api = CreateApi();
        var client = api.CreateClient();

        var health = await client.GetFromJsonAsync<HealthResponse>("/health");
        Assert.NotNull(health);
        Assert.Equal("ok", health!.Status);
        Assert.False(string.IsNullOrWhiteSpace(health.Version));
        Assert.False(string.IsNullOrWhiteSpace(health.Sha));

        var version = await client.GetFromJsonAsync<VersionResponse>("/version");
        Assert.NotNull(version);
        Assert.Equal(health.Version, version!.Version);
    }

    [SkippableFact]
    public async Task Events_endpoint_accepts_a_write()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");

        await using var api = CreateApi();
        var client = api.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/events",
            new EventWriteRequest("page_view", new Dictionary<string, string> { ["surface"] = "home" }));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [SkippableFact]
    public async Task Events_endpoint_rejects_missing_name()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");

        await using var api = CreateApi();
        var client = api.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/events",
            new EventWriteRequest("", null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
