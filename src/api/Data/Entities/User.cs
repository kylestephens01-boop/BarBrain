namespace BarBrain.Api.Data.Entities;

/// <summary>
/// MINIMAL user stub (ADR-026). Exists in Sprint 1 solely so ownership
/// foreign keys on catalog entities are real DB constraints from day one.
/// Sprint 2 (auth) extends this table ADDITIVELY — do not add auth or profile
/// columns here.
///
/// HARD RULES: pseudonymous handle only (no real-name fields, Hard Rule 5);
/// no DOB storage here ever — birth year + attestation land in Sprint 2's
/// dedicated columns per ADR-010.
/// </summary>
public class User
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Pseudonymous unique handle; null until claimed (Sprint 2).</summary>
    public string? Handle { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
