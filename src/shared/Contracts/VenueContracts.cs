namespace BarBrain.Shared.Contracts;

// Sprint 5 (ADR-015): venues, wiki menus, check-in, personalized four-shelf menu.

/// <summary>Wiki add-venue. Geo is optional (denied location permission still adds).</summary>
public record VenueCreateRequest(
    string Name,
    double? Latitude,
    double? Longitude,
    string? Address,
    string? Hours);

public record HomeBarRenameRequest(string Name);

/// <summary>Add a drink to a venue's menu (wiki contribution).</summary>
public record MenuItemAddRequest(Guid DrinkId, decimal? Price);

/// <summary>
/// Wiki menu edit: price and availability change in place; Confirm touches the
/// last-confirmed timestamp ("yes, this really is on the menu today").
/// </summary>
public record MenuItemUpdateRequest(decimal? Price, bool? IsAvailable, bool? Confirm);

public record CheckinRequest(Guid VenueId);

/// <summary>Admin-only (founder): flip a public venue between wiki and verified.</summary>
public record VenueTierRequest(string Tier);

public record VenueDto(
    Guid Id,
    string Name,
    string VenueType,
    string? Tier,
    double? Latitude,
    double? Longitude,
    string? Address,
    string? Hours,
    int MenuCount,
    DateTimeOffset CreatedAt);

/// <summary>Nearby list row. DistanceKm is null when either side lacks geo (sorts last).</summary>
public record NearbyVenueDto(
    Guid Id,
    string Name,
    string? Tier,
    string? Address,
    double? DistanceKm,
    int MenuCount);

public record CheckinDto(
    Guid Id,
    Guid VenueId,
    string VenueName,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);

/// <summary>A public rating recently tagged at this venue (recent activity).</summary>
public record VenueActivityDto(
    string Handle,
    string DrinkName,
    string Category,
    decimal Value,
    DateTimeOffset CreatedAt);

/// <summary>The venue page: info + caller's check-in state + recent activity.</summary>
public record VenuePageDto(
    VenueDto Venue,
    bool CheckedInHere,
    CheckinDto? ActiveCheckin,
    IReadOnlyList<VenueActivityDto> RecentActivity);

public record MenuItemDto(
    Guid Id,
    Guid DrinkId,
    string DrinkName,
    string ProducerName,
    string Category,
    string? StyleName,
    decimal? Abv,
    decimal? Price,
    bool IsAvailable,
    string Source,
    DateTimeOffset? LastConfirmedAt);

/// <summary>A menu item with its personalization (null fields on the plain menu).</summary>
public record MenuRecDto(
    MenuItemDto Item,
    int? MatchPct,
    string? Reason,
    IReadOnlyList<string> ReasonAttributes);

public record MenuShelf(string Key, string Title, IReadOnlyList<MenuRecDto> Items);

/// <summary>
/// The four-shelf personalized menu (post-check-in). When the caller has no
/// palate profile, Personalized is false and shelves fall back to popularity +
/// style grouping (Sprint 5 spec).
/// </summary>
public record PersonalizedMenuResponse(
    Guid VenueId,
    string VenueName,
    bool Personalized,
    string Confidence,
    IReadOnlyList<MenuShelf> Shelves);
