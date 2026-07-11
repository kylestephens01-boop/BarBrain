using BarBrain.Api.Catalog;
using BarBrain.Api.Data;
using BarBrain.Api.Data.Entities;
using BarBrain.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BarBrain.Api.Endpoints;

/// <summary>
/// Admin merge-queue API (Sprint 1 spec): list candidates, approve (source
/// becomes a redirect to target), reject (pair is remembered, never
/// re-proposed). Gated by the shared admin-token stub until Sprint 2 auth.
/// </summary>
public static class AdminMergeEndpoints
{
    public static IEndpointRouteBuilder MapAdminMergeEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/admin/merge-queue")
            .AddEndpointFilter(AdminAuth.AdminTokenFilter)
            .WithTags("Admin");

        admin.MapGet("/", async (
            string? status,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var wanted = string.IsNullOrWhiteSpace(status) ? MergeStatus.Pending : status.ToLowerInvariant();

            var candidates = await db.MergeQueue.AsNoTracking()
                .Include(m => m.SourceProducer)
                .Include(m => m.TargetProducer)
                .Include(m => m.SourceDrink!).ThenInclude(d => d.Producer)
                .Include(m => m.TargetDrink!).ThenInclude(d => d.Producer)
                .Include(m => m.SourceVenue)
                .Include(m => m.TargetVenue)
                .Where(m => m.Status == wanted)
                .OrderByDescending(m => m.Similarity).ThenBy(m => m.CreatedAt)
                .Take(200)
                .ToListAsync(ct);

            return Results.Ok(candidates.Select(ToDto).ToList());
        })
        .WithName("ListMergeCandidates");

        admin.MapPost("/{id:guid}/approve", async (
            Guid id,
            MergeService merges,
            Moderation.ModerationService moderation,
            Badges.BadgeService badges,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var outcome = await merges.ApproveAsync(id, actor: "admin-token", ct);
            if (outcome == MergeDecisionOutcome.Done)
            {
                // Audit + contribution badges live at the endpoint (the only
                // moderator path) so MergeService stays CLI-pure (Sprint 6).
                await moderation.AuditNowAsync("admin-token",
                    ModerationActionKind.MergeApproved, "merge", id, ct);
                foreach (var contributor in await ContributorsAsync(db, id, ct))
                    await badges.EvaluateAsync(contributor, Badges.BadgeService.MergeMetrics, ct);
            }
            return ToResult(outcome);
        })
        .WithName("ApproveMergeCandidate");

        admin.MapPost("/{id:guid}/reject", async (
            Guid id,
            MergeService merges,
            Moderation.ModerationService moderation,
            CancellationToken ct) =>
        {
            var outcome = await merges.RejectAsync(id, actor: "admin-token", ct);
            if (outcome == MergeDecisionOutcome.Done)
                await moderation.AuditNowAsync("admin-token",
                    ModerationActionKind.MergeRejected, "merge", id, ct);
            return ToResult(outcome);
        })
        .WithName("RejectMergeCandidate");

        return app;
    }

    /// <summary>Distinct wiki contributors of a candidate's entities (Clean Catalog badge).</summary>
    private static async Task<List<Guid>> ContributorsAsync(AppDbContext db, Guid candidateId, CancellationToken ct)
    {
        var c = await db.MergeQueue.AsNoTracking()
            .Where(m => m.Id == candidateId)
            .Select(m => new
            {
                A = m.SourceProducer != null ? m.SourceProducer.CreatedByUserId : null,
                B = m.TargetProducer != null ? m.TargetProducer.CreatedByUserId : null,
                C = m.SourceDrink != null ? m.SourceDrink.CreatedByUserId : null,
                D = m.TargetDrink != null ? m.TargetDrink.CreatedByUserId : null,
                E = m.SourceVenue != null ? m.SourceVenue.CreatedByUserId : null,
                F = m.TargetVenue != null ? m.TargetVenue.CreatedByUserId : null,
            })
            .FirstOrDefaultAsync(ct);
        return c is null
            ? []
            : new[] { c.A, c.B, c.C, c.D, c.E, c.F }
                .Where(id => id is not null).Select(id => id!.Value).Distinct().ToList();
    }

    private static IResult ToResult(MergeDecisionOutcome outcome) => outcome switch
    {
        MergeDecisionOutcome.Done => Results.NoContent(),
        MergeDecisionOutcome.NotFound => Results.NotFound(),
        MergeDecisionOutcome.AlreadyDecided => Results.Conflict("Candidate already decided."),
        _ => Results.Problem("Unexpected merge outcome."),
    };

    private static MergeCandidateDto ToDto(MergeCandidate m)
    {
        MergeEntityRef Producer(Producer p) => new(
            p.Id, p.Name, string.Join(", ", new[] { p.City, p.Region }.Where(x => !string.IsNullOrEmpty(x))));
        MergeEntityRef Drink(Drink d) => new(
            d.Id, d.Name, $"{d.Producer.Name} · {d.Category}" + (d.Abv is { } abv ? $" · {abv}%" : ""));
        MergeEntityRef VenueRef(Venue v) => new(
            v.Id, v.Name, v.Address ?? (v.Latitude is { } lat ? $"{lat:0.####}, {v.Longitude:0.####}" : "no location"));

        var (source, target) = m.EntityType switch
        {
            MergeEntityType.Producer => (Producer(m.SourceProducer!), Producer(m.TargetProducer!)),
            MergeEntityType.Drink => (Drink(m.SourceDrink!), Drink(m.TargetDrink!)),
            _ => (VenueRef(m.SourceVenue!), VenueRef(m.TargetVenue!)),
        };

        return new MergeCandidateDto(
            m.Id, m.EntityType, source, target, m.Similarity, m.Reason, m.Status, m.CreatedAt);
    }
}
