using BarBrain.Api.Data;
using BarBrain.Api.Data.Entities;
using Microsoft.AspNetCore.Identity;

namespace BarBrain.Api.Auth;

/// <summary>
/// Creates activated accounts — the single path shared by email signup and the
/// post-OAuth DOB-capture step, so the 21+ gate and the account invariants
/// (birth-year-only persistence, Home Bar creation, signup/activation events)
/// can never diverge between providers.
///
/// The full date of birth arrives as a parameter, feeds the age computation,
/// and is DISCARDED — only <c>dob.Year</c> and the attestation timestamp are
/// stored (ADR-010, Hard Rule 2). Under-21 → no user row is ever created.
/// </summary>
public sealed class AccountService(
    UserManager<User> userManager,
    AppDbContext db,
    TimeProvider clock)
{
    public const int HandleMinLength = 3;
    public const int HandleMaxLength = 32;

    public sealed record CreateOutcome(
        User? User,
        string? ErrorCode,
        string? ErrorMessage);

    public static string NormalizeHandle(string handle) => handle.Trim().ToLowerInvariant();

    /// <summary>Format-level handle validation (uniqueness is Identity's + the DB's job).</summary>
    public static bool IsValidHandle(string handle)
        => handle.Length is >= HandleMinLength and <= HandleMaxLength
           && handle.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_');

    public async Task<CreateOutcome> CreateActivatedAsync(
        string email,
        string handle,
        string? password,
        DateOnly dateOfBirth,
        string signupMethod,
        UserLoginInfo? externalLogin,
        bool emailVerifiedByProvider,
        CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();

        if (!AgeGate.IsOfAge(dateOfBirth, DateOnly.FromDateTime(now.UtcDateTime)))
            return new CreateOutcome(null, "under_21",
                "BarBrain is for people 21 and over. Thanks for your honesty — see you down the road.");

        handle = NormalizeHandle(handle);
        if (!IsValidHandle(handle))
            return new CreateOutcome(null, "invalid_handle",
                $"Handles are {HandleMinLength}–{HandleMaxLength} characters: lowercase letters, numbers, and underscores.");

        var user = new User
        {
            UserName = handle,
            Email = email.Trim(),
            EmailConfirmed = emailVerifiedByProvider,
            BirthYear = dateOfBirth.Year,   // the year is all that survives
            AttestedAt = now,
            ActivatedAt = now,
            CreatedAt = now,
            LockoutEnabled = true,
        };

        var created = password is null
            ? await userManager.CreateAsync(user)
            : await userManager.CreateAsync(user, password);
        if (!created.Succeeded)
            return new CreateOutcome(null, MapIdentityErrorCode(created),
                string.Join(" ", created.Errors.Select(e => e.Description)));

        if (externalLogin is not null)
        {
            var linked = await userManager.AddLoginAsync(user, externalLogin);
            if (!linked.Succeeded)
            {
                // Racing duplicate external login — roll the account back so
                // "no orphan accounts" holds.
                await userManager.DeleteAsync(user);
                return new CreateOutcome(null, "external_login_in_use",
                    "That sign-in is already linked to another account.");
            }
        }

        // Home Bar: the private virtual venue that is the default rating
        // location (ADR-015). One per user, enforced by the DB.
        db.Venues.Add(new Venue
        {
            Name = "Home Bar",
            VenueType = Data.Entities.VenueType.HomeBar,
            OwnerUserId = user.Id,
            Visibility = Visibility.Private,
            CreatedAt = now,
        });

        // First-party events (ADR-017). Signup and activation coincide on
        // every current path (the account only exists once the gate passes),
        // but they stay separate events for the retention dashboard's funnel.
        db.Events.AddRange(
            NewEvent("signup", now, user.Id, signupMethod),
            NewEvent("activation", now, user.Id, signupMethod));

        await db.SaveChangesAsync(ct);
        return new CreateOutcome(user, null, null);
    }

    private static EventRecord NewEvent(string name, DateTimeOffset at, Guid userId, string method) => new()
    {
        Name = name,
        OccurredAt = at,
        Properties = new Dictionary<string, string>
        {
            ["userId"] = userId.ToString(), // pseudonymous id, first-party only (ADR-017)
            ["method"] = method,
        },
    };

    private static string MapIdentityErrorCode(IdentityResult result)
    {
        var codes = result.Errors.Select(e => e.Code).ToHashSet();
        if (codes.Contains(nameof(IdentityErrorDescriber.DuplicateUserName))) return "handle_taken";
        if (codes.Contains(nameof(IdentityErrorDescriber.DuplicateEmail))) return "email_in_use";
        if (codes.Any(c => c.StartsWith("Password", StringComparison.Ordinal))) return "weak_password";
        if (codes.Contains(nameof(IdentityErrorDescriber.InvalidUserName))) return "invalid_handle";
        if (codes.Contains(nameof(IdentityErrorDescriber.InvalidEmail))) return "invalid_email";
        return "signup_failed";
    }
}
