using BarBrain.Api.Settings;

namespace BarBrain.Api.Digest;

/// <summary>
/// Schedules the weekly digest (ADR-019). Fires once a week at a configured day
/// + UTC hour, then delegates to <see cref="DigestService"/>. Flag-gated via
/// <c>digest.enabled</c> (checked inside the run); single-instance VPS scale
/// (ADR-004). Mirrors the nightly background services.
/// </summary>
public sealed class WeeklyDigestService(
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    ILogger<WeeklyDigestService> logger) : BackgroundService
{
    public const string HourFlag = "digest.hour_utc";
    public const string DayFlag = "digest.day_of_week";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                TimeSpan delay;
                await using (var scope = scopeFactory.CreateAsyncScope())
                {
                    var settings = scope.ServiceProvider.GetRequiredService<ISettingsService>();
                    var hour = Math.Clamp(await settings.GetIntAsync(HourFlag, 14, stoppingToken), 0, 23);
                    var day = Math.Clamp(await settings.GetIntAsync(DayFlag, 1, stoppingToken), 0, 6);
                    delay = DelayUntil((DayOfWeek)day, hour);
                }
                await Task.Delay(delay, clock, stoppingToken);

                await using var runScope = scopeFactory.CreateAsyncScope();
                var digest = runScope.ServiceProvider.GetRequiredService<DigestService>();
                await digest.RunOnceAsync(respectEnabledFlag: true, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Weekly digest run failed; retrying next week");
            }
        }
    }

    private TimeSpan DelayUntil(DayOfWeek day, int hour)
    {
        var now = clock.GetUtcNow();
        var daysAhead = ((int)day - (int)now.DayOfWeek + 7) % 7;
        var next = new DateTimeOffset(now.Year, now.Month, now.Day, hour, 0, 0, TimeSpan.Zero)
            .AddDays(daysAhead);
        if (next <= now) next = next.AddDays(7);
        return next - now;
    }
}
