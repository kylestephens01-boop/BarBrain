using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace BarBrain.Api.Tests.Integration;

/// <summary>
/// Sprint 7 security pass: the authz matrix. Every mapped endpoint is
/// classified — Anon (public by design), User (cookie session required), or
/// Admin (X-Admin-Token required) — and the suite enforces the negative side:
/// anonymous requests never reach a User endpoint, and neither anonymous nor
/// ordinary signed-in users ever reach an Admin endpoint.
///
/// THE MATRIX IS THE CONTRACT: adding an endpoint without classifying it here
/// fails the completeness check, so "we forgot to guard the new route" cannot
/// merge. Ownership-scoping negatives (user A attacking user B's rows,
/// 404-not-403 posture) live in AuthzTests; this suite is the route-level
/// perimeter.
/// </summary>
[Collection("postgres")]
public sealed class AuthzMatrixTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const string AdminToken = "authz-matrix-admin-token";

    private ApiFactory _factory = null!;

    public Task InitializeAsync()
    {
        // No DB dependency for the perimeter checks: auth middleware and the
        // admin filter both reject before any handler (or database) runs.
        _factory = new ApiFactory
        {
            ConnectionStringOverride = fixture.DockerAvailable
                ? fixture.ConnectionString
                : "Host=localhost;Database=unused;Username=unused;Password=unused",
            MigrateOnStartup = false,
            Settings = new Dictionary<string, string?>
            {
                ["Auth:EnableMockExternal"] = "true", // mock routes exist in tests — classify them too
                ["Admin:Token"] = AdminToken,          // the stub allows-all when EMPTY; tests must not run that way
            },
        };
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private enum Access { Anon, User, Admin }

    /// <summary>
    /// THE MATRIX. Route params are normalized to bare {name}. Keep sorted by
    /// class, then route — the completeness test enforces exact coverage in
    /// both directions.
    /// </summary>
    private static readonly Dictionary<(string Method, string Route), Access> Matrix = new()
    {
        // ------------------------------------------------------------- Anon
        [("GET", "/health")] = Access.Anon,
        [("GET", "/version")] = Access.Anon,
        [("GET", "/api/config/home")] = Access.Anon,
        [("GET", "/api/config/pwa")] = Access.Anon,
        [("POST", "/api/events")] = Access.Anon,
        [("GET", "/api/catalog/search")] = Access.Anon,
        [("GET", "/api/catalog/drinks")] = Access.Anon,
        [("GET", "/api/catalog/drinks/{id}")] = Access.Anon,
        [("GET", "/api/catalog/drinks/{id}/ratings")] = Access.Anon,
        [("GET", "/api/catalog/styles")] = Access.Anon,
        [("POST", "/api/auth/signup")] = Access.Anon,
        [("POST", "/api/auth/login")] = Access.Anon,
        [("POST", "/api/auth/logout")] = Access.Anon,
        [("GET", "/api/auth/providers")] = Access.Anon,
        [("GET", "/api/auth/verify-email")] = Access.Anon,
        [("GET", "/api/auth/external/{provider}/start")] = Access.Anon,
        [("GET", "/api/auth/external/callback")] = Access.Anon,
        [("GET", "/api/auth/external/pending")] = Access.Anon,  // external cookie is the credential
        [("POST", "/api/auth/external/complete")] = Access.Anon, // ditto — 401s itself without it
        [("GET", "/api/auth/mock/{scheme}/authorize")] = Access.Anon, // test/CI only; hard-blocked in Production
        [("POST", "/api/auth/mock/{scheme}/authorize")] = Access.Anon,
        [("GET", "/api/digest/unsubscribe")] = Access.Anon, // token in link is the credential (CAN-SPAM)
        [("GET", "/api/venues/nearby")] = Access.Anon,
        [("GET", "/api/venues/{id}")] = Access.Anon,
        [("GET", "/api/venues/{id}/menu")] = Access.Anon,
        [("GET", "/api/venues/{id}/qr.png")] = Access.Anon,
        [("GET", "/api/venues/{id}/onepager.pdf")] = Access.Anon,

        // ------------------------------------------------------------- User
        [("GET", "/api/auth/me")] = Access.User,
        [("POST", "/api/auth/verify-email/request")] = Access.User,
        [("PUT", "/api/auth/handle")] = Access.User,
        [("GET", "/api/account/export")] = Access.User,
        [("GET", "/api/account/deletion")] = Access.User,
        [("POST", "/api/account/delete")] = Access.User,
        [("POST", "/api/account/delete/cancel")] = Access.User,
        [("POST", "/api/ratings")] = Access.User,
        [("GET", "/api/ratings/mine")] = Access.User,
        [("GET", "/api/ratings/mine/drink/{drinkId}")] = Access.User,
        [("PATCH", "/api/ratings/{id}")] = Access.User,
        [("DELETE", "/api/ratings/{id}")] = Access.User,
        [("GET", "/api/feed")] = Access.User,
        [("GET", "/api/palate/mine")] = Access.User,
        [("GET", "/api/quiz/interests")] = Access.User,
        [("PUT", "/api/quiz/interests")] = Access.User,
        [("GET", "/api/quiz")] = Access.User,
        [("POST", "/api/quiz/complete")] = Access.User,
        [("GET", "/api/matches")] = Access.User,
        [("GET", "/api/matches/settings")] = Access.User,
        [("PUT", "/api/matches/hide")] = Access.User,
        [("PUT", "/api/digest/subscription")] = Access.User,
        [("GET", "/api/rapidrate/drinks")] = Access.User,
        [("POST", "/api/venues")] = Access.User,
        [("POST", "/api/venues/{id}/menu")] = Access.User,
        [("PATCH", "/api/venues/menu-items/{id}")] = Access.User,
        [("GET", "/api/venues/{id}/menu/personalized")] = Access.User,
        [("GET", "/api/venues/home-bar")] = Access.User,
        [("PATCH", "/api/venues/home-bar")] = Access.User,
        [("POST", "/api/checkins")] = Access.User,
        [("GET", "/api/checkins/active")] = Access.User,
        [("GET", "/api/badges")] = Access.User,
        [("GET", "/api/badges/unseen")] = Access.User,
        [("POST", "/api/badges/seen")] = Access.User,
        [("POST", "/api/reports")] = Access.User,

        // ------------------------------------------------------------ Admin
        [("GET", "/api/admin/settings")] = Access.Admin,
        [("GET", "/api/admin/settings/{key}")] = Access.Admin,
        [("PUT", "/api/admin/settings/{key}")] = Access.Admin,
        [("GET", "/api/admin/merge-queue")] = Access.Admin,
        [("POST", "/api/admin/merge-queue/{id}/approve")] = Access.Admin,
        [("POST", "/api/admin/merge-queue/{id}/reject")] = Access.Admin,
        [("POST", "/api/admin/matches/rebuild")] = Access.Admin,
        [("POST", "/api/admin/digest/run")] = Access.Admin,
        [("POST", "/api/admin/venues/{id}/tier")] = Access.Admin,
        [("POST", "/api/admin/venues/{id}/menu")] = Access.Admin,
        [("GET", "/api/admin/moderation/reports")] = Access.Admin,
        [("POST", "/api/admin/moderation/reports/{id}/action")] = Access.Admin,
        [("POST", "/api/admin/moderation/reports/{id}/dismiss")] = Access.Admin,
        [("POST", "/api/admin/moderation/content/{entityType}/{id}/unhide")] = Access.Admin,
        [("GET", "/api/admin/moderation/anomalies")] = Access.Admin,
        [("POST", "/api/admin/moderation/anomalies/{id}/clear")] = Access.Admin,
        [("POST", "/api/admin/moderation/anomalies/scan")] = Access.Admin,
        [("POST", "/api/admin/moderation/users/{id}/shadow-limit")] = Access.Admin,
        [("POST", "/api/admin/moderation/users/{id}/clear-shadow-limit")] = Access.Admin,
        [("POST", "/api/admin/moderation/users/{id}/ban")] = Access.Admin,
        [("POST", "/api/admin/moderation/users/{id}/unban")] = Access.Admin,
        [("GET", "/api/admin/moderation/audit")] = Access.Admin,
        [("GET", "/api/admin/analytics")] = Access.Admin,
    };

    // -------------------------------------------------------------- discovery

    private List<(string Method, string Route)> DiscoverEndpoints()
    {
        var sources = _factory.Services.GetServices<EndpointDataSource>();
        var found = new List<(string, string)>();
        foreach (var endpoint in sources.SelectMany(s => s.Endpoints).OfType<RouteEndpoint>())
        {
            var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods;
            if (methods is null) continue;
            var route = Normalize(endpoint.RoutePattern.RawText ?? "");
            found.AddRange(methods.Select(m => (m, route)));
        }
        return found.Distinct().ToList();
    }

    /// <summary>Bare {name} params, no trailing slash, leading slash guaranteed.</summary>
    private static string Normalize(string pattern)
    {
        var sb = new StringBuilder("/");
        foreach (var segment in pattern.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (sb.Length > 1) sb.Append('/');
            if (segment.StartsWith('{'))
            {
                var name = segment.Trim('{', '}');
                var colon = name.IndexOf(':');
                sb.Append('{').Append(colon < 0 ? name : name[..colon]).Append('}');
            }
            else
            {
                sb.Append(segment);
            }
        }
        return sb.ToString();
    }

    /// <summary>A callable path: route params filled with plausible junk.</summary>
    private static string Fill(string route) => route
        .Replace("{id}", Guid.NewGuid().ToString())
        .Replace("{drinkId}", Guid.NewGuid().ToString())
        .Replace("{key}", "home.banner_text")
        .Replace("{provider}", "google")
        .Replace("{scheme}", "google")
        .Replace("{entityType}", "rating");

    private static HttpRequestMessage Build(string method, string route, string? adminToken = null)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), Fill(route));
        if (method is "POST" or "PUT" or "PATCH")
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        if (adminToken is not null)
            request.Headers.Add("X-Admin-Token", adminToken);
        return request;
    }

    private static async Task AssertAllUnauthorizedAsync(
        HttpClient client, IEnumerable<(string Method, string Route)> endpoints, string? adminToken = null)
    {
        var leaks = new List<string>();
        foreach (var (method, route) in endpoints)
        {
            using var response = await client.SendAsync(Build(method, route, adminToken));
            if (response.StatusCode != HttpStatusCode.Unauthorized)
                leaks.Add($"{method} {route} → {(int)response.StatusCode}");
        }
        Assert.True(leaks.Count == 0,
            "Expected 401 from every guarded endpoint, got:\n" + string.Join("\n", leaks));
    }

    // ================================================================== tests

    [Fact]
    public void Matrix_classifies_every_mapped_endpoint_exactly()
    {
        var discovered = DiscoverEndpoints();

        var unclassified = discovered.Where(e => !Matrix.ContainsKey(e)).ToList();
        var stale = Matrix.Keys.Where(k => !discovered.Contains(k)).ToList();

        Assert.True(unclassified.Count == 0,
            "New endpoints MUST be classified in the authz matrix:\n"
            + string.Join("\n", unclassified.Select(e => $"{e.Method} {e.Route}")));
        Assert.True(stale.Count == 0,
            "Matrix entries no longer mapped (remove them):\n"
            + string.Join("\n", stale.Select(e => $"{e.Method} {e.Route}")));
    }

    [Fact]
    public async Task Anonymous_requests_never_reach_user_endpoints()
    {
        using var client = _factory.CreateDefaultClient();
        await AssertAllUnauthorizedAsync(client,
            Matrix.Where(kv => kv.Value == Access.User).Select(kv => kv.Key));
    }

    [Fact]
    public async Task Admin_endpoints_refuse_missing_and_wrong_tokens()
    {
        using var client = _factory.CreateDefaultClient();
        var adminRoutes = Matrix.Where(kv => kv.Value == Access.Admin).Select(kv => kv.Key).ToList();

        await AssertAllUnauthorizedAsync(client, adminRoutes);                       // no token
        await AssertAllUnauthorizedAsync(client, adminRoutes, "wrong-token-value");  // wrong token
    }

    [SkippableFact]
    public async Task Signed_in_user_cookie_is_not_an_admin_credential()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");

        await using var harness = new IdentityTestHarness(fixture.ConnectionString);
        await harness.CleanupIdentityDataAsync();

        // The harness factory has no Admin:Token — force one so the stub enforces.
        await using var factory = new ApiFactory
        {
            ConnectionStringOverride = fixture.ConnectionString,
            MigrateOnStartup = false,
            Settings = new Dictionary<string, string?>
            {
                ["Auth:EnableMockExternal"] = "true",
                ["Admin:Token"] = AdminToken,
            },
        };
        var client = factory.CreateDefaultClient(
            new Microsoft.AspNetCore.Mvc.Testing.Handlers.CookieContainerHandler());

        var signup = await client.PostAsJsonAsync("/api/auth/signup",
            new BarBrain.Shared.Contracts.SignupRequest(
                "matrix_user@example.test", "correct-horse-battery", "matrix_user",
                IdentityTestHarness.AdultDob, null));
        signup.EnsureSuccessStatusCode();

        // Ordinary session cookie, no token — every admin route still refuses.
        await AssertAllUnauthorizedAsync(client,
            Matrix.Where(kv => kv.Value == Access.Admin).Select(kv => kv.Key));
    }
}
