# STATE — agent session handoff
> Agents: update this at the END of every session. Keep it short and factual.

## Current sprint
Sprint 4.8 — CI/demo seed-path parity mini-sprint (branch `sprint-4.8`, off
`main` after PR #9 merged). Spec: docs/specs/sprint-4.8.md. Tooling/CI only.
History: 4.7 clear-attribute verb + whiskey-national catalog (PR #9); 4.6
importer v2 (PR #8); 4.5 rapid-rate (PR #7); Sprint 4 (PR #6); 3 (PR #5);
2 (PR #4); 1 (PR #3). STILL OUTSTANDING from Sprint 1: the VPS bulk seed run
per RUNBOOK — now including whiskey-national.

## Done (Sprint 4.8)
- **Fix 1 (ci.yml)**: e2e "Seed catalog" step now runs bundled →
  `import products --file seed/whiskey-national.json` → demo-dupes, so the CI
  demo stack and the seed-verification report include the national whiskey
  catalog. Order matters: whiskey-national needs the styles `bundled` imports.
- **Packaging ambiguity (flagged in brief) — resolved, NO Docker change**:
  the api Dockerfile copies all of src/api/, the csproj marks seed\**\* as
  Content copied to output, .dockerignore doesn't exclude it → the file ships
  at /app/seed/whiskey-national.json; compose exec runs in WORKDIR /app so
  the relative path resolves. Proven by a real local `dotnet publish`
  (whiskey-national.json present in the publish seed/ dir). DATA-SOURCES.md
  needed the PR-#8 Dockerfile fix only because it lives OUTSIDE src/api/.
- **Fix 2 (report) — already generic, NO change**: BuildReportAsync groups
  every section by the Source column (producers by source; drinks by
  category+source with vector coverage; attribute provenance). Nothing
  hardcodes seed:corridor.
- **Verified in CI (PR #10 run)**: Playwright smoke green with the new seed
  step. Seed-report artifact: seed:whiskey-national 19 active + 1 merged
  producers, 55 drinks at 100% vector coverage; 19 moderator rows at 0.80;
  corridor (35 producers; 38/16/8 drinks) + demo-dupes (3) unchanged. Bonus
  observation: one producer auto-merged across sources (1 approved in the
  merge queue) — cross-source overlap behaving as designed.

## Doc inconsistency to flag (carried)
- Muted-text token: BRAND.md `--bb-text-muted` vs DESIGN-REFERENCE `--bb-muted`
  (alias in place; founder may standardize).

## Environment note (this build machine)
Docker/Node absent: the ci.yml change only proves itself on the runner (the
whole point of this mini-sprint). Locally verified: `dotnet publish` output
contains seed/whiskey-national.json; no product code touched so no new tests.

## Blockers / needs founder
- Carried from Sprint 4: HUMAN-CHECKLIST 6 (digest physical address + SMTP),
  Gate C2 real-user follow-ups when users exist.
- Carried: VPS bulk seed run (RUNBOOK, incl. whiskey-national — manual ops
  action, deliberately NOT in this PR); HUMAN-CHECKLIST 7–9 (Google/Apple/
  Turnstile creds), 14 (fonts/logo assets), charter proposals P1–P3, beer.db
  call.
- Carried from 4.7: founder may want to eyeball the 11 whiskey-national
  override values and the representative barrel-proof ABVs.

## Next session should
- Sprint 5 (venues — check-in, personalized menus; ADR-015). Carried from
  4.5: consider surfacing the rapid-rate doorway on the feed's empty states
  if dogfooding says Search isn't discoverable enough.
