namespace BarBrain.Api.Data.Entities;

/// <summary>
/// A check-in: the session primitive (ADR-015). One tap from a venue page —
/// no GPS proximity requirement in v1. A check-in is OPEN until EndedAt is
/// set (checking in elsewhere ends it) and ACTIVE while open and not past
/// ExpiresAt (config `checkin.expiry_hours`). Ratings made while a check-in
/// is active auto-tag the venue. At most one open check-in per user,
/// DB-enforced by a partial unique index.
/// </summary>
public class Checkin
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public Guid VenueId { get; set; }
    public Venue Venue { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Hard end of the session window (CreatedAt + expiry flag).</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Set when superseded by a newer check-in (or a future explicit checkout).</summary>
    public DateTimeOffset? EndedAt { get; set; }
}
