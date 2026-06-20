using BarBrain.Api.Data;
using BarBrain.Api.Data.Entities;
using BarBrain.Shared.Contracts;

namespace BarBrain.Api.Endpoints;

public static class EventEndpoints
{
    public static IEndpointRouteBuilder MapEventEndpoints(this IEndpointRouteBuilder app)
    {
        // First-party analytics write (ADR-017). Schema + write only in Sprint 0;
        // no dashboard. HARD RULES: no real names, no full DOB, no long-term IP.
        // We deliberately do NOT capture the client IP or any identity here.
        app.MapPost("/api/events", async (
            EventWriteRequest request,
            AppDbContext db,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return Results.BadRequest("Event name is required.");

            var record = new EventRecord
            {
                Name = request.Name.Trim(),
                Properties = request.Properties is null
                    ? null
                    : new Dictionary<string, string>(request.Properties),
                OccurredAt = DateTimeOffset.UtcNow,
            };

            db.Events.Add(record);
            await db.SaveChangesAsync(ct);

            return Results.Accepted();
        })
        .WithName("WriteEvent")
        .WithTags("Events");

        return app;
    }
}
