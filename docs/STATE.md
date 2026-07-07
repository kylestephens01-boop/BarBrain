# STATE — agent session handoff
> Agents: update this at the END of every session. Keep it short and factual.

## Current sprint
Sprint 3 — Palate Engine + Onboarding Quiz + Sectioned Feed (branch `sprint-3`,
PR opened into main; Gate C1 review pending — the golden-set eval harness in
CI is the gate). Sprint 2 merged via PR #4 (Gate B cleared).
STILL OUTSTANDING from Sprint 1: the VPS bulk seed run per RUNBOOK — golden-set
tests are seed-independent by design, but the founder's REAL-catalog rec-feel
review (Gate C1 phone task) wants the seeded catalog first.

## Done (Sprint 3)
- **ADR-027 recorded + spec amended**: cross-category bridge recs promoted
  from stretch to REQUIRED (moat mechanic); CF stays deferred (ADR-025 — no
  CF tables/matrices; append-only history is the upgrade path); golden-set
  eval gates rec regressions in CI; min-5 profile floor became a confidence
  TIER so confidence-adaptive feeds can exist.
- **Schema** (`Sprint3Palate`, additive; Origin backfilled 'user'):
  ratings.Origin ('user'|'quiz' CHECK), user_category_interests,
  user_palate_profiles (PreferenceVector 8d for recs, CentroidVector 8d for
  the radar, BridgeVector 6d for cross-category — per (user, category)).
- **PalateProfileService**: preference = (rating − user-mean)-weighted drink
  vectors L2-normalized (identical-ratings fallbacks), centroid over liked
  drinks, bridge on shared dims; pure Compute() for tests; recompute after
  every rating write/delete; flag-gated nightly batch heals catalog-side
  vector drift.
- **RecommendationService**: HNSW-cosine pool → rank-banded sections (Alley/
  Stretch/Wildcard) + MMR-lite diversity + smoothed popularity prior +
  per-(user, UTC-day) deterministic wildcard seed. Confidence-adaptive shape
  (cold: 4-item sections, 6 wildcards, no match %). Bridge picks injected
  into Stretch per (source, target) pair, tagged cross_category, reason names
  the source palate. EVERY rec carries its "because" (attribute names ride
  along for UI emphasis). "Loved by your matches" = ComingSoon slot (Sprint 4).
  All knobs are flags (Hard Rule 10).
- **Quiz**: /onboarding interest gate → per-claimed-category staples quiz →
  radar tease → feed. Staples lists in settings flags (JSON, editable without
  deploy; unresolvable entries drop out): beer/whiskey by product (corridor
  defaults), wine BY VARIETAL (style code → representative catalog drink so
  quiz ratings stay REAL ratings, origin='quiz'). "Haven't tried" writes
  NOTHING. quiz_completed event (ADR-017). Signup now routes to /onboarding
  (skip link preserves the fast Gate B path).
- **Web**: onboarding gate/quiz/done pages (design-ref Screens 1–3),
  RadarChart component (8 spokes, synapse), real /feed (Screen 4: tabs incl.
  amber Wildcard + disabled matches slot, category pills, teal reasons/match%
  vs amber drink accents), Profile Palate tab (radar + teal bars + "N more
  ratings" prompts).
- **Golden-set eval harness** (`Category=RecEval`, own pgvector container,
  fixed seed + frozen clock): 14 personas (12 spec'd + noiseless golden +
  2-rating cold) → real profile job + real engine. Asserted: precision@10
  ≥ 0.70 (env-overridable), LOO hit-rate@10 ≥ 0.25, wildcard farther than
  alley per persona, determinism, golden top-pick == independent brute-force,
  every-rec-has-a-because, confidence-adaptive shape, matches-slot deferred,
  AND the moat test: whiskey-only peat lover surfaces smoky BEER via the
  bridge. Report → TestResults/rec-eval-report.md (CI artifact, pass or fail).
- **e2e**: onboarding-feed.spec (gate → quiz w/ skip → radar → 3 sections
  with reasons → journal shows quiz ratings → palate tab), Sprint 2 specs
  updated for the /onboarding landing.

## In progress
(nothing — Gate C1 review is the blocker by design)

## Decisions made within spec bounds (log)
- Wine varietal quiz items resolve to a representative drink (most publicly
  rated, then name) — keeps "quiz ratings are real ratings" literal; a
  varietal with no catalog drink drops out gracefully.
- Bridge picks live in Stretch (adjacent exploration) and replace tail picks
  rather than growing the section; cross-category MMR pairs get no
  similarity penalty (different geometries).
- Cold profiles show no match % (an early guess shouldn't wear a percent);
  eval asserts this.
- Wildcard determinism is per (user, UTC day) — stable feed within a day,
  fresh tomorrow; the eval freezes the clock.
- Eval thresholds read env vars (RECEVAL_PRECISION_MIN, RECEVAL_LOO_HITRATE_MIN)
  with spec defaults — "thresholds in config" without a settings-table dep in
  tests.
- Attribute bridge naming: bridge dims 0–5 == category dims 0–5 (verified in
  the seed; relied on for bridge reason copy).

## Doc inconsistency to flag (carried)
- Muted-text token: BRAND.md `--bb-text-muted` vs DESIGN-REFERENCE `--bb-muted`
  (alias in place; founder may standardize).

## Environment note (this build machine)
Docker/Node absent: authored-not-run locally = Testcontainers suites (incl.
the new RecEval collection) + Playwright. Verified locally: `dotnet build`
(0 warnings), `dotnet test` (35 passed / 75 skipped absent Docker). CI runs
everything for real.

## Blockers / needs founder
- **Gate C1 review**: skim the CI rec-eval report artifact (per-persona
  table); on the dev URL, make a deliberate-persona account (e.g. rate like
  a stout lover), check the feed agrees, read 5 "because" lines. Real-catalog
  feel needs the VPS bulk seed run (RUNBOOK) first.
- Radar-matches-intuition check for founder-known accounts (gate task).
- Carried: HUMAN-CHECKLIST 6–9 (SMTP/Google/Apple/Turnstile creds), 14
  (fonts/logo/Tabler webfont assets), charter proposals P1–P3, beer.db call.

## Next session should
After Gate C1 + merge: Sprint 4 (matching + "Loved by your matches" — the
feed slot and ADR-014 are waiting; density-weighted co-rating agreement can
now read the append-only history). If the founder runs the bulk seed, spot-
check feed quality on the real catalog and tune flags (bands, popularity
weight) — they're all settings, no deploy needed.
