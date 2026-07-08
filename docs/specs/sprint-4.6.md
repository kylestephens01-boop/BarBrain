# Sprint 4.6 — Catalog Importer v2 (generic product-seed loader) [mini-sprint]
**Objective:** unblock loading any first-party product batch (e.g. a national
whiskey catalog) without corrupting corridor provenance, and let distinctive
expressions deviate from their style baseline via editorial attribute
overrides. Importer capability ONLY — no product data is authored here.

> **Provenance note:** the brief referenced a pre-existing
> docs/proposals/catalog-import-v2.md and docs/SEED-FORMAT.md from an earlier
> analysis pass. Neither existed in the repo (no docs/proposals/ at all), so
> this spec and SEED-FORMAT.md were derived from CatalogImportService +
> SCHEMA.md and authored in this mini-sprint, as the brief instructed for that
> case. The brief's "per ADR-023" license-gate reference is ADR-024 (the
> DATA-SOURCES registry); implemented against ADR-024.

## In scope
- `ImportProductsAsync(filePath)`: reads ANY product-seed file (arbitrary
  path), same shape as corridor-priority.json plus the additions below
  (docs/SEED-FORMAT.md is the format contract).
- Per-file provenance: the file's declared `source` tag (must start with
  `seed:`) is written to every row; corridor keeps `seed:corridor` because
  that is what its file declares. Idempotency stays on the `(Source,
  SourceRef)` partial unique upserts.
- Optional per-drink `attributes` override block → `drink_attributes` rows
  with `source='moderator'` (human editorial judgment — ADR-028 justifies
  against SCHEMA.md's allowed set), confidence from file-level
  `attributeConfidence` else flag `catalog.seed_override_confidence_pct`
  (default 80). Values 0–1 (validated in code, CHECK-enforced in DB); bridge
  dims stay on the shared scale because overrides use the same attribute
  vocabulary as style baselines. No block → pure style inheritance, unchanged.
- License gate, fail-closed (ADR-024): docs/DATA-SOURCES.md is embedded into
  the api binary; an unregistered source tag refuses to import.
- CLI verb `import products --file <path>`, same pattern as existing verbs.
- Back-compat: `ImportCorridorAsync` delegates to the generic path;
  `import corridor` / `import bundled` behave identically.

## Acceptance criteria
- Integration tests (Testcontainers, real Postgres): generic seed with
  overrides imports correctly (moderator rows, right confidence, inheritance
  for non-overridden dims, vectors reflect overrides on category + bridge
  scales); per-file provenance (not seed:corridor); re-run idempotent (no
  dupes, all-unchanged counters); override edits update in place; undocumented
  source refused with nothing written; malformed overrides (unknown key,
  out-of-range value) fail loudly.
- Existing corridor import tests pass unchanged.
- No schema change (drink_attributes already carries Value/Source/Confidence).
- CI green.

## Out of scope (binding)
- Authoring ANY bourbon/whiskey (or other) product data — separate data task
  after this merges.
- Matching, digest, recs, venues, rapid-rate work.
- Schema migrations (none needed; if one had been, flag first).
- Deleting/reverting moderator rows when an override is removed from a seed
  file (manual moderation action; importer never deletes).
