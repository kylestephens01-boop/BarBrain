using BarBrain.Api.Data;
using BarBrain.Api.Data.Entities;
using BarBrain.Api.Settings;
using BarBrain.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BarBrain.Api.Venues;

/// <summary>
/// Check-in: the session primitive (ADR-015). One tap from a venue page, no
/// GPS proximity requirement in v1 (geo only ASSISTS discovery ordering). A
/// user has at most one open check-in (DB-enforced); starting a new one ends
/// the previous. A check-in is active while open and inside the config-flagged
/// expiry window; ratings made while active auto-tag the venue (RatingService).
/// </summary>
public sealed class CheckinService(
    AppDbContext db,
    ISettingsService settings,
    TimeProvider clock,
    Badges.BadgeService badges)
{
    public sealed record Failure(int Status, ApiError Error);

    public const string ExpiryHoursFlag = "checkin.expiry_hours";
    public const int DefaultExpiryHours = 4;

    public async Task<(CheckinDto? Checkin, Failure? Failure)> CheckinAsync(
        Guid userId, Guid venueId, CancellationToken ct = default)
    {
        // Check-in targets a PUBLIC venue: a Home Bar is a rating location,
        // not a session (its ratings tag via LocationContext instead).
        var venue = await db.Venues.AsNoTracking().FirstOrDefaultAsync(v =>
            v.Id == venueId
            && v.VenueType == VenueType.Venue
            && v.Status == EntityStatus.Active
            && v.Visibility == Visibility.Public
            && v.HiddenAt == null, ct);
        if (venue is null)
            return (null, new Failure(404, new ApiError("venue_not_found", "That venue doesn't exist.")));

        var now = clock.GetUtcNow();
        var hours = await settings.GetIntAsync(ExpiryHoursFlag, DefaultExpiryHours, ct);

        var checkin = new Checkin
        {
            UserId = userId,
            VenueId = venue.Id,
            CreatedAt = now,
            ExpiresAt = now.AddHours(Math.Max(1, hours)),
        };

        // End-open + insert atomically; the partial unique index refuses a
        // second open row even if this code regresses.
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            await db.Checkins
                .Where(c => c.UserId == userId && c.EndedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.EndedAt, now), ct);

            db.Checkins.Add(checkin);
            db.Events.Add(new EventRecord
            {
                Name = "checkin",
                OccurredAt = now,
                Properties = new Dictionary<string, string>
                {
                    ["userId"] = userId.ToString(),
                    ["venueId"] = venue.Id.ToString(),
                },
            });

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });

        // Instant badge awards (Sprint 6): venue-variety metric only.
        await badges.EvaluateAsync(userId, Badges.BadgeService.CheckinMetrics, ct);

        return (new CheckinDto(checkin.Id, venue.Id, venue.Name, checkin.CreatedAt, checkin.ExpiresAt), null);
    }

    /// <summary>The caller's active check-in, or null (open AND unexpired).</summary>
    public async Task<CheckinDto?> ActiveAsync(Guid userId, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        return await db.Checkins.AsNoTracking()
            .Where(c => c.UserId == userId && c.EndedAt == null && c.ExpiresAt > now)
            .Select(c => new CheckinDto(c.Id, c.VenueId, c.Venue.Name, c.CreatedAt, c.ExpiresAt))
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>The venue id of the caller's active check-in (rating auto-tag).</summary>
    public async Task<Guid?> ActiveVenueIdAsync(Guid userId, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        return await db.Checkins.AsNoTracking()
            .Where(c => c.UserId == userId && c.EndedAt == null && c.ExpiresAt > now)
            .Select(c => (Guid?)c.VenueId)
            .FirstOrDefaultAsync(ct);
    }
}
