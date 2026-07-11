namespace BarBrain.Api.Data.Entities;

/// <summary>
/// One rating event. Ratings are APPEND-ONLY history (ADR-012): re-rating a
/// drink inserts a new row and moves <see cref="IsLatest"/>; the engine and
/// all aggregates read only the latest row per (user, drink). A partial unique
/// index enforces the single-latest invariant in the database.
///
/// Pseudonymous-public by default with a per-rating private toggle (ADR-012).
/// Owner + visibility are the authz spine (ADR-026): every read path filters
/// on these columns, and CHECK constraints back the vocabulary.
/// No photos (Sprint 2 spec). Note only.
/// </summary>
public class Rating
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>The owner. Required — a rating always belongs to a user.</summary>
    public Guid CreatedByUserId { get; set; }
    public User CreatedBy { get; set; } = null!;

    public Guid DrinkId { get; set; }
    public Drink Drink { get; set; } = null!;

    /// <summary>1.0–5.0 in half-star steps (CHECK-constrained).</summary>
    public decimal Value { get; set; }

    /// <summary>Optional free-text note. Editable in place; never public when the rating is private.</summary>
    public string? Note { get; set; }

    public string Visibility { get; set; } = Entities.Visibility.Public;

    /// <summary>home_bar | venue | untagged (CHECK-constrained; ADR-015).</summary>
    public required string LocationContext { get; set; }

    /// <summary>
    /// user | quiz (CHECK-constrained). Quiz ratings are REAL ratings with
    /// provenance (Sprint 3 spec) — they feed profiles like any other, but the
    /// journal and future analytics can tell them apart.
    /// </summary>
    public string Origin { get; set; } = RatingOrigin.User;

    /// <summary>Set iff the context is a venue/Home Bar (CHECK-paired).</summary>
    public Guid? VenueId { get; set; }
    public Venue? Venue { get; set; }

    /// <summary>
    /// Which feed rec section surfaced the drink, when the rating originated
    /// from a rec card (alley | stretch | wildcard | matches; Sprint 6). Null
    /// for organic ratings. Powers the exploration badges; set going forward
    /// only — founder ruling 2026-07-10: no retroactive credit.
    /// </summary>
    public string? RecSection { get; set; }

    /// <summary>
    /// Moderation-owned hide (Sprint 6): set by an admin actioning a report.
    /// Deliberately distinct from user-chosen <see cref="Visibility"/> so
    /// moderator action and user intent never collide. Hidden rows leave every
    /// public surface; the owner still sees their own row.
    /// </summary>
    public DateTimeOffset? HiddenAt { get; set; }
    public string? HiddenBy { get; set; }

    /// <summary>True on exactly one row per (user, drink) — the one the engine uses.</summary>
    public bool IsLatest { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
