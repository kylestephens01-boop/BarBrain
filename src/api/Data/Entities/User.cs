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

    // --- Palate matching (Sprint 4, ADR-014) ---------------------------------
    /// <summary>
    /// Hide-me: when true the user is removed from ALL match surfaces in BOTH
    /// directions — they appear to no one and see no one. Enforced on read so
    /// flipping it takes effect immediately, not on the next nightly batch.
    /// </summary>
    public bool HideFromMatches { get; set; }

    // --- Weekly digest (Sprint 4, ADR-019) -----------------------------------
    /// <summary>
    /// Set when the user unsubscribed from the weekly digest (CAN-SPAM). Once
    /// set, the sender skips them; cleared only by an explicit re-opt-in.
    /// </summary>
    public DateTimeOffset? DigestUnsubscribedAt { get; set; }

    /// <summary>
    /// Stable unguessable token for the one-click unsubscribe link, so the link
    /// carries no auth and needs no session (full-page nav under /api/*).
    /// </summary>
    public Guid DigestUnsubscribeToken { get; set; } = Guid.CreateVersion7();

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
