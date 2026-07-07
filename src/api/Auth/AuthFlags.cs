using BarBrain.Api.Data.Entities;
using BarBrain.Api.Settings;

namespace BarBrain.Api.Auth;

/// <summary>
/// Flag keys + the verification-grace policy (ADR-011: rate immediately,
/// verify within a config-flagged window; Hard Rule 10: thresholds are flags).
/// </summary>
public static class AuthFlags
{
    public const string VerificationGraceDays = "auth.verification_grace_days";
    public const string HandleCooldownDays = "auth.handle_cooldown_days";

    public const int DefaultVerificationGraceDays = 7;
    public const int DefaultHandleCooldownDays = 30;

    /// <summary>Deadline to verify, or null when already verified.</summary>
    public static async Task<DateTimeOffset?> VerificationDeadlineAsync(
        User user, ISettingsService settings, CancellationToken ct = default)
    {
        if (user.EmailConfirmed) return null;
        var graceDays = await settings.GetIntAsync(VerificationGraceDays, DefaultVerificationGraceDays, ct);
        return user.CreatedAt.AddDays(graceDays);
    }

    /// <summary>Unverified accounts lose rating past the grace window (spec).</summary>
    public static async Task<bool> CanRateAsync(
        User user, ISettingsService settings, TimeProvider clock, CancellationToken ct = default)
    {
        var deadline = await VerificationDeadlineAsync(user, settings, ct);
        return deadline is null || clock.GetUtcNow() < deadline;
    }
}
