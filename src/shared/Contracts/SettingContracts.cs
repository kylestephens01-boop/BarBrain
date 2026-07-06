namespace BarBrain.Shared.Contracts;

/// <summary>
/// A single feature flag / setting as exposed by the admin API.
/// Values are stored as strings and interpreted by typed accessors (ADR-006).
/// </summary>
/// <param name="Key">Dotted key, e.g. "home.banner_text".</param>
/// <param name="Value">Raw string value.</param>
/// <param name="Description">Human-readable purpose of the flag.</param>
/// <param name="UpdatedAt">Last write time (UTC).</param>
public sealed record SettingDto(string Key, string Value, string? Description, DateTimeOffset UpdatedAt);

/// <summary>Body for <c>PUT /api/admin/settings/{key}</c>.</summary>
/// <param name="Value">New raw string value for the setting.</param>
public sealed record SettingUpdateRequest(string Value);
