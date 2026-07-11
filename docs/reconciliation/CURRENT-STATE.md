# BarBrain — CURRENT STATE (operational snapshot)
**As of 2026-07-10 · repo @ 6f2d7a7 (branch sprint-6) · replaces the stale
whiskey-national research report in project knowledge**

BarBrain: multi-category beverage rating & discovery PWA (beer / whiskey /
wine). .NET 10, Blazor WASM + ASP.NET Core API, Postgres 16 + pgvector,
Docker on a Hetzner VPS behind Cloudflare. Dev host: dev.barbrain.co
(Cloudflare Access-gated). Solo founder + AI agents; work ships sprint-by-
sprint via PR, main is protected.

## Sprint status
- **Merged to main:** Sprint 0, 0.5, 1 (catalog+seeding), 2 (identity/auth),
  3 (palate engine + recs), 4 (matching + digest), 4.5 (rapid-rate, PR #7),
  4.6 (generic importer + ADR-028 license gate, PR #8), 4.7 (whiskey-national
  + --clear-attribute verb, PR #9), 4.8 (CI/demo seed parity, PR #10),
  beer-national data (PR #11), Sprint 5 (venues/check-in — Gate D, PR #12,
  merged 2026-07-10).
- **In review:** Sprint 6 (badges, moderation, provenance weighting, PWA
  polish) — PR #13 OPEN, **CI RED** as of 2026-07-10 (Build&test +
  Playwright failing; Contrast passing). 63 unit tests pass locally.
  Founder gate after CI green: earn a badge, action a report in /admin,
  install the PWA.
- **Next:** Sprint 7 — pre-launch hardening (privacy export/deletion,
  backups, launch checklist). Spec exists.

## Catalog inventory (bundled seed files, real counts)
| Provenance tag | Producers | Drinks | Notes |
|---|---|---|---|
| `seed:corridor` | 35 | 62 | corridor-priority, ADR-020 local-depth |
| `seed:whiskey-national` | 20 | 55 | sprint 4.7; research doc said 57/18 — 55/20 is what shipped |
| `seed:beer-national` | 15 | 24 | PR #11; CONFIRMED core + founder rulings; 13 corridor dupes omitted |
| `seed:barbrain-styles` | — | 197 styles | 140 beer / 21 whiskey / 36 wine, facts-only (ADR-023) |

Badges: 15 definitions in seed/badges.json (**sprint-6 branch only until
PR #13 merges**). External importers ready but not yet run: Open Brewery DB
(producers only, MIT), beer.db (license-cleared, stale — founder call).

## Pending manual VPS work (RUNBOOK; outstanding since Sprint 1)
1. `… import bundled` (vocabulary → styles → corridor).
2. `… import products --file seed/whiskey-national.json`
3. `… import products --file seed/beer-national.json`
4. Open Brewery DB producers: download CSV → `… import openbrewerydb --file …`
5. Optional, founder call first: beer.db `… import beerdb --dir …`
   (⚠ imports producers AND drinks, data is ~2012–13; the producers-only
   source is openbrewerydb, not beerdb).
6. Rec-quality check: **not currently runnable** — golden-set eval is
   fixture-only in CI (threshold ≥0.7, sprint-3 spec); a live-catalog
   Precision@10 verb is unbuilt backlog. The 0.71 baseline figure is **not
   recorded in the repo — VERIFY its source before relying on it.**

## Known deferred / blocked (with owner)
- **Founder:** SVG logo masters + WOFF2 fonts (HUMAN-CHECKLIST 14 — icon
  pipeline ships dormant on placeholders); SMTP provider + physical mailing
  address (HC 6 — digest is log-only until set); Google/Apple OAuth +
  Turnstile creds (HC 7–9); trademark knockout (HC 11 — gates all public
  brand use; coined-mark fallback Likli/Savry); launch TLD decision
  post-knockout (barbrain.app was unavailable); charter proposals P1–P3
  adjudication; beer-national VERIFY-ABV backlog (Allagash, Firestone,
  Dogfish, Anchor); beer.db worth-it call.
- **Pre-launch (recorded, not scheduled):** Cloudflare TLS "Flexible" →
  "Full" + origin cert (ARCHITECTURE.md — MUST before public launch);
  Turnstile keys; backups + restore drill (Sprint 7); Iowa alcohol-law
  review + LLC (HC 12–13).
- **Product deferrals:** Lagunitas IPNA held — no non-alcoholic style
  taxonomy (new-ADR territory if wanted); offline rating queue-and-sync
  (S6 stretch, not built); admin auth still the Sprint 0 token stub;
  QuestPDF Community license — revisit before $1M revenue (ADR-029);
  live-catalog eval verb (trigger-based backlog).

## ADR index (all ACCEPTED)
- 001 .NET 10 + Blazor WASM PWA + separate API
- 002 Single Postgres 16 + pgvector for everything
- 003 Docker everywhere, no IIS
- 004 Hetzner VPS day one, Cloudflare in front
- 005 Private monorepo, PR-only merges, CI required
- 006 DB-backed feature flags for anything phase-dependent
- 007 Hand-rolled CF v1 (amended by 025; realized in S4 as density-gated blend)
- 008 Canonical drink = (producer, product, category)
- 009 8 attribute dims/category + 6-dim cross-category bridge
- 010 Pseudonymous identity; birth-year-only persistence
- 011 Auth: email+password, Google, Apple; soft verification
- 012 Ratings pseudonymous-public, per-rating private toggle, history kept
- 013 Sectioned rec feed with mandatory "because"
- 014 Blended match score, named matches, hide-me (implemented S4)
- 015 Check-in as session primitive; Home Bar private virtual venue
- 016 No consumption-volume/frequency incentives, ever
- 017 First-party analytics only
- 018 Deletion: full vs anonymize; JSON export
- 019 Weekly email digest only in MVP (implemented S4, log-only until HC 6)
- 020 Seeding: corridor local-depth first; OBDB = producers only
- 021 Brand system v1; dark-only MVP
- 022 Moat: data asset first, venues as revenue proof (P2 reframe PENDING)
- 023 Licensing-safe styles: names/codes/numeric ranges only, zero prose
- 024 Binding data-source license registry (DATA-SOURCES.md), fail-closed
- 025 pgvector attribute similarity primary; CF deferred
- 026 Ownership + visibility columns from day one, DB-enforced
- 027 Cross-category bridge recs required v1; golden-set eval gates CI
- 028 Generic product-seed importer, per-file provenance, moderator
  overrides, embedded license gate (+ addendum: --clear-attribute verb;
  designations ≠ taxonomy)
- 029 QuestPDF Community for the QR one-pager; revisit before $1M revenue
