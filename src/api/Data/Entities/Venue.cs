namespace BarBrain.Api.Data.Entities;

/// <summary>
/// MINIMAL venue stub (Sprint 2). Exists so a rating's location context can be
/// a real FK — the same day-one-constraints posture that put the users stub in
/// Sprint 1 (ADR-026). The full venue model (addresses, menus, check-in,
/// verification) is Sprint 5; extend this table ADDITIVELY then.
///
/// The only venues that exist in Sprint 2 are Home Bars: one private virtual
/// venue auto-created per user at activation (ADR-015), the default rating
/// location, excluded from discovery (it is private and no venue browse
/// endpoint exists). Home Bar inventory/library stays in the distant backlog —
/// deliberately not built.
/// </summary>
public class Venue
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public required string Name { get; set; }

    /// <summary>home_bar | venue (CHECK-constrained).</summary>
    public required string VenueType { get; set; }

    // --- Ownership + visibility (ADR-026) — a Home Bar is owned and private;
    // real venues (Sprint 5) will be ownerless/public or claimed. -------------
    public Guid? OwnerUserId { get; set; }
    public User? Owner { get; set; }
    public string Visibility { get; set; } = Entities.Visibility.Public;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
