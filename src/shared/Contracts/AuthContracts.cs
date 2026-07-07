namespace BarBrain.Shared.Contracts;

/// <summary>
/// Machine-readable API error. <c>Code</c> is a stable snake_case identifier
/// the web branches on (e.g. "under_21", "handle_taken", "verification_required");
/// <c>Message</c> is user-presentable copy (BRAND.md voice).
/// </summary>
public record ApiError(string Code, string Message);

/// <summary>
/// Email signup. <c>DateOfBirth</c> (yyyy-MM-dd) is TRANSIENT: the server
/// computes the 21+ gate and persists only birth year + attestation timestamp
/// (ADR-010, Hard Rule 2).
/// </summary>
public record SignupRequest(
    string Email,
    string Password,
    string Handle,
    string DateOfBirth,
    string? TurnstileToken);

public record LoginRequest(string Email, string Password);

/// <summary>Post-OAuth DOB-capture step (ADR-011). Same DOB transience.</summary>
public record ExternalCompleteRequest(string Handle, string DateOfBirth);

public record HandleChangeRequest(string Handle);

/// <summary>The signed-in user, as the client is allowed to see itself.</summary>
public record MeResponse(
    Guid Id,
    string Handle,
    string Email,
    bool EmailVerified,
    DateTimeOffset? VerificationDeadline,
    bool CanRate,
    DateTimeOffset CreatedAt);

/// <summary>What the login/signup UI should offer.</summary>
public record AuthProvidersResponse(IReadOnlyList<string> External, string? TurnstileSiteKey);

/// <summary>Pending external sign-in awaiting the DOB step (no account yet).</summary>
public record ExternalPendingResponse(string Provider, string? Email);
