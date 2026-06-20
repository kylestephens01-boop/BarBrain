using BarBrain.Shared.Contracts;

namespace BarBrain.Api.Settings;

/// <summary>
/// Typed, cached access to DB-backed feature flags (ADR-006). Reads come from a
/// short-TTL in-memory snapshot; writes upsert and invalidate the snapshot so a
/// flag flip takes effect without a redeploy.
/// </summary>
public interface ISettingsService
{
    Task<string?> GetStringAsync(string key, CancellationToken ct = default);
    Task<string> GetStringAsync(string key, string fallback, CancellationToken ct = default);
    Task<bool> GetBoolAsync(string key, bool fallback, CancellationToken ct = default);
    Task<int> GetIntAsync(string key, int fallback, CancellationToken ct = default);

    Task<IReadOnlyList<SettingDto>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Upsert a setting and invalidate the cache. Returns the stored row.</summary>
    Task<SettingDto> SetAsync(string key, string value, CancellationToken ct = default);
}
