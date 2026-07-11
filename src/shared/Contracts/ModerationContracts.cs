namespace BarBrain.Shared.Contracts;

// ---- Reports (Sprint 6) --------------------------------------------------------

/// <summary>File a report against public content. Reason: inaccurate | spam | offensive | other.</summary>
public record ReportCreateRequest(
    string EntityType, // rating | venue | drink
    Guid EntityId,
    string Reason,
    string? Note = null);

/// <summary>A report as the admin queue shows it.</summary>
public record ReportDto(
    Guid Id,
    string EntityType,
    Guid EntityId,
    string EntityLabel,
    string? EntityDetail,
    string ReporterHandle,
    string Reason,
    string? Note,
    string Status,
    bool EntityHidden,
    DateTimeOffset CreatedAt);

// ---- Anomaly flags (Sprint 6) --------------------------------------------------

/// <summary>An anomaly flag as the admin queue shows it. Evidence for a human decision.</summary>
public record AnomalyFlagDto(
    Guid Id,
    Guid UserId,
    string UserHandle,
    string Kind,
    string Evidence,
    double Score,
    string Status,
    bool UserShadowLimited,
    bool UserBanned,
    DateTimeOffset CreatedAt);

// ---- Audit log (Sprint 6) ------------------------------------------------------

public record ModerationActionDto(
    long Id,
    string Actor,
    string Action,
    string? EntityType,
    Guid? EntityId,
    IReadOnlyDictionary<string, string>? Details,
    DateTimeOffset CreatedAt);
