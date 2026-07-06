using BarBrain.Api.Catalog;

namespace BarBrain.Api.Endpoints;

/// <summary>
/// Public read-only catalog API (Sprint 1 spec): search, browse, style trees,
/// drink detail with merged-ID redirect handling. No auth — the catalog is
/// public data; per-user visibility enforcement arrives with Sprint 2 authz
/// (the query service already filters to public+active).
/// </summary>
public static class CatalogEndpoints
{
    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        var catalog = app.MapGroup("/api/catalog").WithTags("Catalog");

        catalog.MapGet("/search", async (
            string q,
            string? category,
            int? limit,
            CatalogQueryService queries,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(q))
                return Results.BadRequest("Query parameter 'q' is required.");
            var results = await queries.SearchAsync(
                q, NormalizeCategory(category), Math.Clamp(limit ?? 25, 1, 100), ct);
            return Results.Ok(results);
        })
        .WithName("SearchCatalog");

        catalog.MapGet("/drinks", async (
            string? category,
            Guid? styleId,
            int? page,
            int? pageSize,
            CatalogQueryService queries,
            CancellationToken ct) =>
        {
            var result = await queries.BrowseAsync(
                NormalizeCategory(category), styleId,
                Math.Max(page ?? 1, 1), Math.Clamp(pageSize ?? 50, 1, 200), ct);
            return Results.Ok(result);
        })
        .WithName("BrowseDrinks");

        catalog.MapGet("/drinks/{id:guid}", async (
            Guid id,
            CatalogQueryService queries,
            CancellationToken ct) =>
        {
            var drink = await queries.GetDrinkAsync(id, ct);
            return drink is null ? Results.NotFound() : Results.Ok(drink);
        })
        .WithName("GetDrink");

        catalog.MapGet("/styles", async (
            string? category,
            CatalogQueryService queries,
            CancellationToken ct) =>
        {
            var tree = await queries.GetStyleTreeAsync(NormalizeCategory(category), ct);
            return Results.Ok(tree);
        })
        .WithName("GetStyleTree");

        return app;
    }

    private static string? NormalizeCategory(string? category)
        => string.IsNullOrWhiteSpace(category) ? null : category.Trim().ToLowerInvariant();
}
