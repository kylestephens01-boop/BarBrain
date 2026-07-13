using System.Security.Claims;
using BarBrain.Api.Data.Entities;
using BarBrain.Api.Privacy;
using BarBrain.Shared.Contracts;
using Microsoft.AspNetCore.Identity;

namespace BarBrain.Api.Endpoints;

/// <summary>
/// Privacy self-serve (Sprint 7, ADR-018): export my data, schedule/cancel my
/// deletion. All owner-scoped; the export is a download so it works as a plain
/// link from the profile page.
/// </summary>
public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var account = app.MapGroup("/api/account").WithTags("Account").RequireAuthorization();

        account.MapGet("/export", async (
            ClaimsPrincipal principal,
            UserManager<User> users,
            AccountDataService accounts,
            HttpContext http,
            CancellationToken ct) =>
        {
            var user = await users.GetUserAsync(principal);
            if (user is null) return Results.Unauthorized();

            var export = await accounts.BuildExportAsync(user.Id, ct);
            http.Response.Headers.ContentDisposition =
                $"attachment; filename=\"barbrain-export-{export.ExportedAt:yyyy-MM-dd}.json\"";
            return Results.Json(export);
        })
        .WithName("ExportAccountData");

        account.MapGet("/deletion", async (
            ClaimsPrincipal principal,
            UserManager<User> users,
            AccountDataService accounts,
            CancellationToken ct) =>
        {
            var user = await users.GetUserAsync(principal);
            if (user is null) return Results.Unauthorized();

            var status = await accounts.StatusAsync(user, ct);
            return status is null ? Results.NoContent() : Results.Ok(status);
        })
        .WithName("DeletionStatus");

        account.MapPost("/delete", async (
            DeletionRequest request,
            ClaimsPrincipal principal,
            UserManager<User> users,
            AccountDataService accounts,
            CancellationToken ct) =>
        {
            var user = await users.GetUserAsync(principal);
            if (user is null) return Results.Unauthorized();

            if (request.Mode is not (AccountDataService.ModeDelete or AccountDataService.ModeAnonymize))
                return Results.BadRequest(new ApiError("invalid_mode",
                    "Choose 'delete' (remove my data and contributions) or 'anonymize' (keep public contributions under an anonymous handle)."));

            if (user.DeletionRequestedAt is not null)
                return Results.Conflict(new ApiError("deletion_pending",
                    "A deletion is already scheduled. Cancel it first to change the mode."));

            // Password accounts confirm intent with the password; OAuth-only
            // accounts have none to give (the signed-in session is the proof).
            if (user.PasswordHash is not null
                && (request.Password is null || !await users.CheckPasswordAsync(user, request.Password)))
                return Results.Json(new ApiError("wrong_password",
                    "Enter your current password to confirm."),
                    statusCode: StatusCodes.Status403Forbidden);

            return Results.Ok(await accounts.RequestDeletionAsync(user, request.Mode, ct));
        })
        .WithName("RequestDeletion");

        account.MapPost("/delete/cancel", async (
            ClaimsPrincipal principal,
            UserManager<User> users,
            AccountDataService accounts,
            CancellationToken ct) =>
        {
            var user = await users.GetUserAsync(principal);
            if (user is null) return Results.Unauthorized();
            if (user.DeletionRequestedAt is null) return Results.NoContent();

            await accounts.CancelDeletionAsync(user, ct);
            return Results.NoContent();
        })
        .WithName("CancelDeletion");

        return app;
    }
}
