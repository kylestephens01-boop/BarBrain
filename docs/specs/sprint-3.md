# Sprint 3 — Palate Engine v1
**Objective:** profiles compute, the sectioned feed recommends with reasons,
the quiz bootstraps cold users. Gate C1 = eval harness green.

## In scope
- Profile job: per-category preference-weighted vectors (rating − user mean),
  min 5 ratings; nightly batch + on-demand recompute after rating (cheap at scale).
- Onboarding quiz (ADR PRD): conversational interest gate per category →
  staples quiz (config-driven drink lists; beer/whiskey by product, wine by
  varietal/style) → "haven't tried" skip; quiz ratings are real ratings
  (provenance: quiz). Interest flags stored. ~45s per category target.
- Sectioned feed API + UI: Up Your Alley / Stretch a Little / Wildcard (ADR-013).
  Content-based: pgvector cosine, unrated filter, popularity prior, diversity
  pass (MMR-lite). Confidence-adaptive wildcard count (flags).
- Explainability: per-rec "because" from top contributing attributes.
- Radar chart: live per-category profile on journal/profile page.
- STRETCH: cross-category recs via 6-dim bridge — "Because you love peaty Scotch
  → try this rauchbier" row, behind a flag.
- Eval harness (CI suite): synthetic personas (hophead, malt-sweet, peat-lover,
  dry-red, sweet-white, sour-fan + 6 more) generate noisy ratings; metrics:
  attribute-alignment precision@10 ≥0.7; leave-one-out hit-rate@10 ≥0.25 on
  dense synthetic users; section integrity (Wildcard distance > Alley distance);
  determinism seed. Thresholds in config; failures block merge.

## Acceptance criteria
- New user: interest gate → 1 quiz → feed renders 3 sections w/ reasons,
  immediately (content-based needs no neighbors).
- Eval suite green in CI with report artifact (per-persona table).
- Radar matches intuition for 3 founder-known test accounts (gate task).
- Quiz lists editable via config without deploy.

## Out of scope
CF/matching (Sprint 4), digest, venue filtering, ML.NET.

## Gate C1 (founder, phone, ~15 min)
Make a fresh account as a deliberate persona (e.g., rate like a stout lover);
check the feed agrees with the act; read 5 "because" lines for sanity; skim the
CI eval report. Approve → Sprint 4.
