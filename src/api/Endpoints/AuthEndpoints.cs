using System.Security.Claims;
using System.Text;
using BarBrain.Api.Auth;
using BarBrain.Api.Data.Entities;
using BarBrain.Api.Settings;
using BarBrain.Shared.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;

namespace BarBrain.Api.Endpoints;

/// <summary>
/// Identity endpoints (ADR-010/011). Every signup path — email, Google, Apple —
/// funnels through <see cref="AccountService"/>, so the 21+ gate is uniform
/// and an account row only ever exists AFTER the gate passes. The full DOB
/// lives in the request; only birth year + attestation are stored.
/// </summary>
public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/auth").WithTags("Auth");

        // ---------------------------------------------------------------- signup
        auth.MapPost("/signup", async (
            SignupRequest request,
            ITurnstileVerifier turnstile,
            AccountService accounts,
            SignInManager<User> signIn,
            UserManager<User> users,
            IVerificationEmailSender emailSender,
            ISettingsService settings,
            TimeProvider clock,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (!await turnstile.VerifyAsync(request.TurnstileToken, ct))
                return Results.Json(new ApiError("turnstile_failed",
                    "We couldn't confirm you're human. Give it another try."),
                    statusCode: StatusCodes.Status403Forbidden);

            if (!AgeGate.TryParseDateOfBirth(request.DateOfBirth, out var dob))
                return Results.BadRequest(new ApiError("invalid_dob", "Enter your date of birth as yyyy-MM-dd."));
            if (string.IsNullOrWhiteSpace(request.Email))
                return Results.BadRequest(new ApiError("invalid_email", "An email address is required."));

            var outcome = await accounts.CreateActivatedAsync(
                request.Email, request.Handle, request.Password, dob,
                signupMethod: "email", externalLogin: null, emailVerifiedByProvider: false, ct);

            if (outcome.User is null)
                return SignupFailure(outcome);

            await SendVerificationLinkAsync(outcome.User, users, emailSender, http, ct);
            await signIn.SignInAsync(outcome.User, isPersistent: true);
            return Results.Ok(await BuildMeAsync(outcome.User, settings, clock, ct));
        })
        .WithName("Signup");

        // ----------------------------------------------------------------- login
        auth.MapPost("/login", async (
            LoginRequest request,
            UserManager<User> users,
            SignInManager<User> signIn,
            ISettingsService settings,
            TimeProvider clock,
            CancellationToken ct) =>
        {
            var user = string.IsNullOrWhiteSpace(request.Email)
                ? null
                : await users.FindByEmailAsync(request.Email.Trim());
            if (user is null)
                return InvalidCredentials();

            var check = await signIn.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
            if (check.IsLockedOut)
                return Results.Json(new ApiError("locked_out",
                    "Too many attempts. Take a breather and try again in a few minutes."),
                    statusCode: StatusCodes.Status423Locked);
            if (!check.Succeeded)
                return InvalidCredentials();

            await signIn.SignInAsync(user, isPersistent: true);
            return Results.Ok(await BuildMeAsync(user, settings, clock, ct));

            static IResult InvalidCredentials() => Results.Json(
                new ApiError("invalid_credentials", "That email and password don't match."),
                statusCode: StatusCodes.Status401Unauthorized);
        })
        .WithName("Login");

        auth.MapPost("/logout", async (SignInManager<User> signIn) =>
        {
            await signIn.SignOutAsync();
            return Results.NoContent();
        })
        .WithName("Logout");

        // -------------------------------------------------------------------- me
        auth.MapGet("/me", async (
            ClaimsPrincipal principal,
            UserManager<User> users,
            ISettingsService settings,
            TimeProvider clock,
            CancellationToken ct) =>
        {
            var user = await users.GetUserAsync(principal);
            return user is null
                ? Results.Unauthorized()
                : Results.Ok(await BuildMeAsync(user, settings, clock, ct));
        })
        .RequireAuthorization()
        .WithName("Me");

        // What the login/signup UI should offer (providers, Turnstile key).
        auth.MapGet("/providers", (IConfiguration config) => Results.Ok(new AuthProvidersResponse(
            AuthRegistration.EnabledExternalProviders(config),
            config["Turnstile:SiteKey"])))
        .WithName("AuthProviders");

        // ---------------------------------------------------- email verification
        auth.MapPost("/verify-email/request", async (
            ClaimsPrincipal principal,
            UserManager<User> users,
            IVerificationEmailSender emailSender,
            HttpContext http,
            CancellationToken ct) =>
        {
            var user = await users.GetUserAsync(principal);
            if (user is null) return Results.Unauthorized();
            if (user.EmailConfirmed) return Results.NoContent();

            await SendVerificationLinkAsync(user, users, emailSender, http, ct);
            return Results.Accepted();
        })
        .RequireAuthorization()
        .WithName("RequestVerificationEmail");

        // The link target. GET because it's clicked from an email client;
        // lands back on the web app either way.
        auth.MapGet("/verify-email", async (
            Guid userId,
            string token,
            UserManager<User> users) =>
        {
            var user = await users.FindByIdAsync(userId.ToString());
            if (user is null) return Results.Redirect("/?verified=0");

            string decoded;
            try
            {
                decoded = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token));
            }
            catch (FormatException)
            {
                return Results.Redirect("/?verified=0");
            }

            var result = await users.ConfirmEmailAsync(user, decoded);
            return Results.Redirect(result.Succeeded ? "/?verified=1" : "/?verified=0");
        })
        .WithName("VerifyEmail");

        // ---------------------------------------------------------------- handle
        auth.MapPut("/handle", async (
            HandleChangeRequest request,
            ClaimsPrincipal principal,
            UserManager<User> users,
            SignInManager<User> signIn,
            ISettingsService settings,
            TimeProvider clock,
            CancellationToken ct) =>
        {
            var user = await users.GetUserAsync(principal);
            if (user is null) return Results.Unauthorized();

            var handle = AccountService.NormalizeHandle(request.Handle ?? "");
            if (!AccountService.IsValidHandle(handle))
                return Results.BadRequest(new ApiError("invalid_handle",
                    $"Handles are {AccountService.HandleMinLength}–{AccountService.HandleMaxLength} characters: lowercase letters, numbers, and underscores."));
            if (handle == user.UserName)
                return Results.Ok(await BuildMeAsync(user, settings, clock, ct));

            var cooldownDays = await settings.GetIntAsync(
                AuthFlags.HandleCooldownDays, AuthFlags.DefaultHandleCooldownDays, ct);
            var nextAllowed = user.HandleChangedAt?.AddDays(cooldownDays);
            if (nextAllowed is { } next && clock.GetUtcNow() < next)
                return Results.Json(new ApiError("handle_cooldown",
                    $"Handles can change every {cooldownDays} days. Next change: {next:yyyy-MM-dd}."),
                    statusCode: StatusCodes.Status409Conflict);

            var renamed = await users.SetUserNameAsync(user, handle);
            if (!renamed.Succeeded)
            {
                var code = renamed.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.DuplicateUserName))
                    ? "handle_taken" : "invalid_handle";
                return Results.Conflict(new ApiError(code,
                    string.Join(" ", renamed.Errors.Select(e => e.Description))));
            }

            user.HandleChangedAt = clock.GetUtcNow();
            await users.UpdateAsync(user);
            await signIn.RefreshSignInAsync(user); // claims carry the handle
            return Results.Ok(await BuildMeAsync(user, settings, clock, ct));
        })
        .RequireAuthorization()
        .WithName("ChangeHandle");

        // ------------------------------------------------------- external OAuth
        // Challenge → provider → callback. Existing linked user signs straight
        // in; a new person is parked on the SHORT-LIVED external cookie and
        // sent to the DOB-capture step. No account exists until that passes.
        auth.MapGet("/external/{provider}/start", (
            string provider,
            string? returnUrl,
            SignInManager<User> signIn,
            IConfiguration config) =>
        {
            var enabled = AuthRegistration.EnabledExternalProviders(config);
            var scheme = enabled.FirstOrDefault(p => p.Equals(provider, StringComparison.OrdinalIgnoreCase));
            if (scheme is null)
                return Results.NotFound(new ApiError("unknown_provider", $"'{provider}' sign-in isn't available."));

            var callback = $"/api/auth/external/callback?returnUrl={Uri.EscapeDataString(SafeLocal(returnUrl))}";
            var props = signIn.ConfigureExternalAuthenticationProperties(scheme, callback);
            return Results.Challenge(props, [scheme]);
        })
        .WithName("ExternalStart");

        auth.MapGet("/external/callback", async (
            string? returnUrl,
            SignInManager<User> signIn,
            UserManager<User> users,
            HttpContext http) =>
        {
            var info = await signIn.GetExternalLoginInfoAsync();
            if (info is null)
                return Results.Redirect("/login?error=external");

            var existing = await users.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
            if (existing is not null)
            {
                await signIn.SignInAsync(existing, isPersistent: true);
                await http.SignOutAsync(IdentityConstants.ExternalScheme);
                return Results.Redirect(SafeLocal(returnUrl));
            }

            // New face: age gate next. The external cookie carries the claims.
            return Results.Redirect("/signup/complete");
        })
        .WithName("ExternalCallback");

        // Who is mid-flow (for the DOB-capture page's "continue as" line).
        auth.MapGet("/external/pending", async (SignInManager<User> signIn) =>
        {
            var info = await signIn.GetExternalLoginInfoAsync();
            return info is null
                ? Results.Unauthorized()
                : Results.Ok(new ExternalPendingResponse(
                    info.LoginProvider,
                    info.Principal.FindFirstValue(ClaimTypes.Email)));
        })
        .WithName("ExternalPending");

        auth.MapPost("/external/complete", async (
            ExternalCompleteRequest request,
            SignInManager<User> signIn,
            AccountService accounts,
            ISettingsService settings,
            TimeProvider clock,
            HttpContext http,
            CancellationToken ct) =>
        {
            var info = await signIn.GetExternalLoginInfoAsync();
            if (info is null)
                return Results.Unauthorized();

            if (!AgeGate.TryParseDateOfBirth(request.DateOfBirth, out var dob))
                return Results.BadRequest(new ApiError("invalid_dob", "Enter your date of birth as yyyy-MM-dd."));

            var email = info.Principal.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrWhiteSpace(email))
                return Results.BadRequest(new ApiError("no_email",
                    "Your sign-in didn't share an email address. Use email signup instead."));

            var outcome = await accounts.CreateActivatedAsync(
                email, request.Handle, password: null, dob,
                signupMethod: info.LoginProvider.ToLowerInvariant(),
                externalLogin: new UserLoginInfo(info.LoginProvider, info.ProviderKey, info.ProviderDisplayName),
                emailVerifiedByProvider: true, // Google/Apple emails arrive verified
                ct);

            if (outcome.User is null)
            {
                if (outcome.ErrorCode == "under_21")
                {
                    // Hard stop, and drop the external cookie: nothing persists.
                    await http.SignOutAsync(IdentityConstants.ExternalScheme);
                }
                return SignupFailure(outcome);
            }

            await http.SignOutAsync(IdentityConstants.ExternalScheme);
            await signIn.SignInAsync(outcome.User, isPersistent: true);
            return Results.Ok(await BuildMeAsync(outcome.User, settings, clock, ct));
        })
        .WithName("ExternalComplete");

        MapMockProviderEndpoints(app);
        return app;
    }

    /// <summary>
    /// The mock provider's "account chooser" (see MockExternalAuthHandler).
    /// Exists only when Auth:EnableMockExternal=true (never Production —
    /// startup enforces). Plain HTML on purpose: it plays the role of the
    /// EXTERNAL provider's page, so it deliberately isn't BarBrain-branded.
    /// </summary>
    private static void MapMockProviderEndpoints(IEndpointRouteBuilder app)
    {
        var mock = app.MapGroup("/api/auth/mock").WithTags("Auth (mock provider)");

        mock.MapGet("/{scheme}/authorize", (string scheme, string? redirectUri, IConfiguration config) =>
        {
            if (!MockEnabled(config, scheme, out var canonical))
                return Results.NotFound();

            var target = SafeLocal(redirectUri);
            var html = $"""
                <!doctype html><html><head><meta charset="utf-8">
                <meta name="viewport" content="width=device-width, initial-scale=1">
                <title>{canonical} (mock) — sign in</title></head>
                <body style="font-family:sans-serif;max-width:24rem;margin:4rem auto">
                <h1>{canonical} <small>(mock provider)</small></h1>
                <p>CI/e2e stand-in for the real {canonical} consent screen.</p>
                <form method="post" action="/api/auth/mock/{canonical.ToLowerInvariant()}/authorize">
                  <input type="hidden" name="redirectUri" value="{target}">
                  <label for="email">Email</label><br>
                  <input id="email" name="email" type="email" required style="width:100%;padding:.5rem"><br><br>
                  <button type="submit" style="padding:.5rem 1rem">Continue</button>
                </form></body></html>
                """;
            return Results.Content(html, "text/html");
        });

        mock.MapPost("/{scheme}/authorize", async (string scheme, HttpContext http, IConfiguration config) =>
        {
            if (!MockEnabled(config, scheme, out var canonical))
                return Results.NotFound();

            var form = await http.Request.ReadFormAsync();
            var email = form["email"].ToString().Trim();
            var redirectUri = SafeLocal(form["redirectUri"].ToString());
            if (string.IsNullOrWhiteSpace(email))
                return Results.BadRequest(new ApiError("invalid_email", "Email is required."));

            var identity = new ClaimsIdentity(canonical);
            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, $"mock:{email.ToLowerInvariant()}"));
            identity.AddClaim(new Claim(ClaimTypes.Email, email));

            var props = new AuthenticationProperties();
            props.Items["LoginProvider"] = canonical; // what GetExternalLoginInfoAsync expects

            await http.SignInAsync(IdentityConstants.ExternalScheme, new ClaimsPrincipal(identity), props);
            return Results.Redirect(redirectUri);
        });

        static bool MockEnabled(IConfiguration config, string scheme, out string canonical)
        {
            canonical = scheme.ToLowerInvariant() switch
            {
                "google" => "Google",
                "apple" => "Apple",
                _ => "",
            };
            return canonical.Length > 0 && config.GetValue("Auth:EnableMockExternal", false);
        }
    }

    private static IResult SignupFailure(AccountService.CreateOutcome outcome)
    {
        var status = outcome.ErrorCode switch
        {
            "under_21" => StatusCodes.Status403Forbidden,
            "handle_taken" or "email_in_use" or "external_login_in_use" => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest,
        };
        return Results.Json(new ApiError(outcome.ErrorCode!, outcome.ErrorMessage!), statusCode: status);
    }

    private static async Task SendVerificationLinkAsync(
        User user,
        UserManager<User> users,
        IVerificationEmailSender emailSender,
        HttpContext http,
        CancellationToken ct)
    {
        var token = await users.GenerateEmailConfirmationTokenAsync(user);
        // Base64url so '+' etc. survive the query string round-trip.
        var encoded = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        var url = $"{http.Request.Scheme}://{http.Request.Host}/api/auth/verify-email?userId={user.Id}&token={encoded}";
        await emailSender.SendAsync(user.Email!, url, ct);
    }

    private static async Task<MeResponse> BuildMeAsync(
        User user, ISettingsService settings, TimeProvider clock, CancellationToken ct)
    {
        var deadline = await AuthFlags.VerificationDeadlineAsync(user, settings, ct);
        var canRate = deadline is null || clock.GetUtcNow() < deadline;
        return new MeResponse(
            user.Id, user.UserName!, user.Email!, user.EmailConfirmed,
            deadline, canRate, user.CreatedAt);
    }

    /// <summary>Open-redirect guard: only same-app paths survive.</summary>
    private static string SafeLocal(string? url)
        => !string.IsNullOrEmpty(url) && url.StartsWith('/') && !url.StartsWith("//", StringComparison.Ordinal)
            ? url : "/";
}
