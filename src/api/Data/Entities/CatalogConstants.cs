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
}

/// <summary>Vector geometry (ADR-009): 8 dims per category, 6-dim bridge.</summary>
public static class VectorDims
{
    public const int Category = 8;
    public const int Bridge = 6;
}
