# Sprint 4.8 — CI/demo seed-path parity [mini-sprint]
**Objective:** the e2e job's seed step only ran `import bundled` +
`demo-dupes`, so the CI demo stack and the Sprint-1 seed-verification report
never reflected the whiskey-national catalog merged in PR #9. Tooling/CI fix
only — no product code, no features, no data.

## In scope
- **Fix 1 — CI e2e seed step:** add
  `import products --file seed/whiskey-national.json` to the "Seed catalog"
  step, ordered bundled → whiskey-national → demo-dupes (whiskey-national
  needs the styles `bundled` imports; demo-dupes stays last, unchanged).
- **Packaging check (brief's flagged ambiguity):** resolved with NO Docker
  change needed — the api Dockerfile copies all of `src/api/`, the csproj
  marks `seed\**\*` as Content copied to output, and `.dockerignore` does not
  exclude it, so `dotnet publish` ships the file at `/app/seed/
  whiskey-national.json` (verified by a real local publish). `compose exec`
  runs in WORKDIR `/app`, so the relative path resolves. (DATA-SOURCES.md
  needed a PR-#8 Dockerfile fix only because it lives OUTSIDE `src/api/`.)
- **Fix 2 — seed verification report:** confirmed already generic, NO change:
  `BuildReportAsync` groups every section by the `Source` column (producers
  by source; drinks by category and source with vector coverage; attribute
  rows by provenance). Nothing hardcodes seed:corridor.

## Acceptance criteria
- CI e2e job seeds whiskey-national and Playwright smoke passes for real.
- The uploaded seed-report artifact shows seed:whiskey-national producers and
  drinks (with vector coverage) alongside unchanged corridor + demo-dupes
  rows.
- No product-code diff.

## Out of scope (binding)
- Importer, clear-attribute verb, matching, digest, recs, venues; any new
  product data.
- VPS deployment/seeding (separate manual ops action for the founder).
- Report format redesign.
