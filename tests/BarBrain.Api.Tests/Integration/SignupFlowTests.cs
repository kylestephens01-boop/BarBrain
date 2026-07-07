using System.Net;
using System.Net.Http.Json;
using BarBrain.Api.Data.Entities;
using BarBrain.Shared.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BarBrain.Api.Tests.Integration;

/// <summary>
/// Email signup path (ADR-010/011): the 21+ gate, birth-year-only persistence,
/// Home Bar auto-creation, signup/activation events, session cookie, soft
/// email verification, and the handle-change cooldown.
/// </summary>
[Collection("postgres")]
public sealed class SignupFlowTests(PostgresFixture fixture) : IAsyncLifetime
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

    [SkippableFact]
    public async Task Signup_creates_activated_user_home_bar_and_events_and_signs_in()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        var client = _harness.CreateClient();

        var me = await _harness.SignupAsync(client, "flow_ada", dob: "1990-04-17");
        Assert.Equal("flow_ada", me.Handle);
        Assert.False(me.EmailVerified);
        Assert.True(me.CanRate); // soft verification: full use immediately
        Assert.NotNull(me.VerificationDeadline);

        // The cookie is live.
        var again = await client.GetFromJsonAsync<MeResponse>("/api/auth/me");
        Assert.Equal(me.Id, again!.Id);

        await using var db = _harness.CreateDb();
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.UserName == "flow_ada");
        Assert.Equal(1990, user.BirthYear);           // year only (ADR-010)
        Assert.NotNull(user.AttestedAt);
        Assert.NotNull(user.ActivatedAt);

        // Home Bar: private, owned, exactly one (ADR-015).
        var homeBar = await db.Venues.AsNoTracking().SingleAsync(v => v.OwnerUserId == user.Id);
        Assert.Equal("home_bar", homeBar.VenueType);
        Assert.Equal("private", homeBar.Visibility);

        // First-party events (ADR-017).
        var events = await db.Events.AsNoTracking()
            .Where(e => e.Name == "signup" || e.Name == "activation").ToListAsync();
        Assert.Equal(2, events.Count);
        Assert.All(events, e => Assert.Equal("email", e.Properties!["method"]));
    }

    [SkippableFact]
    public async Task Under_21_is_politely_refused_and_no_account_row_exists()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        var client = _harness.CreateClient();

        // 20 years old today.
        var dob = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-20).ToString("yyyy-MM-dd");
        var response = await client.PostAsJsonAsync("/api/auth/signup", new SignupRequest(
            "flow_kid@example.test", "correct-horse-battery", "flow_kid", dob, null));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("under_21", error!.Code);

        await using var db = _harness.CreateDb();
        Assert.False(await db.Users.AnyAsync(u => u.Email == "flow_kid@example.test"));
        Assert.False(await db.Events.AnyAsync(e => e.Name == "signup")); // nothing recorded
    }

    [SkippableFact]
    public async Task Exactly_21_today_is_allowed()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        var client = _harness.CreateClient();
        var dob = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-21).ToString("yyyy-MM-dd");
        var me = await _harness.SignupAsync(client, "flow_birthday", dob: dob);
        Assert.Equal("flow_birthday", me.Handle);
    }

    [SkippableFact]
    public async Task Duplicate_handle_and_email_conflict_with_stable_codes()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        await _harness.SignupAsync(_harness.CreateClient(), "flow_dupe", "dupe@example.test");

        var second = await _harness.CreateClient().PostAsJsonAsync("/api/auth/signup",
            new SignupRequest("other@example.test", "correct-horse-battery", "flow_dupe", IdentityTestHarness.AdultDob, null));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("handle_taken", (await second.Content.ReadFromJsonAsync<ApiError>())!.Code);

        var third = await _harness.CreateClient().PostAsJsonAsync("/api/auth/signup",
            new SignupRequest("dupe@example.test", "correct-horse-battery", "flow_dupe2", IdentityTestHarness.AdultDob, null));
        Assert.Equal(HttpStatusCode.Conflict, third.StatusCode);
        Assert.Equal("email_in_use", (await third.Content.ReadFromJsonAsync<ApiError>())!.Code);
    }

    [SkippableFact]
    public async Task Handles_are_normalized_lowercase_and_validated()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        var me = await _harness.SignupAsync(_harness.CreateClient(), "Flow_MiXeD");
        Assert.Equal("flow_mixed", me.Handle);

        var bad = await _harness.CreateClient().PostAsJsonAsync("/api/auth/signup",
            new SignupRequest("bad@example.test", "correct-horse-battery", "no spaces!", IdentityTestHarness.AdultDob, null));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Equal("invalid_handle", (await bad.Content.ReadFromJsonAsync<ApiError>())!.Code);
    }

    [SkippableFact]
    public async Task Login_logout_lifecycle_works_and_rejects_bad_credentials()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        await _harness.SignupAsync(_harness.CreateClient(), "flow_login", "login@example.test");

        var client = _harness.CreateClient();
        var bad = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest("login@example.test", "wrong-password"));
        Assert.Equal(HttpStatusCode.Unauthorized, bad.StatusCode);

        var good = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest("login@example.test", "correct-horse-battery"));
        good.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);

        (await client.PostAsync("/api/auth/logout", null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [SkippableFact]
    public async Task Email_verification_link_confirms_the_account()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        var client = _harness.CreateClient();
        var me = await _harness.SignupAsync(client, "flow_verify");

        // Mint the token exactly as the (logged) email link would carry it.
        using var scope = _harness.Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var user = await users.FindByIdAsync(me.Id.ToString());
        var token = await users.GenerateEmailConfirmationTokenAsync(user!);
        var encoded = WebEncoders.Base64UrlEncode(System.Text.Encoding.UTF8.GetBytes(token));

        var response = await client.GetAsync($"/api/auth/verify-email?userId={me.Id}&token={encoded}");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/?verified=1", response.Headers.Location!.ToString());

        var after = await client.GetFromJsonAsync<MeResponse>("/api/auth/me");
        Assert.True(after!.EmailVerified);
        Assert.Null(after.VerificationDeadline);
    }

    [SkippableFact]
    public async Task Handle_change_works_once_then_hits_the_cooldown()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        var client = _harness.CreateClient();
        await _harness.SignupAsync(client, "flow_rename");

        var first = await client.PutAsJsonAsync("/api/auth/handle", new HandleChangeRequest("flow_renamed"));
        first.EnsureSuccessStatusCode();
        Assert.Equal("flow_renamed", (await first.Content.ReadFromJsonAsync<MeResponse>())!.Handle);

        // Immediately again → 30-day (flag) cooldown.
        var second = await client.PutAsJsonAsync("/api/auth/handle", new HandleChangeRequest("flow_renamed2"));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("handle_cooldown", (await second.Content.ReadFromJsonAsync<ApiError>())!.Code);
    }
}
