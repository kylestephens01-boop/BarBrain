namespace BarBrain.Api.Endpoints;

/// <summary>
/// Test-only synthetic-error endpoint (Sprint 7): the monitoring acceptance
/// is "synthetic error → no PII in event", which needs a real unhandled
/// exception travelling the real pipeline. Exists ONLY when
/// <c>Testing:EnableThrowEndpoint</c> is set — and never in Production
/// (startup refuses, same posture as the mock OAuth provider).
/// </summary>
public static class DebugEndpoints
{
    public static IEndpointRouteBuilder MapDebugEndpoints(
        this IEndpointRouteBuilder app, IConfiguration config, IHostEnvironment env)
    {
        if (!config.GetValue("Testing:EnableThrowEndpoint", false))
            return app;
        if (env.IsProduction())
            throw new InvalidOperationException(
                "Testing:EnableThrowEndpoint must never be set in Production.");

        app.MapGet("/api/debug/throw", (string? note) =>
        {
            // The note goes INTO the exception message on purpose — the test
            // plants PII here and asserts the tracker scrubbed it.
            throw new InvalidOperationException($"Synthetic test failure: {note ?? "none"}");
        })
        .WithTags("Debug (test only)")
        .WithName("DebugThrow");

        return app;
    }
}
