using System.Security.Claims;
using BarBrain.Api.Data;
using BarBrain.Api.Data.Entities;
using BarBrain.Api.Palate;
using BarBrain.Api.Settings;
using BarBrain.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BarBrain.Api.Endpoints;

/// <summary>
/// Sprint 4 surface: "Your Matches" (one-way, read-only — no interaction of any
/// kind, ADR-014), the hide-me + digest-subscription toggles, and the CAN-SPAM
/// one-click unsubscribe. The unsubscribe endpoint is deliberately UNauthed and
/// under <c>/api/*</c> so the digest link is a true full-page navigation the
/// SPA service worker leaves alone (Sprint 2 fix).
/// </summary>
public static class MatchEndpoints
{
    public const string ListSizeFlag = "match.list_size";

    public static IEndpointRouteBuilder MapMatchEndpoints(this IEndpointRouteBuilder app)
    {
        // ---------------------------------------------------------- matches
        app.MapGet("/api/matches", async (
            ClaimsPrincipal principal,
            MatchService matches,
            ISettingsService settings,
            CancellationToken ct) =>
        {
            var take = await settings.GetIntAsync(ListSizeFlag, 25, ct);
            return Results.Ok(await matches.GetMatchesAsync(UserId(principal), take, ct));
        })
        .RequireAuthorization()
        .WithTags("Matches")
        .WithName("GetMatches");

        // ------------------------------------------- matching/digest settings
        app.MapGet("/api/matches/settings", async (
            ClaimsPrincipal principal,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var userId = UserId(principal);
            var row = await db.Users.AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => new { u.HideFromMatches, u.DigestUnsubscribedAt })
                .FirstOrDefaultAsync(ct);
            if (row is null) return Results.NotFound();
            return Results.Ok(new MatchSettingsResponse(
                row.HideFromMatches, row.DigestUnsubscribedAt is null));
        })
        .RequireAuthorization()
        .WithTags("Matches")
        .WithName("GetMatchSettings");

        app.MapPut("/api/matches/hide", async (
            HideFromMatchesRequest request,
            ClaimsPrincipal principal,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var userId = UserId(principal);
            await db.Users.Where(u => u.Id == userId)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.HideFromMatches, request.Hidden), ct);
            return Results.Ok(new { hidden = request.Hidden });
        })
        .RequireAuthorization()
        .WithTags("Matches")
        .WithName("SetHideFromMatches");

        // In-app digest opt-out/in (the emailed link uses the token route below).
        app.MapPut("/api/digest/subscription", async (
            DigestSubscriptionRequest request,
            ClaimsPrincipal principal,
            AppDbContext db,
            TimeProvider clock,
            CancellationToken ct) =>
        {
            var userId = UserId(principal);
            DateTimeOffset? unsubscribedAt = request.Subscribed ? null : clock.GetUtcNow();
            await db.Users.Where(u => u.Id == userId)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.DigestUnsubscribedAt, unsubscribedAt), ct);
            return Results.Ok(new { subscribed = request.Subscribed });
        })
        .RequireAuthorization()
        .WithTags("Digest")
        .WithName("SetDigestSubscription");

        // ------------------------------------------ CAN-SPAM one-click unsub
        // Unauthenticated on purpose: an email client following the link has no
        // session. The token is the credential. Full-page HTML response.
        app.MapGet("/api/digest/unsubscribe", async (
            Guid? token,
            AppDbContext db,
            TimeProvider clock,
            CancellationToken ct) =>
        {
            if (token is null || token == Guid.Empty)
                return Results.Content(UnsubscribePage(false), "text/html");

            var user = await db.Users.FirstOrDefaultAsync(u => u.DigestUnsubscribeToken == token, ct);
            if (user is null)
                return Results.Content(UnsubscribePage(false), "text/html");

            if (user.DigestUnsubscribedAt is null)
            {
                user.DigestUnsubscribedAt = clock.GetUtcNow();
                db.Events.Add(new EventRecord
                {
                    Name = "digest_unsubscribed",
                    OccurredAt = clock.GetUtcNow(),
                    Properties = new Dictionary<string, string> { ["userId"] = user.Id.ToString() },
                });
                await db.SaveChangesAsync(ct);
            }
            return Results.Content(UnsubscribePage(true), "text/html");
        })
        .AllowAnonymous()
        .WithTags("Digest")
        .WithName("DigestUnsubscribe");

        // ---------------------------------------------- admin operational triggers
        // The nightly jobs run on a schedule; these let the founder (Gate C2) and
        // e2e build the match graph / fire a digest on demand. Admin-token gated.
        var admin = app.MapGroup("/api/admin")
            .AddEndpointFilter(AdminAuth.AdminTokenFilter)
            .WithTags("Admin");

        admin.MapPost("/matches/rebuild", async (MatchService matches, CancellationToken ct) =>
            Results.Ok(await matches.ComputeAllAsync(ct)))
            .WithName("RebuildMatches");

        // force=true bypasses the digest.enabled flag so a preview run works
        // before the digest is switched on (still log-only without an address).
        admin.MapPost("/digest/run", async (
            bool? force, BarBrain.Api.Digest.DigestService digest, CancellationToken ct) =>
            Results.Ok(await digest.RunOnceAsync(respectEnabledFlag: !(force ?? false), ct)))
            .WithName("RunDigest");

        return app;
    }

    // Minimal self-contained confirmation page — no SPA, no fonts/CDN (the link
    // bypasses the service worker). Copy stays within BRAND.md voice.
    private static string UnsubscribePage(bool ok)
    {
        var message = ok
            ? "You're unsubscribed from the weekly BarBrain digest. Your ratings and palate are untouched."
            : "That unsubscribe link isn't valid. If you keep getting the digest, reach out and we'll sort it.";
        return $$"""
            <!doctype html><html lang="en"><head><meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>BarBrain digest</title>
            <style>
              body{background:#0f1115;color:#e8e8ea;font-family:system-ui,sans-serif;
                   display:flex;min-height:100vh;align-items:center;justify-content:center;margin:0}
              main{max-width:32rem;padding:2rem;text-align:center}
              a{color:#3ecfb8}
            </style></head>
            <body><main>
              <h1>{{(ok ? "Unsubscribed" : "Link not valid")}}</h1>
              <p>{{message}}</p>
              <p><a href="/">Back to BarBrain</a></p>
            </main></body></html>
            """;
    }

    private static Guid UserId(ClaimsPrincipal principal)
        => Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
