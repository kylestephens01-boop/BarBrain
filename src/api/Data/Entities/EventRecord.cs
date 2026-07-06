namespace BarBrain.Api.Data.Entities;

/// <summary>
/// A first-party analytics event (ADR-017). Schema-only in Sprint 0 — rows are
/// written via <c>POST /api/events</c>; no dashboard yet.
///
/// HARD RULES (CLAUDE.md): no real names, no full DOB, no long-term IP. Only
/// non-PII, aggregate-safe context belongs in <see cref="Properties"/>.
/// </summary>
public class EventRecord
{
    /// <summary>Surrogate identity key.</summary>
    public long Id { get; set; }

    /// <summary>Event name, e.g. "page_view", "rating_created".</summary>
    public required string Name { get; set; }

    /// <summary>Non-PII context, persisted as jsonb.</summary>
    public Dictionary<string, string>? Properties { get; set; }

    /// <summary>When the event occurred (UTC).</summary>
    public DateTimeOffset OccurredAt { get; set; }
}
