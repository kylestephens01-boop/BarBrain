using BarBrain.Api.Catalog;
using BarBrain.Api.Data;
using BarBrain.Api.Data.Entities;
using BarBrain.Api.Settings;
using BarBrain.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BarBrain.Api.Venues;

/// <summary>
/// Venues + wiki menus (Sprint 5 spec, ADR-015). Public venues are a wiki:
/// any account adds venues and menu items under flag-driven rate limits, with
/// the events table as the audit trail. Dedupe on add goes through the merge
/// queue (never auto-merged). Home Bars stay private: every read here filters
/// them out of public surfaces by construction, and the negative tests attack
/// exactly that.
/// </summary>
public sealed class VenueService(
    AppDbContext db,
    MergeService merges,
    ISettingsService settings,
    TimeProvider clock)
{
    public sealed record Failure(int Status, ApiError Error);

    public const string AddPerDayFlag = "venues.add_per_day";
    public const string MenuEditsPerDayFlag = "venues.menu_edits_per_day";
    private const int MergeHopLimit = 10;

    // --- Wiki add-venue ------------------------------------------------------

    public async Task<(VenueDto? Venue, Failure? Failure)> CreateAsync(
        Guid userId, VenueCreateRequest request, CancellationToken ct = default)
    {
        var name = (request.Name ?? "").Trim();
        if (name.Length is < 2 or > 128)
            return (null, Err(400, "invalid_name", "Venue names are 2 to 128 characters."));
        if ((request.Latitude is null) != (request.Longitude is null))
            return (null, Err(400, "invalid_geo", "Latitude and longitude come together."));
        if (request.Latitude is < -90 or > 90 || request.Longitude is < -180 or > 180)
            return (null, Err(400, "invalid_geo", "Those coordinates aren't on Earth."));
        if (request.Address is { Length: > 256 } || request.Hours is { Length: > 256 })
            return (null, Err(400, "too_long", "Address and hours max out at 256 characters."));

        var now = clock.GetUtcNow();
        var limit = await settings.GetIntAsync(AddPerDayFlag, 5, ct);
        var since = now.AddHours(-24);
        var recent = await db.Venues.CountAsync(
            v => v.CreatedByUserId == userId && v.CreatedAt >= since, ct);
        if (recent >= limit)
            return (null, Err(429, "rate_limited", "You've added a lot of venues today — try again tomorrow."));

        var venue = new Venue
        {
            Name = name,
            NormalizedName = NameNormalizer.Normalize(name),
            VenueType = VenueType.Venue,
            Tier = VenueTier.Wiki,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            Address = string.IsNullOrWhiteSpace(request.Address) ? null : request.Address.Trim(),
            Hours = string.IsNullOrWhiteSpace(request.Hours) ? null : request.Hours.Trim(),
            Visibility = Visibility.Public,
            CreatedByUserId = userId,
            CreatedAt = now,
        };
        db.Venues.Add(venue);
        db.Events.Add(NewEvent("venue_added", now, userId, ("venueId", venue.Id.ToString())));
        await db.SaveChangesAsync(ct);

        // Dedupe on add (spec): suspected duplicates queue for a moderator;
        // the venue itself always lands — never auto-merged.
        await merges.GenerateVenueCandidatesAsync(venue.Id, ct);

        return (await VenueDtoAsync(venue.Id, ct), null);
    }

    // --- Discovery -------------------------------------------------------------

    /// <summary>
    /// Distance-sorted nearby list (no map, Sprint 5 spec). Without caller geo
    /// (permission denied) the list falls back to name order; venues without
    /// geo sort last within either ordering.
    /// </summary>
    public async Task<List<NearbyVenueDto>> NearbyAsync(
        double? lat, double? lng, CancellationToken ct = default)
    {
        var venues = await PublicActiveVenues()
            .Select(v => new
            {
                v.Id, v.Name, v.Tier, v.Address, v.Latitude, v.Longitude,
                MenuCount = db.VenueMenuItems.Count(mi => mi.VenueId == v.Id && mi.IsAvailable),
            })
            .ToListAsync(ct);

        var rows = venues.Select(v => new NearbyVenueDto(
            v.Id, v.Name, v.Tier, v.Address,
            lat is not null && lng is not null && v.Latitude is not null
                ? Math.Round(HaversineKm(lat.Value, lng.Value, v.Latitude!.Value, v.Longitude!.Value), 2)
                : null,
            v.MenuCount));

        return (lat is not null && lng is not null
                ? rows.OrderBy(v => v.DistanceKm ?? double.MaxValue).ThenBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
                : rows.OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// The venue page. Merged ids follow redirects to the survivor; Home Bars
    /// are visible only to their owner (private surfaces are not "the venue
    /// page" — but the id must not leak existence either, so anyone else 404s).
    /// </summary>
    public async Task<(VenuePageDto? Page, Failure? Failure)> GetPageAsync(
        Guid venueId, Guid? callerId, CancellationToken ct = default)
    {
        var venue = await db.Venues.AsNoTracking().FirstOrDefaultAsync(v => v.Id == venueId, ct);
        for (var hops = 0; venue is { Status: EntityStatus.Merged, MergedIntoVenueId: { } next } && hops < MergeHopLimit; hops++)
            venue = await db.Venues.AsNoTracking().FirstOrDefaultAsync(v => v.Id == next, ct);
        if (venue is null || venue.Status != EntityStatus.Active)
            return (null, Err(404, "venue_not_found", "That venue doesn't exist."));
        if (venue.Visibility == Visibility.Private && venue.OwnerUserId != callerId)
            return (null, Err(404, "venue_not_found", "That venue doesn't exist."));

        var dto = await VenueDtoAsync(venue.Id, ct);

        CheckinDto? active = null;
        if (callerId is not null)
        {
            var now = clock.GetUtcNow();
            active = await db.Checkins.AsNoTracking()
                .Where(c => c.UserId == callerId && c.EndedAt == null && c.ExpiresAt > now)
                .Select(c => new CheckinDto(c.Id, c.VenueId, c.Venue.Name, c.CreatedAt, c.ExpiresAt))
                .FirstOrDefaultAsync(ct);
        }

        // Recent activity: latest PUBLIC ratings tagged here (check-in-tagged
        // ratings show up — Sprint 5 acceptance criterion).
        var activity = venue.VenueType == VenueType.Venue
            ? await db.Ratings.AsNoTracking()
                .Where(r => r.VenueId == venue.Id && r.Visibility == Visibility.Public && r.IsLatest)
                .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
                .Take(10)
                .Select(r => new VenueActivityDto(
                    r.CreatedBy.UserName!, r.Drink.Name, r.Drink.Category, r.Value, r.CreatedAt))
                .ToListAsync(ct)
            : [];

        return (new VenuePageDto(dto!, active?.VenueId == venue.Id, active, activity), null);
    }

    // --- Wiki menu -------------------------------------------------------------

    public async Task<List<MenuItemDto>> MenuAsync(Guid venueId, CancellationToken ct = default)
        // Order BEFORE the constructor projection — EF can't translate member
        // access on a constructor-bound DTO back to a column (CI e2e caught
        // the 500).
        => await ProjectMenu(db.VenueMenuItems.AsNoTracking()
                .Where(mi => mi.VenueId == venueId && mi.Drink.Status == EntityStatus.Active)
                .OrderBy(mi => mi.Drink.Name))
            .ToListAsync(ct);

    /// <summary>
    /// Add a drink to a menu. <paramref name="userId"/> null = founder-as-admin
    /// (verified-tier rows, founder ruling 2026-07-10): the row is ownerless
    /// (public by CHECK) and skips the wiki rate limit.
    /// </summary>
    public async Task<(MenuItemDto? Item, Failure? Failure)> AddMenuItemAsync(
        Guid? userId, Guid venueId, MenuItemAddRequest request, string source,
        CancellationToken ct = default)
    {
        if (source is not (MenuItemSource.Crowd or MenuItemSource.Venue))
            return (null, Err(400, "invalid_source", "Menu source is 'crowd' or 'venue'."));
        if (request.Price is < 0 or > 9999)
            return (null, Err(400, "invalid_price", "That price doesn't look right."));

        var venue = await db.Venues.AsNoTracking().FirstOrDefaultAsync(v => v.Id == venueId, ct);
        if (venue is null || venue.VenueType != VenueType.Venue || venue.Status != EntityStatus.Active)
            return (null, Err(404, "venue_not_found", "That venue doesn't exist."));

        var drink = await db.Drinks.AsNoTracking().FirstOrDefaultAsync(d => d.Id == request.DrinkId, ct);
        for (var hops = 0; drink is { Status: EntityStatus.Merged, MergedIntoDrinkId: { } next } && hops < MergeHopLimit; hops++)
            drink = await db.Drinks.AsNoTracking().FirstOrDefaultAsync(d => d.Id == next, ct);
        if (drink is null || drink.Status != EntityStatus.Active || drink.Visibility != Visibility.Public)
            return (null, Err(404, "drink_not_found", "That drink isn't in the catalog."));

        if (userId is { } contributorId)
        {
            var rateFailure = await CheckMenuEditLimitAsync(contributorId, ct);
            if (rateFailure is not null) return (null, rateFailure);
        }

        var now = clock.GetUtcNow();
        var existing = await db.VenueMenuItems
            .FirstOrDefaultAsync(mi => mi.VenueId == venueId && mi.DrinkId == drink.Id, ct);
        VenueMenuItem item;
        if (existing is not null)
        {
            // Re-adding a listed drink is a confirm + revive, not a conflict.
            item = existing;
            item.IsAvailable = true;
            item.Price = request.Price ?? item.Price;
            item.LastConfirmedAt = now;
            item.UpdatedAt = now;
        }
        else
        {
            item = new VenueMenuItem
            {
                VenueId = venueId,
                DrinkId = drink.Id,
                Price = request.Price,
                IsAvailable = true,
                LastConfirmedAt = now,
                Source = source,
                CreatedByUserId = userId,
                Visibility = Visibility.Public,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.VenueMenuItems.Add(item);
        }
        db.Events.Add(NewEvent("menu_item_added", now, userId,
            ("venueId", venueId.ToString()), ("drinkId", drink.Id.ToString()), ("source", source)));
        await db.SaveChangesAsync(ct);

        var dto = await ProjectMenu(db.VenueMenuItems.AsNoTracking().Where(mi => mi.Id == item.Id))
            .FirstAsync(ct);
        return (dto, null);
    }

    public async Task<(MenuItemDto? Item, Failure? Failure)> UpdateMenuItemAsync(
        Guid userId, Guid menuItemId, MenuItemUpdateRequest request, CancellationToken ct = default)
    {
        if (request.Price is < 0 or > 9999)
            return (null, Err(400, "invalid_price", "That price doesn't look right."));

        var item = await db.VenueMenuItems.FirstOrDefaultAsync(mi => mi.Id == menuItemId, ct);
        if (item is null)
            return (null, Err(404, "menu_item_not_found", "That menu item doesn't exist."));

        var rateFailure = await CheckMenuEditLimitAsync(userId, ct);
        if (rateFailure is not null) return (null, rateFailure);

        var now = clock.GetUtcNow();
        if (request.Price is not null) item.Price = request.Price;
        if (request.IsAvailable is not null) item.IsAvailable = request.IsAvailable.Value;
        if (request.Confirm == true) item.LastConfirmedAt = now;
        item.UpdatedAt = now;

        db.Events.Add(NewEvent("menu_item_edited", now, userId,
            ("menuItemId", item.Id.ToString()), ("venueId", item.VenueId.ToString())));
        await db.SaveChangesAsync(ct);

        var dto = await ProjectMenu(db.VenueMenuItems.AsNoTracking().Where(mi => mi.Id == item.Id))
            .FirstAsync(ct);
        return (dto, null);
    }

    // --- Home Bar (ADR-015: private, rename allowed) -----------------------------

    public async Task<VenueDto?> HomeBarAsync(Guid userId, CancellationToken ct = default)
    {
        var id = await db.Venues.AsNoTracking()
            .Where(v => v.OwnerUserId == userId && v.VenueType == VenueType.HomeBar)
            .Select(v => (Guid?)v.Id)
            .FirstOrDefaultAsync(ct);
        return id is null ? null : await VenueDtoAsync(id.Value, ct);
    }

    public async Task<(VenueDto? Venue, Failure? Failure)> RenameHomeBarAsync(
        Guid userId, string name, CancellationToken ct = default)
    {
        name = (name ?? "").Trim();
        if (name.Length is < 2 or > 128)
            return (null, Err(400, "invalid_name", "Names are 2 to 128 characters."));

        var homeBar = await db.Venues.FirstOrDefaultAsync(
            v => v.OwnerUserId == userId && v.VenueType == VenueType.HomeBar, ct);
        if (homeBar is null)
            return (null, Err(409, "no_home_bar", "Your Home Bar is missing — contact support."));

        homeBar.Name = name;
        homeBar.NormalizedName = NameNormalizer.Normalize(name);
        await db.SaveChangesAsync(ct);
        return (await VenueDtoAsync(homeBar.Id, ct), null);
    }

    // --- Admin (founder-as-admin: tier flag, verified menu rows) -----------------

    public async Task<(VenueDto? Venue, Failure? Failure)> SetTierAsync(
        Guid venueId, string tier, CancellationToken ct = default)
    {
        if (tier is not (VenueTier.Wiki or VenueTier.Verified))
            return (null, Err(400, "invalid_tier", "Tier is 'wiki' or 'verified'."));
        var venue = await db.Venues.FirstOrDefaultAsync(
            v => v.Id == venueId && v.VenueType == VenueType.Venue, ct);
        if (venue is null)
            return (null, Err(404, "venue_not_found", "That venue doesn't exist."));

        venue.Tier = tier;
        await db.SaveChangesAsync(ct);
        return (await VenueDtoAsync(venue.Id, ct), null);
    }

    // --- Internals ----------------------------------------------------------------

    private IQueryable<Venue> PublicActiveVenues()
        => db.Venues.AsNoTracking().Where(v =>
            v.VenueType == VenueType.Venue
            && v.Status == EntityStatus.Active
            && v.Visibility == Visibility.Public);

    private async Task<Failure?> CheckMenuEditLimitAsync(Guid userId, CancellationToken ct)
    {
        var limit = await settings.GetIntAsync(MenuEditsPerDayFlag, 40, ct);
        var since = clock.GetUtcNow().AddHours(-24);
        // The events table is the wiki audit trail; a day's menu events are few
        // at MVP scale, so the per-user filter happens in memory rather than
        // betting on jsonb-indexer translation.
        var recent = await db.Events.AsNoTracking()
            .Where(e => (e.Name == "menu_item_added" || e.Name == "menu_item_edited")
                && e.OccurredAt >= since)
            .Select(e => e.Properties)
            .ToListAsync(ct);
        var mine = userId.ToString();
        var count = recent.Count(p => p is not null && p.GetValueOrDefault("userId") == mine);
        return count >= limit
            ? new Failure(429, new ApiError("rate_limited",
                "That's a lot of menu edits for one day — try again tomorrow."))
            : null;
    }

    private async Task<VenueDto?> VenueDtoAsync(Guid id, CancellationToken ct)
        => await db.Venues.AsNoTracking()
            .Where(v => v.Id == id)
            .Select(v => new VenueDto(
                v.Id, v.Name, v.VenueType, v.Tier, v.Latitude, v.Longitude,
                v.Address, v.Hours,
                db.VenueMenuItems.Count(mi => mi.VenueId == v.Id && mi.IsAvailable),
                v.CreatedAt))
            .FirstOrDefaultAsync(ct);

    private static IQueryable<MenuItemDto> ProjectMenu(IQueryable<VenueMenuItem> query)
        => query.Select(mi => new MenuItemDto(
            mi.Id, mi.DrinkId, mi.Drink.Name, mi.Drink.Producer.Name, mi.Drink.Category,
            mi.Drink.Style != null ? mi.Drink.Style.Name : null, mi.Drink.Abv,
            mi.Price, mi.IsAvailable, mi.Source, mi.LastConfirmedAt));

    /// <summary>Great-circle distance; corridor-scale accuracy is far beyond list-sorting needs.</summary>
    public static double HaversineKm(double lat1, double lng1, double lat2, double lng2)
    {
        const double R = 6371.0;
        var dLat = Radians(lat2 - lat1);
        var dLng = Radians(lng2 - lng1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
            + Math.Cos(Radians(lat1)) * Math.Cos(Radians(lat2))
              * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return R * 2 * Math.Asin(Math.Sqrt(Math.Min(1, a)));

        static double Radians(double deg) => deg * Math.PI / 180.0;
    }

    private static Failure Err(int status, string code, string message)
        => new(status, new ApiError(code, message));

    /// <summary>Null user = founder-as-admin (labeled, keeps the audit trail honest).</summary>
    private static EventRecord NewEvent(
        string name, DateTimeOffset at, Guid? userId, params (string Key, string Value)[] props)
    {
        var properties = new Dictionary<string, string>
        {
            ["userId"] = userId?.ToString() ?? "admin",
        };
        foreach (var (key, value) in props) properties[key] = value;
        return new EventRecord { Name = name, OccurredAt = at, Properties = properties };
    }
}
