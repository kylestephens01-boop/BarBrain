using BarBrain.Api.Settings;
using BarBrain.Shared.Contracts;

namespace BarBrain.Api.Endpoints;

public static class AdminSettingsEndpoints
{
    public static IEndpointRouteBuilder MapAdminSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        // Auth is STUBBED in Sprint 0 (real auth is Sprint 2 / ADR-011). The
        // group is gated by a shared admin token so the flag-flip demo can run
        // safely; replace this filter with proper authz when auth lands.
        var admin = app.MapGroup("/api/admin")
            .AddEndpointFilter(AdminAuth.AdminTokenFilter)
            .WithTags("Admin");

        admin.MapGet("/settings", async (ISettingsService settings, CancellationToken ct) =>
            Results.Ok(await settings.GetAllAsync(ct)))
            .WithName("ListSettings");

        admin.MapGet("/settings/{key}", async (string key, ISettingsService settings, CancellationToken ct) =>
        {
            var value = await settings.GetStringAsync(key, ct);
            if (value is null)
                return Results.NotFound();

            var all = await settings.GetAllAsync(ct);
            var dto = all.FirstOrDefault(s => s.Key == key);
            return dto is null ? Results.NotFound() : Results.Ok(dto);
        })
        .WithName("GetSetting");

        admin.MapPut("/settings/{key}", async (
            string key,
            SettingUpdateRequest request,
            ISettingsService settings,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(key))
                return Results.BadRequest("Key is required.");

            var saved = await settings.SetAsync(key, request.Value, ct);
            return Results.Ok(saved);
        })
        .WithName("UpdateSetting");

        return app;
    }
}
