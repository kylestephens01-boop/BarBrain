using BarBrain.Api.Data;
using BarBrain.Api.Data.Entities;
using BarBrain.Api.Settings;
using Microsoft.EntityFrameworkCore;

namespace BarBrain.Api.Digest;

/// <summary>
/// One weekly-digest run (ADR-019): compose → render → send for every
/// subscribed, activated user. Split out from the scheduling
/// <see cref="WeeklyDigestService"/> so tests can drive a run deterministically.
///
/// CAN-SPAM guard: a run only DELIVERS when a physical mailing address is
/// configured (<c>digest.physical_address</c>). With no address it still
/// composes and renders, but sends with <c>deliver: false</c> — the digest is
/// log-only until the founder provides an address (HUMAN-CHECKLIST 6), exactly
/// like the Sprint 2 verification emails.
/// </summary>
public sealed class DigestService(
    AppDbContext db,
    DigestComposer composer,
    IDigestSender sender,
    ISettingsService settings,
    TimeProvider clock,
    ILogger<DigestService> logger)
{
    public const string EnabledFlag = "digest.enabled";
    public const string PhysicalAddressFlag = "digest.physical_address";
    public const string BaseUrlFlag = "digest.public_base_url";

    public sealed record RunSummary(bool Ran, int Composed, int Delivered, int LoggedOnly, string? SkipReason);

    public async Task<RunSummary> RunOnceAsync(bool respectEnabledFlag = true, CancellationToken ct = default)
    {
        if (respectEnabledFlag && !await settings.GetBoolAsync(EnabledFlag, false, ct))
            return new RunSummary(false, 0, 0, 0, "digest.enabled is off");

        var address = (await settings.GetStringAsync(PhysicalAddressFlag, "", ct)).Trim();
        var baseUrl = (await settings.GetStringAsync(BaseUrlFlag, "", ct)).TrimEnd('/');
        var canDeliver = !string.IsNullOrWhiteSpace(address);
        if (!canDeliver)
            logger.LogWarning(
                "Weekly digest running LOG-ONLY: no CAN-SPAM physical address configured (digest.physical_address / HUMAN-CHECKLIST 6).");

        var recipients = await db.Users.AsNoTracking()
            .Where(u => u.ActivatedAt != null && u.Email != null && u.DigestUnsubscribedAt == null)
            .Select(u => new { u.Id, Handle = u.UserName!, u.Email, u.DigestUnsubscribeToken })
            .ToListAsync(ct);

        int composed = 0, delivered = 0, loggedOnly = 0;
        foreach (var r in recipients)
        {
            var unsubscribeUrl = $"{baseUrl}/api/digest/unsubscribe?token={r.DigestUnsubscribeToken}";
            var model = await composer.ComposeAsync(r.Id, r.Handle, unsubscribeUrl,
                canDeliver ? address : null, ct);
            if (!model.HasContent)
                continue; // never send an empty email

            composed++;
            var (subject, html) = DigestRenderer.Render(model);
            await sender.SendAsync(r.Email!, subject, html, canDeliver, ct);
            if (canDeliver) delivered++; else loggedOnly++;

            db.Events.Add(new EventRecord
            {
                Name = "digest_sent",
                OccurredAt = clock.GetUtcNow(),
                Properties = new Dictionary<string, string>
                {
                    ["userId"] = r.Id.ToString(),
                    ["delivered"] = canDeliver ? "true" : "false",
                },
            });
        }
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Weekly digest run: composed {Composed}, delivered {Delivered}, log-only {LoggedOnly}",
            composed, delivered, loggedOnly);
        return new RunSummary(true, composed, delivered, loggedOnly, null);
    }
}
