using System.Security.Claims;
using BarBrain.Api.Data.Entities;
using BarBrain.Api.Venues;
using BarBrain.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BarBrain.Api.Endpoints;

/// <summary>
/// Venues, wiki menus, check-in, the four-shelf personalized menu, and the QR
/// kit (Sprint 5, ADR-015). Reads are anonymous like the catalog (Home Bars
/// 404 for anyone but their owner — existence must not leak); writes require
/// the session cookie; tier changes are founder-as-admin only.
/// </summary>
public static class VenueEndpoints
{
    public static IEndpointRouteBuilder MapVenueEndpoints(this IEndpointRouteBuilder app)
    {
        var venues = app.MapGroup("/api/venues").WithTags("Venues");

        // --- Discovery (anonymous, like catalog browse) -----------------------

        venues.MapGet("/nearby", async (
            double? lat,
            double? lng,
            VenueService service,
            CancellationToken ct) =>
        {
            if ((lat is null) != (lng is null))
                return Results.BadRequest(new ApiError("invalid_geo", "Latitude and longitude come together."));
            return Results.Ok(await service.NearbyAsync(lat, lng, ct));
        })
        .WithName("NearbyVenues");

        venues.MapGet("/{id:guid}", async (
            Guid id,
            ClaimsPrincipal principal,
            VenueService service,
            CancellationToken ct) =>
        {
            var (page, failure) = await service.GetPageAsync(id, OptionalUserId(principal), ct);
            return failure is not null
                ? Results.Json(failure.Error, statusCode: failure.Status)
                : Results.Ok(page);
        })
        .WithName("VenuePage");

        venues.MapGet("/{id:guid}/menu", async (
            Guid id,
            VenueService service,
            CancellationToken ct) =>
            Results.Ok(await service.MenuAsync(id, ct)))
        .WithName("VenueMenu");

        // --- QR kit (anonymous: the printed QR itself resolves here) ----------

        venues.MapGet("/{id:guid}/qr.png", async (
            Guid id,
            VenueService service,
            VenueKitService kit,
            CancellationToken ct) =>
        {
            var (page, failure) = await service.GetPageAsync(id, null, ct);
            if (failure is not null)
                return Results.Json(failure.Error, statusCode: failure.Status);
            var url = await kit.VenueUrlAsync(page!.Venue.Id, ct);
            return Results.File(kit.QrPng(url), "image/png");
        })
        .WithName("VenueQr");

        venues.MapGet("/{id:guid}/onepager.pdf", async (
            Guid id,
            VenueService service,
            VenueKitService kit,
            CancellationToken ct) =>
        {
            var (page, failure) = await service.GetPageAsync(id, null, ct);
            if (failure is not null)
                return Results.Json(failure.Error, statusCode: failure.Status);
            var url = await kit.VenueUrlAsync(page!.Venue.Id, ct);
            return Results.File(kit.OnePagerPdf(page.Venue.Name, url),
                "application/pdf", $"barbrain-{page.Venue.Id}.pdf");
        })
        .WithName("VenueOnePager");

        // --- Wiki writes (authed) ---------------------------------------------

        var authed = app.MapGroup("/api/venues").WithTags("Venues").RequireAuthorization();

        authed.MapPost("/", async (
            VenueCreateRequest request,
            ClaimsPrincipal principal,
            VenueService service,
            CancellationToken ct) =>
        {
            var (venue, failure) = await service.CreateAsync(RequireUserId(principal), request, ct);
            return failure is not null
                ? Results.Json(failure.Error, statusCode: failure.Status)
                : Results.Created($"/api/venues/{venue!.Id}", venue);
        })
        .WithName("AddVenue");

        authed.MapPost("/{id:guid}/menu", async (
            Guid id,
            MenuItemAddRequest request,
            ClaimsPrincipal principal,
            VenueService service,
            CancellationToken ct) =>
        {
            var (item, failure) = await service.AddMenuItemAsync(
                RequireUserId(principal), id, request, MenuItemSource.Crowd, ct);
            return failure is not null
                ? Results.Json(failure.Error, statusCode: failure.Status)
                : Results.Created($"/api/venues/{id}/menu", item);
        })
        .WithName("AddMenuItem");

        authed.MapPatch("/menu-items/{id:guid}", async (
            Guid id,
            MenuItemUpdateRequest request,
            ClaimsPrincipal principal,
            VenueService service,
            CancellationToken ct) =>
        {
            var (item, failure) = await service.UpdateMenuItemAsync(RequireUserId(principal), id, request, ct);
            return failure is not null
                ? Results.Json(failure.Error, statusCode: failure.Status)
                : Results.Ok(item);
        })
        .WithName("UpdateMenuItem");

        // --- Personalized menu (authed; the check-in unlocks it) ---------------

        authed.MapGet("/{id:guid}/menu/personalized", async (
            Guid id,
            ClaimsPrincipal principal,
            VenueService venueService,
            CheckinService checkins,
            PersonalizedMenuService menus,
            CancellationToken ct) =>
        {
            var userId = RequireUserId(principal);
            var (page, failure) = await venueService.GetPageAsync(id, userId, ct);
            if (failure is not null)
                return Results.Json(failure.Error, statusCode: failure.Status);

            // Pre-check-in the client shows the teaser + plain menu; this
            // endpoint is the thing the teaser says checking in unlocks.
            var active = await checkins.ActiveVenueIdAsync(userId, ct);
            if (active != page!.Venue.Id)
                return Results.Json(new ApiError("checkin_required",
                    "Check in to see this menu sorted for you."),
                    statusCode: StatusCodes.Status409Conflict);

            return Results.Ok(await menus.BuildAsync(userId, page.Venue.Id, page.Venue.Name, ct));
        })
        .WithName("PersonalizedMenu");

        // --- Home Bar (ADR-015: private, rename allowed) ------------------------

        authed.MapGet("/home-bar", async (
            ClaimsPrincipal principal,
            VenueService service,
            CancellationToken ct) =>
        {
            var homeBar = await service.HomeBarAsync(RequireUserId(principal), ct);
            return homeBar is null
                ? Results.Json(new ApiError("no_home_bar", "Your Home Bar is missing — contact support."),
                    statusCode: StatusCodes.Status409Conflict)
                : Results.Ok(homeBar);
        })
        .WithName("MyHomeBar");

        authed.MapPatch("/home-bar", async (
            HomeBarRenameRequest request,
            ClaimsPrincipal principal,
            VenueService service,
            CancellationToken ct) =>
        {
            var (venue, failure) = await service.RenameHomeBarAsync(
                RequireUserId(principal), request.Name, ct);
            return failure is not null
                ? Results.Json(failure.Error, statusCode: failure.Status)
                : Results.Ok(venue);
        })
        .WithName("RenameHomeBar");

        // --- Check-in (authed; one tap, no GPS gate) -----------------------------

        var checkinGroup = app.MapGroup("/api/checkins").WithTags("Venues").RequireAuthorization();

        checkinGroup.MapPost("/", async (
            CheckinRequest request,
            ClaimsPrincipal principal,
            CheckinService service,
            CancellationToken ct) =>
        {
            var (checkin, failure) = await service.CheckinAsync(RequireUserId(principal), request.VenueId, ct);
            return failure is not null
                ? Results.Json(failure.Error, statusCode: failure.Status)
                : Results.Created($"/api/checkins/{checkin!.Id}", checkin);
        })
        .WithName("Checkin");

        checkinGroup.MapGet("/active", async (
            ClaimsPrincipal principal,
            CheckinService service,
            CancellationToken ct) =>
        {
            var active = await service.ActiveAsync(RequireUserId(principal), ct);
            return active is null ? Results.NoContent() : Results.Ok(active);
        })
        .WithName("ActiveCheckin");

        // --- Admin: tier flag + verified-tier menu rows (founder-as-admin) ------

        var admin = app.MapGroup("/api/admin/venues")
            .AddEndpointFilter(AdminAuth.AdminTokenFilter)
            .WithTags("Admin");

        admin.MapPost("/{id:guid}/tier", async (
            Guid id,
            VenueTierRequest request,
            VenueService service,
            CancellationToken ct) =>
        {
            var (venue, failure) = await service.SetTierAsync(id, request.Tier, ct);
            return failure is not null
                ? Results.Json(failure.Error, statusCode: failure.Status)
                : Results.Ok(venue);
        })
        .WithName("SetVenueTier");

        // Verified menus (founder ruling 2026-07-10): same menu machinery, rows
        // land as source='venue', ownerless (the admin token isn't a user) and
        // exempt from the wiki rate limit — no venue-owner auth concept in MVP.
        admin.MapPost("/{id:guid}/menu", async (
            Guid id,
            MenuItemAddRequest request,
            VenueService service,
            CancellationToken ct) =>
        {
            var (item, failure) = await service.AddMenuItemAsync(
                userId: null, id, request, MenuItemSource.Venue, ct);
            return failure is not null
                ? Results.Json(failure.Error, statusCode: failure.Status)
                : Results.Created($"/api/venues/{id}/menu", item);
        })
        .WithName("AddVerifiedMenuItem");

        return app;
    }

    private static Guid RequireUserId(ClaimsPrincipal principal)
        => Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private static Guid? OptionalUserId(ClaimsPrincipal principal)
        => Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
