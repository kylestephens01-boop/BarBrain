namespace BarBrain.Api.Data.Entities;

/// <summary>
/// Append-only moderation audit log (Sprint 6): every merge decision, report
/// decision, hide/unhide, shadow-limit, and ban writes a row. EntityType/
/// EntityId deliberately carry NO foreign keys — the audit trail must survive
/// the deletion of whatever it describes.
/// </summary>
public class ModerationAction
{
    public long Id { get; set; }

    /// <summary>Actor label (admin-token stub until real admin identity).</summary>
    public required string Actor { get; set; }

    /// <summary>Closed vocabulary (see <see cref="ModerationActionKind"/>).</summary>
    public required string Action { get; set; }

    /// <summary>What the action targeted, e.g. "rating"/"user"/"merge". No FK by design.</summary>
    public string? EntityType { get; set; }
    public Guid? EntityId { get; set; }

    /// <summary>Non-PII context, persisted as jsonb (reason, prior state, …).</summary>
    public Dictionary<string, string>? Details { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
