# Sprint 4.5 — Rapid Rate (mini-sprint)
**Objective:** a fast browse-and-rate surface so a user can teach the engine many
drinks they already know in one sitting — no per-drink search-and-navigate. This
attacks cold-start directly: matching, recs, and "Loved by your matches" all need
palate density the old one-at-a-time flow discourages. Gate = e2e proof (with
screenshots + timing artifact) that rating 8+ drinks inline is dramatically
faster than the search-per-drink flow.

## In scope
- One authenticated READ endpoint: `GET /api/rapidrate/drinks` — paged browse of
  the existing public catalog with `category` filter, `unratedOnly` filter
  ("haven't rated yet" = no rating row by me, any visibility/origin — the
  QuizService semantic), `sort=popular|name`, and per-row `PublicRatingCount` +
  `MyLatestValue` (prefill / "your latest" caption). New `RapidRateItem` contract;
  response is the existing `PagedResult<T>`.
- `/rapid-rate` page (Blazor): scrollable card list, inline half-step star
  rating per card (reuses `StarRating`), category pills, "Haven't rated" toggle,
  Popular/A–Z sort, "Show more" paging. Per-card saving/saved/error states.
  Secondary per-card Private toggle (pre-rating → next POST is private;
  post-save → PATCH visibility) — never blocks the rating flow.
- Writes go through the EXISTING rating pipeline untouched: POST `/api/ratings`
  with origin `user`, location `home_bar` (auto-created at signup), visibility
  public by default. A rapid-rate rating is just a rating — same append-only
  history, same palate recompute, same events.
- Entry points (NO new bottom-nav item — founder decision, design reference nav
  stays as-is): a "Rapid rate" affordance at the top of Search, and a secondary
  CTA on the onboarding-done page.
- One flag: `rapidrate.page_size` (server default page size). No kill switch —
  the surface is not phase-dependent.
- Tests: integration coverage of the new query path (ordering, filters, paging,
  auth, visibility rules); Playwright e2e per the gate below.

## Acceptance criteria
- A signed-in user can rate 8+ drinks inline on `/rapid-rate` with zero
  navigations, each rating landing as a normal append-only rating (visible in
  the journal, feeding the palate profile).
- Category filter, "haven't rated" filter, and popular ordering work; a drink
  the user ever rated (public, private, or quiz-origin) is excluded by
  `unratedOnly` on the next fetch.
- Per-card Private toggle produces a private rating (or flips a just-saved one)
  without interrupting flow on other cards.
- e2e green in CI with screenshots + a timing artifact showing the rapid flow
  per-drink time beats the old search-per-drink flow measured in the same run.
- All copy passes BRAND.md prohibited-language review: no volume/consumption
  framing, no session counters, no streaks, no "keep going" nudges anywhere on
  the surface.

## Out of scope — binding
- Schema changes or migrations of any kind.
- Any change to the rating write path, its validation, or provenance (no new
  origin value).
- Global search / filter-by-attribute / advanced catalog discovery (separate
  venue-era feature).
- Matching, digest, venues.
- ANY consumption-volume gamification: no "N rated" achievements, no progress
  bars over counts, no quantity celebration (Hard Rule 4).

## Design decisions applied (within spec bounds)
- **Popular ≡ most-rated.** No stored popularity aggregate exists; both framings
  collapse to the existing metric (count of latest public ratings, the
  RecommendationService/QuizService pattern) computed on the fly. Becomes a
  materialized rollup only if it ever shows a profile trace.
- **Re-tap = re-rate = new rating row.** Already the platform semantic
  (append-only, previous latest retired atomically). No undo on this surface —
  undo lives in the journal.
- **Page drift accepted.** `unratedOnly` applies at fetch time only; rated cards
  stay in place (state flips to "Noted") so the list never jumps. "Show more"
  can skip a few drinks after mid-page ratings; the client de-dupes by drink id
  and skipped drinks reappear next visit. No excludeIds param, no keyset
  pagination — deliberate simplicity.
- **No kill switch.** Hard Rule 10 covers phase-dependent display modes,
  thresholds, and prompts; a permanent core surface is none of those. The one
  tunable (page size) is a flag.
- **Entry points instead of a 7th nav item** (founder decision): the design
  reference's nav inventory is a binding contract and 7 items crowds narrow
  phones.

## Gate (CI, no founder phone check required)
The rapid-rate e2e suite is the acceptance: a fresh user rates 8 drinks inline
(screenshots at 1/4/8), a 9th privately, exercises the unrated filter, and the
timing test attaches a founder-readable artifact comparing per-drink time in the
rapid flow vs the old search-per-drink flow from the same run.
