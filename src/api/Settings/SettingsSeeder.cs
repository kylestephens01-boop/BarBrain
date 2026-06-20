using System.Text.Json;
using BarBrain.Api.Data;
using BarBrain.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BarBrain.Api.Settings;

/// <summary>
/// Seeds feature flags from <c>seed/feature-flags.json</c> on startup. Only
/// INSERTS keys that don't exist yet — never overwrites a value an operator has
/// flipped, so flag changes survive restarts/redeploys (ADR-006).
/// </summary>
public static class SettingsSeeder
{
    private sealed record SeedFlag(string Key, string Value, string? Description);

    public static async Task SeedAsync(AppDbContext db, string contentRootPath, ILogger logger, CancellationToken ct = default)
    {
        var path = Path.Combine(contentRootPath, "seed", "feature-flags.json");
        if (!File.Exists(path))
        {
            logger.LogWarning("Feature-flag seed file not found at {Path}; skipping seed.", path);
            return;
        }

        await using var stream = File.OpenRead(path);
        var flags = await JsonSerializer.DeserializeAsync<List<SeedFlag>>(
            stream,
            new JsonSerializerOptions(JsonSerializerDefaults.Web),
            ct) ?? [];

        var existingKeys = await db.Settings.Select(s => s.Key).ToHashSetAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var added = 0;

        foreach (var flag in flags)
        {
            if (existingKeys.Contains(flag.Key))
                continue; // preserve operator-set values

            db.Settings.Add(new Setting
            {
                Key = flag.Key,
                Value = flag.Value,
                Description = flag.Description,
                UpdatedAt = now,
            });
            added++;
        }

        if (added > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Seeded {Count} new feature flag(s).", added);
        }
    }
}
