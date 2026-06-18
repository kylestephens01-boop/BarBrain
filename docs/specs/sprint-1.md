# Sprint 1 — Catalog & Data Foundation
**Objective:** the canonical drink database exists, seeded, deduped, browsable
via API. THE expensive-to-reverse sprint; schema review is the gate.

## In scope
- Schema (ADR-008/009): producers, styles (per-category trees), style_attributes
  (8-dim baselines), drinks, drink_attributes (value+source+confidence; pgvector
  column synced), merge_queue, audit/provenance columns throughout.
- Importers (idempotent, resumable, provenance-tagged): BJCP style taxonomy →
  styles + baseline vectors (verify license permits derived numeric baselines;
  paraphrase descriptions, never copy text); Open Brewery DB → producers ONLY;
  beer.db → beer products; TTB COLA → extraction pipeline STUB + sample batch
  (full extraction is ongoing background work, not a launch blocker).
- Corridor priority list: config file of ~50 producers/brands to seed deep
  (macros + IA regionals: Toppling Goliath, Big Grove, Exile, Millstream,
  Cedar Ridge, Templeton + national staples). Founder edits the list later.
- Entity resolution: normalization (case/punct/abbrev), pg_trgm similarity
  candidates → merge_queue with confidence; admin API to approve/merge/reject;
  merged-entity redirect handling (old IDs resolve).
- Catalog API: search (full-text + trigram), browse by category/style, drink
  detail w/ attribute vector + provenance.
- Seed verification report: counts per category/source, attribute coverage %,
  duplicate-rate estimate (artifact in CI).

## Acceptance criteria
- Migrations from empty AND from Sprint 0 schema both green.
- Importers runnable via CLI + idempotent (re-run = no dupes).
- ≥ all BJCP styles w/ baseline vectors; ≥1,000 producers; ≥2,000 beer products;
  corridor list fully covered w/ attribute vectors (inherited or better).
- Search returns sane results for: "toppling goliath", "buffalo trace", "fat tire",
  misspelled "guiness".
- Merge queue: seeded near-duplicate fixtures produce candidates; approve/reject
  works; e2e screenshot of admin queue.

## Out of scope
Users, ratings, UI beyond admin queue stub, wine/whiskey product depth beyond
corridor list (styles yes, products thin is OK), full TTB pipeline.

## Gate A (founder — THE long sitting, ~60–90 min at a keyboard)
Review: schema ERD + migration files; attribute taxonomy values for 10 sample
drinks you know personally (do the numbers smell right?); search quality on 10
queries you pick; merge queue demo. This is the one gate worth real time —
everything downstream builds on these tables.
