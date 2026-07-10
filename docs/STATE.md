# STATE — agent session handoff
> Agents: update this at the END of every session. Keep it short and factual.

## Current sprint
Sprint 4.5 — Rapid Rate mini-sprint (branch `sprint-4.5`, off `main` which now
has Sprint 4 via PR #6). Spec: docs/specs/sprint-4.5.md. A fast browse-and-rate
surface over the existing catalog + existing rating API — cold-start friction
fix found via dogfooding. Sprint 4 merged (PR #6); Sprint 3 (PR #5); Sprint 2
(PR #4); Sprint 1 (PR #3). STILL OUTSTANDING from Sprint 1: the VPS bulk seed
run per RUNBOOK.

## Done (Sprint 4.5)
- **Docs**: sprint-4.5 spec (objective, binding out-of-scope incl. NO volume
  gamification, design decisions applied). No ADR — no architecture decision
  (one read endpoint composed from existing patterns).
- **API** (read-only addition; rating write path untouched):
  `GET /api/rapidrate/drinks` (authenticated) — paged browse with `category`,
  `unratedOnly` (no rating row by me, any visibility/origin — QuizService
  semantic), `sort=popular|name` (popular ≡ most-rated = count of latest public
  ratings, computed on the fly like RecommendationService), per-row
  `PublicRatingCount` + `MyLatestValue`. New `RapidRateItem` contract,
  `RapidRateQueryService`, `RapidRateEndpoints`. One flag:
  `rapidrate.page_size` (default 20). NO schema change, NO migration.
- **Web**: `/rapid-rate` page — category pills, Haven't-rated toggle,
  Popular/A–Z sort, Show-more paging (client de-dupes across unrated-window
  drift; rated cards stay in place with a teal "Noted"). Cards rate inline via
  the EXISTING POST /api/ratings (origin `user`, `home_bar` default); per-card
  saving/saved/error states; secondary Private toggle (pre-rating → next post
  is private; post-save → PATCH visibility) never blocks flow. CanRate=false →
  notice card, stars read-only. Entry points: doorway card on Search (teal —
  it recommends a flow, not a drink) + secondary CTA on onboarding-done.
  NO new bottom-nav item (founder decision — design-ref nav inventory holds).
- **Tests**: RapidRateBrowseTests (9 integration tests: auth, popular ordering
  counts latest-public only, private/MyLatestValue, unratedOnly across
  visibility+origin, category/sort validation, deterministic paging, page-size
  flag, merged/private exclusion). rapid-rate.spec.ts e2e: flow proof (8 inline
  ratings + private 9th + unrated filter + banned-copy grep) and a same-run
  timing comparison old-flow vs rapid with a `timing-summary.md` CI artifact
  (asserts 8 ratings < 60s AND rapid per-drink < old per-drink; no ratio
  assert = no flake).

## Decisions made within spec bounds (log)
- Popular ≡ most-rated (one sort option), on-the-fly count — materialize only
  if it ever shows up in a profile trace.
- Rapid ratings reuse origin `user` — the brief is explicit that a rapid-rate
  rating is just a rating; no provenance change.
- Re-tap = re-rate = new append-only row (platform semantic); no undo on the
  surface — undo lives in the journal.
- unratedOnly page drift accepted: filter applies at fetch time; client
  de-dupes appended pages by drink id; skipped drinks reappear next visit.
- No kill-switch flag: the surface is not phase-dependent (Hard Rule 10 covers
  modes/thresholds/prompts); the one tunable (page size) is a flag.
- Per-card error slots (deviation from the quiz's single global error) — with
  ~20 cards on screen the error must sit next to the card it belongs to.

## Doc inconsistency to flag (carried)
- Muted-text token: BRAND.md `--bb-text-muted` vs DESIGN-REFERENCE `--bb-muted`
  (alias in place; founder may standardize).

## Environment note (this build machine)
Docker/Node absent: authored-not-run locally = RapidRateBrowseTests +
rapid-rate.spec.ts (plus all pre-existing Testcontainers/Playwright suites).
Verified locally: `dotnet build` 0 warnings, `dotnet test` 38 passed / 95
skipped absent Docker. CI runs everything for real.

## Blockers / needs founder
- Carried from Sprint 4: HUMAN-CHECKLIST 6 (digest physical address + SMTP),
  Gate C2 real-user follow-ups when users exist.
- Carried: VPS bulk seed run (RUNBOOK); HUMAN-CHECKLIST 7–9 (Google/Apple/
  Turnstile creds), 14 (fonts/logo assets), charter proposals P1–P3, beer.db call.

## Next session should
After sprint-4.5 CI green + merge: Sprint 5 (venues — check-in, personalized
menus; ADR-015). Consider surfacing the rapid-rate doorway on the feed's empty
states too if dogfooding says Search isn't discoverable enough.
