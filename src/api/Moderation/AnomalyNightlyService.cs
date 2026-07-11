using BarBrain.Api.Settings;

namespace BarBrain.Api.Moderation;

/// <summary>
/// Runs the anomaly scan nightly (Sprint 6). Flag-gated; single-instance VPS
/// scale (ADR-004) — same shape as the palate/match/badge nightlies.
/// </summary>
public sealed class AnomalyNightlyService(
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    ILogger<AnomalyNightlyService> logger) : BackgroundService
{
    public const string EnabledFlag = "anomaly.nightly_enabled";
    public const string HourUtcFlag = "anomaly.nightly_hour_utc";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using (var scope = scopeFactory.CreateAsyncScope())
                {
                    var settings = scope.ServiceProvider.GetRequiredService<ISettingsService>();
                    var hour = Math.Clamp(await settings.GetIntAsync(HourUtcFlag, 7, stoppingToken), 0, 23);
                    await Task.Delay(DelayUntilHourUtc(hour), clock, stoppingToken);

                    if (!await settings.GetBoolAsync(EnabledFlag, true, stoppingToken))
                        continue;
                }

                await using var runScope = scopeFactory.CreateAsyncScope();
                var scanner = runScope.ServiceProvider.GetRequiredService<AnomalyScanService>();
                var summary = await scanner.ScanAsync(stoppingToken);
                logger.LogInformation(
                    "Anomaly scan finished: {ZScore} z-score flags, {RapidFire} rapid-fire flags",
                    summary.ZScoreFlags, summary.RapidFireFlags);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Anomaly scan failed; retrying tomorrow");
            }
        }
    }

    private TimeSpan DelayUntilHourUtc(int hour)
    {
        var now = clock.GetUtcNow();
        var next = new DateTimeOffset(now.Year, now.Month, now.Day, hour, 0, 0, TimeSpan.Zero);
        if (next <= now) next = next.AddDays(1);
        return next - now;
    }
}
