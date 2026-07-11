using System.Net;
using System.Net.Http.Json;
using BarBrain.Api.Data.Entities;
using BarBrain.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BarBrain.Api.Tests.Integration;

/// <summary>
/// Sprint 6 acceptance: report → admin queue → action (hide) reflected
/// publicly; shadow-limit and ban enforcement; anomaly scan surfacing; the
/// append-only audit log. Admin calls ride the token stub (blank in tests),
/// exactly like the merge queue.
/// </summary>
[Collection("postgres")]
public sealed class ModerationFlowTests(PostgresFixture fixture) : IAsyncLifetime
{
    private IdentityTestHarness _harness = null!;
    private HttpClient _author = null!;   // owns the content being reported
    private HttpClient _reporter = null!; // files reports

    public async Task InitializeAsync()
    {
        if (!fixture.DockerAvailable) return;
        _harness = new IdentityTestHarness(fixture.ConnectionString);
        await CleanupAsync();
        _author = _harness.CreateClient();
        _reporter = _harness.CreateClient();
        await _harness.SignupAsync(_author, "content_author");
        await _harness.SignupAsync(_reporter, "reporter_user");
    }

    public async Task DisposeAsync()
    {
        if (_harness is not null) await _harness.DisposeAsync();
    }

    private async Task CleanupAsync()
    {
        await using var db = _harness.CreateDb();
        await db.UserBadges.ExecuteDeleteAsync();
        await db.Reports.ExecuteDeleteAsync();
        await db.AnomalyFlags.ExecuteDeleteAsync();
        await db.ModerationActions.ExecuteDeleteAsync();
        await db.Checkins.ExecuteDeleteAsync();
        await _harness.CleanupIdentityDataAsync();
    }

    private async Task<Guid> RatePubliclyAsync(HttpClient client, Guid drinkId, decimal value = 4.5m)
    {
        var response = await client.PostAsJsonAsync("/api/ratings",
            new RateRequest(drinkId, value, "solid pour", "public", "home_bar"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RatingDto>())!.Id;
    }

    private async Task<DrinkRatingsResponse> PublicRatingsAsync(Guid drinkId)
        => (await _reporter.GetFromJsonAsync<DrinkRatingsResponse>(
            $"/api/catalog/drinks/{drinkId}/ratings"))!;

    // ======================================================================

    [SkippableFact]
    public async Task Report_flows_to_queue_action_hides_publicly_and_audits()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");

        var drinkId = await _harness.PlantDrinkAsync("Reported Ale");
        var ratingId = await RatePubliclyAsync(_author, drinkId);
        Assert.Single((await PublicRatingsAsync(drinkId)).Recent);

        // File the report.
        var create = await _reporter.PostAsJsonAsync("/api/reports",
            new ReportCreateRequest("rating", ratingId, "offensive", "not okay"));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        // A second open report on the same target from the same reporter → 409.
        var duplicate = await _reporter.PostAsJsonAsync("/api/reports",
            new ReportCreateRequest("rating", ratingId, "spam", null));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        // It's in the admin queue.
        var queue = await _reporter.GetFromJsonAsync<List<ReportDto>>(
            "/api/admin/moderation/reports?status=open");
        var report = Assert.Single(queue!);
        Assert.Equal("rating", report.EntityType);
        Assert.Equal("reporter_user", report.ReporterHandle);
        Assert.False(report.EntityHidden);

        // Action it → the rating leaves the public surface immediately.
        var action = await _reporter.PostAsync(
            $"/api/admin/moderation/reports/{report.Id}/action", null);
        action.EnsureSuccessStatusCode();

        var after = await PublicRatingsAsync(drinkId);
        Assert.Empty(after.Recent);
        Assert.Equal(0, after.PublicCount);

        // The owner still sees their own rating (hide is public-surface only).
        var mine = await _author.GetFromJsonAsync<List<RatingDto>>(
            $"/api/ratings/mine/drink/{drinkId}");
        Assert.Single(mine!);

        // Queue drained; audit trail written (hide + report action).
        Assert.Empty((await _reporter.GetFromJsonAsync<List<ReportDto>>(
            "/api/admin/moderation/reports?status=open"))!);
        var audit = await _reporter.GetFromJsonAsync<List<ModerationActionDto>>(
            "/api/admin/moderation/audit");
        Assert.Contains(audit!, a => a.Action == "content_hidden");
        Assert.Contains(audit!, a => a.Action == "report_actioned");

        // Unhide restores the public surface (reversible by design).
        var unhide = await _reporter.PostAsync(
            $"/api/admin/moderation/content/rating/{ratingId}/unhide", null);
        unhide.EnsureSuccessStatusCode();
        Assert.Single((await PublicRatingsAsync(drinkId)).Recent);
    }

    [SkippableFact]
    public async Task Report_validation_holds_the_line()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");

        var drinkId = await _harness.PlantDrinkAsync("Valid Ale");

        // Unknown entity type / reason → 400.
        Assert.Equal(HttpStatusCode.BadRequest, (await _reporter.PostAsJsonAsync("/api/reports",
            new ReportCreateRequest("producer", drinkId, "spam", null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _reporter.PostAsJsonAsync("/api/reports",
            new ReportCreateRequest("drink", drinkId, "bogus", null))).StatusCode);

        // A PRIVATE rating must not leak through the report flow → 404.
        var privateRating = await _author.PostAsJsonAsync("/api/ratings",
            new RateRequest(drinkId, 2.0m, null, "private", "home_bar"));
        privateRating.EnsureSuccessStatusCode();
        var privateId = (await privateRating.Content.ReadFromJsonAsync<RatingDto>())!.Id;
        Assert.Equal(HttpStatusCode.NotFound, (await _reporter.PostAsJsonAsync("/api/reports",
            new ReportCreateRequest("rating", privateId, "spam", null))).StatusCode);

        // Anonymous callers can't report at all.
        var anonymous = _harness.CreateClient();
        var anon = await anonymous.PostAsJsonAsync("/api/reports",
            new ReportCreateRequest("drink", drinkId, "spam", null));
        Assert.NotEqual(HttpStatusCode.Created, anon.StatusCode);
    }

    [SkippableFact]
    public async Task Shadow_limit_silently_removes_content_from_public_surfaces()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");

        var drinkId = await _harness.PlantDrinkAsync("Shadowed Stout");
        await RatePubliclyAsync(_author, drinkId);

        await using var db = _harness.CreateDb();
        var authorId = (await db.Users.SingleAsync(u => u.UserName == "content_author")).Id;

        var limit = await _reporter.PostAsync(
            $"/api/admin/moderation/users/{authorId}/shadow-limit", null);
        limit.EnsureSuccessStatusCode();

        // Gone from the public surface...
        var hidden = await PublicRatingsAsync(drinkId);
        Assert.Empty(hidden.Recent);

        // ...but the author sees their own world unchanged (that's the point).
        var mine = await _author.GetFromJsonAsync<List<RatingDto>>(
            $"/api/ratings/mine/drink/{drinkId}");
        Assert.Single(mine!);

        // Clearing restores immediately (enforced on read).
        var clear = await _reporter.PostAsync(
            $"/api/admin/moderation/users/{authorId}/clear-shadow-limit", null);
        clear.EnsureSuccessStatusCode();
        Assert.Single((await PublicRatingsAsync(drinkId)).Recent);
    }

    [SkippableFact]
    public async Task Ban_refuses_sign_in_and_blocks_writes()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");

        var drinkId = await _harness.PlantDrinkAsync("Banned Bock");

        await using var db = _harness.CreateDb();
        var authorId = (await db.Users.SingleAsync(u => u.UserName == "content_author")).Id;

        var ban = await _reporter.PostAsync($"/api/admin/moderation/users/{authorId}/ban", null);
        ban.EnsureSuccessStatusCode();

        // Existing session: the write guard refuses immediately.
        var write = await _author.PostAsJsonAsync("/api/ratings",
            new RateRequest(drinkId, 3.0m, null, "public", "home_bar"));
        Assert.Equal(HttpStatusCode.Unauthorized, write.StatusCode);

        // Fresh sign-in: refused with the disabled-account message.
        var freshClient = _harness.CreateClient();
        var login = await freshClient.PostAsJsonAsync("/api/auth/login",
            new LoginRequest("content_author@example.test", "correct-horse-battery"));
        Assert.Equal(HttpStatusCode.Forbidden, login.StatusCode);

        // Unban → sign-in works again.
        var unban = await _reporter.PostAsync($"/api/admin/moderation/users/{authorId}/unban", null);
        unban.EnsureSuccessStatusCode();
        var retry = await freshClient.PostAsJsonAsync("/api/auth/login",
            new LoginRequest("content_author@example.test", "correct-horse-battery"));
        retry.EnsureSuccessStatusCode();
    }

    [SkippableFact]
    public async Task Anomaly_scan_flags_rapid_fire_for_human_review()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");

        // Plant an implausible burst: 30 rating rows in 5 minutes, straight
        // into the DB (the API's own rate limit would slow a real bot the
        // same way — this tests the DETECTOR, not the limiter).
        await using (var db = _harness.CreateDb())
        {
            var authorId = (await db.Users.SingleAsync(u => u.UserName == "content_author")).Id;
            var producer = await db.Producers.FirstOrDefaultAsync()
                ?? new Producer { Name = "Burst Brewing", NormalizedName = "burst brewing", Source = "test" };
            var t0 = DateTimeOffset.UtcNow.AddHours(-1);
            for (var i = 0; i < 30; i++)
            {
                var drink = new Drink
                {
                    Producer = producer,
                    Name = $"Burst {i}",
                    NormalizedName = $"burst {i}",
                    Category = "beer",
                    Source = "test",
                };
                db.Drinks.Add(drink);
                db.Ratings.Add(new Rating
                {
                    CreatedByUserId = authorId,
                    Drink = drink,
                    Value = 3.0m,
                    LocationContext = "untagged",
                    CreatedAt = t0.AddSeconds(i * 8),
                    UpdatedAt = t0.AddSeconds(i * 8),
                });
            }
            await db.SaveChangesAsync();
        }

        var scan = await _reporter.PostAsync("/api/admin/moderation/anomalies/scan", null);
        scan.EnsureSuccessStatusCode();

        var flags = await _reporter.GetFromJsonAsync<List<AnomalyFlagDto>>(
            "/api/admin/moderation/anomalies?status=open");
        var flag = Assert.Single(flags!, f => f.Kind == "rapid_fire");
        Assert.Equal("content_author", flag.UserHandle);
        Assert.Contains("30", flag.Evidence);

        // Clear closes it and audits.
        var clear = await _reporter.PostAsync(
            $"/api/admin/moderation/anomalies/{flag.Id}/clear", null);
        clear.EnsureSuccessStatusCode();
        Assert.Empty((await _reporter.GetFromJsonAsync<List<AnomalyFlagDto>>(
            "/api/admin/moderation/anomalies?status=open"))!);
        var audit = await _reporter.GetFromJsonAsync<List<ModerationActionDto>>(
            "/api/admin/moderation/audit");
        Assert.Contains(audit!, a => a.Action == "anomaly_cleared");
    }
}
