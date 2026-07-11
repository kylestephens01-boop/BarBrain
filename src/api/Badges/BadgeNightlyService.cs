using BarBrain.Api.Data;
using BarBrain.Api.Settings;
using Microsoft.EntityFrameworkCore;

namespace BarBrain.Api.Badges;

/// <summary>
/// Nightly full badge evaluation (Sprint 6). Inline hooks award instantly on
/// writes; this pass heals anything they missed and catches awards that happen
/// WITHOUT a write — chiefly weekly-streak thresholds crossing between
/// sessions. Flag-gated; single-instance VPS scale (ADR-004).
/// </summary>
public sealed class BadgeNightlyService(
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    ILogger<BadgeNightlyService> logger) : BackgroundService
{
    public const string EnabledFlag = "badges.nightly_enabled";
    public const string HourUtcFlag = "badges.nightly_hour_utc";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using (var scope = scopeFactory.CreateAsyncScope())
                {
                    var settings = scope.ServiceProvider.GetRequiredService<ISettingsService>();
                    var hour = Math.Clamp(await settings.GetIntAsync(HourUtcFlag, 8, stoppingToken), 0, 23);
                    await Task.Delay(DelayUntilHourUtc(hour), clock, stoppingToken);

                    if (!await settings.GetBoolAsync(EnabledFlag, true, stoppingToken))
                        continue;
                }

                await EvaluateEveryoneAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Nightly badge evaluation failed; retrying tomorrow");
            }
        }
    }

    private async Task EvaluateEveryoneAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var badges = scope.ServiceProvider.GetRequiredService<BadgeService>();

        var userIds = await db.Users.AsNoTracking()
            .Where(u => u.ActivatedAt != null && u.BannedAt == null)
            .Select(u => u.Id)
            .ToListAsync(ct);

        var awarded = 0;
        foreach (var userId in userIds)
            awarded += await badges.EvaluateAsync(userId, metrics: null, ct);

        logger.LogInformation(
            "Nightly badge evaluation finished: {Users} users, {Awarded} new awards", userIds.Count, awarded);
    }

    private TimeSpan DelayUntilHourUtc(int hour)
    {
        var now = clock.GetUtcNow();
        var next = new DateTimeOffset(now.Year, now.Month, now.Day, hour, 0, 0, TimeSpan.Zero);
        if (next <= now) next = next.AddDays(1);
        return next - now;
    }
}
