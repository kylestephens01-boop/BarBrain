using BarBrain.Api.Data;
using BarBrain.Api.Data.Entities;
using BarBrain.Api.Palate;
using BarBrain.Api.Settings;
using BarBrain.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BarBrain.Api.Venues;

/// <summary>
/// The four-shelf personalized menu (Sprint 5 spec; the Gate D judgment
/// surface). Shelf mapping per founder ruling 2026-07-10:
///
///   Favorites    — menu drinks whose latest own rating clears the flag
///   Familiar     — rated before below that bar, or unrated in a style the
///                  user has rated
///   New for You  — the closest share of unrated, unfamiliar drinks by
///                  palate-vector similarity, carrying "because" chips
///   Adventurous  — the far share of the same ranking
///
/// Engine output filtered to the venue menu: similarity reuses the feed's
/// attribute geometry (ADR-025) and "because" machinery (ADR-013 — every rec
/// carries its reason). No-profile fallback: popularity + style grouping,
/// explicitly not personalized. Copy stays inside BRAND.md.
/// </summary>
public sealed class PersonalizedMenuService(
    AppDbContext db,
    ISettingsService settings,
    TimeProvider clock)
{
    public const string FavoritesMinFlag = "menu.favorites_min";
    public const string NewForYouShareFlag = "menu.new_for_you_share_pct";

    public const string ShelfFavorites = "favorites";
    public const string ShelfFamiliar = "familiar";
    public const string ShelfAdventurous = "adventurous";
    public const string ShelfNewForYou = "new_for_you";

    private sealed record MenuDrink(
        MenuItemDto Item,
        Guid? StyleId,
        float[]? Vector,
        decimal? OwnRating,
        int PublicRatings);

    public async Task<PersonalizedMenuResponse> BuildAsync(
        Guid userId, Guid venueId, string venueName, CancellationToken ct = default)
    {
        var favoritesMin = await GetDecimalAsync(FavoritesMinFlag, 4.0m, ct);
        var sharePct = await settings.GetIntAsync(NewForYouShareFlag, 50, ct);

        var rows = await db.VenueMenuItems.AsNoTracking()
            .Where(mi => mi.VenueId == venueId && mi.IsAvailable
                && mi.Drink.Status == EntityStatus.Active
                && mi.Drink.Visibility == Visibility.Public
                && mi.Drink.HiddenAt == null)
            .Select(mi => new
            {
                Item = new MenuItemDto(
                    mi.Id, mi.DrinkId, mi.Drink.Name, mi.Drink.Producer.Name, mi.Drink.Category,
                    mi.Drink.Style != null ? mi.Drink.Style.Name : null, mi.Drink.Abv,
                    mi.Price, mi.IsAvailable, mi.Source, mi.LastConfirmedAt),
                mi.Drink.StyleId,
                mi.Drink.CategoryVector,
                OwnRating = db.Ratings
                    .Where(r => r.CreatedByUserId == userId && r.DrinkId == mi.DrinkId && r.IsLatest)
                    .Select(r => (decimal?)r.Value)
                    .FirstOrDefault(),
                PublicRatings = db.Ratings.Count(r =>
                    r.DrinkId == mi.DrinkId && r.IsLatest && r.Visibility == Visibility.Public
                    && r.HiddenAt == null
                    && r.CreatedBy.ShadowLimitedAt == null && r.CreatedBy.BannedAt == null),
            })
            .ToListAsync(ct);

        var menu = rows
            .Select(r => new MenuDrink(r.Item, r.StyleId, r.CategoryVector?.ToArray(), r.OwnRating, r.PublicRatings))
            .OrderBy(m => m.Item.DrinkName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var profiles = await db.UserPalateProfiles.AsNoTracking()
            .Where(p => p.UserId == userId).ToListAsync(ct);
        var totalRatings = profiles.Sum(p => p.RatingsCount);
        var warm = await settings.GetIntAsync(PalateProfileService.WarmRatingsFlag, PalateProfileService.DefaultWarmRatings, ct);
        var full = await settings.GetIntAsync(PalateProfileService.FullRatingsFlag, PalateProfileService.DefaultFullRatings, ct);
        var confidence = totalRatings >= full ? "full" : totalRatings >= warm ? "warm" : "cold";

        db.Events.Add(new EventRecord
        {
            Name = "menu_viewed_personalized",
            OccurredAt = clock.GetUtcNow(),
            Properties = new Dictionary<string, string>
            {
                ["userId"] = userId.ToString(),
                ["venueId"] = venueId.ToString(),
                ["personalized"] = (profiles.Count > 0).ToString(),
            },
        });
        await db.SaveChangesAsync(ct);

        if (profiles.Count == 0)
            return FallbackMenu(venueId, venueName, menu);

        // Styles the user has rated (any drink, latest rows) — Familiar's
        // "you know this style" signal.
        var ratedStyleIds = (await db.Ratings.AsNoTracking()
                .Where(r => r.CreatedByUserId == userId && r.IsLatest && r.Drink.StyleId != null)
                .Select(r => r.Drink.StyleId!.Value)
                .Distinct()
                .ToListAsync(ct))
            .ToHashSet();

        var attributeNames = await LoadAttributeNamesAsync(ct);
        var profileByCategory = profiles.ToDictionary(p => p.Category);

        var favorites = new List<MenuRecDto>();
        var familiar = new List<MenuRecDto>();
        var unplaced = new List<(MenuDrink Drink, double Distance)>();

        foreach (var m in menu)
        {
            profileByCategory.TryGetValue(m.Item.Category, out var profile);
            var distance = Distance(profile, m.Vector);

            if (m.OwnRating is { } own && own >= favoritesMin)
            {
                favorites.Add(new MenuRecDto(m.Item, MatchPct(distance, profile, confidence),
                    $"You rated this {own:0.#} — a proven favorite.", []));
            }
            else if (m.OwnRating is not null)
            {
                familiar.Add(new MenuRecDto(m.Item, MatchPct(distance, profile, confidence),
                    "You've had this before — you know where it lands.", []));
            }
            else if (m.StyleId is { } styleId && ratedStyleIds.Contains(styleId))
            {
                familiar.Add(new MenuRecDto(m.Item, MatchPct(distance, profile, confidence),
                    m.Item.StyleName is { } style
                        ? $"You've rated {style} before — familiar ground, new pour."
                        : "A style you've rated before — familiar ground, new pour.", []));
            }
            else
            {
                unplaced.Add((m, distance));
            }
        }

        // Unrated + unfamiliar, ranked by palate similarity: the closest share
        // shelves as New for You, the far share as Adventurous.
        var ranked = unplaced.OrderBy(u => u.Distance).ThenBy(u => u.Drink.Item.DrinkId).ToList();
        var newForYouCount = (int)Math.Ceiling(ranked.Count * Math.Clamp(sharePct, 0, 100) / 100.0);

        var newForYou = new List<MenuRecDto>();
        var adventurous = new List<MenuRecDto>();
        foreach (var (drink, distance) in ranked)
        {
            profileByCategory.TryGetValue(drink.Item.Category, out var profile);
            var names = attributeNames.GetValueOrDefault(drink.Item.Category) ?? [];
            var top = RecommendationService.TopDims(
                profile?.PreferenceVector?.ToArray(), drink.Vector, names, 2);

            if (newForYou.Count < newForYouCount)
            {
                var reason = top.Count switch
                {
                    >= 2 => $"Because your {drink.Item.Category} ratings lean {top[0].ToLowerInvariant()} and {top[1].ToLowerInvariant()} — this fits that shape.",
                    1 => $"Because your {drink.Item.Category} ratings lean {top[0].ToLowerInvariant()} — this fits that shape.",
                    _ => $"Close to the shape of your {drink.Item.Category} ratings.",
                };
                newForYou.Add(new MenuRecDto(drink.Item, MatchPct(distance, profile, confidence), reason, top));
            }
            else
            {
                adventurous.Add(new MenuRecDto(drink.Item,
                    MatchPct(distance, profile, confidence),
                    "A step outside the shape of your ratings — that's the point.", top));
            }
        }

        return new PersonalizedMenuResponse(venueId, venueName, Personalized: true, confidence,
        [
            new MenuShelf(ShelfFavorites, "Favorites", favorites),
            new MenuShelf(ShelfFamiliar, "Familiar", familiar),
            new MenuShelf(ShelfAdventurous, "Adventurous", adventurous),
            new MenuShelf(ShelfNewForYou, "New for you", newForYou),
        ]);
    }

    /// <summary>
    /// No-profile fallback (spec): popularity + style grouping. Shelves become
    /// style groups so the UI still renders tabs; nothing wears a match
    /// percent and the response says it isn't personalized.
    /// </summary>
    private static PersonalizedMenuResponse FallbackMenu(
        Guid venueId, string venueName, List<MenuDrink> menu)
    {
        var shelves = menu
            .GroupBy(m => m.Item.StyleName ?? Char.ToUpperInvariant(m.Item.Category[0]) + m.Item.Category[1..])
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new MenuShelf(
                "style_" + NameKey(g.Key),
                g.Key,
                g.OrderByDescending(m => m.PublicRatings).ThenBy(m => m.Item.DrinkName, StringComparer.OrdinalIgnoreCase)
                    .Select(m => new MenuRecDto(m.Item, null,
                        "A well-rated pick while your palate takes shape.", []))
                    .ToList()))
            .ToList();

        return new PersonalizedMenuResponse(venueId, venueName, Personalized: false, "cold", shelves);
    }

    private static string NameKey(string name)
        => new([.. name.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '_')]);

    private static double Distance(UserPalateProfile? profile, float[]? vector)
    {
        if (profile?.PreferenceVector is null || vector is null) return 1;
        var sim = RecommendationService.Cosine(profile.PreferenceVector.ToArray(), vector);
        return Math.Clamp(1 - sim, 0, 2);
    }

    /// <summary>Same display rule as the feed: a percent only past cold confidence.</summary>
    private static int? MatchPct(double distance, UserPalateProfile? profile, string confidence)
        => profile?.PreferenceVector is null || confidence == "cold" || distance > 1
            ? null
            : (int)Math.Clamp(Math.Round((1 - distance) * 100), 1, 99);

    private async Task<decimal> GetDecimalAsync(string key, decimal fallback, CancellationToken ct)
    {
        var raw = await settings.GetStringAsync(key, ct);
        return decimal.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;
    }

    private async Task<Dictionary<string, string[]>> LoadAttributeNamesAsync(CancellationToken ct)
    {
        var defs = await db.AttributeDefinitions.AsNoTracking().ToListAsync(ct);
        return defs.GroupBy(d => d.Category).ToDictionary(
            g => g.Key,
            g =>
            {
                var names = new string[VectorDims.Category];
                foreach (var def in g) names[def.DimIndex] = def.DisplayName;
                return names;
            });
    }
}
