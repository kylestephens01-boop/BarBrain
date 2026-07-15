using BarBrain.Api.Data;
using BarBrain.Api.Data.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;

namespace BarBrain.Api.Auth;

/// <summary>
/// Identity + cookie + external-provider wiring (ADR-011: email+password,
/// Google, Apple; soft verification). Cookie auth over the same-origin Caddy
/// proxy: HttpOnly + SameSite=Lax means the browser won't attach the session
/// to cross-site POSTs, which is the CSRF posture for this JSON-only API.
/// </summary>
public static class AuthRegistration
{
    public static void AddBarBrainAuth(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        var config = builder.Configuration;

        services.AddSingleton(TimeProvider.System);
        services.AddHttpClient("turnstile");
        services.AddScoped<ITurnstileVerifier, TurnstileVerifier>();
        // IVerificationEmailSender is registered by AddBarBrainEmail (SMTP or
        // logging, chosen by config) alongside the other email paths.
        services.AddScoped<AccountService>();

        services.AddIdentityCore<User>(o =>
        {
            o.User.RequireUniqueEmail = true;
            // The handle alphabet (stored lowercase; DB CHECK backs this up).
            o.User.AllowedUserNameCharacters = "abcdefghijklmnopqrstuvwxyz0123456789_";
            // Length-first password policy (composition rules add friction to
            // the <2-min Gate B flow without adding much strength).
            o.Password.RequiredLength = 8;
            o.Password.RequireNonAlphanumeric = false;
            o.Password.RequireUppercase = false;
            o.Password.RequireLowercase = false;
            o.Password.RequireDigit = false;
            o.Lockout.MaxFailedAccessAttempts = 10;
            o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
        })
        .AddEntityFrameworkStores<AppDbContext>()
        .AddSignInManager()
        .AddDefaultTokenProviders();

        var auth = services.AddAuthentication(IdentityConstants.ApplicationScheme);

        auth.AddCookie(IdentityConstants.ApplicationScheme, o =>
        {
            o.Cookie.Name = "bb_session";
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Lax;
            // Secure follows the request scheme: https in prod (Caddy sets
            // X-Forwarded-Proto, honored via UseForwardedHeaders), http for
            // localhost e2e.
            o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            o.ExpireTimeSpan = TimeSpan.FromDays(30);
            o.SlidingExpiration = true;
            // JSON API: status codes, never login-page redirects.
            o.Events.OnRedirectToLogin = ctx =>
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            o.Events.OnRedirectToAccessDenied = ctx =>
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });

        // Short-lived carrier between the OAuth callback and the DOB-capture
        // step. An under-21 answer just abandons this cookie — no account.
        auth.AddCookie(IdentityConstants.ExternalScheme, o =>
        {
            o.Cookie.Name = "bb_external";
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Lax;
            o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            o.ExpireTimeSpan = TimeSpan.FromMinutes(15);
        });

        // --- Real providers: registered only when configured (creds are a
        // HUMAN-CHECKLIST item; the code path ships now). ---------------------
        var googleClientId = config["Auth:Google:ClientId"];
        var googleConfigured = !string.IsNullOrWhiteSpace(googleClientId);
        if (googleConfigured)
        {
            auth.AddGoogle(o =>
            {
                o.ClientId = googleClientId!;
                o.ClientSecret = config["Auth:Google:ClientSecret"]
                    ?? throw new InvalidOperationException("Auth:Google:ClientSecret is required when ClientId is set.");
                o.SignInScheme = IdentityConstants.ExternalScheme;
                o.CallbackPath = "/api/auth/callback/google";
            });
        }

        var appleClientId = config["Auth:Apple:ClientId"];
        var appleConfigured = !string.IsNullOrWhiteSpace(appleClientId);
        if (appleConfigured)
        {
            auth.AddApple(o =>
            {
                o.ClientId = appleClientId!;
                o.TeamId = config["Auth:Apple:TeamId"]
                    ?? throw new InvalidOperationException("Auth:Apple:TeamId is required when ClientId is set.");
                o.KeyId = config["Auth:Apple:KeyId"]
                    ?? throw new InvalidOperationException("Auth:Apple:KeyId is required when ClientId is set.");
                var privateKey = config["Auth:Apple:PrivateKey"]
                    ?? throw new InvalidOperationException("Auth:Apple:PrivateKey (.p8 contents) is required when ClientId is set.");
                o.PrivateKey = (_, _) => Task.FromResult(privateKey.AsMemory());
                o.SignInScheme = IdentityConstants.ExternalScheme;
                o.CallbackPath = "/api/auth/callback/apple";
            });
        }

        // --- Mock providers for CI/e2e (Sprint 2 acceptance) -----------------
        if (config.GetValue("Auth:EnableMockExternal", false))
        {
            if (builder.Environment.IsProduction())
                throw new InvalidOperationException(
                    "Auth:EnableMockExternal must never be on in Production — it bypasses real OAuth.");

            if (!googleConfigured)
                auth.AddScheme<AuthenticationSchemeOptions, MockExternalAuthHandler>("Google", "Google (mock)", _ => { });
            if (!appleConfigured)
                auth.AddScheme<AuthenticationSchemeOptions, MockExternalAuthHandler>("Apple", "Apple (mock)", _ => { });
        }

        services.AddAuthorization();
    }

    /// <summary>External providers currently signable-in (for the web's buttons).</summary>
    public static IReadOnlyList<string> EnabledExternalProviders(IConfiguration config)
    {
        var providers = new List<string>();
        var mock = config.GetValue("Auth:EnableMockExternal", false);
        if (mock || !string.IsNullOrWhiteSpace(config["Auth:Google:ClientId"])) providers.Add("Google");
        if (mock || !string.IsNullOrWhiteSpace(config["Auth:Apple:ClientId"])) providers.Add("Apple");
        return providers;
    }
}
