namespace BarBrain.Api.Endpoints;

/// <summary>
/// STUB auth shared by all /api/admin groups. Requires header
/// <c>X-Admin-Token</c> to match the configured <c>Admin:Token</c>. If no
/// token is configured (local dev), requests pass with a logged warning.
/// Replaced by real authz in Sprint 2 (ADR-011) — enforce against the
/// ADR-026 ownership/visibility columns then.
/// </summary>
public static class AdminAuth
{
    public static async ValueTask<object?> AdminTokenFilter(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var config = http.RequestServices.GetRequiredService<IConfiguration>();
        var logger = http.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("AdminAuth");

        var expected = config["Admin:Token"];
        if (string.IsNullOrWhiteSpace(expected))
        {
            logger.LogWarning("Admin endpoint hit with no Admin:Token configured — STUB allowing request. Set Admin:Token before any non-local use.");
            return await next(context);
        }

        var provided = http.Request.Headers["X-Admin-Token"].ToString();
        if (!string.Equals(provided, expected, StringComparison.Ordinal))
            return Results.Unauthorized();

        return await next(context);
    }
}
