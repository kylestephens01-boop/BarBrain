using BarBrain.Api.Data;
using BarBrain.Api.Data.Entities;
using BarBrain.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BarBrain.Api.Catalog;

/// <summary>
/// Read side of the catalog: fuzzy search (pg_trgm over drink AND producer
/// normalized names — the misspelled-"guiness" path), browse, style trees,
/// and drink detail with merged-ID redirect resolution.
///
/// Deliberate deviation from the spec's "full-text + trigram": with ADR-023
/// there is no descriptive text corpus, so tsvector FTS adds nothing over
/// trigram on short names. Revisit if licensed text ever lands.
/// </summary>
public sealed class CatalogQueryService(AppDbContext db)
{
    private sealed class SearchRow
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public string ProducerName { get; set; } = "";
        public string Category { get; set; } = "";
        public string? StyleName { get; set; }
        public decimal? Abv { get; set; }
        public double Score { get; set; }
    }

    public async Task<IReadOnlyList<DrinkSummary>> SearchAsync(
        string query, string? category, int limit, CancellationToken ct = default)
    {
        var q = NameNormalizer.Normalize(query);
        if (q.Length == 0)
            return [];

        // Trigram similarity against both the drink and its producer, so
        // "toppling goliath" ranks that brewery's drinks and "guiness" still
        // finds Guinness. word_similarity covers partial queries ("sue" →
        // "pseudo sue"). The % operators keep the GIN indexes in play.
        var rows = await db.Database.SqlQuery<SearchRow>($"""
            SELECT d."Id", d."Name", p."Name" AS "ProducerName", d."Category",
                   s."Name" AS "StyleName", d."Abv",
                   GREATEST(
                       similarity(d."NormalizedName", {q}),
                       word_similarity({q}, d."NormalizedName") * 0.9,
                       similarity(p."NormalizedName", {q}) * 0.95,
                       word_similarity({q}, p."NormalizedName") * 0.85
                   )::float8 AS "Score"
            FROM drinks d
            JOIN producers p ON p."Id" = d."ProducerId"
            LEFT JOIN styles s ON s."Id" = d."StyleId"
            WHERE d."Status" = 'active' AND d."Visibility" = 'public'
              AND ({category}::text IS NULL OR d."Category" = {category})
              AND (d."NormalizedName" % {q} OR p."NormalizedName" % {q}
                   OR word_similarity({q}, d."NormalizedName") >= 0.6
                   OR word_similarity({q}, p."NormalizedName") >= 0.6)
            ORDER BY "Score" DESC, d."Name"
            LIMIT {limit}
            """).ToListAsync(ct);

        return rows.Select(r => new DrinkSummary(
            r.Id, r.Name, r.ProducerName, r.Category, r.StyleName, r.Abv, r.Score)).ToList();
    }

    public async Task<PagedResult<DrinkSummary>> BrowseAsync(
        string? category, Guid? styleId, int page, int pageSize, CancellationToken ct = default)
    {
        var query = db.Drinks.AsNoTracking()
            .Where(d => d.Status == EntityStatus.Active && d.Visibility == Visibility.Public);
        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(d => d.Category == category);
        if (styleId is { } sid)
            query = query.Where(d => d.StyleId == sid);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(d => d.Name).ThenBy(d => d.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(d => new DrinkSummary(
                d.Id, d.Name, d.Producer.Name, d.Category,
                d.Style != null ? d.Style.Name : null, d.Abv, null))
            .ToListAsync(ct);

        return new PagedResult<DrinkSummary>(page, pageSize, total, items);
    }

    public async Task<IReadOnlyList<StyleNode>> GetStyleTreeAsync(
        string? category, CancellationToken ct = default)
    {
        var query = db.Styles.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(s => s.Category == category);

        var styles = await query.OrderBy(s => s.Code).ThenBy(s => s.Name).ToListAsync(ct);
        var byParent = styles.ToLookup(s => s.ParentStyleId);

        List<StyleNode> Build(Guid? parentId) =>
            byParent[parentId]
                .Where(s => parentId is not null || s.ParentStyleId is null)
                .Select(s => new StyleNode(
                    s.Id, s.Name, s.Code, s.Category,
                    s.AbvMin, s.AbvMax, s.IbuMin, s.IbuMax, s.SrmMin, s.SrmMax,
                    s.OgMin, s.OgMax, s.FgMin, s.FgMax,
                    s.CategoryVector is not null,
                    Build(s.Id)))
                .ToList();

        return Build(null);
    }

    /// <summary>
    /// Loads a drink by id, following merge redirects (bounded — a cycle would
    /// be a data bug, not a reason to hang). Returns null if unknown or, for
    /// now, non-public (authz proper lands in Sprint 2 against ADR-026 columns).
    /// </summary>
    public async Task<DrinkDetail?> GetDrinkAsync(Guid id, CancellationToken ct = default)
    {
        var requestedId = id;
        var redirected = false;

        for (var hops = 0; hops < 5; hops++)
        {
            var drink = await db.Drinks.AsNoTracking()
                .Include(d => d.Producer)
                .Include(d => d.Style)
                .Include(d => d.Attributes).ThenInclude(a => a.Attribute)
                .FirstOrDefaultAsync(d => d.Id == id, ct);

            if (drink is null)
                return null;

            if (drink.Status == EntityStatus.Merged && drink.MergedIntoDrinkId is { } next)
            {
                id = next;
                redirected = true;
                continue;
            }

            if (drink.Visibility != Visibility.Public)
                return null;

            var attributes = drink.Attributes
                .OrderBy(a => a.Attribute.DimIndex)
                .Select(a => new DrinkAttributeDto(
                    a.AttributeKey, a.Attribute.DisplayName, a.Value,
                    (float)Math.Round(a.Value * 10, 1), a.Source, a.Confidence))
                .ToList();

            return new DrinkDetail(
                drink.Id, drink.Name, drink.Category,
                new ProducerRef(drink.Producer.Id, drink.Producer.Name,
                    drink.Producer.City, drink.Producer.Region),
                drink.Style is null ? null : new StyleRef(drink.Style.Id, drink.Style.Name, drink.Style.Code),
                drink.Abv, drink.Source, drink.SourceRef,
                redirected, redirected ? requestedId : null,
                attributes,
                drink.CategoryVector?.ToArray(),
                drink.BridgeVector?.ToArray(),
                drink.CreatedAt, drink.UpdatedAt);
        }

        return null; // redirect chain too deep — treat as unresolvable
    }
}
