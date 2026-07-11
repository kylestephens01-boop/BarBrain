using System.Security.Claims;
using BarBrain.Api.Badges;

namespace BarBrain.Api.Endpoints;

/// <summary>
/// Badges HTTP surface (Sprint 6): the profile gallery, the unseen-award poll
/// that drives toasts, and the acknowledgement. All authed — badges are the
/// caller's own state; there is no public badge surface (profile tab + toast
/// only, founder ruling 2026-07-10).
/// </summary>
public static class BadgeEndpoints
{
    public static IEndpointRouteBuilder MapBadgeEndpoints(this IEndpointRouteBuilder app)
    {
        var badges = app.MapGroup("/api/badges").WithTags("Badges").RequireAuthorization();

        badges.MapGet("/", async (
            ClaimsPrincipal principal, BadgeService service, CancellationToken ct) =>
            Results.Ok(await service.GalleryAsync(RequireUserId(principal), ct)))
        .WithName("BadgeGallery");

        badges.MapGet("/unseen", async (
            ClaimsPrincipal principal, BadgeService service, CancellationToken ct) =>
            Results.Ok(await service.UnseenAsync(RequireUserId(principal), ct)))
        .WithName("UnseenBadges");

        badges.MapPost("/seen", async (
            ClaimsPrincipal principal, BadgeService service, CancellationToken ct) =>
        {
            await service.MarkSeenAsync(RequireUserId(principal), ct);
            return Results.NoContent();
        })
        .WithName("MarkBadgesSeen");

        return app;
    }

    private static Guid RequireUserId(ClaimsPrincipal principal)
        => Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
