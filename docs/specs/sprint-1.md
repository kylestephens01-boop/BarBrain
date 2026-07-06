# Sprint 1 — Catalog & Data Foundation
**Objective:** the canonical drink database exists, seeded, deduped, browsable
via API. THE expensive-to-reverse sprint; schema review is the gate.

> **Amended 2026-07-06 (founder decisions A–E, applied throughout):**
> (A) NO BJCP guideline text — styles are name + code + numeric ranges + our
> own attribute vocabulary only (ADR-023). (B) docs/DATA-SOURCES.md license
> gate precedes ANY ingestion; beer.db means github.com/openbeer (PD), never
> openbeerdb.com (ODbL) (ADR-024). (C) pgvector attribute similarity is the
> primary rec mechanism; CF deferred (ADR-025). (D) ownership + visibility
> columns with DB constraints from day one (ADR-026). (E) charter update
> proposals are a distinct deliverable (docs/project-charter.md).

## In scope
- Schema (ADR-008/009, ADR-023/026): producers, styles (per-category trees;
  name/code/numeric ranges ONLY — no descriptive text), style_attributes
  (8-dim baselines, BarBrain-original editorial values), drinks,
  drink_attributes (value+source+confidence; pgvector columns synced),
  merge_queue, minimal pseudonymous users stub (ownership FK target),
  audit/provenance columns throughout; owner + visibility with DB constraints
  on user-ownable entities.
- Importers (idempotent, resumable, provenance-tagged, LICENSE-GATED per
  ADR-024/docs/DATA-SOURCES.md): style taxonomy seed (BarBrain-authored; BJCP
  used as factual reference for names/codes/ranges only — never text); Open
  Brewery DB → producers ONLY; beer.db (github.com/openbeer, public domain —
  NOT openbeerdb.com) → beer products; TTB COLA → extraction pipeline STUB +
  sample batch (full extraction is ongoing background work, not a launch
  blocker).
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
- Charter update proposals (decision E) drafted in docs/project-charter.md as a
  clearly-marked proposed-changes section for founder review: domain reality,
  moat reframe, rec-engine sequencing. No silent rewrites of settled sections.

## Acceptance criteria
- Migrations from empty AND from Sprint 0 schema both green.
- Importers runnable via CLI + idempotent (re-run = no dupes).
- Style taxonomy covers the full BJCP 2021 style LIST (names/codes/ranges — no
  guideline text, per ADR-023) w/ baseline vectors, plus whiskey + wine style
  trees; ≥1,000 producers; ≥2,000 beer products (both gated on the licensed
  bulk seed run — see DATA-SOURCES.md); corridor list fully covered w/
  attribute vectors (inherited or better).
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
