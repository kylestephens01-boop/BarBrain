namespace BarBrain.Api.Data.Entities;

/// <summary>
/// A place drinks are rated (ADR-015). Two kinds:
///
/// Home Bar (Sprint 2) — one private virtual venue auto-created per user at
/// activation, the default rating location, excluded from discovery. Never
/// carries geo (that would be the user's home coordinates — CHECK-banned).
/// Home Bar inventory/library stays in the distant backlog.
///
/// Public venue (Sprint 5) — wiki-contributed or admin-verified (tier flag
/// only; no billing in MVP). Dedupe follows the producer pattern: trigram +
/// geo proximity candidates land in the merge queue, merged rows become
/// redirects and their menus move to the survivor.
/// </summary>
public class Venue
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public required string Name { get; set; }

    /// <summary>lower(Name); trigram dedupe + search key (public venues only).</summary>
    public string NormalizedName { get; set; } = "";

    /// <summary>home_bar | venue (CHECK-constrained).</summary>
    public required string VenueType { get; set; }

    /// <summary>wiki | verified for public venues, NULL for home bars (CHECK-paired). Admin-set only.</summary>
    public string? Tier { get; set; }

    // --- Geo + info (public venues only; home bars are CHECK-banned from geo).
    // Nullable: a wiki add without location permission still succeeds; geo-less
    // venues simply sort last in the nearby list. ---------------------------
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? Address { get; set; }

    /// <summary>Free-text hours, display only (hours-aware features are out of scope).</summary>
    public string? Hours { get; set; }

    // --- Ownership + visibility (ADR-026) — a Home Bar is owned and private;
    // public venues are ownerless (no venue-owner auth in MVP). --------------
    public Guid? OwnerUserId { get; set; }
    public User? Owner { get; set; }
    public string Visibility { get; set; } = Entities.Visibility.Public;

    /// <summary>Wiki provenance: the contributor who added the venue (NULL for home bars/founder imports).</summary>
    public Guid? CreatedByUserId { get; set; }
    public User? CreatedBy { get; set; }

    // --- Merge lifecycle (producer pattern; home bars never merge). ---------
    public string Status { get; set; } = EntityStatus.Active;
    public Guid? MergedIntoVenueId { get; set; }
    public Venue? MergedInto { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
