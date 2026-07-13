# ANALYTICS — the admin retention dashboard's SQL (Sprint 7, ADR-017)

> First-party only: every number comes from OUR Postgres (events + ratings +
> check-ins + users). No third-party trackers exist. The dashboard is
> aggregate-only by construction — no per-user drill-down endpoint exists
> (Hard Rule 6 posture). Source of truth for these queries:
> `src/api/Analytics/AnalyticsService.cs` — keep this file in sync with it.

"Active" = **rated or checked in**. Deliberately not page views: the metric
must be honest about product use, not traffic.

## Signups / activation

Signups are activated accounts (`users.ActivatedAt IS NOT NULL`), windowed by
`CreatedAt`. Activation rate is events-funnel based, so it survives account
deletion (events are kept, scrubbed of the user link):

```sql
SELECT count(*) FILTER (WHERE "Name" = 'activation')::float
     / nullif(count(*) FILTER (WHERE "Name" = 'signup'), 0) * 100
FROM events;
```

Today signup and activation coincide on every path (an account only exists
once the 21+ gate passes), so this reads 100% — it exists to catch that ever
changing.

## Day-N cohort retention (D1 / D7 / D30)

Of activated accounts at least N+1 days old, the share with any activity on
day N after signup:

```sql
-- eligible
SELECT count(*) FROM users u
WHERE u."ActivatedAt" IS NOT NULL
  AND u."CreatedAt" <= now() - make_interval(days => :n + 1);

-- retained
SELECT count(DISTINCT u."Id")
FROM users u
JOIN (SELECT "CreatedByUserId" AS uid, "CreatedAt" AS at FROM ratings
      UNION ALL
      SELECT "UserId", "CreatedAt" FROM checkins) a
  ON a.uid = u."Id"
 AND a.at >= u."CreatedAt" + make_interval(days => :n)
 AND a.at <  u."CreatedAt" + make_interval(days => :n + 1)
WHERE u."ActivatedAt" IS NOT NULL
  AND u."CreatedAt" <= now() - make_interval(days => :n + 1);
```

**Decision numbers (PRD, surfaced as flags):** D30 "well under"
`analytics.d30_kill_pct` (3%) = kill/pivot conversation; at or above
`analytics.d30_excellent_pct` (7%) = accelerate.

## WAU

Distinct users with a rating or check-in in the trailing 7 days:

```sql
SELECT count(DISTINCT uid) FROM (
  SELECT "CreatedByUserId" AS uid FROM ratings  WHERE "CreatedAt" >= now() - interval '7 days'
  UNION
  SELECT "UserId"          FROM checkins WHERE "CreatedAt" >= now() - interval '7 days') w;
```

## Weekly pace (ratings/week, check-ins/week)

Last 8 ISO weeks, oldest first (same shape for `checkins`):

```sql
SELECT date_trunc('week', "CreatedAt")::date AS week, count(*)
FROM ratings
WHERE "CreatedAt" >= now() - interval '56 days'
GROUP BY 1 ORDER BY 1;
```

## Data-asset metrics (PRD moat instrumentation)

```sql
-- ratings per active user (latest rows only — ADR-012)
SELECT coalesce(avg(cnt), 0)
FROM (SELECT count(*) AS cnt FROM ratings WHERE "IsLatest" GROUP BY "CreatedByUserId") s;

-- % of raters active in 2+ categories (the cross-category moat metric)
WITH per_user AS (
  SELECT r."CreatedByUserId" AS uid, count(DISTINCT d."Category") AS cats
  FROM ratings r JOIN drinks d ON d."Id" = r."DrinkId"
  WHERE r."IsLatest" GROUP BY 1)
SELECT CASE WHEN count(*) = 0 THEN 0
       ELSE 100.0 * count(*) FILTER (WHERE cats >= 2) / count(*) END
FROM per_user;
```
