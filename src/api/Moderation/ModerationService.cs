using BarBrain.Api.Data;
using BarBrain.Api.Data.Entities;
using BarBrain.Shared.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BarBrain.Api.Moderation;

/// <summary>
/// Moderator actions (Sprint 6): hide/unhide content, shadow-limit, ban, and
/// the append-only audit log every action writes to. Hide is moderation-owned
/// state (HiddenAt/HiddenBy), deliberately distinct from user Visibility and
/// merge Status. Shadow-limit and ban are enforced at read/auth time, so
/// clearing either takes effect immediately.
/// </summary>
public sealed class ModerationService(
    AppDbContext db,
    UserManager<User> users,
    TimeProvider clock)
{
    // ======================================================================
    //  Audit log
    // ======================================================================

    /// <summary>Queue an audit row on the current unit of work (caller saves).</summary>
    public void Audit(string actor, string action, string? entityType, Guid? entityId,
        params (string Key, string Value)[] details)
        => db.ModerationActions.Add(new ModerationAction
        {
            Actor = actor,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Details = details.Length == 0 ? null : details.ToDictionary(d => d.Key, d => d.Value),
            CreatedAt = clock.GetUtcNow(),
        });

    /// <summary>Write an audit row immediately (for actions outside this service).</summary>
    public async Task AuditNowAsync(string actor, string action, string? entityType, Guid? entityId,
        CancellationToken ct = default, params (string Key, string Value)[] details)
    {
        Audit(actor, action, entityType, entityId, details);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ModerationActionDto>> AuditLogAsync(int take, CancellationToken ct = default)
        => await db.ModerationActions.AsNoTracking()
            .OrderByDescending(a => a.Id)
            .Take(take)
            .Select(a => new ModerationActionDto(
                a.Id, a.Actor, a.Action, a.EntityType, a.EntityId, a.Details, a.CreatedAt))
            .ToListAsync(ct);

    // ======================================================================
    //  Hide / unhide (report targets)
    // ======================================================================

    /// <summary>Hide a report target. Idempotent. False = target gone.</summary>
    public async Task<bool> HideAsync(
        string entityType, Guid entityId, string actor, CancellationToken ct = default)
        => await SetHiddenAsync(entityType, entityId, actor, hide: true, ct);

    public async Task<bool> UnhideAsync(
        string entityType, Guid entityId, string actor, CancellationToken ct = default)
        => await SetHiddenAsync(entityType, entityId, actor, hide: false, ct);

    private async Task<bool> SetHiddenAsync(
        string entityType, Guid entityId, string actor, bool hide, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        DateTimeOffset? at = hide ? now : null;
        var by = hide ? actor : null;

        var found = entityType switch
        {
            ReportEntityType.Rating => await db.Ratings.Where(r => r.Id == entityId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.HiddenAt, at)
                    .SetProperty(r => r.HiddenBy, by), ct),
            ReportEntityType.Venue => await db.Venues.Where(v => v.Id == entityId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(v => v.HiddenAt, at)
                    .SetProperty(v => v.HiddenBy, by), ct),
            ReportEntityType.Drink => await db.Drinks.Where(d => d.Id == entityId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(d => d.HiddenAt, at)
                    .SetProperty(d => d.HiddenBy, by), ct),
            _ => 0,
        };
        if (found == 0) return false;

        await AuditNowAsync(actor,
            hide ? ModerationActionKind.ContentHidden : ModerationActionKind.ContentUnhidden,
            entityType, entityId, ct);
        return true;
    }

    // ======================================================================
    //  User actions
    // ======================================================================

    public async Task<bool> ShadowLimitAsync(
        Guid userId, string actor, string? note, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return false;

        user.ShadowLimitedAt = clock.GetUtcNow();
        if (note is not null) user.ModerationNote = Truncate(note);
        Audit(actor, ModerationActionKind.ShadowLimited, "user", userId);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> ClearShadowLimitAsync(Guid userId, string actor, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return false;

        user.ShadowLimitedAt = null;
        Audit(actor, ModerationActionKind.ShadowCleared, "user", userId);
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Ban: sign-in refused from now on; the security-stamp rotation makes
    /// Identity's cookie validation evict existing sessions.
    /// </summary>
    public async Task<bool> BanAsync(
        Guid userId, string actor, string? note, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return false;

        user.BannedAt = clock.GetUtcNow();
        if (note is not null) user.ModerationNote = Truncate(note);
        Audit(actor, ModerationActionKind.Banned, "user", userId);
        await db.SaveChangesAsync(ct);
        await users.UpdateSecurityStampAsync(user);
        return true;
    }

    public async Task<bool> UnbanAsync(Guid userId, string actor, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return false;

        user.BannedAt = null;
        Audit(actor, ModerationActionKind.Unbanned, "user", userId);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static string? Truncate(string note)
        => string.IsNullOrWhiteSpace(note) ? null : note.Trim() is var t && t.Length > 256 ? t[..256] : t;
}
