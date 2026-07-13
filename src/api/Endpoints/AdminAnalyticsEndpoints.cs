using BarBrain.Api.Analytics;

namespace BarBrain.Api.Endpoints;

/// <summary>
/// The admin retention dashboard read (Sprint 7, ADR-017). One aggregate
/// endpoint, admin-token gated like every /api/admin surface. Aggregate-only
/// by construction — no per-user drill-down exists (Hard Rule 6 posture).
/// </summary>
public static class AdminAnalyticsEndpoints
{
    public static IEndpointRouteBuilder MapAdminAnalyticsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/admin/analytics").WithTags("Admin (analytics)")
            .AddEndpointFilter(AdminAuth.AdminTokenFilter)
            .MapGet("/", async (AnalyticsService analytics, CancellationToken ct)
                => Results.Ok(await analytics.BuildAsync(ct)))
            .WithName("AdminAnalytics");

        return app;
    }
}
