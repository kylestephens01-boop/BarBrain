using System.Text.Json;
using BarBrain.Api.Catalog;
using BarBrain.Api.Data;
using BarBrain.Api.Data.Entities;
using BarBrain.Api.Settings;
using BarBrain.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BarBrain.Api.Palate;

/// <summary>
/// Builds the onboarding staples quiz (Sprint 3 spec): config-driven lists —
/// beer/whiskey by PRODUCT, wine by VARIETAL (each varietal resolves to a
/// representative catalog drink so quiz ratings stay REAL ratings, ADR-012
/// provenance 'quiz'). Lists live in the settings table (Hard Rule 10) and
/// are editable without a deploy; entries that don't resolve against the
/// current catalog drop out gracefully (sparse-catalog degradation).
/// Drinks the user already rated are omitted — they've already told us.
/// </summary>
public sealed class QuizService(AppDbContext db, ISettingsService settings)
{
    public const string StaplesFlagPrefix = "quiz.staples.";

    private sealed record StapleEntry(string? Producer, string? Name, string? Style);

    public async Task<QuizResponse> BuildAsync(Guid userId, CancellationToken ct = default)
    {
        var interests = await db.UserCategoryInterests.AsNoTracking()
            .Where(i => i.UserId == userId)
            .Select(i => i.Category)
            .ToListAsync(ct);
        // Only claimed categories get quizzed (charter). Nothing claimed →
        // nothing to quiz; the UI routes through the interest gate first.
        var categories = new List<QuizCategory>();
        foreach (var category in DrinkCategory.All.Where(interests.Contains))
        {
            var items = await BuildCategoryAsync(userId, category, ct);
            if (items.Count > 0)
                categories.Add(new QuizCategory(category, items));
        }
        return new QuizResponse(categories);
    }

    private async Task<List<QuizItem>> BuildCategoryAsync(
        Guid userId, string category, CancellationToken ct)
    {
        var json = await settings.GetStringAsync(StaplesFlagPrefix + category, ct);
        if (string.IsNullOrWhiteSpace(json))
            return [];

        List<StapleEntry>? entries;
        try
        {
            entries = JsonSerializer.Deserialize<List<StapleEntry>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return []; // operator typo in the flag shouldn't 500 onboarding
        }
        if (entries is null || entries.Count == 0)
            return [];

        var items = new List<QuizItem>();
        foreach (var entry in entries)
        {
            var item = entry switch
            {
                { Style: { Length: > 0 } style } => await ResolveVarietalAsync(userId, category, style, ct),
                { Name: { Length: > 0 } name } => await ResolveProductAsync(userId, category, entry.Producer, name, ct),
                _ => null,
            };
            if (item is not null)
                items.Add(item);
        }
        return items;
    }

    private async Task<QuizItem?> ResolveProductAsync(
        Guid userId, string category, string? producer, string name, CancellationToken ct)
    {
        var normalized = NameNormalizer.Normalize(name);
        var query = db.Drinks.AsNoTracking()
            .Where(d => d.Category == category
                && d.Status == EntityStatus.Active
                && d.Visibility == Visibility.Public
                && d.NormalizedName == normalized);
        if (!string.IsNullOrWhiteSpace(producer))
        {
            var producerNormalized = NameNormalizer.Normalize(producer);
            query = query.Where(d => d.Producer.NormalizedName == producerNormalized);
        }

        var drink = await query
            .OrderBy(d => d.Id)
            .Select(d => new
            {
                d.Id, d.Name, ProducerName = d.Producer.Name,
                StyleName = d.Style != null ? d.Style.Name : null,
                Rated = db.Ratings.Any(r => r.CreatedByUserId == userId && r.DrinkId == d.Id),
            })
            .FirstOrDefaultAsync(ct);

        if (drink is null || drink.Rated)
            return null;
        return new QuizItem(drink.Id, drink.Name,
            drink.StyleName is null ? drink.ProducerName : $"{drink.ProducerName} · {drink.StyleName}");
    }

    /// <summary>
    /// Wine by varietal: the quiz card is the STYLE ("Cabernet Sauvignon");
    /// the rating lands on a representative drink of that style — most
    /// publicly-rated first, then name, so the pick is stable and sane.
    /// </summary>
    private async Task<QuizItem?> ResolveVarietalAsync(
        Guid userId, string category, string styleRef, CancellationToken ct)
    {
        var normalized = NameNormalizer.Normalize(styleRef);
        var style = await db.Styles.AsNoTracking()
            .Where(s => s.Category == category
                && (s.Code == styleRef || s.NormalizedName == normalized))
            .OrderBy(s => s.Code == styleRef ? 0 : 1)
            .Select(s => new { s.Id, s.Name })
            .FirstOrDefaultAsync(ct);
        if (style is null)
            return null;

        var drink = await db.Drinks.AsNoTracking()
            .Where(d => d.Category == category
                && d.StyleId == style.Id
                && d.Status == EntityStatus.Active
                && d.Visibility == Visibility.Public
                && !db.Ratings.Any(r => r.CreatedByUserId == userId && r.DrinkId == d.Id))
            .OrderByDescending(d => db.Ratings.Count(r => r.DrinkId == d.Id && r.IsLatest && r.Visibility == Visibility.Public))
            .ThenBy(d => d.Name)
            .Select(d => new { d.Id, d.Name, ProducerName = d.Producer.Name })
            .FirstOrDefaultAsync(ct);

        return drink is null
            ? null
            : new QuizItem(drink.Id, style.Name, $"e.g. {drink.ProducerName} {drink.Name}");
    }
}
