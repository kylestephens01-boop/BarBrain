namespace BarBrain.Api.Data.Entities;

/// <summary>
/// Closed vocabularies for Sprint 6 (badges + moderation), CHECK-enforced in
/// <see cref="Data.AppDbContext"/> like every other vocabulary (ADR-026).
/// </summary>
/// <summary>
/// Feed-section provenance on a rating. The values ARE the feed's own section
/// keys (RecommendationService.Section*) — one vocabulary, no mapping layer,
/// so the client passes FeedSection.Key straight through when a rating
/// originates from a rec card.
/// </summary>
public static class RecSection
{
    public const string Alley = "up_your_alley";
    public const string Stretch = "stretch_a_little";
    public const string Wildcard = "wildcard";
    public const string Matches = "loved_by_your_matches";

    public static readonly string[] All = [Alley, Stretch, Wildcard, Matches];
    public static bool IsValid(string value) => All.Contains(value);
}

public static class ReportEntityType
{
    public const string Rating = "rating";
    public const string Venue = "venue";
    public const string Drink = "drink";

    public static readonly string[] All = [Rating, Venue, Drink];
    public static bool IsValid(string value) => All.Contains(value);
}

public static class ReportReason
{
    public const string Inaccurate = "inaccurate";
    public const string Spam = "spam";
    public const string Offensive = "offensive";
    public const string Other = "other";

    public static readonly string[] All = [Inaccurate, Spam, Offensive, Other];
    public static bool IsValid(string value) => All.Contains(value);
}

public static class ReportStatus
{
    public const string Open = "open";
    public const string Actioned = "actioned";
    public const string Dismissed = "dismissed";
}

public static class AnomalyKind
{
    public const string RatingZScoreOutlier = "rating_zscore_outlier";
    public const string RapidFire = "rapid_fire";
}

public static class AnomalyStatus
{
    public const string Open = "open";
    public const string Cleared = "cleared";
    public const string Actioned = "actioned";
}

/// <summary>
/// Audit-log action vocabulary. Adding an action is a migration — deliberate:
/// the audit trail's meaning should never drift silently.
/// </summary>
public static class ModerationActionKind
{
    public const string MergeApproved = "merge_approved";
    public const string MergeRejected = "merge_rejected";
    public const string ReportActioned = "report_actioned";
    public const string ReportDismissed = "report_dismissed";
    public const string ContentHidden = "content_hidden";
    public const string ContentUnhidden = "content_unhidden";
    public const string ShadowLimited = "shadow_limited";
    public const string ShadowCleared = "shadow_cleared";
    public const string Banned = "banned";
    public const string Unbanned = "unbanned";
    public const string AnomalyCleared = "anomaly_cleared";

    public static readonly string[] All =
    [
        MergeApproved, MergeRejected, ReportActioned, ReportDismissed,
        ContentHidden, ContentUnhidden, ShadowLimited, ShadowCleared,
        Banned, Unbanned, AnomalyCleared,
    ];
}

public static class BadgeGroup
{
    public const string Breadth = "breadth";
    public const string Exploration = "exploration";
    public const string Venues = "venues";
    public const string Contribution = "contribution";
    public const string Streak = "streak";

    public static readonly string[] All = [Breadth, Exploration, Venues, Contribution, Streak];
}

/// <summary>
/// Badge criteria metrics (the DSL's verb). Every metric is a DISTINCT-entity
/// or weekly-streak count — never a consumption volume or frequency (ADR-016;
/// Hard Rule 4). Adding a metric = code (an evaluator) + this vocabulary;
/// adding a BADGE on an existing metric = a badges.json edit, no deploy.
/// </summary>
public static class BadgeMetric
{
    public const string DistinctStylesRated = "distinct_styles_rated";
    public const string DistinctCategoriesRated = "distinct_categories_rated";
    public const string WildcardDistinctDrinks = "wildcard_distinct_drinks";
    public const string DistinctVenuesCheckedIn = "distinct_venues_checked_in";
    public const string WikiContributions = "wiki_contributions";
    public const string MenuConfirms = "menu_confirms";
    public const string AcceptedMergeContributions = "accepted_merge_contributions";
    public const string WeeklyStreakWeeks = "weekly_streak_weeks";

    public static readonly string[] All =
    [
        DistinctStylesRated, DistinctCategoriesRated, WildcardDistinctDrinks,
        DistinctVenuesCheckedIn, WikiContributions, MenuConfirms,
        AcceptedMergeContributions, WeeklyStreakWeeks,
    ];
    public static bool IsValid(string value) => All.Contains(value);
}
