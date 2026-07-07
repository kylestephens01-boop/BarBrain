using Pgvector;

namespace BarBrain.Api.Data.Entities;

/// <summary>
/// A user's computed palate for one category (Sprint 3, ADR-025/027). Derived
/// data — recomputed from the append-only ratings history (latest row per
/// drink) after every rating and by the nightly batch; never hand-edited.
///
/// Two different vectors on purpose:
///  - <see cref="PreferenceVector"/> drives RECS: drink attributes weighted by
///    (rating − user mean), so it points from "what you rate" toward "what
///    you LIKE" — dims can be negative.
///  - <see cref="CentroidVector"/> drives the RADAR: absolute 0–1 average of
///    liked drinks' attributes — the shape of the palate, displayable 0–10.
///  - <see cref="BridgeVector"/> is the 6-dim preference vector on the shared
///    dims — the cross-category moat mechanic (ADR-027).
/// </summary>
public class UserPalateProfile
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    /// <summary>beer | whiskey | wine (CHECK-constrained).</summary>
    public required string Category { get; set; }

    public Vector? PreferenceVector { get; set; }
    public Vector? CentroidVector { get; set; }
    public Vector? BridgeVector { get; set; }

    /// <summary>Distinct drinks whose LATEST rating fed this profile.</summary>
    public int RatingsCount { get; set; }

    /// <summary>The user's mean rating in this category (the preference zero-point).</summary>
    public float UserMean { get; set; }

    public DateTimeOffset ComputedAt { get; set; } = DateTimeOffset.UtcNow;
}
