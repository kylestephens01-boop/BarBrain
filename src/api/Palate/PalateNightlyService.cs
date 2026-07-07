using BarBrain.Api.Data;
using BarBrain.Api.Settings;
using Microsoft.EntityFrameworkCore;

namespace BarBrain.Api.Palate;

/// <summary>
/// Nightly full recompute of every palate profile (Sprint 3 spec). Belt and
/// suspenders: profiles are already recomputed on every rating write, so this
/// exists to heal drift after catalog-side vector changes (imports, attribute
/// edits, merges) that don't touch ratings. Flag-gated; single-instance VPS
/// scale (ADR-004) — no distributed locking needed.
/// </summary>
public sealed class PalateNightlyService(
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    ILogger<PalateNightlyService> logger) : BackgroundService
{
    public const string EnabledFlag = "palate.nightly_recompute";
    public const string HourUtcFlag = "palate.nightly_hour_utc";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using (var scope = scopeFactory.CreateAsyncScope())
                {
                    var settings = scope.ServiceProvider.GetRequiredService<ISettingsService>();
                    var hour = Math.Clamp(await settings.GetIntAsync(HourUtcFlag, 9, stoppingToken), 0, 23);
                    await Task.Delay(DelayUntilHourUtc(hour), clock, stoppingToken);

                    if (!await settings.GetBoolAsync(EnabledFlag, true, stoppingToken))
                        continue;
                }

                await RecomputeEverythingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Nightly palate recompute failed; retrying tomorrow");
                // fall through to the next loop iteration → next night
            }
        }
    }

    private async Task RecomputeEverythingAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var profiles = scope.ServiceProvider.GetRequiredService<PalateProfileService>();

        var pairs = await db.Ratings.AsNoTracking()
            .Where(r => r.IsLatest)
            .Select(r => new { r.CreatedByUserId, r.Drink.Category })
            .Distinct()
            .ToListAsync(ct);

        foreach (var pair in pairs)
            await profiles.RecomputeAsync(pair.CreatedByUserId, pair.Category, ct);

        logger.LogInformation("Nightly palate recompute finished: {Count} profiles", pairs.Count);
    }

    private TimeSpan DelayUntilHourUtc(int hour)
    {
        var now = clock.GetUtcNow();
        var next = new DateTimeOffset(now.Year, now.Month, now.Day, hour, 0, 0, TimeSpan.Zero);
        if (next <= now) next = next.AddDays(1);
        return next - now;
    }
}
