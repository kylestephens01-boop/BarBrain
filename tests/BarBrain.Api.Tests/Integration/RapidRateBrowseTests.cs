using System.Net;
using System.Net.Http.Json;
using BarBrain.Api.Data.Entities;
using BarBrain.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BarBrain.Api.Tests.Integration;

/// <summary>
/// Sprint 4.5 rapid-rate browse read: auth requirement, popular ordering
/// (latest public ratings only), the caller's MyLatestValue, the unratedOnly
/// filter (any visibility/origin counts as "rated"), category/sort validation,
/// deterministic paging, and the page-size flag. The shared DB carries catalog
/// rows from other suites, so assertions target planted drink IDs (relative
/// order / presence), never global list shapes.
/// </summary>
[Collection("postgres")]
public sealed class RapidRateBrowseTests(PostgresFixture fixture) : IAsyncLifetime
{
    private IdentityTestHarness _harness = null!;
    private HttpClient _alice = null!;
    private Guid _beerA, _beerB, _beerC, _whiskey;

    public async Task InitializeAsync()
    {
        if (!fixture.DockerAvailable) return;
        _harness = new IdentityTestHarness(fixture.ConnectionString);
        await _harness.CleanupIdentityDataAsync(); // zero ratings anywhere at test start
        _alice = _harness.CreateClient();
        await _harness.SignupAsync(_alice, "rr_alice");
        _beerA = await _harness.PlantDrinkAsync("RR Citra Haze");
        _beerB = await _harness.PlantDrinkAsync("RR Amber Route");
        _beerC = await _harness.PlantDrinkAsync("RR Stout Ledger");
        _whiskey = await _harness.PlantDrinkAsync("RR Cask Ledger", "whiskey");
    }

    public async Task DisposeAsync()
    {
        if (_harness is not null) await _harness.DisposeAsync();
    }

    private static Task<HttpResponseMessage> RateAsync(HttpClient client, Guid drinkId,
        decimal value, string visibility = "public", string origin = "user",
        string location = "home_bar")
        => client.PostAsJsonAsync("/api/ratings",
            new RateRequest(drinkId, value, null, visibility, location, null, origin));

    private async Task<IReadOnlyList<RapidRateItem>> BrowseAsync(
        HttpClient client, string query = "?pageSize=100")
    {
        var result = await client.GetFromJsonAsync<PagedResult<RapidRateItem>>(
            $"/api/rapidrate/drinks{query}");
        return result!.Items;
    }

    [SkippableFact]
    public async Task Anonymous_request_is_401()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        var anon = _harness.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anon.GetAsync("/api/rapidrate/drinks")).StatusCode);
    }

    [SkippableFact]
    public async Task Popular_sort_counts_latest_public_ratings_only_and_orders_by_count()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        var bob = _harness.CreateClient();
        await _harness.SignupAsync(bob, "rr_bob");

        // beerB: two distinct raters → count 2. beerC: one rater who re-rated
        // (append-only → ONE latest row) → count 1. beerA: unrated → count 0.
        (await RateAsync(_alice, _beerB, 4.0m)).EnsureSuccessStatusCode();
        (await RateAsync(bob, _beerB, 3.5m)).EnsureSuccessStatusCode();
        (await RateAsync(bob, _beerC, 3.0m)).EnsureSuccessStatusCode();
        (await RateAsync(bob, _beerC, 4.5m)).EnsureSuccessStatusCode();

        var items = await BrowseAsync(_alice);
        var byId = items.ToDictionary(i => i.Id);
        Assert.Equal(2, byId[_beerB].PublicRatingCount);
        Assert.Equal(1, byId[_beerC].PublicRatingCount); // re-rate counted once
        Assert.Equal(0, byId[_beerA].PublicRatingCount);

        // All ratings were wiped at test start, so the planted rated drinks
        // lead the popular ordering globally.
        var order = items.Select(i => i.Id).ToList();
        Assert.True(order.IndexOf(_beerB) < order.IndexOf(_beerC));
        Assert.True(order.IndexOf(_beerC) < order.IndexOf(_beerA));
    }

    [SkippableFact]
    public async Task Private_ratings_hide_from_public_count_but_fill_my_value_only()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        var bob = _harness.CreateClient();
        await _harness.SignupAsync(bob, "rr_bob2");
        (await RateAsync(_alice, _beerA, 4.0m, visibility: "private")).EnsureSuccessStatusCode();

        var aliceRow = (await BrowseAsync(_alice)).Single(i => i.Id == _beerA);
        Assert.Equal(0, aliceRow.PublicRatingCount);
        Assert.Equal(4.0m, aliceRow.MyLatestValue);

        var bobRow = (await BrowseAsync(bob)).Single(i => i.Id == _beerA);
        Assert.Null(bobRow.MyLatestValue);
    }

    [SkippableFact]
    public async Task My_latest_value_reflects_a_re_rate()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        (await RateAsync(_alice, _beerA, 3.0m)).EnsureSuccessStatusCode();
        (await RateAsync(_alice, _beerA, 4.5m)).EnsureSuccessStatusCode();

        var row = (await BrowseAsync(_alice)).Single(i => i.Id == _beerA);
        Assert.Equal(4.5m, row.MyLatestValue);
    }

    [SkippableFact]
    public async Task UnratedOnly_excludes_anything_I_ever_rated_any_visibility_or_origin()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        (await RateAsync(_alice, _beerA, 4.0m)).EnsureSuccessStatusCode();
        (await RateAsync(_alice, _beerB, 3.5m, visibility: "private")).EnsureSuccessStatusCode();
        (await RateAsync(_alice, _whiskey, 4.0m, origin: "quiz", location: "untagged"))
            .EnsureSuccessStatusCode();

        var ids = (await BrowseAsync(_alice, "?unratedOnly=true&pageSize=100"))
            .Select(i => i.Id).ToHashSet();
        Assert.DoesNotContain(_beerA, ids);
        Assert.DoesNotContain(_beerB, ids);   // private still counts as rated
        Assert.DoesNotContain(_whiskey, ids); // quiz-origin still counts as rated
        Assert.Contains(_beerC, ids);

        // Another user's ratings do NOT hide drinks from me.
        var bob = _harness.CreateClient();
        await _harness.SignupAsync(bob, "rr_bob3");
        var bobIds = (await BrowseAsync(bob, "?unratedOnly=true&pageSize=100"))
            .Select(i => i.Id).ToHashSet();
        Assert.Contains(_beerA, bobIds);
    }

    [SkippableFact]
    public async Task Category_filter_applies_and_bad_category_or_sort_is_400()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        var whiskeyItems = await BrowseAsync(_alice, "?category=whiskey&pageSize=100");
        Assert.All(whiskeyItems, i => Assert.Equal("whiskey", i.Category));
        Assert.Contains(_whiskey, whiskeyItems.Select(i => i.Id));
        Assert.DoesNotContain(_beerA, whiskeyItems.Select(i => i.Id));

        var badCategory = await _alice.GetAsync("/api/rapidrate/drinks?category=vodka");
        Assert.Equal(HttpStatusCode.BadRequest, badCategory.StatusCode);
        Assert.Equal("invalid_category",
            (await badCategory.Content.ReadFromJsonAsync<ApiError>())!.Code);

        var badSort = await _alice.GetAsync("/api/rapidrate/drinks?sort=vibes");
        Assert.Equal(HttpStatusCode.BadRequest, badSort.StatusCode);
        Assert.Equal("invalid_sort",
            (await badSort.Content.ReadFromJsonAsync<ApiError>())!.Code);
    }

    [SkippableFact]
    public async Task Paging_is_deterministic_with_disjoint_pages()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        var all = await _alice.GetFromJsonAsync<PagedResult<RapidRateItem>>(
            "/api/rapidrate/drinks?sort=name&pageSize=100");
        var page1 = await _alice.GetFromJsonAsync<PagedResult<RapidRateItem>>(
            "/api/rapidrate/drinks?sort=name&page=1&pageSize=2");
        var page2 = await _alice.GetFromJsonAsync<PagedResult<RapidRateItem>>(
            "/api/rapidrate/drinks?sort=name&page=2&pageSize=2");

        Assert.Equal(all!.Total, page1!.Total);
        var stitched = page1.Items.Concat(page2!.Items).Select(i => i.Id).ToList();
        Assert.Equal(stitched.Count, stitched.Distinct().Count()); // disjoint
        Assert.Equal(all.Items.Take(stitched.Count).Select(i => i.Id), stitched); // stable order
    }

    [SkippableFact]
    public async Task Page_size_flag_sets_the_default_page_size()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        (await _alice.PutAsJsonAsync("/api/admin/settings/rapidrate.page_size",
            new SettingUpdateRequest("2"))).EnsureSuccessStatusCode();
        try
        {
            var result = await _alice.GetFromJsonAsync<PagedResult<RapidRateItem>>(
                "/api/rapidrate/drinks");
            Assert.Equal(2, result!.PageSize);
            Assert.True(result.Items.Count <= 2);

            // Explicit pageSize still wins over the flag default.
            var explicitSize = await _alice.GetFromJsonAsync<PagedResult<RapidRateItem>>(
                "/api/rapidrate/drinks?pageSize=3");
            Assert.Equal(3, explicitSize!.PageSize);
        }
        finally
        {
            (await _alice.PutAsJsonAsync("/api/admin/settings/rapidrate.page_size",
                new SettingUpdateRequest("20"))).EnsureSuccessStatusCode();
        }
    }

    [SkippableFact]
    public async Task Merged_and_private_drinks_are_excluded()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        await using (var db = _harness.CreateDb())
        {
            await db.Drinks.Where(d => d.Id == _beerA)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(d => d.Status, EntityStatus.Merged)
                    .SetProperty(d => d.MergedIntoDrinkId, _beerB));
            // ck_drinks_owner_visibility: a private drink must have an owner.
            var aliceId = await db.Users.Where(u => u.UserName == "rr_alice")
                .Select(u => u.Id).SingleAsync();
            await db.Drinks.Where(d => d.Id == _beerC)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(d => d.Visibility, Visibility.Private)
                    .SetProperty(d => d.CreatedByUserId, aliceId));
        }
        try
        {
            var ids = (await BrowseAsync(_alice)).Select(i => i.Id).ToHashSet();
            Assert.DoesNotContain(_beerA, ids);
            Assert.DoesNotContain(_beerC, ids);
            Assert.Contains(_beerB, ids);
        }
        finally
        {
            // Catalog rows outlive CleanupIdentityDataAsync — an owned drink
            // left behind would break every later suite's Users delete (FK).
            await using var db = _harness.CreateDb();
            await db.Drinks.Where(d => d.Id == _beerC)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(d => d.Visibility, Visibility.Public)
                    .SetProperty(d => d.CreatedByUserId, (Guid?)null));
        }
    }
}
