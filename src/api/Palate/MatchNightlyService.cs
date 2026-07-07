using BarBrain.Api.Settings;

namespace BarBrain.Api.Palate;

/// <summary>
/// Nightly user-user CF batch (ADR-007/014, Sprint 4). Rebuilds the
/// materialized <c>user_match_neighbors</c> graph from current palate profiles
/// and latest ratings. Runs after the palate recompute so it reads fresh
/// preference vectors. Flag-gated; single-instance VPS scale (ADR-004) — no
/// distributed locking. Mirrors <see cref="PalateNightlyService"/>.
/// </summary>
public sealed class MatchNightlyService(
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    ILogger<MatchNightlyService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using (var scope = scopeFactory.CreateAsyncScope())
                {
                    var settings = scope.ServiceProvider.GetRequiredService<ISettingsService>();
                    // A little after the palate recompute (default hour 9) so
                    // profiles are current when CF reads them.
                    var hour = Math.Clamp(
                        await settings.GetIntAsync(MatchService.NightlyHourFlag, 10, stoppingToken), 0, 23);
                    await Task.Delay(DelayUntilHourUtc(hour), clock, stoppingToken);

                    if (!await settings.GetBoolAsync(MatchService.NightlyEnabledFlag, true, stoppingToken))
                        continue;
                }

                await RebuildAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Nightly match rebuild failed; retrying tomorrow");
            }
        }
    }

    private async Task RebuildAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var matches = scope.ServiceProvider.GetRequiredService<MatchService>();
        var summary = await matches.ComputeAllAsync(ct);
        logger.LogInformation(
            "Nightly match rebuild finished: {Edges} edges over {Users} users",
            summary.Edges, summary.Users);
    }

    private TimeSpan DelayUntilHourUtc(int hour)
    {
        var now = clock.GetUtcNow();
        var next = new DateTimeOffset(now.Year, now.Month, now.Day, hour, 0, 0, TimeSpan.Zero);
        if (next <= now) next = next.AddDays(1);
        return next - now;
    }
}
