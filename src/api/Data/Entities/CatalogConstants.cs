namespace BarBrain.Api.Data.Entities;

/// <summary>
/// Closed string vocabularies enforced by DB CHECK constraints (ADR-026 —
/// constraints in the database, not just app checks). Text-with-CHECK was
/// chosen over Postgres enums for diffable migrations and painless additive
/// growth; the CHECKs live in <see cref="AppDbContext"/>.
/// </summary>
public static class DrinkCategory
{
    public const string Beer = "beer";
    public const string Whiskey = "whiskey";
    public const string Wine = "wine";

    public static readonly string[] All = [Beer, Whiskey, Wine];
    public static bool IsValid(string value) => All.Contains(value);
}

/// <summary>Visibility of user-ownable entities (ADR-026).</summary>
public static class Visibility
{
    public const string Public = "public";
    public const string Private = "private";
}

/// <summary>Lifecycle of dedupable catalog entities (merge redirects).</summary>
public static class EntityStatus
{
    public const string Active = "active";
    public const string Merged = "merged";
}

/// <summary>Provenance of an attribute value (ADR-009).</summary>
public static class AttributeValueSource
{
    public const string Inherited = "inherited";
    public const string Manufacturer = "manufacturer";
    public const string Crowd = "crowd";
    public const string Llm = "llm";
    public const string Moderator = "moderator";
}

/// <summary>Where a rating happened (ADR-015). Sprint 2 spec: enum + nullable venue ref.</summary>
public static class LocationContext
{
    public const string HomeBar = "home_bar";
    public const string Venue = "venue";
    public const string Untagged = "untagged";

    public static readonly string[] All = [HomeBar, Venue, Untagged];
    public static bool IsValid(string value) => All.Contains(value);
}

/// <summary>Kind of venue (stub vocabulary; the full model is Sprint 5).</summary>
public static class VenueType
{
    public const string HomeBar = "home_bar";
    public const string Venue = "venue";
}

/// <summary>
/// Trust tier of a public venue (Sprint 5). Admin-set only — there is no
/// billing and no venue self-service claim flow in MVP.
/// </summary>
public static class VenueTier
{
    public const string Wiki = "wiki";
    public const string Verified = "verified";
}

/// <summary>
/// Provenance of a venue menu item (Sprint 5): crowd = wiki contribution,
/// venue = maintained on behalf of a verified venue (founder-as-admin in MVP).
/// </summary>
public static class MenuItemSource
{
    public const string Crowd = "crowd";
    public const string Venue = "venue";
}

/// <summary>Provenance of a rating (Sprint 3: quiz ratings are real ratings).</summary>
public static class RatingOrigin
{
    public const string User = "user";
    public const string Quiz = "quiz";
}

public static class MergeStatus
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
}

public static class MergeEntityType
{
    public const string Producer = "producer";
    public const string Drink = "drink";
    public const string Venue = "venue";
}

/// <summary>Vector geometry (ADR-009): 8 dims per category, 6-dim bridge.</summary>
public static class VectorDims
{
    public const int Category = 8;
    public const int Bridge = 6;
}
