using BarBrain.Api.Data;
using BarBrain.Api.Data.Entities;
using BarBrain.Api.Settings;
using BarBrain.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BarBrain.Api.Catalog;

/// <summary>
/// Read side of the rapid-rate surface (Sprint 4.5): a per-user browse over the
/// public catalog with popularity ordering, an unrated-only filter, and the
/// caller's latest rating on each row. Lives apart from CatalogQueryService on
/// purpose — that service is the anonymous public-data surface; this one is
/// per-user by definition.
///
/// "Popular" and "most-rated" are the same signal here: the count of latest
/// public ratings (the metric RecommendationService/QuizService already use).
/// Computed on the fly — becomes a materialized rollup only if it ever shows
/// up in a profile trace.
/// </summary>
public sealed class RapidRateQueryService(AppDbContext db, ISettingsService settings)
{
    public const string PageSizeFlag = "rapidrate.page_size";
    public const int DefaultPageSize = 20;

    public const string SortPopular = "popular";
    public const string SortName = "name";

    public async Task<PagedResult<RapidRateItem>> BrowseAsync(
        Guid userId, string? category, bool unratedOnly, string sort,
        int page, int? pageSize, CancellationToken ct = default)
    {
        var size = pageSize
            ?? await settings.GetIntAsync(PageSizeFlag, DefaultPageSize, ct);
        size = Math.Clamp(size, 1, 100);
        page = Math.Max(1, page);

        var query = db.Drinks.AsNoTracking()
            .Where(d => d.Status == EntityStatus.Active && d.Visibility == Visibility.Public);
        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(d => d.Category == category);
        if (unratedOnly)
        {
            // "Haven't rated" = the engine has no signal from me at all —
            // any visibility, any origin (the QuizService semantic).
            query = query.Where(d =>
                !db.Ratings.Any(r => r.CreatedByUserId == userId && r.DrinkId == d.Id));
        }

        var projected = query.Select(d => new
        {
            d.Id,
            d.Name,
            ProducerName = d.Producer.Name,
            d.Category,
            StyleName = d.Style != null ? d.Style.Name : null,
            d.Abv,
            PublicCount = db.Ratings.Count(r =>
                r.DrinkId == d.Id && r.IsLatest && r.Visibility == Visibility.Public),
            MyValue = db.Ratings
                .Where(r => r.CreatedByUserId == userId && r.DrinkId == d.Id && r.IsLatest)
                .Select(r => (decimal?)r.Value)
                .FirstOrDefault(),
        });

        projected = sort == SortName
            ? projected.OrderBy(x => x.Name).ThenBy(x => x.Id)
            : projected.OrderByDescending(x => x.PublicCount).ThenBy(x => x.Name).ThenBy(x => x.Id);

        var total = await query.CountAsync(ct);
        var items = await projected
            .Skip((page - 1) * size).Take(size)
            .ToListAsync(ct);

        return new PagedResult<RapidRateItem>(page, size, total,
            items.Select(x => new RapidRateItem(
                x.Id, x.Name, x.ProducerName, x.Category, x.StyleName, x.Abv,
                x.PublicCount, x.MyValue)).ToList());
    }
}
