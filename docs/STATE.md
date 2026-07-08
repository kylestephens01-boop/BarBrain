# STATE — agent session handoff
> Agents: update this at the END of every session. Keep it short and factual.

## Current sprint
Sprint 4.6 — Catalog Importer v2 mini-sprint (branch `sprint-4.6`, off `main`
which has Sprint 4 via PR #6). Spec: docs/specs/sprint-4.6.md. Generic
product-seed loader: per-file provenance, editorial attribute overrides,
machine-enforced license gate. NOTE: sprint-4.5 (rapid-rate) is a SIBLING
branch, PR still open when this branched — this branch deliberately avoids all
rapid-rate files except an additive feature-flags.json entry (expect a trivial
merge there whichever lands second). Sprint 4 merged (PR #6); Sprint 3 (PR #5);
Sprint 2 (PR #4); Sprint 1 (PR #3). STILL OUTSTANDING from Sprint 1: the VPS
bulk seed run per RUNBOOK.

## Done (Sprint 4.6)
- **Flag (brief provenance)**: the brief said docs/proposals/catalog-import-v2.md
  and docs/SEED-FORMAT.md already existed from an earlier analysis pass —
  NEITHER did (no docs/proposals/ at all). Design was derived from
  CatalogImportService + SCHEMA.md per the brief's fallback; SEED-FORMAT.md and
  the sprint spec were authored in this mini-sprint. Also the brief's license
  gate cite is ADR-024 (not ADR-023); implemented against ADR-024.
- **Importer** (`ImportProductsAsync(filePath, dataSourcesPath?)`): any
  product-seed file, corridor shape + additions (docs/SEED-FORMAT.md is the
  contract). Per-file `source` tag (must start `seed:`) on every row;
  idempotency unchanged on (Source, SourceRef) upserts. `ImportCorridorAsync`
  is now a one-line delegation → corridor behavior/provenance identical by
  construction, proven by CatalogImportTests running unchanged.
- **Overrides**: optional per-drink `attributes` block → drink_attributes rows
  with source='moderator' (ADR-028 justifies vs SCHEMA.md's closed set),
  confidence = file-level `attributeConfidence` else new flag
  `catalog.seed_override_confidence_pct` (default 80). Non-overridden dims
  inherit exactly as before (vector sync fills only MISSING dims). Malformed
  overrides (unknown key, out-of-range) fail the run loudly. Importer never
  deletes attribute rows (removing an override ≠ revert).
- **License gate (ADR-024, fail-closed)**: docs/DATA-SOURCES.md embedded in
  the api binary (csproj EmbeddedResource); unregistered tag → refuse before
  any DB write. Tests can substitute the registry via `dataSourcesPath`.
- **CLI**: `import products --file <path>` (+ usage/RUNBOOK). NO migration —
  drink_attributes already had Value/Source/Confidence.
- **Docs**: sprint-4.6 spec, SEED-FORMAT.md (new), ADR-028, DATA-SOURCES.md
  gate note, RUNBOOK verb.
- **Tests**: CatalogProductImportTests — provenance + moderator rows + right
  confidence + inheritance + override lands in category AND bridge vectors;
  flag-default confidence; idempotent re-run (all-unchanged, no dupes) +
  override edit updates in place; undocumented source refused by the REAL
  embedded registry with nothing written; unknown key / 0–10-scale value fail
  loudly; plain Fact (runs without Docker) proves the embedded registry ships
  with the bundled tags. Ctor gained ISettingsService → 4 test construction
  sites updated.

## Decisions made within spec bounds (log)
- moderator (not manufacturer) for editorial seed overrides: our authored
  numbers are curator judgment, not producer-published claims (ADR-028).
- Gate reads an EMBEDDED registry (not a docs path at runtime): works
  identically in dev/CI/container, fail-closed if missing; new source ⇒
  rebuild, which its data batch needs anyway.
- Malformed override = hard failure (vs corridor's warn-and-skip for
  category/style, which is preserved): first-party editorial data must not be
  silently skewed.
- Importer never deletes moderator rows → seed re-runs can't clobber future
  moderation-UI edits; removing an override from a file does not revert it.
- ImportResult label for product files is the file's source tag (corridor logs
  "seed:corridor" now, was "corridor") — log-only cosmetic change.

## Doc inconsistency to flag (carried)
- Muted-text token: BRAND.md `--bb-text-muted` vs DESIGN-REFERENCE `--bb-muted`
  (alias in place; founder may standardize).

## Environment note (this build machine)
Docker/Node absent: authored-not-run locally = CatalogProductImportTests' four
Testcontainers tests (plus all pre-existing suites). Verified locally:
`dotnet build` 0 warnings; `dotnet test` 39 passed / 90 skipped (the new
embedded-registry Fact runs locally). CI runs everything for real.

## Blockers / needs founder
- Carried from Sprint 4: HUMAN-CHECKLIST 6 (digest physical address + SMTP),
  Gate C2 real-user follow-ups when users exist.
- Carried: VPS bulk seed run (RUNBOOK); HUMAN-CHECKLIST 7–9 (Google/Apple/
  Turnstile creds), 14 (fonts/logo assets), charter proposals P1–P3, beer.db call.

## Next session should
- After this merges: the bourbon/whiskey national catalog is now unblocked as
  a pure DATA task (author seed file + DATA-SOURCES.md entry + rebuild; zero
  code). Remember the sprint-4.5 rapid-rate PR if still open.
- Then Sprint 5 (venues — check-in, personalized menus; ADR-015).
