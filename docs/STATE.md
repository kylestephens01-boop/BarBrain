# STATE — agent session handoff
> Agents: update this at the END of every session. Keep it short and factual.

## Current sprint
data-beer-national — intake playbook + national beer catalog (branch
`data-beer-national`, off `main` after PR #10 merged). Unnumbered data
mini-sprint, sprint discipline applies. History: 4.8 CI seed parity (PR #10);
4.7 clear-attribute verb + whiskey-national (PR #9); 4.6 importer v2 (PR #8);
4.5 rapid-rate (PR #7); Sprint 4 (PR #6); 3 (PR #5); 2 (PR #4); 1 (PR #3).
STILL OUTSTANDING from Sprint 1: the VPS bulk seed run per RUNBOOK — now
including whiskey-national AND beer-national.

## Done (this session)
- **docs/DATA-INTAKE.md** — standing research→seed playbook (first-party-only
  sourcing, CONFIRMED/VERIFY/UNCONFIRMED gating, register-tag-first, 1-decimal
  ABV, existing-style-codes-only with flagged non-clean mappings, sparing
  overrides, explicit non-numeric-field decisions, verify-by-import, merge
  queue owns overlap, batch-the-decisions rule, session prompt template).
  Cross-referenced from SEED-FORMAT.md and CLAUDE.md Hard Rule 1.
- **docs/research/** — both first-party research compilations committed as
  source-of-record (whiskey doc untouched, per brief).
- **seed:beer-national registered** in DATA-SOURCES.md (own commit, ADR-024),
  then **src/api/seed/beer-national.json**: 15 producers / 24 drinks from the
  research doc's CONFIRMED core + founder batch rulings. 3 drinks (12.5%)
  carry overrides (KBS, Juice Force, Black Butte XXXV). 2 ship with NULL ABV
  by ruling (Allagash White, Firestone 805 — style CONFIRMED, exact ABV
  pending first-party capture; never publish an unverified numeric).
- **Founder batch rulings (2026-07-10, all recommendations accepted)**:
  corridor-duped drinks omitted (13 of 41 researched — union-of-seeds rule);
  HOLD Dogfish 60 Minute (site blocks automated fetch), Anchor Steam
  (re-confirm post-2024 revival), Lagunitas IPNA (no NA style taxonomy — ADR
  territory if wanted); EXCLUDE Yuengling Traditional Lager (UNCONFIRMED,
  conflicting ABV); Stone Delicious = 21A (gluten-reduction is process);
  Black Butte XXXV = 20A + override (annual reserve); Ruination "100+" IBU
  moot (seed format has no drink IBU field).
- **Verification**: Testcontainers test imports the real bundled
  beer-national.json via the real embedded registry (zero skips, all styled,
  full category+bridge vector coverage incl. the null-ABV drinks, overrides
  sparse, idempotent re-run); registry Fact asserts the tag ships in the
  binary (runs without Docker — passed locally); ci.yml smoke seed step now
  imports beer-national after whiskey-national (4.8 parity pattern).

## Doc inconsistency to flag (carried)
- Muted-text token: BRAND.md `--bb-text-muted` vs DESIGN-REFERENCE `--bb-muted`
  (alias in place; founder may standardize).

## Environment note (this build machine)
Docker/Node absent: the new Testcontainers test authored-not-run locally; CI
runs it. Verified locally: build 0 warnings; `dotnet test` 39 passed / 103
skipped; seed JSON parse/refs/vocab/ABV-precision checked by script.

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
- Sprint 5 (venues — check-in, personalized menus; ADR-015). Carried from
  4.5: rapid-rate doorway on feed empty states if dogfooding warrants.
- Wine-national is now a template exercise: DATA-INTAKE.md §4 prompt +
  a wine research doc.
