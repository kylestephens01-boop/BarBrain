using BarBrain.Api.Moderation;

namespace BarBrain.Api.Endpoints;

/// <summary>
/// The unified moderation queues' admin API (Sprint 6): reports (list, action
/// = hide + resolve, dismiss), anomaly flags (list, clear, shadow-limit, ban),
/// direct user actions, and the audit log. Same admin-token stub as the merge
/// queue; every decision writes moderation_actions.
/// </summary>
public static class AdminModerationEndpoints
{
    public static IEndpointRouteBuilder MapAdminModerationEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/admin/moderation")
            .AddEndpointFilter(AdminAuth.AdminTokenFilter)
            .WithTags("Admin");

        // ------------------------------------------------------------ reports
        admin.MapGet("/reports", async (
            string? status, ReportService reports, CancellationToken ct) =>
            Results.Ok(await reports.ListAsync(status, ct)))
        .WithName("ListReports");

        admin.MapPost("/reports/{id:guid}/action", async (
            Guid id, ReportService reports, CancellationToken ct) =>
            await reports.ActionAsync(id, actor: "admin-token", ct)
                ? Results.NoContent()
                : Results.NotFound())
        .WithName("ActionReport");

        admin.MapPost("/reports/{id:guid}/dismiss", async (
            Guid id, ReportService reports, CancellationToken ct) =>
            await reports.DismissAsync(id, actor: "admin-token", ct)
                ? Results.NoContent()
                : Results.NotFound())
        .WithName("DismissReport");

        admin.MapPost("/content/{entityType}/{id:guid}/unhide", async (
            string entityType, Guid id, ModerationService moderation, CancellationToken ct) =>
            await moderation.UnhideAsync(entityType, id, actor: "admin-token", ct)
                ? Results.NoContent()
                : Results.NotFound())
        .WithName("UnhideContent");

        // ------------------------------------------------------------ anomalies
        admin.MapGet("/anomalies", async (
            string? status, AnomalyScanService anomalies, CancellationToken ct) =>
            Results.Ok(await anomalies.ListAsync(status, ct)))
        .WithName("ListAnomalies");

        admin.MapPost("/anomalies/{id:guid}/clear", async (
            Guid id, AnomalyScanService anomalies, ModerationService moderation, CancellationToken ct) =>
            await anomalies.ClearAsync(id, actor: "admin-token", moderation, ct)
                ? Results.NoContent()
                : Results.NotFound())
        .WithName("ClearAnomaly");

        // Run the scan on demand (admin convenience; the nightly does it anyway).
        admin.MapPost("/anomalies/scan", async (
            AnomalyScanService anomalies, CancellationToken ct) =>
            Results.Ok(await anomalies.ScanAsync(ct)))
        .WithName("RunAnomalyScan");

        // ------------------------------------------------------------ users
        admin.MapPost("/users/{id:guid}/shadow-limit", async (
            Guid id, string? note, Guid? flagId,
            ModerationService moderation, AnomalyScanService anomalies, CancellationToken ct) =>
        {
            if (!await moderation.ShadowLimitAsync(id, actor: "admin-token", note, ct))
                return Results.NotFound();
            if (flagId is { } f) await anomalies.MarkActionedAsync(f, "admin-token", ct);
            return Results.NoContent();
        })
        .WithName("ShadowLimitUser");

        admin.MapPost("/users/{id:guid}/clear-shadow-limit", async (
            Guid id, ModerationService moderation, CancellationToken ct) =>
            await moderation.ClearShadowLimitAsync(id, actor: "admin-token", ct)
                ? Results.NoContent()
                : Results.NotFound())
        .WithName("ClearShadowLimit");

        admin.MapPost("/users/{id:guid}/ban", async (
            Guid id, string? note, Guid? flagId,
            ModerationService moderation, AnomalyScanService anomalies, CancellationToken ct) =>
        {
            if (!await moderation.BanAsync(id, actor: "admin-token", note, ct))
                return Results.NotFound();
            if (flagId is { } f) await anomalies.MarkActionedAsync(f, "admin-token", ct);
            return Results.NoContent();
        })
        .WithName("BanUser");

        admin.MapPost("/users/{id:guid}/unban", async (
            Guid id, ModerationService moderation, CancellationToken ct) =>
            await moderation.UnbanAsync(id, actor: "admin-token", ct)
                ? Results.NoContent()
                : Results.NotFound())
        .WithName("UnbanUser");

        // ------------------------------------------------------------ audit log
        admin.MapGet("/audit", async (
            int? take, ModerationService moderation, CancellationToken ct) =>
            Results.Ok(await moderation.AuditLogAsync(Math.Clamp(take ?? 100, 1, 500), ct)))
        .WithName("ModerationAuditLog");

        return app;
    }
}
