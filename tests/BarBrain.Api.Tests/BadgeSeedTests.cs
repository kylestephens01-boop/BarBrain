using System.Text.Json;
using BarBrain.Api.Data.Entities;
using BarBrain.Api.Tests.Integration;

namespace BarBrain.Api.Tests;

/// <summary>
/// Sanity + brand lint over seed/badges.json (Sprint 6). No Docker needed.
///
/// The brand lint is the PR checklist's teeth: badge copy is user-facing and
/// the BRAND.md prohibited-language list is binding (Hard Rule 11; ADR-016) —
/// no volume/quantity framing, no intoxication references, no "drink more".
/// </summary>
public class BadgeSeedTests
{
    private sealed record SeedBadge(
        string Slug, string Name, string Description, string Icon,
        string DisplayGroup, string Metric, int Threshold, int SortOrder);

    private static readonly Lazy<List<SeedBadge>> Badges = new(() =>
        JsonSerializer.Deserialize<List<SeedBadge>>(
            File.ReadAllText(Path.Combine(CatalogTestHarness.SeedDir, "badges.json")),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!);

    // BRAND.md prohibited-language list, distilled to lintable stems. Word
    // matching is case-insensitive substring over name+description.
    private static readonly string[] ProhibitedStems =
    [
        "crush", "binge", "power hour", "power drinker", "chug", "shot",
        "drunk", "buzz", "tipsy", "wasted", "hammered", "intoxicat",
        "drink more", "another round", "keep drinking", "one more",
        "healthy", "wellness", "good for you",
    ];

    [Fact]
    public void Seed_parses_and_has_the_launch_set()
    {
        Assert.Equal(15, Badges.Value.Count);
        Assert.Equal(Badges.Value.Count, Badges.Value.Select(b => b.Slug).Distinct().Count());
    }

    [Fact]
    public void Every_metric_and_group_is_in_the_closed_vocabulary()
    {
        Assert.All(Badges.Value, b =>
        {
            Assert.True(BadgeMetric.IsValid(b.Metric), $"{b.Slug}: unknown metric '{b.Metric}'");
            Assert.Contains(b.DisplayGroup, BadgeGroup.All);
            Assert.True(b.Threshold >= 1, $"{b.Slug}: threshold must be >= 1");
        });
    }

    [Fact]
    public void No_badge_copy_violates_the_brand_prohibited_language_list()
    {
        foreach (var badge in Badges.Value)
        {
            var copy = $"{badge.Name} {badge.Description}".ToLowerInvariant();
            foreach (var stem in ProhibitedStems)
                Assert.False(copy.Contains(stem, StringComparison.OrdinalIgnoreCase),
                    $"Badge '{badge.Slug}' copy contains prohibited language: '{stem}' (BRAND.md / Hard Rule 11)");
        }
    }

    [Fact]
    public void No_metric_can_express_volume_or_frequency()
    {
        // ADR-016's guardrail is the vocabulary itself: every metric is a
        // distinct-entity or weekly-streak count. If someone adds a metric,
        // this list forces the conversation.
        Assert.Equal(
            [
                "distinct_styles_rated", "distinct_categories_rated",
                "wildcard_distinct_drinks", "distinct_venues_checked_in",
                "wiki_contributions", "menu_confirms",
                "accepted_merge_contributions", "weekly_streak_weeks",
            ],
            BadgeMetric.All);
    }
}
