using System.Net;
using BarBrain.Api.Data.Entities;
using BarBrain.Api.Digest;
using BarBrain.Api.Monitoring;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace BarBrain.Api.Tests.Integration;

/// <summary>
/// Sprint 7 monitoring acceptance: a SYNTHETIC error through the real
/// pipeline lands as an <c>error</c> event with the planted PII scrubbed
/// (screenshot-equivalent proof for Gate E), and the error-rate alert fires
/// once at threshold, then throttles.
/// </summary>
[Collection("postgres")]
public sealed class MonitoringTests(PostgresFixture fixture) : IAsyncLifetime
{
    private ApiFactory _factory = null!;

    public async Task InitializeAsync()
    {
        if (!fixture.DockerAvailable) return;
        _factory = new ApiFactory
        {
            ConnectionStringOverride = fixture.ConnectionString,
            MigrateOnStartup = false,
            Settings = new Dictionary<string, string?>
            {
                ["Testing:EnableThrowEndpoint"] = "true",
            },
        };
        await using var db = PostgresFixture.CreateContext(fixture.ConnectionString);
        await db.Events.ExecuteDeleteAsync();
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null) await _factory.DisposeAsync();
    }

    private sealed class RecordingSender : IDigestSender
    {
        public List<(string Recipient, string Subject, bool Deliver)> Sent { get; } = [];
        public Task SendAsync(string recipient, string subject, string htmlBody,
            bool deliver, CancellationToken ct = default)
        {
            Sent.Add((recipient, subject, deliver));
            return Task.CompletedTask;
        }
    }

    [SkippableFact]
    public async Task Synthetic_error_lands_as_event_with_pii_scrubbed()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        const string plantedPii = "kyle.probe@example.com";

        var client = _factory.CreateDefaultClient();
        using var response = await client.GetAsync($"/api/debug/throw?note={plantedPii}");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        await using var db = PostgresFixture.CreateContext(fixture.ConnectionString);
        var error = await db.Events.AsNoTracking()
            .Where(e => e.Name == "error")
            .OrderByDescending(e => e.OccurredAt)
            .FirstAsync();

        Assert.Equal("GET", error.Properties!["method"]);
        Assert.Equal("/api/debug/throw", error.Properties["path"]); // query string never stored
        Assert.Equal(nameof(InvalidOperationException), error.Properties["exception"]);
        Assert.Contains("[email]", error.Properties["message"]);

        var serialized = System.Text.Json.JsonSerializer.Serialize(error.Properties);
        Assert.DoesNotContain(plantedPii, serialized);   // THE acceptance assertion
        Assert.DoesNotContain("userId", serialized);      // operational, not behavioral
    }

    [SkippableFact]
    public async Task Error_spike_alert_fires_at_threshold_then_throttles()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");

        await using (var db = PostgresFixture.CreateContext(fixture.ConnectionString))
        {
            var now = DateTimeOffset.UtcNow;
            for (var i = 0; i < ErrorRateAlertService.DefaultThreshold; i++)
                db.Events.Add(new EventRecord
                {
                    Name = "error",
                    OccurredAt = now.AddMinutes(-1),
                    Properties = new Dictionary<string, string> { ["exception"] = "Synthetic" },
                });
            await db.SaveChangesAsync();
        }

        var recorder = new RecordingSender();
        var service = new ErrorRateAlertService(
            _factory.Services.GetRequiredService<IServiceScopeFactory>(),
            recorder, TimeProvider.System,
            NullLogger<ErrorRateAlertService>.Instance);

        await service.CheckOnceAsync();
        var sent = Assert.Single(recorder.Sent);
        Assert.Contains("errors in the last", sent.Subject);
        Assert.False(sent.Deliver); // monitoring.alert_email unset → logged, not delivered

        // Still spiking one minute later — but throttled to one alert per hour.
        await service.CheckOnceAsync();
        Assert.Single(recorder.Sent);
    }
}
