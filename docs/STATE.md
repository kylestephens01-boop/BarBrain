# STATE — agent session handoff
> Agents: update this at the END of every session. Keep it short and factual.

## Current sprint
Sprint 5 — venues: check-in, personalized menus, QR kit (branch `sprint-5`,
off `main` after PR #11 merged). Spec: docs/specs/sprint-5.md (pre-existing).
History: data-beer-national (PR #11); 4.8 CI seed parity (PR #10);
4.7 clear-attribute verb + whiskey-national (PR #9); 4.6 importer v2 (PR #8);
4.5 rapid-rate (PR #7); Sprint 4 (PR #6); 3 (PR #5); 2 (PR #4); 1 (PR #3).
STILL OUTSTANDING from Sprint 1: the VPS bulk seed run per RUNBOOK — now
including whiskey-national AND beer-national.

## Done (this session)
- **Founder rulings captured (2026-07-10)**: shelf mapping (Favorites = own
  rating ≥ flag; Familiar = rated before or known style; New for You /
  Adventurous = closest/far share of unrated by palate similarity);
  check-in expiry 4h via `checkin.expiry_hours`; NO manager-user-id concept
  (founder-as-admin maintains verified menus; venue-owner auth deferred);
  QuestPDF Community accepted → **ADR-029** (revenue-cap revisit recorded).
  Batch: hours free-text, wiki rate limits as flags, denied-geo fallback =
  name sort + hint, venue dedupe thresholds mirror producer trigram.
- **Schema (`Sprint5Venues`)**: venues extended additively (NormalizedName
  + partial trigram index on public venues, geo with range/pairing CHECKs and
  a home-bar-no-geo CHECK — never store a user's home coordinates, address,
  free-text hours, wiki|verified tier CHECK-paired to venue type, contributor
  provenance, producer-pattern merge lifecycle); new `venue_menu_items` and
  `checkins` (one OPEN per user via partial unique index); merge_queue venue
  FK pair. Data ops: NormalizedName backfill + idempotent Home Bar backfill.
- **API**: VenueService (wiki add w/ dedupe-on-add → merge queue, nearby
  distance sort with haversine, wiki menu add/edit/confirm, rate limits from
  flags with events as audit trail, Home Bar get/rename, admin tier);
  CheckinService (one-tap, expiry flag, supersede-previous); RatingService
  auto-tags untagged ratings during an active check-in (explicit contexts
  win); PersonalizedMenuService (four shelves per ruling, "because" on every
  rec, match % suppressed cold, popularity+style-group fallback);
  MergeService venue merges (menus preserved, survivor wins collisions,
  check-ins/ratings repoint); VenueKitService (QR PNG + QuestPDF one-pager).
  Events: venue_added, menu_item_added/edited, checkin,
  menu_viewed_personalized.
- **Web (Screen 5)**: venues list (distance/name sort, wiki add form with
  optional device geo), venue page (pre-check-in teaser + flat menu →
  checked-in banner + four shelf tabs, amber Favorites/New-for-you underline
  per design ref), wiki add-to-menu picker, recent activity. New icons:
  circle-check, map-pin-check, lock, qrcode.
- **Tests**: VenueFlowTests (all acceptance flows incl. Home Bar negative
  surface + merge-preserves-menus + QR/PDF), MigrationFromSprint4Tests
  (previous-schema chain + both backfills), VenueDistanceTests (no-Docker),
  venues.spec.ts Playwright Gate D demo (geolocation denied throughout — no
  GPS gate anywhere).

## Doc inconsistency to flag (carried)
- Muted-text token: BRAND.md `--bb-text-muted` vs DESIGN-REFERENCE `--bb-muted`
  (alias in place; founder may standardize).

## Environment note (this build machine)
Docker/Node absent: Testcontainers + Playwright suites authored-not-run
locally; CI runs them — CI green is the done gate. Verified locally: build 0
warnings; `dotnet test` 43 passed / 112 skipped.

## Deferred within Sprint 5 (noted in PR)
- Home Bar rename ships API-only (`PATCH /api/venues/home-bar`); no web
  control yet — smallest matching surface is a profile-page field.
- Admin venue-merge decisions ride the existing merge-queue API/page via the
  shared DTO; AdminMergeQueue.razor was not restyled for venue rows.
- Verified-tier menu maintenance is admin-token API only (founder ruling
  2026-07-10 — no venue-owner auth concept in MVP).

## Backlog (unscheduled — revisit on a concrete trigger, not speculatively)
- **Live-catalog rec-quality eval verb (not yet built).** The golden-set eval
  harness (ADR-027, RecEvalFixture/MatchEvalFixture) is fixture-only by design:
  it builds its own throwaway Testcontainers Postgres, seeds a fully synthetic
  catalog (8 archetype + 110 seeded-random drinks/category) and synthetic
  users, and has no connection-string injection point. There is currently NO
  way to measure Precision@10 (or any rec-quality metric) against the
  live/deployed catalog. The `report` CLI verb confirms catalog/vector
  coverage (producers/drinks by source, % vector coverage) but computes no
  rec-quality metric.
  Design notes for pickup:
  - Persona.GenerateRatings only needs (Id, Category, Vector) — the live
    catalog can supply this; a future `eval` CLI verb could run personas
    through the real PalateProfileService/RecommendationService against real
    data.
  - MUST run against a snapshot/clone of the DB or inside a rolled-back
    transaction — eval users/ratings must never persist into live data.
  - The CI threshold (0.70) will NOT transfer as-is: the live catalog has far
    fewer drinks per category (~47 vs. 110 synthetic-uniform), making the
    "top quartile" ground truth noisier. A live baseline would need to be
    established fresh, not compared against the CI number.
  - Not urgent — current mitigation is manual spot-check (done 2026-07-10,
    looked reasonable to the founder) + 100% vector coverage confirmed via
    `report`. Triggers to revisit: user complaint about rec quality, or a
    pre-launch quality gate.
  Status at logging (2026-07-10): catalog 58 producers / 141 drinks
  (whiskey/beer/wine) post whiskey-national + beer-national import and
  merge-queue cleanup (12 producer merges approved, 10 drink-level
  false-positive candidates correctly rejected). No Precision@10 number
  available for the live catalog.

## Blockers / needs founder
- VERIFY backlog for beer-national: capture exact ABV from allagash.com and
  firestonewalker.com (then backfill the two null ABVs), dogfish.com (manual
  browser — blocks automation), revived anchorbrewing.com. Promote per
  DATA-INTAKE §1 thresholds.
- Carried: VPS bulk seed run (RUNBOOK, + both national catalogs);
  HUMAN-CHECKLIST 6 (digest address/SMTP), 7–9 (OAuth/Turnstile creds), 14
  (fonts/logo); Gate C2 follow-ups; charter proposals P1–P3. beer.db call:
  superseded for products by this first-party batch (beer.db inspection found
  84 stale product lines; producers-only import remains an option).

## Next session should
- Watch Sprint 5 PR CI (Testcontainers + Playwright run there, not on this
  machine); fix red if any; then Gate D review (founder, in a real corridor
  bar: add it, 6 real taps, check in, judge the shelves; print the one-pager).
- After Gate D approval → Sprint 6 (gamification + moderation; the unified
  moderation queue absorbs venue merges).
- Carried from 4.5: rapid-rate doorway on feed empty states if dogfooding
  warrants. Wine-national remains a template exercise: DATA-INTAKE.md §4
  prompt + a wine research doc.
