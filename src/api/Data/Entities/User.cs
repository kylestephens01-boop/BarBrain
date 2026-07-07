using Microsoft.AspNetCore.Identity;

namespace BarBrain.Api.Data.Entities;

/// <summary>
/// The application user. Sprint 1 shipped this as a minimal stub so ownership
/// FKs were real from day one (ADR-026); Sprint 2 extends the SAME
/// <c>users</c> table additively into the ASP.NET Identity user (ADR-011).
/// Identity's <c>UserName</c> IS the pseudonymous handle (stored lowercase in
/// the original <c>Handle</c> column).
///
/// HARD RULES: pseudonymous handle only — no real-name fields (Hard Rule 5);
/// NO full date of birth, ever — only <see cref="BirthYear"/> +
/// <see cref="AttestedAt"/> (Hard Rule 2, ADR-010). The full DOB exists
/// transiently in the signup request for the 21+ computation and is discarded.
/// Phone number and 2FA columns are deliberately unmapped — we never collect
/// phone numbers.
/// </summary>
public class User : IdentityUser<Guid>
{
    public User() => Id = Guid.CreateVersion7();

    // --- 21+ gate (ADR-010) --------------------------------------------------
    /// <summary>Birth YEAR only. The full DOB is never persisted.</summary>
    public int? BirthYear { get; set; }

    /// <summary>When the user attested they are 21+ (the gate passed).</summary>
    public DateTimeOffset? AttestedAt { get; set; }

    /// <summary>
    /// Set when the account became usable: the age gate passed and a handle was
    /// claimed. OAuth arrivals have no row at all until this moment — an
    /// under-21 OAuth login never creates an account.
    /// </summary>
    public DateTimeOffset? ActivatedAt { get; set; }

    /// <summary>Last handle change — enforces the 30-day cooldown (flag-driven).</summary>
    public DateTimeOffset? HandleChangedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
