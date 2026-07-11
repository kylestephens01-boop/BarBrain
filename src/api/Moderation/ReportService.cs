using BarBrain.Api.Data;
using BarBrain.Api.Data.Entities;
using BarBrain.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BarBrain.Api.Moderation;

/// <summary>
/// The report flow (Sprint 6): any signed-in account can report public
/// content; reports land in the unified admin queue; actioning one hides the
/// target (reflected publicly at once) and writes the audit log. Duplicate
/// open reports per (reporter, target) are refused by the DB.
/// </summary>
public sealed class ReportService(
    AppDbContext db,
    ModerationService moderation,
    RateLimitService limits,
    TimeProvider clock)
{
    public sealed record Failure(int Status, ApiError Error);

    public async Task<(Guid? Id, Failure? Failure)> CreateAsync(
        Guid reporterId, ReportCreateRequest request, CancellationToken ct = default)
    {
        if (!ReportEntityType.IsValid(request.EntityType))
            return Fail(400, "invalid_entity_type", "Reports target a rating, venue, or drink.");
        if (!ReportReason.IsValid(request.Reason))
            return Fail(400, "invalid_reason", "Reason is inaccurate, spam, offensive, or other.");
        if (request.Note is { Length: > 500 })
            return Fail(400, "note_too_long", "Notes max out at 500 characters.");

        if (await limits.CheckReportsAsync(reporterId, ct) is { } limited)
            return Fail(429, "rate_limited", limited);

        // The target must exist and be PUBLIC content — private rows must not
        // leak their existence through the report flow (404 either way).
        var exists = request.EntityType switch
        {
            ReportEntityType.Rating => await db.Ratings.AsNoTracking().AnyAsync(
                r => r.Id == request.EntityId && r.Visibility == Visibility.Public, ct),
            ReportEntityType.Venue => await db.Venues.AsNoTracking().AnyAsync(
                v => v.Id == request.EntityId && v.Visibility == Visibility.Public
                     && v.VenueType == VenueType.Venue, ct),
            _ => await db.Drinks.AsNoTracking().AnyAsync(
                d => d.Id == request.EntityId && d.Visibility == Visibility.Public, ct),
        };
        if (!exists)
            return Fail(404, "not_found", "That content doesn't exist.");

        var openDuplicate = await db.Reports.AsNoTracking().AnyAsync(r =>
            r.ReporterUserId == reporterId && r.Status == ReportStatus.Open
            && (r.RatingId == request.EntityId || r.VenueId == request.EntityId || r.DrinkId == request.EntityId), ct);
        if (openDuplicate)
            return Fail(409, "already_reported", "You've already reported this — it's in the review queue.");

        var now = clock.GetUtcNow();
        var report = new Report
        {
            EntityType = request.EntityType,
            RatingId = request.EntityType == ReportEntityType.Rating ? request.EntityId : null,
            VenueId = request.EntityType == ReportEntityType.Venue ? request.EntityId : null,
            DrinkId = request.EntityType == ReportEntityType.Drink ? request.EntityId : null,
            ReporterUserId = reporterId,
            Reason = request.Reason,
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
            CreatedAt = now,
        };
        db.Reports.Add(report);
        db.Events.Add(new EventRecord
        {
            Name = "report_filed",
            OccurredAt = now,
            Properties = new Dictionary<string, string>
            {
                ["userId"] = reporterId.ToString(),
                ["entityType"] = request.EntityType,
                ["reason"] = request.Reason,
            },
        });
        await db.SaveChangesAsync(ct);
        return (report.Id, null);
    }

    // ======================================================================
    //  Admin queue
    // ======================================================================

    public async Task<IReadOnlyList<ReportDto>> ListAsync(string? status, CancellationToken ct = default)
    {
        var wanted = string.IsNullOrWhiteSpace(status) ? ReportStatus.Open : status.ToLowerInvariant();

        var rows = await db.Reports.AsNoTracking()
            .Include(r => r.Reporter)
            .Include(r => r.Rating!).ThenInclude(x => x.Drink)
            .Include(r => r.Rating!).ThenInclude(x => x.CreatedBy)
            .Include(r => r.Venue)
            .Include(r => r.Drink!).ThenInclude(d => d.Producer)
            .Where(r => r.Status == wanted)
            .OrderBy(r => r.CreatedAt)
            .Take(200)
            .ToListAsync(ct);

        return rows.Select(ToDto).ToList();
    }

    /// <summary>Action a report: hide the target, resolve the report, audit both.</summary>
    public async Task<bool> ActionAsync(Guid reportId, string actor, CancellationToken ct = default)
    {
        var report = await db.Reports.FirstOrDefaultAsync(
            r => r.Id == reportId && r.Status == ReportStatus.Open, ct);
        if (report is null) return false;

        var hidden = await moderation.HideAsync(report.EntityType, TargetId(report), actor, ct);
        if (!hidden) return false;

        report.Status = ReportStatus.Actioned;
        report.DecidedAt = clock.GetUtcNow();
        report.DecidedBy = actor;
        moderation.Audit(actor, ModerationActionKind.ReportActioned, report.EntityType, TargetId(report),
            ("reportId", report.Id.ToString()), ("reason", report.Reason));
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DismissAsync(Guid reportId, string actor, CancellationToken ct = default)
    {
        var report = await db.Reports.FirstOrDefaultAsync(
            r => r.Id == reportId && r.Status == ReportStatus.Open, ct);
        if (report is null) return false;

        report.Status = ReportStatus.Dismissed;
        report.DecidedAt = clock.GetUtcNow();
        report.DecidedBy = actor;
        moderation.Audit(actor, ModerationActionKind.ReportDismissed, report.EntityType, TargetId(report),
            ("reportId", report.Id.ToString()), ("reason", report.Reason));
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static Guid TargetId(Report r)
        => r.RatingId ?? r.VenueId ?? r.DrinkId ?? Guid.Empty;

    private static (Guid?, Failure?) Fail(int status, string code, string message)
        => (null, new Failure(status, new ApiError(code, message)));

    private static ReportDto ToDto(Report r)
    {
        var (label, detail, hidden) = r.EntityType switch
        {
            ReportEntityType.Rating => (
                $"{r.Rating!.Value:0.#}★ by @{r.Rating.CreatedBy.UserName} on {r.Rating.Drink.Name}",
                r.Rating.Note,
                r.Rating.HiddenAt is not null),
            ReportEntityType.Venue => (
                r.Venue!.Name,
                r.Venue.Address,
                r.Venue.HiddenAt is not null),
            _ => (
                r.Drink!.Name,
                $"{r.Drink.Producer.Name} · {r.Drink.Category}",
                r.Drink.HiddenAt is not null),
        };
        return new ReportDto(
            r.Id, r.EntityType, TargetId(r), label, detail,
            r.Reporter.UserName ?? "?", r.Reason, r.Note, r.Status, hidden, r.CreatedAt);
    }
}
