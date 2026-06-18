# Sprint 6 — Gamification & Moderation
**Objective:** retention mechanics live (ethically), data quality defended,
PWA feels like an app.

## In scope
- Badge framework (data-driven): badge definitions in config/DB (criteria DSL:
  metric, threshold, scope), evaluator in nightly batch + on-event for instant
  awards; badge gallery UI; award toasts; events instrumented.
- Launch badge set (~15): breadth (styles ×5/×15, all-3-categories), exploration
  (first/5th/15th Wildcard tried), venue variety (3/10 distinct venues),
  contribution (first drink added, 5 menu confirms, first accepted merge),
  weekly streak (2/5/10 weeks). HARD RULE ADR-016 enforced: zero volume/
  frequency rewards; copy sweep in review.
- Weekly streak: "logged ≥1 rating this calendar week"; freeze item NOT included
  (no pressure mechanics).
- Provenance weighting live: public aggregates + CF include ratings only from
  accounts ≥7 days AND ≥5 ratings (config); own profile unaffected; recompute
  job handles graduation.
- Moderation admin: unified queues (drink merges, venue merges, reports),
  report flow on ratings/venues/drinks, anomaly flags (z-score outliers,
  rapid-fire patterns) surfaced for review, shadow-limit + ban actions, audit log.
- Rate limits hardened (per-endpoint, per-account, config).
- PWA polish: manifest, icons/splash generated at build from the SVG mark per
  docs/BRAND.md (192/512/maskable; single-node variant for favicon), install
  prompt, offline app shell
  (catalog browse cached, rating queue-and-sync when offline — stretch),
  Lighthouse PWA pass ≥90.

## Acceptance criteria
- Badge awarded e2e (rate 5 styles → breadth badge toast + gallery; screenshots).
- A 0-day-old account's ratings provably excluded from public average (test),
  included after threshold graduation (time-travel test).
- Report → appears in admin queue → action (hide) reflected publicly.
- No badge/copy references consumption volume (checklist in PR).
- Lighthouse PWA ≥90 artifact in CI.

## Out of scope
Leaderboards (cut — fast-follow), XP/points (cut), photos (cut), push.

## Gate (founder, phone, ~10 min)
Earn a badge on your real account; file a report and action it in admin; install
the PWA to your home screen — does it feel like an app? Approve → Sprint 7.
