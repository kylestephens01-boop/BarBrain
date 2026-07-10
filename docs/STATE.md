# STATE — agent session handoff
> Agents: update this at the END of every session. Keep it short and factual.

## Current sprint
Sprint 4.7 — override-clear verb + whiskey-national catalog mini-sprint
(branch `sprint-4.7`, off `main` after PR #8 merged). Spec:
docs/specs/sprint-4.7.md (authored this session from the founder brief).
History: 4.6 importer v2 merged (PR #8); 4.5 rapid-rate (PR #7); Sprint 4
(PR #6); 3 (PR #5); 2 (PR #4); 1 (PR #3). STILL OUTSTANDING from Sprint 1:
the VPS bulk seed run per RUNBOOK.

## Done (Sprint 4.7)
- **Part 1 (code)** — `import products --clear-attribute --source <tag>
  --drink-ref <ref> --key <short-key>`: sanctioned corrective for a wrong
  editorial override (`CatalogImportService.ClearAttributeOverrideAsync`).
  Resolves by (Source, SourceRef); deletes the drink_attributes row ONLY if
  source='moderator' (manufacturer/crowd/llm refuse loudly, row intact);
  reuses the importer's vector resync so the dim falls back to style-baseline
  inheritance and category+bridge vectors recompute. Idempotent: no row or an
  'inherited' row is a no-op. Unknown ref/key fail loudly. CLI-only by design
  (no seed-format change, no admin auth, no UI — Sprint 6 not pulled forward).
- **Ambiguity resolved (flagged, not silent)**: brief said refuse 'inherited'
  AND be idempotent — but a successful clear MATERIALIZES an inherited row
  (that row IS baseline state), so refusing it would error every re-run.
  Inherited ⇒ already-at-baseline no-op; refusal reserved for
  manufacturer/crowd/llm. Documented in spec, ADR-028 addendum, SEED-FORMAT.
- **Part 2 (data)** — `seed:whiskey-national` registered in DATA-SOURCES.md
  FIRST (own commit, per ADR-024), then src/api/seed/whiskey-national.json:
  20 distilleries / 55 drinks, first-party facts, all WH-AM-* styles;
  11 drinks (20%) with sparing editorial overrides (barrel proof, double-oak/
  port finish, peated malt); batch-varying barrel proofs carry representative
  ABVs (noted inline). Corridor-covered drinks NOT re-listed (union = catalog;
  producer overlap → merge queue as designed). BiB/single-barrel/barrel-proof
  = name facts or overrides, NOT style codes (founder ruling recorded in the
  ADR-028 addendum).
- **Tests**: clear → reverts to inherited baseline (value/source/confidence
  match a never-overridden drink) + vectors resync on both scales + sibling
  override survives + idempotent re-runs; refusal of a crowd-sourced row +
  loud unknown-ref/key errors; whiskey-national REAL file imports via the
  REAL embedded registry with zero skips, full style+vector coverage,
  overrides sparse, idempotent re-run; registry Fact (no Docker needed) now
  asserts the new tag ships in the binary.
- **Docs**: SEED-FORMAT § Correcting a wrong override; RUNBOOK verbs (clear +
  whiskey-national); ADR-028 addendum (additive, not a new ADR); sprint-4.7
  spec; DATA-SOURCES.md entry.

## Decisions made within spec bounds (log)
- 'inherited' row on clear = no-op, not refusal (see above — the only reading
  where idempotency holds).
- Unknown drink ref / attribute key on clear = loud error, not no-op (a typo
  must not read as "cleared"); idempotency clause applies to keys, not refs.
- `--key` takes the SHORT key exactly as seed files write it; category
  auto-prefixed (consistent with SEED-FORMAT authoring).
- No license gate on clear — it removes data, imports nothing.
- whiskey-national omits drinks corridor already carries, to avoid authoring
  known drink-dupes into the merge queue; producer overlap kept (normal
  cross-source pattern).
- Whiskey-national verification is a CI integration test against the real
  bundled file (this machine has no Docker); VPS run still per RUNBOOK.

## Doc inconsistency to flag (carried)
- Muted-text token: BRAND.md `--bb-text-muted` vs DESIGN-REFERENCE `--bb-muted`
  (alias in place; founder may standardize).

## Environment note (this build machine)
Docker/Node absent: the 3 new Testcontainers tests (clear ×2, whiskey-national
×1) authored-not-run locally; CI runs them for real. Verified locally:
`dotnet build` 0 warnings; `dotnet test` 39 passed / 102 skipped (embedded-
registry Fact incl. the new tag runs locally). Whiskey seed JSON parse +
vocab/range/ref-uniqueness checked by script locally.

## Blockers / needs founder
- Carried from Sprint 4: HUMAN-CHECKLIST 6 (digest physical address + SMTP),
  Gate C2 real-user follow-ups when users exist.
- Carried: VPS bulk seed run (RUNBOOK) — now also includes
  `import products --file seed/whiskey-national.json`; HUMAN-CHECKLIST 7–9
  (Google/Apple/Turnstile creds), 14 (fonts/logo assets), charter proposals
  P1–P3, beer.db call.

## Next session should
- After this merges: Sprint 5 (venues — check-in, personalized menus;
  ADR-015). Carried from 4.5: consider surfacing the rapid-rate doorway on
  the feed's empty states if dogfooding says Search isn't discoverable enough.
- Founder may want to eyeball the 11 whiskey-national override values (pure
  editorial judgment) and the batch-varying representative ABVs.
