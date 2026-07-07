using System.Security.Claims;
using BarBrain.Api.Data;
using BarBrain.Api.Data.Entities;
using BarBrain.Api.Palate;
using BarBrain.Api.Settings;
using BarBrain.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BarBrain.Api.Endpoints;

/// <summary>
/// Sprint 3 surface: the sectioned feed, the palate (radar) read, and the
/// onboarding quiz (interest gate + staples). All authenticated — a feed and
/// a palate are per-user by definition; profiles are never exposed for other
/// users (ADR-026 posture).
/// </summary>
public static class PalateEndpoints
{
    public static IEndpointRouteBuilder MapPalateEndpoints(this IEndpointRouteBuilder app)
    {
        // ------------------------------------------------------------- feed
        app.MapGet("/api/feed", async (
            string? category,
            ClaimsPrincipal principal,
            RecommendationService recommendations,
            CancellationToken ct) =>
        {
            var normalized = string.IsNullOrWhiteSpace(category) || category == "all"
                ? null : category.Trim().ToLowerInvariant();
            if (normalized is not null && !DrinkCategory.IsValid(normalized))
                return Results.BadRequest(new ApiError("invalid_category", "Category is beer, whiskey, or wine."));

            var feed = await recommendations.BuildFeedAsync(UserId(principal), normalized, ct);
            return Results.Ok(feed);
        })
        .RequireAuthorization()
        .WithTags("Feed")
        .WithName("GetFeed");

        // ----------------------------------------------------------- palate
        app.MapGet("/api/palate/mine", async (
            ClaimsPrincipal principal,
            AppDbContext db,
            ISettingsService settings,
            CancellationToken ct) =>
        {
            var userId = UserId(principal);
            var profiles = await db.UserPalateProfiles.AsNoTracking()
                .Where(p => p.UserId == userId).ToListAsync(ct);
            var defs = await db.AttributeDefinitions.AsNoTracking()
                .OrderBy(d => d.DimIndex).ToListAsync(ct);
            var warm = await settings.GetIntAsync(
                PalateProfileService.WarmRatingsFlag, PalateProfileService.DefaultWarmRatings, ct);
            var full = await settings.GetIntAsync(
                PalateProfileService.FullRatingsFlag, PalateProfileService.DefaultFullRatings, ct);

            var categories = new List<CategoryPalate>();
            foreach (var profile in profiles.OrderBy(p => p.Category))
            {
                var centroid = profile.CentroidVector?.ToArray();
                var attributes = defs
                    .Where(d => d.Category == profile.Category)
                    .Select(d => new PalateAttribute(
                        d.Key, d.DisplayName,
                        centroid is null ? 0 : (float)Math.Round(centroid[d.DimIndex] * 10, 1)))
                    .ToList();
                var confidence = profile.RatingsCount >= full ? "full"
                    : profile.RatingsCount >= warm ? "warm" : "cold";
                categories.Add(new CategoryPalate(
                    profile.Category, profile.RatingsCount, confidence,
                    Math.Max(0, full - profile.RatingsCount), attributes, profile.ComputedAt));
            }
            return Results.Ok(new PalateResponse(categories));
        })
        .RequireAuthorization()
        .WithTags("Palate")
        .WithName("MyPalate");

        // ------------------------------------------------- onboarding: gate
        app.MapGet("/api/quiz/interests", async (
            ClaimsPrincipal principal,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var userId = UserId(principal);
            var interests = await db.UserCategoryInterests.AsNoTracking()
                .Where(i => i.UserId == userId).Select(i => i.Category).ToListAsync(ct);
            return Results.Ok(new InterestsResponse(interests));
        })
        .RequireAuthorization()
        .WithTags("Quiz")
        .WithName("GetInterests");

        app.MapPut("/api/quiz/interests", async (
            InterestsRequest request,
            ClaimsPrincipal principal,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var wanted = (request.Categories ?? [])
                .Select(c => c.Trim().ToLowerInvariant())
                .Distinct()
                .ToList();
            if (wanted.Count == 0 || wanted.Any(c => !DrinkCategory.IsValid(c)))
                return Results.BadRequest(new ApiError("invalid_categories",
                    "Pick at least one of beer, whiskey, wine."));

            var userId = UserId(principal);
            var existing = await db.UserCategoryInterests
                .Where(i => i.UserId == userId).ToListAsync(ct);
            db.UserCategoryInterests.RemoveRange(existing.Where(i => !wanted.Contains(i.Category)));
            foreach (var category in wanted.Where(c => existing.All(i => i.Category != c)))
                db.UserCategoryInterests.Add(new UserCategoryInterest { UserId = userId, Category = category });
            await db.SaveChangesAsync(ct);

            return Results.Ok(new InterestsResponse(wanted));
        })
        .RequireAuthorization()
        .WithTags("Quiz")
        .WithName("SetInterests");

        // ------------------------------------------------ onboarding: quiz
        app.MapGet("/api/quiz", async (
            ClaimsPrincipal principal,
            QuizService quiz,
            CancellationToken ct) =>
            Results.Ok(await quiz.BuildAsync(UserId(principal), ct)))
        .RequireAuthorization()
        .WithTags("Quiz")
        .WithName("GetQuiz");

        // Funnel event (ADR-017): the quiz finished; ratings were already
        // written one by one through POST /api/ratings with origin=quiz.
        app.MapPost("/api/quiz/complete", async (
            ClaimsPrincipal principal,
            AppDbContext db,
            TimeProvider clock,
            CancellationToken ct) =>
        {
            db.Events.Add(new EventRecord
            {
                Name = "quiz_completed",
                OccurredAt = clock.GetUtcNow(),
                Properties = new Dictionary<string, string>
                {
                    ["userId"] = UserId(principal).ToString(),
                },
            });
            await db.SaveChangesAsync(ct);
            return Results.Accepted();
        })
        .RequireAuthorization()
        .WithTags("Quiz")
        .WithName("CompleteQuiz");

        return app;
    }

    private static Guid UserId(ClaimsPrincipal principal)
        => Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
