# Sprint 4.7 — Override-clear verb + whiskey-national catalog [mini-sprint]
**Objective:** two clearly separated deliverables. (1) A CLI corrective for
wrong editorial overrides — the ADR-028 never-delete rule left no sanctioned
undo path, which becomes urgent with the override volume in (2). (2) The
national American whiskey/bourbon catalog as a pure DATA task (seed file +
source registration; zero product code).

## Part 1 — `import products --clear-attribute` (code)

### In scope
- CLI verb (not a seed-format change, not a UI):
  `import products --clear-attribute --source <seed-tag> --drink-ref <ref>
  --key <attribute-key>`.
- Resolves the drink by `(Source, SourceRef)` — the importer's identity keys.
  `--key` is the short key as seed files write it; category auto-prefixed.
- Deletes that key's `drink_attributes` row ONLY if `source='moderator'`;
  `manufacturer`/`crowd`/`llm` provenance refuses loudly, row untouched.
- Triggers the importer's existing vector resync so the dim falls back to
  style-baseline inheritance and category + bridge vectors recompute.
- Idempotent: key already gone / never overridden → no-op, not an error.
- Docs: SEED-FORMAT.md + RUNBOOK.md verb docs; additive addendum on ADR-028
  (NOT a new ADR).

> **Ambiguity resolved (flagged, not silent):** the brief says refuse
> `inherited` rows AND demands idempotent re-runs. After a successful clear,
> the vector sync MATERIALIZES an `inherited` row — that row is how "not
> overridden" is represented (never-overridden drinks have 8 of them). So
> `inherited` is treated as the already-at-baseline NO-OP case; refusal is
> reserved for manufacturer/crowd/llm, where deletion would destroy real
> non-editorial provenance. Any other reading makes every second run an error.

### Acceptance criteria (Part 1)
- Integration tests: clear removes the moderator row and the dim reverts to
  the inherited baseline (value, source, confidence match a never-overridden
  drink); vectors resync on category + bridge scales; the drink's other
  override survives; re-run and never-overridden key are no-ops; a
  non-moderator row is refused and left intact; unknown ref/key fail loudly.
- No schema change. No new auth surface. CI green.

## Part 2 — whiskey-national catalog (data)

### In scope
- Register `seed:whiskey-national` in docs/DATA-SOURCES.md FIRST (first-party
  basis, capture date) per ADR-024, then author the seed file per
  docs/SEED-FORMAT.md.
- First-party facts only: distillery/producer names, product names, ABV,
  mapping to existing WH-* style codes (Hard Rule 1 — no competitor DBs).
- Editorial overrides SPARINGLY: only expressions that meaningfully deviate
  from their style baseline (barrel-proof, heavily-peated-equivalent, etc.);
  most drinks are pure style inheritance, per corridor precedent.
- Founder ruling (recorded in the ADR-028 addendum): bottled-in-bond /
  single-barrel / barrel-proof are per-drink attribute overrides or name
  facts, NOT new WH-* style taxonomy codes.
- Verification per SEED-FORMAT acceptance criteria: idempotent re-run, correct
  provenance, vectors populated. This machine has no Docker, so verification
  runs as a Testcontainers integration test against the REAL bundled seed
  file in CI (plus the VPS run per RUNBOOK when infra exists).

### Acceptance criteria (Part 2)
- DATA-SOURCES.md entry exists with the quoted tag (gate requirement) before
  any data references it.
- Seed file imports cleanly; re-run all-unchanged; every styled drink gets
  category + bridge vectors; overrides land as moderator rows.
- CI green.

## Out of scope (binding)
- Moderation/admin auth surface or UI for overrides (Sprint 6 — this verb is
  deliberately CLI-only so that is not pulled forward).
- Seed-format changes.
- New WH-* style taxonomy codes.
- Scotch/Irish/world whisky data beyond the American national batch.
- Venues, recs, matching, rapid-rate work.
