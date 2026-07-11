# STATE — agent session handoff
> Agents: update this at the END of every session. Keep it short and factual.

## Current sprint
Sprint 6 — gamification + moderation + PWA polish (branch `sprint-6`, off
`main` after the Sprint 5 PR merged). Spec: docs/specs/sprint-6.md
(pre-existing). History: Sprint 5 (venues); data-beer-national (PR #11);
4.8 CI seed parity (PR #10); 4.7 clear-attribute verb (PR #9); 4.6 importer
v2 (PR #8); 4.5 rapid-rate (PR #7); 4 (PR #6); 3 (PR #5); 2 (PR #4); 1 (PR #3).
STILL OUTSTANDING from Sprint 1: the VPS bulk seed run per RUNBOOK.

## Founder adjudications (2026-07-10, recorded in docs/charter-v7-adjudication)
- Charter proposals **P1–P3 all APPROVED**, merged into charter v7 (founder
  holds the settled text; docs/project-charter.md now carries the
  adjudication record). ADR-022 amended founder-signed per P2; BRAND.md
  prohibited-language gains the proof/ABV-as-potency line.
- **beer.db REJECTED** (quality, not license) — moved to "Rejected sources"
  in DATA-SOURCES.md; RUNBOOK import block removed. NOTE: rejection is
  doc-enforced only — the quoted `"seed:beerdb"` tag stays in the registry
  (a build test pins it), so the products-import gate would still pass it
  and `import beerdb` never consulted the registry; fail-closing it for
  real is a small code+test change, unscheduled.
- **Live-catalog rec-quality eval verb: assigned to Sprint 7** (launch-gate
  trigger fired) — scoped in docs/specs/sprint-7.md; removed from Backlog.

## Done (this session)
- **Founder rulings captured (2026-07-10)**: badges are AMBER per
  DESIGN-REFERENCE Screen 7 (overriding the teal guess); "first drink added"
  = first wiki contribution (no user drink-add path exists); "Wildcard tried"
  via new `ratings.RecSection` (= the feed's own section keys), forward-only,
  no retroactive credit; streak = the digest's rolling-7-day-bucket
  definition, badges permanent once earned; venue variety excludes Home Bar
  (structural — check-ins only target public venues); badge display = profile
  tab + toast only; hide = distinct moderation-owned state; SVG icon pipeline
  ships wired-but-dormant on placeholder PNGs (HUMAN-CHECKLIST 14 is a
  founder to-do, not a blocker). "Corridor Cartographer" name approved.
- **Schema (`Sprint6GamificationModeration`, fully additive)**:
  badge_definitions (metric CHECK vocabulary IS the ADR-016 guardrail —
  only distinct-entity/weekly-streak metrics are expressible) + user_badges
  (unique per user+badge, SeenAt drives toasts); reports (typed FK trio,
  one OPEN per reporter+target); anomaly_flags (one OPEN per user+kind);
  moderation_actions audit log (append-only, deliberately no FKs);
  HiddenAt/HiddenBy pairs on ratings/venues/drinks; users
  ShadowLimitedAt/BannedAt/ModerationNote; ratings.RecSection.
- **Badges**: 15-badge launch set in seed/badges.json (file = source of
  truth, upserted at startup — new badge on an existing metric = config edit,
  no deploy); BadgeService evaluator (idempotent, race-safe, never throws
  into write paths) with inline hooks (rating/check-in/venue add/menu
  add+confirm/merge approve) + nightly heal; StreakMath extracted from the
  digest so digest and badges share ONE streak definition (also fixed a
  future-timestamp truncation quirk the new unit tests surfaced);
  gallery/unseen/seen API; new `menu_item_confirmed` event (Menu Keeper
  counts distinct confirmed listings — LastConfirmedAt keeps no per-user
  history).
- **Moderation**: report flow (public content only, 404-non-leak for private,
  rate-limited, dup-refused) → unified admin surface at /admin with tabs
  Merges (all three entity types — the Sprint 5 deferred restyle) / Reports /
  Anomalies / Audit; hide enforced across EVERY public read path (catalog
  search/browse, recs + popularity, venue nearby/page/menus, personalized
  menu, match loves, drink ratings) and reversible via unhide; shadow-limit
  and ban enforced on READ + both sign-in paths + a write-guard endpoint
  filter (ban also rotates the security stamp); nightly anomaly scan
  (z-score outliers, rapid-fire bursts — evidence for HUMAN review, never
  auto-action; pure math unit-tested); every decision (incl. merge
  approve/reject, hooked at the endpoint so MergeService stays CLI-pure)
  writes moderation_actions.
- **Provenance weighting**: public drink aggregate + CF co-rating data count
  only accounts ≥7d AND ≥5 latest ratings (flags
  `ratings.public_min_account_age_days` / `ratings.public_min_rating_count`).
  Read-time + nightly ⇒ graduation is AUTOMATIC — the spec's "recompute job"
  is unnecessary (noted in PR). Young accounts KEEP attribute-similarity
  matches; only their CF weight is withheld. Recent-ratings list stays
  visible (social content, not an aggregate) — provenance test pins this.
- **Rate limits**: flag-driven per-account write limits on ratings
  (limits.ratings_per_hour=120) + reports (limits.reports_per_day=20);
  Sprint 5 wiki limits unchanged.
- **PWA**: manifest completed (description/orientation/scope/maskable);
  published SW gains a network-first catalog/config API cache (catalog
  browse works offline; nothing personal cached); flag-gated install prompt
  (beforeinstallprompt + iOS hint) + offline banner; icon pipeline
  (infra/generate-icons.mjs + CI step) DORMANT until SVG masters land;
  CI Lighthouse PWA gate ≥0.9 pinned to Lighthouse 11.x (v12 REMOVED the
  PWA category — pin is deliberate), report as artifact.
- **Tests**: 63 pass locally (no Docker); new suites: StreakMath, anomaly
  math, badges.json brand-lint (BRAND.md prohibited-language stems) +
  ADR-016 metric pin; BadgeFlowTests, ProvenanceWeightingTests,
  ModerationFlowTests, MigrationFromSprint5Tests; e2e badges.spec.ts +
  moderation.spec.ts (screenshots for the acceptance criteria).
  NOTE: integration harness skips startup seeding — badge suites replay
  BadgeSeeder themselves.

## Deferred within Sprint 6 (noted in PR)
- Rating queue-and-sync while offline: the spec's own "stretch" — not built.
  Offline = browse + shell; writes need a connection (banner says so).
- Maskable icon is the placeholder 512 doubling via `purpose` until the SVG
  masters land; generate-icons.mjs prints the manifest-swap reminder.
- Admin auth is still the Sprint 0 token stub (real admin identity is
  outside every sprint spec so far); moderation_actions.Actor is a label.

## Doc inconsistency to flag (carried)
- Muted-text token: BRAND.md `--bb-text-muted` vs DESIGN-REFERENCE
  `--bb-muted` (alias in place; founder may standardize).
- Charter: P1–P3 adjudicated (approved 2026-07-10); docs/project-charter.md
  carries the adjudication record. Settled v7 text remains uncommitted —
  founder to paste it above the marker (spec + ADRs used as scope record).

## Environment note (this build machine)
Docker/Node absent: Testcontainers + Playwright suites authored-not-run
locally; CI runs them — CI green is the done gate. Verified locally: build
0 warnings; `dotnet test` 63 passed / 125 skipped.

## Backlog (unscheduled — revisit on a concrete trigger, not speculatively)
- beer.db rejection is docs-only; the registry has no rejected-source
  semantics. Trigger: next time importer code is touched, add a rejected
  flag to the embedded registry, loud refusal on import, flip the CI test
  accordingly.
- Events table has no user_id column (userId lives in jsonb Properties) —
  fine for audit, awkward for per-user analytics; if the Sprint 7 dashboard
  needs per-user event queries, consider an additive indexed column then.

## Blockers / needs founder
- HUMAN-CHECKLIST 14: SVG logo masters (wordmark, mark, single-node) +
  WOFF2 fonts into the repo — activates the icon pipeline; fonts also
  affect Lighthouse/brand fidelity.
- Carried: VERIFY backlog for beer-national ABVs (allagash/firestone/
  dogfish/anchor); VPS bulk seed run (RUNBOOK); HUMAN-CHECKLIST 6 (SMTP +
  physical address), 7–9 (OAuth/Turnstile creds); Gate C2 follow-ups.

## Next session should
- Sprint 6 PR (#13) is MERGED. Next: the Sprint 6 founder Gate (phone,
  ~10 min): earn a badge on a real account, file + action a report in
  /admin, install the PWA to the home screen. Approve → Sprint 7 kickoff
  (pre-launch hardening, spec now includes the live-catalog eval verb).
- Sprint 7 heads-up: JSON export must include badges (spec says so — the
  badge tables are ready for it); deletion flow interacts with
  moderation_actions' no-FK design (audit survives deletion, by design).
