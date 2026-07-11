using System.Text.Json;
using BarBrain.Api.Data;
using BarBrain.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BarBrain.Api.Badges;

/// <summary>
/// Upserts badge definitions from <c>seed/badges.json</c> on startup. Unlike
/// the flags seeder (insert-only, operator values win), the FILE is the source
/// of truth here: copy/threshold/active changes in badges.json take effect on
/// the next deploy — launching or retiring a badge is a config edit, no code.
/// Definitions are never deleted (user_badges FK); retire via active:false.
/// </summary>
public static class BadgeSeeder
{
    private sealed record SeedBadge(
        string Slug, string Name, string Description, string Icon,
        string DisplayGroup, string Metric, int Threshold,
        int SortOrder, bool? Active);

    public static async Task SeedAsync(
        AppDbContext db, string contentRootPath, ILogger logger, CancellationToken ct = default)
    {
        var path = Path.Combine(contentRootPath, "seed", "badges.json");
        if (!File.Exists(path))
        {
            logger.LogWarning("Badge seed file not found at {Path}; skipping seed.", path);
            return;
        }

        await using var stream = File.OpenRead(path);
        var seeds = await JsonSerializer.DeserializeAsync<List<SeedBadge>>(
            stream, new JsonSerializerOptions(JsonSerializerDefaults.Web), ct) ?? [];

        // Malformed definitions fail the seed loudly — a bad metric would
        // otherwise be refused by the CHECK constraint mid-insert anyway.
        foreach (var seed in seeds)
        {
            if (!BadgeMetric.IsValid(seed.Metric))
                throw new InvalidOperationException(
                    $"badges.json: unknown metric '{seed.Metric}' on badge '{seed.Slug}'.");
            if (!BadgeGroup.All.Contains(seed.DisplayGroup))
                throw new InvalidOperationException(
                    $"badges.json: unknown displayGroup '{seed.DisplayGroup}' on badge '{seed.Slug}'.");
            if (seed.Threshold < 1)
                throw new InvalidOperationException(
                    $"badges.json: threshold must be >= 1 on badge '{seed.Slug}'.");
        }

        var existing = await db.BadgeDefinitions.ToDictionaryAsync(b => b.Slug, ct);
        var now = DateTimeOffset.UtcNow;
        int added = 0, updated = 0;

        foreach (var seed in seeds)
        {
            if (existing.TryGetValue(seed.Slug, out var row))
            {
                if (row.Name == seed.Name && row.Description == seed.Description
                    && row.Icon == seed.Icon && row.DisplayGroup == seed.DisplayGroup
                    && row.Metric == seed.Metric && row.Threshold == seed.Threshold
                    && row.SortOrder == seed.SortOrder && row.Active == (seed.Active ?? true))
                    continue;

                row.Name = seed.Name;
                row.Description = seed.Description;
                row.Icon = seed.Icon;
                row.DisplayGroup = seed.DisplayGroup;
                row.Metric = seed.Metric;
                row.Threshold = seed.Threshold;
                row.SortOrder = seed.SortOrder;
                row.Active = seed.Active ?? true;
                row.UpdatedAt = now;
                updated++;
            }
            else
            {
                db.BadgeDefinitions.Add(new BadgeDefinition
                {
                    Slug = seed.Slug,
                    Name = seed.Name,
                    Description = seed.Description,
                    Icon = seed.Icon,
                    DisplayGroup = seed.DisplayGroup,
                    Metric = seed.Metric,
                    Threshold = seed.Threshold,
                    SortOrder = seed.SortOrder,
                    Active = seed.Active ?? true,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
                added++;
            }
        }

        if (added + updated > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Badge seed: {Added} added, {Updated} updated.", added, updated);
        }
    }
}
