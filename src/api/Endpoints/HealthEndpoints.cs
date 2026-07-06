using BarBrain.Shared.Contracts;

namespace BarBrain.Api.Endpoints;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        // Liveness + which build is serving. Used by the web smoke check and the
        // deploy health gate. No DB dependency: stays green during a DB blip so
        // the gate distinguishes "app down" from "db down".
        app.MapGet("/health", () =>
            Results.Ok(new HealthResponse("ok", BuildInfo.Version, BuildInfo.Sha)))
            .WithName("Health")
            .WithTags("Diagnostics");

        app.MapGet("/version", () =>
            Results.Ok(new VersionResponse(BuildInfo.Version, BuildInfo.Sha)))
            .WithName("Version")
            .WithTags("Diagnostics");

        return app;
    }
}
