using System.Text.Json.Serialization;

namespace BarBrain.Api.Auth;

/// <summary>Server-side Cloudflare Turnstile verification (Sprint 2 spec).</summary>
public interface ITurnstileVerifier
{
    /// <summary>
    /// Verifies a client token. When no secret key is configured (local dev,
    /// CI, and until the founder creates the Turnstile site — HUMAN-CHECKLIST)
    /// verification passes with a logged warning so signup keeps working.
    /// </summary>
    Task<bool> VerifyAsync(string? token, CancellationToken ct = default);
}

public sealed class TurnstileVerifier(
    IHttpClientFactory httpClientFactory,
    IConfiguration config,
    ILogger<TurnstileVerifier> logger) : ITurnstileVerifier
{
    private const string VerifyUrl = "https://challenges.cloudflare.com/turnstile/v0/siteverify";

    public async Task<bool> VerifyAsync(string? token, CancellationToken ct = default)
    {
        var secret = config["Turnstile:SecretKey"];
        if (string.IsNullOrWhiteSpace(secret))
        {
            logger.LogWarning("Turnstile:SecretKey not configured — skipping bot check. Set it before public launch (HUMAN-CHECKLIST).");
            return true;
        }

        if (string.IsNullOrWhiteSpace(token))
            return false;

        try
        {
            var client = httpClientFactory.CreateClient("turnstile");
            using var response = await client.PostAsync(VerifyUrl, new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["secret"] = secret,
                ["response"] = token,
            }), ct);
            var result = await response.Content.ReadFromJsonAsync<SiteVerifyResponse>(ct);
            return result?.Success == true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Fail closed: if Cloudflare is unreachable we reject rather than
            // letting bots through while the check is down.
            logger.LogError(ex, "Turnstile siteverify call failed");
            return false;
        }
    }

    private sealed record SiteVerifyResponse([property: JsonPropertyName("success")] bool Success);
}
