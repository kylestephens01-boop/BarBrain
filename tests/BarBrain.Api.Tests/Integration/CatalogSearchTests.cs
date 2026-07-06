using Microsoft.EntityFrameworkCore;

namespace BarBrain.Api.Tests.Integration;

/// <summary>
/// Search acceptance (Sprint 1 spec): sane results for "toppling goliath",
/// "buffalo trace", "fat tire", and the misspelled "guiness" — trigram over
/// drink AND producer normalized names.
/// </summary>
[Collection("postgres")]
public sealed class CatalogSearchTests(PostgresFixture fixture) : IAsyncLifetime
{
    private string _connectionString = "";

    public async Task InitializeAsync()
    {
        if (!fixture.DockerAvailable)
            return;
        _connectionString = await fixture.CreateEmptyDatabaseAsync($"catalog_search_{Guid.NewGuid():N}");
        using var harness = new CatalogTestHarness(_connectionString);
        await harness.Db.Database.MigrateAsync();
        await harness.ImportBundledAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [SkippableFact]
    public async Task Producer_query_returns_that_producers_drinks()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        using var harness = new CatalogTestHarness(_connectionString);

        var results = await harness.Queries.SearchAsync("toppling goliath", category: null, limit: 10);
        Assert.NotEmpty(results);
        Assert.Contains("Toppling Goliath", results[0].ProducerName);
    }

    [SkippableFact]
    public async Task Whiskey_producer_query_finds_bourbon()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        using var harness = new CatalogTestHarness(_connectionString);

        var results = await harness.Queries.SearchAsync("buffalo trace", category: null, limit: 10);
        Assert.NotEmpty(results);
        Assert.Contains("Buffalo Trace", results[0].ProducerName + results[0].Name);
        Assert.Equal("whiskey", results[0].Category);
    }

    [SkippableFact]
    public async Task Exact_drink_name_ranks_first()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        using var harness = new CatalogTestHarness(_connectionString);

        var results = await harness.Queries.SearchAsync("fat tire", category: null, limit: 10);
        Assert.NotEmpty(results);
        Assert.Equal("Fat Tire", results[0].Name);
    }

    [SkippableFact]
    public async Task Misspelled_guiness_still_finds_guinness()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        using var harness = new CatalogTestHarness(_connectionString);

        var results = await harness.Queries.SearchAsync("guiness", category: null, limit: 10);
        Assert.NotEmpty(results);
        Assert.Contains(results.Take(3), r => r.ProducerName == "Guinness");
    }

    [SkippableFact]
    public async Task Category_filter_restricts_results()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        using var harness = new CatalogTestHarness(_connectionString);

        var results = await harness.Queries.SearchAsync("bourbon", category: "whiskey", limit: 20);
        Assert.NotEmpty(results);
        Assert.All(results, r => Assert.Equal("whiskey", r.Category));
    }

    [SkippableFact]
    public async Task Browse_pages_by_style()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        using var harness = new CatalogTestHarness(_connectionString);

        var ipa = await harness.Db.Styles.SingleAsync(s => s.Code == "21A");
        var page = await harness.Queries.BrowseAsync("beer", ipa.Id, page: 1, pageSize: 10);
        Assert.True(page.Total >= 3, $"expected several American IPAs in the corridor seed, got {page.Total}");
        Assert.All(page.Items, i => Assert.Equal("beer", i.Category));
    }
}
