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

    /// <summary>True on exactly one row per (user, drink) — the one the engine uses.</summary>
    public bool IsLatest { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
