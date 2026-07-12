using BarBrain.Api.Data;
using BarBrain.Api.Digest;
using BarBrain.Api.Settings;
using Microsoft.EntityFrameworkCore;

namespace BarBrain.Api.Monitoring;

/// <summary>
/// Error-rate spike alerting (Sprint 7): every check interval, count recent
/// <c>error</c> events; at/over the threshold, email the founder through the
/// digest sender (which logs until SMTP exists — HUMAN-CHECKLIST 6). One
/// alert per quiet-period so a sustained incident doesn't become an inbox
/// incident. The DOWN &gt; 2min rule is the EXTERNAL uptime monitor's job
/// (HUMAN-CHECKLIST 15) — a box that is down can't email anyone.
/// </summary>
public sealed class ErrorRateAlertService(
    IServiceScopeFactory scopeFactory,
    IDigestSender sender,
    TimeProvider clock,
    ILogger<ErrorRateAlertService> logger) : BackgroundService
{
    public const string ThresholdFlag = "monitoring.error_spike_threshold";
    public const string CheckMinutesFlag = "monitoring.check_minutes";
    public const string AlertEmailFlag = "monitoring.alert_email";
    public const int DefaultThreshold = 10;
    public const int DefaultCheckMinutes = 15;

    private DateTimeOffset _lastAlertAt = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var minutes = DefaultCheckMinutes;
            try
            {
                minutes = await CheckOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error-rate check failed; retrying next interval");
            }

            await Task.Delay(TimeSpan.FromMinutes(Math.Clamp(minutes, 1, 120)), clock, stoppingToken);
        }
    }

    /// <summary>One check pass. Returns the configured interval (minutes). Public for tests.</summary>
    public async Task<int> CheckOnceAsync(CancellationToken ct = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var settings = scope.ServiceProvider.GetRequiredService<ISettingsService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var minutes = await settings.GetIntAsync(CheckMinutesFlag, DefaultCheckMinutes, ct);
        var threshold = await settings.GetIntAsync(ThresholdFlag, DefaultThreshold, ct);
        var now = clock.GetUtcNow();

        var errors = await db.Events.CountAsync(
            e => e.Name == "error" && e.OccurredAt >= now.AddMinutes(-minutes), ct);

        if (errors >= threshold && now - _lastAlertAt >= TimeSpan.FromHours(1))
        {
            _lastAlertAt = now;
            var email = await settings.GetStringAsync(AlertEmailFlag, "", ct);
            var deliver = !string.IsNullOrWhiteSpace(email);
            await sender.SendAsync(
                deliver ? email! : "founder@localhost (monitoring.alert_email unset — logged only)",
                $"BarBrain alert: {errors} errors in the last {minutes} min",
                $"<p>{errors} unhandled API errors in the last {minutes} minutes "
                + $"(threshold {threshold}). Check <code>docker compose logs api</code> "
                + "and the recent <code>error</code> events.</p>",
                deliver, ct);
            logger.LogWarning("Error-rate alert raised: {Errors} errors in {Minutes} min", errors, minutes);
        }

        return minutes;
    }
}
