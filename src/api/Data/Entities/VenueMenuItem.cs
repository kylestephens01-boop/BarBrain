namespace BarBrain.Api.Data.Entities;

/// <summary>
/// A drink on a venue's menu (Sprint 5). Wiki-editable: crowd contributions
/// carry source='crowd' and the contributor's id; menu rows maintained for a
/// verified venue carry source='venue' (founder-as-admin in MVP — no venue
/// owner auth). One row per (venue, drink); availability toggles in place and
/// LastConfirmedAt records the freshest human confirmation.
/// </summary>
public class VenueMenuItem
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid VenueId { get; set; }
    public Venue Venue { get; set; } = null!;

    public Guid DrinkId { get; set; }
    public Drink Drink { get; set; } = null!;

    /// <summary>Optional listed price in USD (CHECK >= 0).</summary>
    public decimal? Price { get; set; }

    public bool IsAvailable { get; set; } = true;

    /// <summary>Last time a human confirmed this item is really on the menu.</summary>
    public DateTimeOffset? LastConfirmedAt { get; set; }

    /// <summary>crowd | venue (CHECK-constrained).</summary>
    public required string Source { get; set; }

    // --- Ownership + visibility (ADR-026). Menu items are always public;
    // the columns exist for the schema-wide invariant. -----------------------
    public Guid? CreatedByUserId { get; set; }
    public User? CreatedBy { get; set; }
    public string Visibility { get; set; } = Entities.Visibility.Public;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
