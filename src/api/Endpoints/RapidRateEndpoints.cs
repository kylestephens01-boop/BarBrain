using System.Security.Claims;
using BarBrain.Api.Catalog;
using BarBrain.Api.Data.Entities;
using BarBrain.Shared.Contracts;

namespace BarBrain.Api.Endpoints;

/// <summary>
/// Sprint 4.5 surface: the rapid-rate browse read. Authenticated — the rows
/// carry the caller's own latest ratings, so there is no anonymous shape.
/// Writes stay on POST /api/ratings; this surface adds no write path.
/// </summary>
public static class RapidRateEndpoints
{
    public static IEndpointRouteBuilder MapRapidRateEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/rapidrate/drinks", async (
            string? category,
            bool? unratedOnly,
            string? sort,
            int? page,
            int? pageSize,
            ClaimsPrincipal principal,
            RapidRateQueryService rapidRate,
            CancellationToken ct) =>
        {
            var normalized = string.IsNullOrWhiteSpace(category) || category == "all"
                ? null : category.Trim().ToLowerInvariant();
            if (normalized is not null && !DrinkCategory.IsValid(normalized))
                return Results.BadRequest(new ApiError("invalid_category", "Category is beer, whiskey, or wine."));

            var normalizedSort = string.IsNullOrWhiteSpace(sort)
                ? RapidRateQueryService.SortPopular : sort.Trim().ToLowerInvariant();
            if (normalizedSort is not (RapidRateQueryService.SortPopular or RapidRateQueryService.SortName))
                return Results.BadRequest(new ApiError("invalid_sort", "Sort is popular or name."));

            var result = await rapidRate.BrowseAsync(
                UserId(principal), normalized, unratedOnly ?? false, normalizedSort,
                page ?? 1, pageSize, ct);
            return Results.Ok(result);
        })
        .RequireAuthorization()
        .WithTags("RapidRate")
        .WithName("RapidRateBrowse");

        return app;
    }

    private static Guid UserId(ClaimsPrincipal principal)
        => Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
