using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace BarBrain.Api.Auth;

/// <summary>
/// CI/e2e stand-in for Google/Apple (Sprint 2 acceptance: "mocked providers in
/// CI"). Registered under the REAL scheme names ("Google", "Apple") only when
/// <c>Auth:EnableMockExternal=true</c> and the real provider is not configured,
/// so the challenge → external-cookie → DOB-capture pipeline is byte-for-byte
/// the one production uses. Startup refuses to boot with this enabled in the
/// Production environment.
///
/// The challenge redirects to a local "consent" page
/// (<c>/api/auth/mock/{scheme}/authorize</c>, mapped in AuthEndpoints) where
/// the test types an email, exactly like a provider account chooser.
/// </summary>
public sealed class MockExternalAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        => Task.FromResult(AuthenticateResult.NoResult());

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var redirectUri = properties.RedirectUri ?? "/";
        Response.Redirect($"/api/auth/mock/{Uri.EscapeDataString(Scheme.Name.ToLowerInvariant())}/authorize?redirectUri={Uri.EscapeDataString(redirectUri)}");
        return Task.CompletedTask;
    }
}
