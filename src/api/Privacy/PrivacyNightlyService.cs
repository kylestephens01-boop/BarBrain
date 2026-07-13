using BarBrain.Api.Settings;

namespace BarBrain.Api.Privacy;

/// <summary>
/// Nightly execution of due account deletions (Sprint 7, ADR-018). The grace
/// period is the user's undo window; this job is what makes it end. Same
/// schedule pattern as the other nightly services: delay to the flagged UTC
/// hour, run once, repeat. Single-instance VPS scale (ADR-004).
/// </summary>
public sealed class PrivacyNightlyService(
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    ILogger<PrivacyNightlyService> logger) : BackgroundService
{
    public const string EnabledFlag = "privacy.nightly_enabled";
    public const string HourUtcFlag = "privacy.nightly_hour_utc";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using (var scope = scopeFactory.CreateAsyncScope())
                {
                    var settings = scope.ServiceProvider.GetRequiredService<ISettingsService>();
                    var hour = Math.Clamp(await settings.GetIntAsync(HourUtcFlag, 6, stoppingToken), 0, 23);
                    await Task.Delay(DelayUntilHourUtc(hour), clock, stoppingToken);

                    if (!await settings.GetBoolAsync(EnabledFlag, true, stoppingToken))
                        continue;
                }

                await using (var scope = scopeFactory.CreateAsyncScope())
                {
                    var accounts = scope.ServiceProvider.GetRequiredService<AccountDataService>();
                    var executed = await accounts.ExecuteDueDeletionsAsync(stoppingToken);
                    if (executed > 0)
                        logger.LogInformation("Nightly privacy run executed {Count} due deletion(s)", executed);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Nightly privacy run failed; retrying tomorrow");
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
