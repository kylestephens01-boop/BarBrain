namespace BarBrain.Shared.Contracts;

/// <summary>
/// Body for <c>POST /api/events</c> — first-party analytics only (ADR-017).
/// Schema-only in Sprint 0: events are persisted, no dashboard yet.
///
/// HARD RULES: never carries real-name fields, full date of birth, or long-term
/// IP addresses. <see cref="Properties"/> must hold only non-PII, aggregate-safe
/// context (e.g. category, surface, variant).
/// </summary>
/// <param name="Name">Event name, e.g. "page_view", "rating_created".</param>
/// <param name="Properties">Optional non-PII key/value context.</param>
public sealed record EventWriteRequest(
    string Name,
    IReadOnlyDictionary<string, string>? Properties = null);
