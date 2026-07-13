namespace BarBrain.Shared.Contracts;

/// <summary>
/// Admin retention dashboard (Sprint 7, ADR-017 — first-party analytics only).
/// Kill/accelerate thresholds ride along so the dashboard shows the PRD's
/// decision numbers next to the live values (flags, Hard Rule 10).
/// </summary>
public sealed record AdminAnalyticsResponse(
    DateTimeOffset GeneratedAt,
    int SignupsTotal,
    int Signups7d,
    int Signups30d,
    double ActivationRatePct,
    RetentionCohort D1,
    RetentionCohort D7,
    RetentionCohort D30,
    int Wau,
    IReadOnlyList<WeeklyCount> RatingsPerWeek,
    IReadOnlyList<WeeklyCount> CheckinsPerWeek,
    double RatingsPerActiveUser,
    double MultiCategoryPct,
    int D30KillPct,
    int D30ExcellentPct);

/// <summary>Day-N retention: of accounts old enough, how many were active that day.</summary>
public sealed record RetentionCohort(int Eligible, int Retained, double Pct);

public sealed record WeeklyCount(DateTime WeekStart, int Count);
