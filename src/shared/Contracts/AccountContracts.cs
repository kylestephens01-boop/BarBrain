namespace BarBrain.Shared.Contracts;

/// <summary>
/// Self-serve data export (ADR-018). Everything BarBrain holds about the
/// account, in one JSON document: profile, full rating history (ADR-012 —
/// append-only, so history rows are part of the user's data), check-ins, and
/// badges. Pseudonymous ids of OTHER users never appear.
/// </summary>
public sealed record AccountExport(
    DateTimeOffset ExportedAt,
    ExportProfile Profile,
    IReadOnlyList<ExportRating> Ratings,
    IReadOnlyList<ExportCheckin> Checkins,
    IReadOnlyList<ExportBadge> Badges);

public sealed record ExportProfile(
    Guid Id,
    string Handle,
    string? Email,
    bool EmailVerified,
    int? BirthYear,
    DateTimeOffset? AttestedAt,
    DateTimeOffset CreatedAt,
    bool HideFromMatches,
    bool DigestSubscribed,
    IReadOnlyList<string> Interests);

public sealed record ExportRating(
    string Drink,
    string Producer,
    string Category,
    decimal Value,
    string? Note,
    string Visibility,
    string LocationContext,
    string? Venue,
    string Origin,
    bool IsLatest,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ExportCheckin(
    string Venue,
    DateTimeOffset CreatedAt,
    DateTimeOffset? EndedAt);

public sealed record ExportBadge(
    string Slug,
    string Name,
    DateTimeOffset AwardedAt);

/// <summary>
/// Account deletion (ADR-018). <see cref="Mode"/> is 'delete' (personal data
/// and contributions removed) or 'anonymize' (public contributions stay,
/// reassigned to an anonymous handle; PII purged either way). Password
/// confirms intent on password accounts; OAuth-only accounts omit it.
/// </summary>
public sealed record DeletionRequest(string Mode, string? Password);

public sealed record DeletionStatusResponse(
    DateTimeOffset RequestedAt,
    string Mode,
    DateTimeOffset EffectiveAt);
