using BarBrain.Api.Data;
using BarBrain.Api.Data.Entities;
using BarBrain.Shared.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace BarBrain.Api.Settings;

/// <summary>
/// Caches the whole settings table as one snapshot under <see cref="CacheKey"/>
/// with a short TTL. The table is tiny (a handful of flags), so a single cached
/// dictionary keeps reads allocation-light and avoids per-key DB round-trips.
/// Writes invalidate the snapshot immediately.
/// </summary>
public sealed class SettingsService(AppDbContext db, IMemoryCache cache) : ISettingsService
{
    public const string CacheKey = "settings:snapshot";
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

    private async Task<IReadOnlyDictionary<string, Setting>> GetSnapshotAsync(CancellationToken ct)
    {
        if (cache.TryGetValue(CacheKey, out IReadOnlyDictionary<string, Setting>? snapshot) && snapshot is not null)
            return snapshot;

        var rows = await db.Settings.AsNoTracking().ToListAsync(ct);
        snapshot = rows.ToDictionary(s => s.Key, StringComparer.Ordinal);
        cache.Set(CacheKey, snapshot, Ttl);
        return snapshot;
    }

    public async Task<string?> GetStringAsync(string key, CancellationToken ct = default)
    {
        var snapshot = await GetSnapshotAsync(ct);
        return snapshot.TryGetValue(key, out var s) ? s.Value : null;
    }

    public async Task<string> GetStringAsync(string key, string fallback, CancellationToken ct = default)
        => await GetStringAsync(key, ct) ?? fallback;

    public async Task<bool> GetBoolAsync(string key, bool fallback, CancellationToken ct = default)
    {
        var raw = await GetStringAsync(key, ct);
        if (raw is null) return fallback;
        if (bool.TryParse(raw, out var b)) return b;
        return raw.Trim() switch
        {
            "1" or "yes" or "on" => true,
            "0" or "no" or "off" => false,
            _ => fallback,
        };
    }

    public async Task<int> GetIntAsync(string key, int fallback, CancellationToken ct = default)
    {
        var raw = await GetStringAsync(key, ct);
        return int.TryParse(raw, out var i) ? i : fallback;
    }

    public async Task<IReadOnlyList<SettingDto>> GetAllAsync(CancellationToken ct = default)
    {
        var snapshot = await GetSnapshotAsync(ct);
        return snapshot.Values
            .OrderBy(s => s.Key, StringComparer.Ordinal)
            .Select(ToDto)
            .ToList();
    }

    public async Task<SettingDto> SetAsync(string key, string value, CancellationToken ct = default)
    {
        var existing = await db.Settings.FirstOrDefaultAsync(s => s.Key == key, ct);
        if (existing is null)
        {
            existing = new Setting { Key = key, Value = value, UpdatedAt = DateTimeOffset.UtcNow };
            db.Settings.Add(existing);
        }
        else
        {
            existing.Value = value;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        cache.Remove(CacheKey); // flip takes effect on the next read — no redeploy
        return ToDto(existing);
    }

    private static SettingDto ToDto(Setting s) => new(s.Key, s.Value, s.Description, s.UpdatedAt);
}
