using BarBrain.Api.Data;
using BarBrain.Api.Data.Entities;
using BarBrain.Api.Settings;
using BarBrain.Shared.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BarBrain.Api.Privacy;

/// <summary>
/// Privacy self-serve (Sprint 7, ADR-018): JSON export, and the two-mode
/// deletion flow — FULL DELETE (personal data and contributions removed;
/// shared public catalog rows go ownerless, never destroying other users'
/// ratings) vs ANONYMIZE (public contributions stay attached to the user row,
/// which is scrubbed to an anonymous handle). PII is purged either way.
///
/// Deletion is scheduled, not immediate: a flag-driven grace period
/// (<see cref="GraceDaysFlag"/>, default 7) lets the user change their mind;
/// the nightly job executes what's due. moderation_actions deliberately
/// survives (no FK, Sprint 6 design) — the audit outlives the account.
/// </summary>
public sealed class AccountDataService(
    AppDbContext db,
    UserManager<User> userManager,
    ISettingsService settings,
    IAccountEmailSender emailSender,
    TimeProvider clock,
    ILogger<AccountDataService> logger)
{
    public const string GraceDaysFlag = "privacy.deletion_grace_days";
    public const int DefaultGraceDays = 7;

    public const string ModeDelete = "delete";
    public const string ModeAnonymize = "anonymize";

    // ------------------------------------------------------------------ export

    public async Task<AccountExport> BuildExportAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId, ct);

        var interests = await db.UserCategoryInterests.AsNoTracking()
            .Where(i => i.UserId == userId)
            .OrderBy(i => i.Category)
            .Select(i => i.Category)
            .ToListAsync(ct);

        var ratings = await db.Ratings.AsNoTracking()
            .Where(r => r.CreatedByUserId == userId)
            .OrderBy(r => r.CreatedAt)
            .Select(r => new ExportRating(
                r.Drink.Name, r.Drink.Producer.Name, r.Drink.Category,
                r.Value, r.Note, r.Visibility, r.LocationContext,
                r.Venue != null ? r.Venue.Name : null, r.Origin,
                r.IsLatest, r.CreatedAt, r.UpdatedAt))
            .ToListAsync(ct);

        var checkins = await db.Checkins.AsNoTracking()
            .Where(c => c.UserId == userId)
            .OrderBy(c => c.CreatedAt)
            .Select(c => new ExportCheckin(c.Venue.Name, c.CreatedAt, c.EndedAt))
            .ToListAsync(ct);

        var badges = await db.UserBadges.AsNoTracking()
            .Where(b => b.UserId == userId)
            .Join(db.BadgeDefinitions, b => b.BadgeSlug, d => d.Slug,
                (b, d) => new { b.BadgeSlug, d.Name, b.AwardedAt })
            .OrderBy(b => b.AwardedAt)
            .Select(b => new ExportBadge(b.BadgeSlug, b.Name, b.AwardedAt))
            .ToListAsync(ct);

        return new AccountExport(
            clock.GetUtcNow(),
            new ExportProfile(
                user.Id, user.UserName!, user.Email, user.EmailConfirmed,
                user.BirthYear, user.AttestedAt, user.CreatedAt,
                user.HideFromMatches, user.DigestUnsubscribedAt == null, interests),
            ratings, checkins, badges);
    }

    // --------------------------------------------------------------- scheduling

    public async Task<DeletionStatusResponse> RequestDeletionAsync(
        User user, string mode, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        user.DeletionRequestedAt = now;
        user.DeletionMode = mode;
        await db.SaveChangesAsync(ct);

        var effectiveAt = now.AddDays(await GraceDaysAsync(ct));
        if (!string.IsNullOrEmpty(user.Email))
            await emailSender.SendDeletionScheduledAsync(user.Email, mode, effectiveAt, ct);

        return new DeletionStatusResponse(now, mode, effectiveAt);
    }

    public async Task CancelDeletionAsync(User user, CancellationToken ct = default)
    {
        user.DeletionRequestedAt = null;
        user.DeletionMode = null;
        await db.SaveChangesAsync(ct);
    }

    public async Task<DeletionStatusResponse?> StatusAsync(User user, CancellationToken ct = default)
        => user.DeletionRequestedAt is not { } requested
            ? null
            : new DeletionStatusResponse(
                requested, user.DeletionMode!, requested.AddDays(await GraceDaysAsync(ct)));

    private async Task<int> GraceDaysAsync(CancellationToken ct)
        => Math.Max(0, await settings.GetIntAsync(GraceDaysFlag, DefaultGraceDays, ct));

    // ---------------------------------------------------------------- execution

    /// <summary>Executes every deletion whose grace period has elapsed. Returns the count.</summary>
    public async Task<int> ExecuteDueDeletionsAsync(CancellationToken ct = default)
    {
        var cutoff = clock.GetUtcNow().AddDays(-await GraceDaysAsync(ct));
        var due = await db.Users
            .Where(u => u.DeletionRequestedAt != null && u.DeletionRequestedAt <= cutoff)
            .ToListAsync(ct);

        foreach (var user in due)
        {
            var mode = user.DeletionMode!;
            var strategy = db.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                if (mode == ModeAnonymize) await AnonymizeAsync(user, ct);
                else await FullDeleteAsync(user, ct);
                await tx.CommitAsync(ct);
            });
            logger.LogInformation("Executed account deletion: mode={Mode}", mode);
        }

        return due.Count;
    }

    /// <summary>
    /// ANONYMIZE: public contributions (ratings, venue/menu/wiki adds) stay,
    /// attached to this row under a fresh anonymous handle. Everything private
    /// or social goes; the row keeps no PII and can never sign in again.
    /// </summary>
    private async Task AnonymizeAsync(User user, CancellationToken ct)
    {
        var id = user.Id;

        // Private ratings serve no one once the owner is gone (reports on them
        // cascade in the database).
        await db.Ratings.Where(r => r.CreatedByUserId == id && r.Visibility == Visibility.Private)
            .ExecuteDeleteAsync(ct);

        await ReleasePrivateVenuesAsync(id, ct);
        await db.Checkins.Where(c => c.UserId == id).ExecuteDeleteAsync(ct);
        await db.Venues.Where(v => v.OwnerUserId == id && v.Visibility == Visibility.Private)
            .ExecuteDeleteAsync(ct);

        await DeleteSocialRowsAsync(id, ct);
        await ScrubEventsAsync(id, ct);

        // Identity link rows would let the person sign back into the scrubbed row.
        await db.Set<IdentityUserLogin<Guid>>().Where(l => l.UserId == id).ExecuteDeleteAsync(ct);
        await db.Set<IdentityUserToken<Guid>>().Where(t => t.UserId == id).ExecuteDeleteAsync(ct);
        await db.Set<IdentityUserClaim<Guid>>().Where(c => c.UserId == id).ExecuteDeleteAsync(ct);

        // The scrub. ActivatedAt goes with BirthYear/AttestedAt so the
        // activation CHECK holds; a dead handle in the right format keeps the
        // lowercase CHECK and uniqueness happy.
        user.UserName = $"anonymous_{Guid.NewGuid():N}"[..20];
        user.NormalizedUserName = user.UserName.ToUpperInvariant();
        user.Email = null;
        user.NormalizedEmail = null;
        user.EmailConfirmed = false;
        user.PasswordHash = null;
        user.SecurityStamp = Guid.NewGuid().ToString("N"); // evict sessions
        user.BirthYear = null;
        user.AttestedAt = null;
        user.ActivatedAt = null;
        user.HandleChangedAt = null;
        user.HideFromMatches = true;
        user.DigestUnsubscribedAt = clock.GetUtcNow();
        user.DigestUnsubscribeToken = Guid.CreateVersion7();
        user.ModerationNote = null;
        user.DeletionRequestedAt = null;
        user.DeletionMode = null;
        await db.SaveChangesAsync(ct);

        db.Events.Add(new EventRecord
        {
            Name = "account_anonymized",
            OccurredAt = clock.GetUtcNow(),
            Properties = new Dictionary<string, string>(), // deliberately no userId
        });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// FULL DELETE: personal data and own contributions removed. Shared public
    /// catalog rows (drinks/producers/venues others may depend on) go
    /// ownerless instead of being destroyed — deleting them would cascade into
    /// OTHER users' ratings, which deletion of one account must never do.
    /// </summary>
    private async Task FullDeleteAsync(User user, CancellationToken ct)
    {
        var id = user.Id;

        // Own rating history first (frees the drink/venue FKs; reports on the
        // rows cascade in the database).
        await db.Ratings.Where(r => r.CreatedByUserId == id).ExecuteDeleteAsync(ct);
        await db.Checkins.Where(c => c.UserId == id).ExecuteDeleteAsync(ct);
        await db.VenueMenuItems.Where(mi => mi.CreatedByUserId == id).ExecuteDeleteAsync(ct);

        await ReleasePrivateVenuesAsync(id, ct);
        await db.Venues.Where(v => v.OwnerUserId == id && v.Visibility == Visibility.Private)
            .ExecuteDeleteAsync(ct);

        // Shared public rows go ownerless (allowed: the ADR-026 CHECK only
        // requires an owner on PRIVATE rows).
        await db.Venues.Where(v => v.OwnerUserId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.OwnerUserId, (Guid?)null), ct);
        await db.Venues.Where(v => v.CreatedByUserId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.CreatedByUserId, (Guid?)null), ct);
        await db.Drinks.Where(d => d.CreatedByUserId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.CreatedByUserId, (Guid?)null), ct);
        await db.Producers.Where(p => p.CreatedByUserId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.CreatedByUserId, (Guid?)null), ct);

        await DeleteSocialRowsAsync(id, ct);
        await ScrubEventsAsync(id, ct);

        // Cascades take the identity link rows, interests, palate profiles,
        // badges, reports filed, and anomaly flags with the user row.
        db.ChangeTracker.Clear();
        var tracked = await db.Users.SingleAsync(u => u.Id == id, ct);
        var deleted = await userManager.DeleteAsync(tracked);
        if (!deleted.Succeeded)
            throw new InvalidOperationException(
                $"User row deletion failed: {string.Join("; ", deleted.Errors.Select(e => e.Description))}");

        db.Events.Add(new EventRecord
        {
            Name = "account_deleted",
            OccurredAt = clock.GetUtcNow(),
            Properties = new Dictionary<string, string>(), // deliberately no userId
        });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Ratings that survive a deletion may still point at a private venue
    /// (Home Bar) that is about to go away. Retag them 'untagged' — the venue
    /// pairing CHECK requires the pair to move together.
    /// </summary>
    private async Task ReleasePrivateVenuesAsync(Guid userId, CancellationToken ct)
    {
        await db.Ratings
            .Where(r => r.Venue != null
                        && r.Venue.OwnerUserId == userId
                        && r.Venue.Visibility == Visibility.Private)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.VenueId, (Guid?)null)
                .SetProperty(r => r.LocationContext, "untagged"), ct);
    }

    /// <summary>Both directions of the match graph — the neighbor side is Restrict.</summary>
    private async Task DeleteSocialRowsAsync(Guid userId, CancellationToken ct)
    {
        await db.UserMatchNeighbors
            .Where(m => m.UserId == userId || m.NeighborUserId == userId)
            .ExecuteDeleteAsync(ct);
        await db.UserPalateProfiles.Where(p => p.UserId == userId).ExecuteDeleteAsync(ct);
        await db.UserCategoryInterests.Where(i => i.UserId == userId).ExecuteDeleteAsync(ct);
        await db.UserBadges.Where(b => b.UserId == userId).ExecuteDeleteAsync(ct);
        await db.Reports.Where(r => r.ReporterUserId == userId).ExecuteDeleteAsync(ct);
        await db.AnomalyFlags.Where(f => f.UserId == userId).ExecuteDeleteAsync(ct);
    }

    /// <summary>
    /// First-party events keep their aggregate value but lose the user link
    /// (the jsonb key is the only reference — no FK).
    /// </summary>
    private async Task ScrubEventsAsync(Guid userId, CancellationToken ct)
    {
        var uid = userId.ToString();
        await db.Database.ExecuteSqlAsync(
            $"""UPDATE events SET "Properties" = "Properties" - 'userId' WHERE "Properties"->>'userId' = {uid}""",
            ct);
    }
}
