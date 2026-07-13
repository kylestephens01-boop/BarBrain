using BarBrain.Api.Data;
using BarBrain.Api.Data.Entities;
using Microsoft.AspNetCore.Diagnostics;

namespace BarBrain.Api.Monitoring;

/// <summary>
/// First-party error tracker (Sprint 7; ADR-017 posture — no third-party
/// tracker, ever). Every unhandled exception lands as an <c>error</c> event:
/// method, path (no query string — that's where PII rides), exception type,
/// and a PII-scrubbed message. Deliberately NO userId: operational data, not
/// behavioral, and it must survive account deletion untouched.
///
/// Returns false so the default handler still produces the ProblemDetails
/// response; tracking failure (DB down…) is swallowed — the tracker must
/// never make an outage worse.
/// </summary>
public sealed class ErrorTrackingExceptionHandler(
    ILogger<ErrorTrackingExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken ct)
    {
        try
        {
            var db = httpContext.RequestServices.GetRequiredService<AppDbContext>();
            db.Events.Add(new EventRecord
            {
                Name = "error",
                OccurredAt = DateTimeOffset.UtcNow,
                Properties = new Dictionary<string, string>
                {
                    ["method"] = httpContext.Request.Method,
                    ["path"] = httpContext.Request.Path.ToString(), // no query string
                    ["exception"] = exception.GetType().Name,
                    ["message"] = PiiScrubber.Scrub(exception.Message),
                },
            });
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception trackingFailure)
        {
            logger.LogWarning(trackingFailure, "Error tracking itself failed; continuing");
        }

        return false; // default pipeline still renders the 500 ProblemDetails
    }
}
