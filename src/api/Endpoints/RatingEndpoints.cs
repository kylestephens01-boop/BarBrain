using System.Security.Claims;
using BarBrain.Api.Auth;
using BarBrain.Api.Data.Entities;
using BarBrain.Api.Ratings;
using BarBrain.Api.Settings;
using BarBrain.Shared.Contracts;
using Microsoft.AspNetCore.Identity;

namespace BarBrain.Api.Endpoints;

/// <summary>
/// The core rating loop's HTTP surface. Everything under /api/ratings requires
/// the session cookie; ownership scoping happens in <see cref="RatingService"/>
/// (other users' private rows 404 — never 403, existence must not leak).
/// The one anonymous route is the drink page's public ratings.
/// </summary>
public static class RatingEndpoints
{
    public static IEndpointRouteBuilder MapRatingEndpoints(this IEndpointRouteBuilder app)
    {
        var ratings = app.MapGroup("/api/ratings").WithTags("Ratings").RequireAuthorization()
            .AddEndpointFilter(ModerationGuards.NotBannedFilter); // Sprint 6: banned accounts can't write

        ratings.MapPost("/", async (
            RateRequest request,
            ClaimsPrincipal principal,
            RatingService service,
            UserManager<User> users,
            ISettingsService settings,
            TimeProvider clock,
            CancellationToken ct) =>
        {
            var user = await users.GetUserAsync(principal);
            if (user is null) return Results.Unauthorized();

            // Soft verification (ADR-011): full use immediately, rating locked
            // once the flag-driven grace window lapses unverified.
            if (!await AuthFlags.CanRateAsync(user, settings, clock, ct))
                return Results.Json(new ApiError("verification_required",
                    "Verify your email to keep rating — the link is in your inbox."),
                    statusCode: StatusCodes.Status403Forbidden);

            var (rating, failure) = await service.CreateAsync(user.Id, request, ct);
            return failure is not null
                ? Results.Json(failure.Error, statusCode: failure.Status)
                : Results.Created($"/api/ratings/{rating!.Id}", rating);
        })
        .WithName("CreateRating");

        ratings.MapGet("/mine", async (
            string? category,
            int? page,
            int? pageSize,
            ClaimsPrincipal principal,
            RatingService service,
            CancellationToken ct) =>
        {
            var userId = RequireUserId(principal);
            var normalized = string.IsNullOrWhiteSpace(category)
                ? null : category.Trim().ToLowerInvariant();
            if (normalized is not null && !DrinkCategory.IsValid(normalized))
                return Results.BadRequest(new ApiError("invalid_category", "Category is beer, whiskey, or wine."));

            var result = await service.JournalAsync(
                userId, normalized, Math.Max(page ?? 1, 1), Math.Clamp(pageSize ?? 50, 1, 200), ct);
            return Results.Ok(result);
        })
        .WithName("MyJournal");

        ratings.MapGet("/mine/drink/{drinkId:guid}", async (
            Guid drinkId,
            ClaimsPrincipal principal,
            RatingService service,
            CancellationToken ct) =>
            Results.Ok(await service.OwnForDrinkAsync(RequireUserId(principal), drinkId, ct)))
        .WithName("MyRatingsForDrink");

        ratings.MapPatch("/{id:guid}", async (
            Guid id,
            RatingUpdateRequest request,
            ClaimsPrincipal principal,
            RatingService service,
            CancellationToken ct) =>
        {
            var (rating, failure) = await service.UpdateAsync(RequireUserId(principal), id, request, ct);
            return failure is not null
                ? Results.Json(failure.Error, statusCode: failure.Status)
                : Results.Ok(rating);
        })
        .WithName("UpdateRating");

        ratings.MapDelete("/{id:guid}", async (
            Guid id,
            ClaimsPrincipal principal,
            RatingService service,
            CancellationToken ct) =>
        {
            var failure = await service.DeleteAsync(RequireUserId(principal), id, ct);
            return failure is not null
                ? Results.Json(failure.Error, statusCode: failure.Status)
                : Results.NoContent();
        })
        .WithName("DeleteRating");

        // Anonymous: the drink page's aggregate + recent PUBLIC ratings.
        // Latest rows only; private rows are invisible here by construction.
        app.MapGet("/api/catalog/drinks/{id:guid}/ratings", async (
            Guid id,
            int? limit,
            RatingService service,
            CancellationToken ct) =>
            Results.Ok(await service.PublicForDrinkAsync(id, Math.Clamp(limit ?? 10, 1, 50), ct)))
        .WithTags("Catalog")
        .WithName("DrinkPublicRatings");

        return app;
    }

    /// <summary>Only called inside RequireAuthorization routes — the claim exists.</summary>
    private static Guid RequireUserId(ClaimsPrincipal principal)
        => Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
