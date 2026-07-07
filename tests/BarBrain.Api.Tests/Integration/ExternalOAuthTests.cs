using System.Net;
using System.Net.Http.Json;
using BarBrain.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BarBrain.Api.Tests.Integration;

/// <summary>
/// External (Google/Apple) signup via the mock providers — the SAME scheme
/// names and challenge → external-cookie → DOB-capture pipeline production
/// uses (Sprint 2 acceptance: OAuth paths hit DOB capture; under-21 blocked
/// on all paths; no account exists before the gate passes).
/// </summary>
[Collection("postgres")]
public sealed class ExternalOAuthTests(PostgresFixture fixture) : IAsyncLifetime
{
    private IdentityTestHarness _harness = null!;

    public async Task InitializeAsync()
    {
        if (!fixture.DockerAvailable) return;
        _harness = new IdentityTestHarness(fixture.ConnectionString);
        await _harness.CleanupIdentityDataAsync();
    }

    public async Task DisposeAsync()
    {
        if (_harness is not null) await _harness.DisposeAsync();
    }

    /// <summary>Drives challenge → mock consent → callback; returns the final redirect path.</summary>
    private static async Task<string> RunOAuthDanceAsync(HttpClient client, string provider, string email)
    {
        // 1) Challenge: the mock handler redirects to its consent page.
        var start = await client.GetAsync($"/api/auth/external/{provider}/start?returnUrl=%2F");
        Assert.Equal(HttpStatusCode.Redirect, start.StatusCode);
        var consentUrl = start.Headers.Location!.ToString();
        Assert.StartsWith($"/api/auth/mock/{provider}/authorize", consentUrl);

        // 2) The consent page renders (this is the screenshot step in e2e)…
        var consent = await client.GetAsync(consentUrl);
        Assert.Equal(HttpStatusCode.OK, consent.StatusCode);

        // 3) …and submitting it signs the external cookie and bounces back.
        var redirectUri = System.Web.HttpUtility.ParseQueryString(
            new Uri("http://x" + consentUrl).Query)["redirectUri"]!;
        var submit = await client.PostAsync($"/api/auth/mock/{provider}/authorize",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["email"] = email,
                ["redirectUri"] = redirectUri,
            }));
        Assert.Equal(HttpStatusCode.Redirect, submit.StatusCode);

        // 4) The shared callback decides: existing user → sign in; new → DOB step.
        var callback = await client.GetAsync(submit.Headers.Location!.ToString());
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        return callback.Headers.Location!.ToString();
    }

    [SkippableTheory]
    [InlineData("google", "Google")]
    [InlineData("apple", "Apple")]
    public async Task New_oauth_user_is_routed_to_dob_capture_and_activated_after_the_gate(
        string provider, string canonical)
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        var client = _harness.CreateClient();

        var next = await RunOAuthDanceAsync(client, provider, $"oauth_new@{provider}.test");
        Assert.Equal("/signup/complete", next); // DOB capture, not an account

        // No account yet — the gate hasn't passed.
        await using (var db = _harness.CreateDb())
            Assert.False(await db.Users.AnyAsync(u => u.Email == $"oauth_new@{provider}.test"));

        var pending = await client.GetFromJsonAsync<ExternalPendingResponse>("/api/auth/external/pending");
        Assert.Equal(canonical, pending!.Provider);
        Assert.Equal($"oauth_new@{provider}.test", pending.Email);

        var complete = await client.PostAsJsonAsync("/api/auth/external/complete",
            new ExternalCompleteRequest($"oauth_{provider}_user", IdentityTestHarness.AdultDob));
        complete.EnsureSuccessStatusCode();
        var me = await complete.Content.ReadFromJsonAsync<MeResponse>();
        Assert.Equal($"oauth_{provider}_user", me!.Handle);
        Assert.True(me.EmailVerified); // provider-verified email

        await using (var db = _harness.CreateDb())
        {
            var user = await db.Users.AsNoTracking().SingleAsync(u => u.UserName == $"oauth_{provider}_user");
            Assert.Equal(1990, user.BirthYear);
            Assert.NotNull(user.ActivatedAt);
            Assert.True(await db.Venues.AnyAsync(v => v.OwnerUserId == user.Id && v.VenueType == "home_bar"));
            var signupEvent = await db.Events.AsNoTracking().SingleAsync(e => e.Name == "signup");
            Assert.Equal(provider, signupEvent.Properties!["method"]);
        }
    }

    [SkippableTheory]
    [InlineData("google")]
    [InlineData("apple")]
    public async Task Under_21_oauth_arrival_is_blocked_and_leaves_nothing_behind(string provider)
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        var client = _harness.CreateClient();

        var next = await RunOAuthDanceAsync(client, provider, $"oauth_kid@{provider}.test");
        Assert.Equal("/signup/complete", next);

        var dob = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-20).ToString("yyyy-MM-dd");
        var complete = await client.PostAsJsonAsync("/api/auth/external/complete",
            new ExternalCompleteRequest("oauth_kid", dob));
        Assert.Equal(HttpStatusCode.Forbidden, complete.StatusCode);
        Assert.Equal("under_21", (await complete.Content.ReadFromJsonAsync<ApiError>())!.Code);

        // The external cookie was dropped: retrying the DOB step finds no session…
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.GetAsync("/api/auth/external/pending")).StatusCode);
        // …and nothing was persisted, on any table.
        await using var db = _harness.CreateDb();
        Assert.False(await db.Users.AnyAsync(u => u.Email == $"oauth_kid@{provider}.test"));
        Assert.False(await db.Events.AnyAsync());
    }

    [SkippableFact]
    public async Task Returning_oauth_user_signs_straight_in()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        var first = _harness.CreateClient();
        await RunOAuthDanceAsync(first, "google", "oauth_return@google.test");
        (await first.PostAsJsonAsync("/api/auth/external/complete",
            new ExternalCompleteRequest("oauth_returner", IdentityTestHarness.AdultDob))).EnsureSuccessStatusCode();

        // Fresh browser, same Google account: callback goes home, session live.
        var second = _harness.CreateClient();
        var next = await RunOAuthDanceAsync(second, "google", "oauth_return@google.test");
        Assert.Equal("/", next);
        var me = await second.GetFromJsonAsync<MeResponse>("/api/auth/me");
        Assert.Equal("oauth_returner", me!.Handle);
    }
}
