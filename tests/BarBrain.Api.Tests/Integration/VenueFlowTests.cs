using System.Net;
using System.Net.Http.Json;
using BarBrain.Api.Catalog;
using BarBrain.Api.Data.Entities;
using BarBrain.Shared.Contracts;
using Microsoft.EntityFrameworkCore;
using Pgvector;

namespace BarBrain.Api.Tests.Integration;

/// <summary>
/// Sprint 5 acceptance (ADR-015): wiki add-venue + distance-sorted discovery,
/// one-tap check-in (expiry-flagged, one open per user), the four-shelf
/// personalized menu for a profiled user, the untagged-rating auto-tag, the
/// no-profile fallback, the QR kit, and the Home Bar NEGATIVE surface tests —
/// the private venue must be invisible everywhere public.
/// </summary>
[Collection("postgres")]
public sealed class VenueFlowTests(PostgresFixture fixture) : IAsyncLifetime
{
    private IdentityTestHarness _harness = null!;
    private HttpClient _client = null!;

    // Cedar Rapids-ish and Iowa City-ish coordinates (corridor scale).
    private const double CrLat = 41.9779, CrLng = -91.6656;
    private const double IcLat = 41.6611, IcLng = -91.5302;

    public async Task InitializeAsync()
    {
        if (!fixture.DockerAvailable) return;
        _harness = new IdentityTestHarness(fixture.ConnectionString);
        await CleanupAsync();
        _client = _harness.CreateClient();
        await _harness.SignupAsync(_client, "venue_user");
    }

    public async Task DisposeAsync()
    {
        if (_harness is not null) await _harness.DisposeAsync();
    }

    private async Task CleanupAsync()
    {
        await using var db = _harness.CreateDb();
        await db.Checkins.ExecuteDeleteAsync();
        await db.VenueMenuItems.ExecuteDeleteAsync();
        await db.MergeQueue.Where(m => m.EntityType == MergeEntityType.Venue).ExecuteDeleteAsync();
        // The rate-limit test plants a low flag; don't let it leak across tests.
        await db.Settings.Where(s => s.Key.StartsWith("venues.")).ExecuteDeleteAsync();
        await _harness.CleanupIdentityDataAsync();
    }

    private async Task<VenueDto> AddVenueAsync(
        string name, double? lat = null, double? lng = null, HttpClient? client = null)
    {
        var response = await (client ?? _client).PostAsJsonAsync("/api/venues",
            new VenueCreateRequest(name, lat, lng, null, null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<VenueDto>())!;
    }

    /// <summary>A public drink with a category vector (and optional style) the engine can score.</summary>
    private async Task<Guid> PlantVectorDrinkAsync(
        string name, float[] vector, Guid? styleId = null, string category = "beer")
    {
        await using var db = _harness.CreateDb();
        var producer = await db.Producers.FirstOrDefaultAsync(p => p.NormalizedName == "venue harness brewing");
        producer ??= new Producer
        {
            Name = "Venue Harness Brewing",
            NormalizedName = "venue harness brewing",
            Source = "test",
        };
        var drink = new Drink
        {
            Producer = producer,
            Name = name,
            NormalizedName = name.ToLowerInvariant(),
            Category = category,
            StyleId = styleId,
            Source = "test",
            CategoryVector = new Vector(vector),
        };
        db.Drinks.Add(drink);
        await db.SaveChangesAsync();
        return drink.Id;
    }

    private async Task<Guid> PlantStyleAsync(string name, string category = "beer")
    {
        await using var db = _harness.CreateDb();
        var style = new Style
        {
            Category = category,
            Name = name,
            NormalizedName = name.ToLowerInvariant(),
            Source = "test",
        };
        db.Styles.Add(style);
        await db.SaveChangesAsync();
        return style.Id;
    }

    // --- Discovery ---------------------------------------------------------------

    [SkippableFact]
    public async Task Nearby_list_distance_sorts_with_geo_and_name_sorts_without()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        await AddVenueAsync("Zeta Alehouse", IcLat, IcLng);      // far from CR
        await AddVenueAsync("Corridor Taproom", CrLat, CrLng);   // at CR
        await AddVenueAsync("Anonymous Basement Bar");           // no geo → sorts last

        var byDistance = (await _client.GetFromJsonAsync<List<NearbyVenueDto>>(
            $"/api/venues/nearby?lat={CrLat}&lng={CrLng}"))!;
        Assert.Equal(
            ["Corridor Taproom", "Zeta Alehouse", "Anonymous Basement Bar"],
            byDistance.Select(v => v.Name).ToArray());
        Assert.True(byDistance[0].DistanceKm!.Value < 1);
        Assert.InRange(byDistance[1].DistanceKm!.Value, 30, 50); // CR→IC ≈ 37 km
        Assert.Null(byDistance[2].DistanceKm);

        var byName = await _client.GetFromJsonAsync<List<NearbyVenueDto>>("/api/venues/nearby");
        Assert.Equal(
            ["Anonymous Basement Bar", "Corridor Taproom", "Zeta Alehouse"],
            byName!.Select(v => v.Name).ToArray());
    }

    [SkippableFact]
    public async Task Venue_add_rate_limit_is_flag_driven()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        // Plant the flag row BEFORE the app first reads it (cache fills on read).
        await using (var db = _harness.CreateDb())
        {
            db.Settings.Add(new Setting
            {
                Key = "venues.add_per_day", Value = "2", UpdatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        await AddVenueAsync("First Bar", CrLat, CrLng);
        await AddVenueAsync("Second Bar", CrLat, CrLng);
        var third = await _client.PostAsJsonAsync("/api/venues",
            new VenueCreateRequest("Third Bar", CrLat, CrLng, null, null));
        Assert.Equal((HttpStatusCode)429, third.StatusCode);
        Assert.Equal("rate_limited", (await third.Content.ReadFromJsonAsync<ApiError>())!.Code);
    }

    // --- Home Bar negative surface (spec acceptance) -------------------------------

    [SkippableFact]
    public async Task Home_bar_is_absent_from_every_public_surface_and_renames_for_its_owner()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        await AddVenueAsync("Public Corner", CrLat, CrLng);

        // Absent from discovery.
        var nearby = await _client.GetFromJsonAsync<List<NearbyVenueDto>>("/api/venues/nearby");
        Assert.DoesNotContain(nearby!, v => v.Name == "Home Bar");

        Guid homeBarId;
        await using (var db = _harness.CreateDb())
            homeBarId = (await db.Venues.SingleAsync(v => v.VenueType == VenueType.HomeBar)).Id;

        // The venue page 404s for OTHER users (existence must not leak) and anon.
        var stranger = _harness.CreateClient();
        await _harness.SignupAsync(stranger, "stranger");
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/venues/{homeBarId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await _harness.Factory.CreateDefaultClient().GetAsync($"/api/venues/{homeBarId}")).StatusCode);

        // Check-in against it refuses even for the owner (it's not a session venue).
        var checkin = await _client.PostAsJsonAsync("/api/checkins", new CheckinRequest(homeBarId));
        Assert.Equal(HttpStatusCode.NotFound, checkin.StatusCode);

        // The owner sees and renames it (ADR-015: rename allowed).
        var mine = await _client.GetFromJsonAsync<VenueDto>("/api/venues/home-bar");
        Assert.Equal("Home Bar", mine!.Name);
        var renamed = await _client.PatchAsJsonAsync("/api/venues/home-bar",
            new HomeBarRenameRequest("The Snug"));
        renamed.EnsureSuccessStatusCode();
        Assert.Equal("The Snug", (await renamed.Content.ReadFromJsonAsync<VenueDto>())!.Name);
    }

    // --- Check-in + four shelves -----------------------------------------------------

    [SkippableFact]
    public async Task Checkin_unlocks_four_shelves_mapped_per_the_founder_ruling()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        var venue = await AddVenueAsync("Shelf Test Bar", CrLat, CrLng);

        // A palate that loves hoppy (dim 0) — vectors are 8-dim (ADR-009).
        var ipaStyle = await PlantStyleAsync("Test IPA");
        float[] hoppy = [0.9f, 0.1f, 0.5f, 0f, 0.2f, 0.3f, 0.1f, 0.2f];
        float[] hoppyish = [0.8f, 0.2f, 0.5f, 0f, 0.2f, 0.3f, 0.1f, 0.2f];
        float[] malty = [0.1f, 0.8f, 0.6f, 0f, 0.1f, 0.2f, 0.6f, 0.4f];

        var favorite = await PlantVectorDrinkAsync("Rated High", hoppy, ipaStyle);
        var ratedLow = await PlantVectorDrinkAsync("Rated Low", malty);
        var knownStyle = await PlantVectorDrinkAsync("Same Style Unrated", hoppyish, ipaStyle);
        var closeNew = await PlantVectorDrinkAsync("Close Unrated", hoppyish);
        var farNew = await PlantVectorDrinkAsync("Far Unrated", malty);

        // Ratings build the profile (recompute-on-rate) AND the shelf history.
        foreach (var (drink, value) in new[] { (favorite, 4.5m), (ratedLow, 2.0m) })
            (await _client.PostAsJsonAsync("/api/ratings",
                new RateRequest(drink, value, null, "public", "home_bar"))).EnsureSuccessStatusCode();

        foreach (var drink in new[] { favorite, ratedLow, knownStyle, closeNew, farNew })
            (await _client.PostAsJsonAsync($"/api/venues/{venue.Id}/menu",
                new MenuItemAddRequest(drink, 7.50m))).EnsureSuccessStatusCode();

        // Pre-check-in: the personalized menu is locked (teaser state).
        var locked = await _client.GetAsync($"/api/venues/{venue.Id}/menu/personalized");
        Assert.Equal(HttpStatusCode.Conflict, locked.StatusCode);
        Assert.Equal("checkin_required", (await locked.Content.ReadFromJsonAsync<ApiError>())!.Code);

        // One tap.
        (await _client.PostAsJsonAsync("/api/checkins", new CheckinRequest(venue.Id)))
            .EnsureSuccessStatusCode();

        var menu = (await _client.GetFromJsonAsync<PersonalizedMenuResponse>(
            $"/api/venues/{venue.Id}/menu/personalized"))!;
        Assert.True(menu.Personalized);
        Assert.Equal(
            ["favorites", "familiar", "adventurous", "new_for_you"],
            menu.Shelves.Select(s => s.Key).ToArray());

        string[] ShelfOf(Guid drinkId) => menu.Shelves
            .Where(s => s.Items.Any(i => i.Item.DrinkId == drinkId))
            .Select(s => s.Key).ToArray();

        Assert.Equal(["favorites"], ShelfOf(favorite));          // rated ≥ 4.0 flag
        Assert.Equal(["familiar"], ShelfOf(ratedLow));           // rated before, below the bar
        Assert.Equal(["familiar"], ShelfOf(knownStyle));         // unrated, style rated before
        Assert.Equal(["new_for_you"], ShelfOf(closeNew));        // closest unrated half
        Assert.Equal(["adventurous"], ShelfOf(farNew));          // far unrated half

        // Every non-favorite rec carries its because (ADR-013).
        Assert.All(menu.Shelves.SelectMany(s => s.Items), i => Assert.False(string.IsNullOrEmpty(i.Reason)));
    }

    [SkippableFact]
    public async Task No_profile_fallback_groups_by_style_and_says_so()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        var venue = await AddVenueAsync("Cold Start Bar", CrLat, CrLng);
        var style = await PlantStyleAsync("Fallback Lager");
        var drink = await PlantVectorDrinkAsync("Cold Start Pils", [0.5f, 0.5f, 0.5f, 0f, 0.5f, 0.5f, 0.5f, 0.5f], style);
        (await _client.PostAsJsonAsync($"/api/venues/{venue.Id}/menu",
            new MenuItemAddRequest(drink, null))).EnsureSuccessStatusCode();

        var cold = _harness.CreateClient();
        await _harness.SignupAsync(cold, "cold_user");
        (await cold.PostAsJsonAsync("/api/checkins", new CheckinRequest(venue.Id))).EnsureSuccessStatusCode();

        var menu = (await cold.GetFromJsonAsync<PersonalizedMenuResponse>(
            $"/api/venues/{venue.Id}/menu/personalized"))!;
        Assert.False(menu.Personalized);
        Assert.All(menu.Shelves, s => Assert.StartsWith("style_", s.Key));
        Assert.All(menu.Shelves.SelectMany(s => s.Items), i => Assert.Null(i.MatchPct));
    }

    [SkippableFact]
    public async Task A_second_checkin_ends_the_first_and_untagged_ratings_auto_tag_the_venue()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        var first = await AddVenueAsync("First Stop", CrLat, CrLng);
        var second = await AddVenueAsync("Second Stop", IcLat, IcLng);
        var drink = await PlantVectorDrinkAsync("Tagged Pour", [0.4f, 0.4f, 0.4f, 0f, 0.4f, 0.4f, 0.4f, 0.4f]);

        (await _client.PostAsJsonAsync("/api/checkins", new CheckinRequest(first.Id))).EnsureSuccessStatusCode();
        (await _client.PostAsJsonAsync("/api/checkins", new CheckinRequest(second.Id))).EnsureSuccessStatusCode();

        var active = await _client.GetFromJsonAsync<CheckinDto>("/api/checkins/active");
        Assert.Equal(second.Id, active!.VenueId);
        await using (var db = _harness.CreateDb())
            Assert.Equal(1, await db.Checkins.CountAsync(c => c.EndedAt == null));

        // UNTAGGED rating during the active check-in lands on the venue…
        (await _client.PostAsJsonAsync("/api/ratings",
            new RateRequest(drink, 4.0m, "great pour", "public", "untagged"))).EnsureSuccessStatusCode();
        await using (var db = _harness.CreateDb())
        {
            var rating = await db.Ratings.SingleAsync(r => r.DrinkId == drink);
            Assert.Equal(LocationContext.Venue, rating.LocationContext);
            Assert.Equal(second.Id, rating.VenueId);
        }

        // …and shows in the venue's recent activity (spec acceptance criterion).
        var page = await _client.GetFromJsonAsync<VenuePageDto>($"/api/venues/{second.Id}");
        Assert.Contains(page!.RecentActivity, a => a.DrinkName == "Tagged Pour" && a.Value == 4.0m);

        // An EXPLICIT home_bar rating during a check-in stays at the Home Bar.
        var homeDrink = await PlantVectorDrinkAsync("Home Pour", [0.4f, 0.4f, 0.4f, 0f, 0.4f, 0.4f, 0.4f, 0.4f]);
        (await _client.PostAsJsonAsync("/api/ratings",
            new RateRequest(homeDrink, 3.0m, null, "public", "home_bar"))).EnsureSuccessStatusCode();
        await using (var db = _harness.CreateDb())
            Assert.Equal(LocationContext.HomeBar,
                (await db.Ratings.SingleAsync(r => r.DrinkId == homeDrink)).LocationContext);
    }

    // --- Dedupe + merge ---------------------------------------------------------------

    [SkippableFact]
    public async Task Near_duplicate_venues_queue_on_add_and_merge_preserves_menus()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        var drinkA = await PlantVectorDrinkAsync("Merge Keeper", [0.4f, 0.4f, 0.4f, 0f, 0.4f, 0.4f, 0.4f, 0.4f]);
        var drinkB = await PlantVectorDrinkAsync("Merge Mover", [0.3f, 0.3f, 0.3f, 0f, 0.3f, 0.3f, 0.3f, 0.3f]);

        var target = await AddVenueAsync("The Corner Tap", CrLat, CrLng);       // older → survivor
        (await _client.PostAsJsonAsync($"/api/venues/{target.Id}/menu",
            new MenuItemAddRequest(drinkA, 6m))).EnsureSuccessStatusCode();

        var source = await AddVenueAsync("Corner Tap", CrLat + 0.0005, CrLng);  // ~55m away, newer
        (await _client.PostAsJsonAsync($"/api/venues/{source.Id}/menu",
            new MenuItemAddRequest(drinkA, 7m))).EnsureSuccessStatusCode();     // collision: survivor's wins
        (await _client.PostAsJsonAsync($"/api/venues/{source.Id}/menu",
            new MenuItemAddRequest(drinkB, 8m))).EnsureSuccessStatusCode();     // moves across

        // The wiki add itself queued the candidate (dedupe-on-add, never auto-merge).
        using var harness = new CatalogTestHarness(_harness.ConnectionString);
        var candidate = await harness.Db.MergeQueue.SingleAsync(m =>
            m.EntityType == MergeEntityType.Venue
            && m.SourceVenueId == source.Id && m.TargetVenueId == target.Id
            && m.Status == MergeStatus.Pending);

        Assert.Equal(MergeDecisionOutcome.Done, await harness.Merges.ApproveAsync(candidate.Id, "test"));

        await using (var db = _harness.CreateDb())
        {
            var merged = await db.Venues.SingleAsync(v => v.Id == source.Id);
            Assert.Equal(EntityStatus.Merged, merged.Status);
            Assert.Equal(target.Id, merged.MergedIntoVenueId);

            var menu = await db.VenueMenuItems.Where(mi => mi.VenueId == target.Id).ToListAsync();
            Assert.Equal(2, menu.Count);
            Assert.Equal(6m, menu.Single(mi => mi.DrinkId == drinkA).Price); // survivor's row won
            Assert.Contains(menu, mi => mi.DrinkId == drinkB);               // mover moved
        }

        // The old id (a printed QR!) still resolves — to the survivor's page.
        var page = await _client.GetFromJsonAsync<VenuePageDto>($"/api/venues/{source.Id}");
        Assert.Equal(target.Id, page!.Venue.Id);

        // A geo-distant same-name venue does NOT queue (the radius gate).
        var faraway = await AddVenueAsync("The Corner Tap", IcLat, IcLng);
        using var harness2 = new CatalogTestHarness(_harness.ConnectionString);
        Assert.False(await harness2.Db.MergeQueue.AnyAsync(m =>
            m.SourceVenueId == faraway.Id || m.TargetVenueId == faraway.Id));
    }

    // --- QR kit ------------------------------------------------------------------------

    [SkippableFact]
    public async Task Qr_resolves_and_the_one_pager_generates()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        var venue = await AddVenueAsync("Print Shop Bar", CrLat, CrLng);
        var anon = _harness.Factory.CreateDefaultClient();

        var qr = await anon.GetAsync($"/api/venues/{venue.Id}/qr.png");
        qr.EnsureSuccessStatusCode();
        Assert.Equal("image/png", qr.Content.Headers.ContentType!.MediaType);
        var png = await qr.Content.ReadAsByteArrayAsync();
        Assert.Equal([0x89, 0x50, 0x4E, 0x47], png[..4]); // PNG magic

        var pdf = await anon.GetAsync($"/api/venues/{venue.Id}/onepager.pdf");
        pdf.EnsureSuccessStatusCode();
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType!.MediaType);
        var bytes = await pdf.Content.ReadAsByteArrayAsync();
        Assert.Equal("%PDF"u8.ToArray(), bytes[..4]);
    }
}
