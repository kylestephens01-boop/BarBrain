using System.Security.Claims;
using BarBrain.Api.Moderation;
using BarBrain.Shared.Contracts;

namespace BarBrain.Api.Endpoints;

/// <summary>
/// The user-facing report flow (Sprint 6): any signed-in account can report a
/// public rating, venue, or drink. Rate-limited per account; one open report
/// per (reporter, target).
/// </summary>
public static class ReportEndpoints
{
    public static IEndpointRouteBuilder MapReportEndpoints(this IEndpointRouteBuilder app)
    {
        var reports = app.MapGroup("/api/reports").WithTags("Reports")
            .RequireAuthorization()
            .AddEndpointFilter(ModerationGuards.NotBannedFilter);

        reports.MapPost("/", async (
            ReportCreateRequest request,
            ClaimsPrincipal principal,
            ReportService service,
            CancellationToken ct) =>
        {
            var reporterId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var (id, failure) = await service.CreateAsync(reporterId, request, ct);
            return failure is not null
                ? Results.Json(failure.Error, statusCode: failure.Status)
                : Results.Created($"/api/reports/{id}", new { id });
        })
        .WithName("CreateReport");

        return app;
    }
}
