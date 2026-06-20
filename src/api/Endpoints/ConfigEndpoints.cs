using BarBrain.Api.Settings;
using BarBrain.Shared.Contracts;

namespace BarBrain.Api.Endpoints;

public static class ConfigEndpoints
{
    public static IEndpointRouteBuilder MapConfigEndpoints(this IEndpointRouteBuilder app)
    {
        // Public, read-only, flag-driven config the web shell fetches on load.
        // Flipping home.banner_text via the admin API changes this without a
        // redeploy — the Sprint 0 proof of the feature-flag pipeline.
        app.MapGet("/api/config/home", async (ISettingsService settings, CancellationToken ct) =>
        {
            var banner = await settings.GetStringAsync("home.banner_text", "Know what you'll love.", ct);
            var showStatus = await settings.GetBoolAsync("home.show_status", true, ct);
            return Results.Ok(new HomeConfig(banner, showStatus));
        })
        .WithName("HomeConfig")
        .WithTags("Config");

        return app;
    }
}
