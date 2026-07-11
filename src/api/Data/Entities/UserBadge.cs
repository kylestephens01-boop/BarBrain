namespace BarBrain.Api.Data.Entities;

/// <summary>
/// An awarded badge. Awards are permanent high-water marks — never revoked,
/// never duplicated (DB-unique per user+badge). <see cref="SeenAt"/> drives
/// the award toast: null until the client acknowledges it.
/// </summary>
public class UserBadge
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public required string BadgeSlug { get; set; }
    public BadgeDefinition Badge { get; set; } = null!;

    public DateTimeOffset AwardedAt { get; set; }

    /// <summary>Set when the award toast has been shown (POST /api/badges/seen).</summary>
    public DateTimeOffset? SeenAt { get; set; }
}
