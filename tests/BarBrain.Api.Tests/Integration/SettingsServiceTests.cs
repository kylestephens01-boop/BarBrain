using BarBrain.Api.Settings;
using Microsoft.Extensions.Caching.Memory;

namespace BarBrain.Api.Tests.Integration;

/// <summary>
/// The typed, cached settings accessor (ADR-006): writes upsert and invalidate
/// the cache so a flag flip is visible on the next read without a redeploy.
/// </summary>
[Collection("postgres")]
public sealed class SettingsServiceTests(PostgresFixture fixture)
{
    [SkippableFact]
    public async Task Set_invalidates_cache_and_typed_getters_parse_values()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");

        using var cache = new MemoryCache(new MemoryCacheOptions());
        await using var db = PostgresFixture.CreateContext(fixture.ConnectionString);
        var svc = new SettingsService(db, cache);

        // String round-trip.
        await svc.SetAsync("test.str", "hello");
        Assert.Equal("hello", await svc.GetStringAsync("test.str", "fallback"));

        // Bool flag, then flip — the flip must be visible immediately (no TTL wait).
        await svc.SetAsync("test.bool", "true");
        Assert.True(await svc.GetBoolAsync("test.bool", false));
        await svc.SetAsync("test.bool", "false");
        Assert.False(await svc.GetBoolAsync("test.bool", true));

        // Int flag.
        await svc.SetAsync("test.int", "42");
        Assert.Equal(42, await svc.GetIntAsync("test.int", 0));

        // Missing keys fall back.
        Assert.Equal("dflt", await svc.GetStringAsync("test.missing", "dflt"));
        Assert.Equal(7, await svc.GetIntAsync("test.missing", 7));
        Assert.True(await svc.GetBoolAsync("test.missing", true));
    }
}
