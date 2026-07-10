# DATA-INTAKE — research → seed playbook (BINDING for data batches)

> How a first-party research document in `docs/research/` becomes imported
> catalog data. Established across whiskey-national (sprint 4.7, the worked
> reference) and beer-national; future categories (wine, additional batches)
> follow this instead of re-deriving the rules. Companion docs:
> docs/SEED-FORMAT.md (file shape + import semantics), docs/DATA-SOURCES.md
> (license registry, ADR-024), ADR-028 (provenance + overrides).

## 1. What a research doc must already satisfy

A research doc is intake-ready only if:

- **First-party sourcing only**: producer official sites, official press
  releases, label/regulatory statements. NEVER competitor rating/aggregator
  databases (Untappd, Vivino, Distiller, RateBeer, BeerAdvocate, …) — Hard
  Rule 1, no exceptions, not even "just to double-check".
- **Three-tier confidence on every entry/numeric field**:
  - **CONFIRMED** — value seen on the producer's own domain, an official
    press release, or a label image.
  - **VERIFY** — the producer sells the item, but the exact numeric was not
    captured from a qualifying first-party page.
  - **UNCONFIRMED** — no qualifying first-party source. (Common for IBU-type
    figures that live only on aggregators.)
- Promotion/demotion is evidence-driven: VERIFY→CONFIRMED only on a
  first-party sighting; CONFIRMED→VERIFY when a producer reformulates or
  respecs (Fat Tire, Jan 2023, is the cautionary example).

## 2. Authoring checklist (in order)

a. **Register the tag first (ADR-024).** Add `seed:<batch>` to
   docs/DATA-SOURCES.md — quoted, exactly as the gate matches it — in its own
   commit BEFORE any data is authored against it. The registry is embedded in
   the api binary, so the batch needs the rebuild anyway.

b. **Confidence gates what ships.**
   - CONFIRMED entries → full drinks (name, ABV, style).
   - VERIFY entries → an explicit founder call per batch: include-with-flag
     or hold. NEVER decided silently; batch the question (see §3).
   - UNCONFIRMED numeric fields are NEVER imported. An UNCONFIRMED entry
     ships only if its unconfirmed fields can be omitted entirely (e.g. drink
     with null ABV) AND the founder approves; otherwise hold.

c. **ABV at ONE decimal** — the column is `numeric(4,1)`; finer values round
   on write and then break the all-unchanged idempotency contract on every
   re-run (the 46.85 lesson, sprint 4.7). Batch-varying values (barrel proof,
   market-dependent) use a representative producer-published figure, marked
   inline with a comment.

d. **Style mapping**: map to EXISTING style codes in the styles table
   (BJCP-derived for beer, WH-*/WN-* for whiskey/wine). A mapping is "clean"
   when the producer's own words name the style or an unambiguous leaf
   exists. Anything else — marketing styles ("session IPA"), process facts
   dressed as styles (gluten-reduced, non-alcoholic), hybrid/anniversary
   one-offs — is flagged for the founder batch, NEVER auto-mapped and NEVER
   solved by inventing a style code. Designations like bottled-in-bond /
   single-barrel / barrel-proof are name facts or attribute overrides, not
   taxonomy (founder ruling, ADR-028 addendum).

e. **Attribute overrides sparingly.** Only expressions that meaningfully
   deviate from their style baseline (barrel-proof strength, unusual
   finishes, peated malt, coffee/fruit adjuncts). Most drinks ship with pure
   style inheritance. whiskey-national ran 20% of drinks — treat that as the
   ceiling, not the target; bulk/generic entries should be at or near 0%.

f. **Non-numeric label statements** ("100+ IBU", "<0.5% ABV", "108–114
   proof") get an explicit per-field decision recorded in the batch — never
   silent truncation into a plausible-looking number. If the field isn't in
   the seed format at all (e.g. per-drink IBU), say so and leave it in the
   research doc only.

g. **Verify by import.** `import products --file <path>`, then the
   seed-verification report: expected per-source producer/drink counts, 100%
   vector coverage for styled drinks, override rows under `moderator` at the
   expected confidence, and an idempotent re-run (all-unchanged, no row
   growth). No local Docker → encode this as a Testcontainers integration
   test against the real bundled file (whiskey-national pattern) so CI proves
   it, and add the batch to the CI smoke seed step for demo/report parity
   (sprint 4.8 pattern).

h. **Cross-source overlap belongs to the merge queue.** Drinks another
   bundled seed already carries are omitted from the new file (the catalog is
   the union of seeds; re-listing authors known duplicates). Producer overlap
   is expected and handled by merge candidates — NEVER hand-dedupe by editing
   or deleting another source's rows.

## 3. Batch the open decisions

Collect every VERIFY call, non-clean mapping, and non-numeric field decision
and present them to the founder in ONE batch (with a recommendation each),
rather than stopping serially. Record the rulings in the PR and STATE.md.

## 4. Session prompt template

```text
Data intake: author seed/<batch>.json from docs/research/<research-doc>.md
per docs/DATA-INTAKE.md. Branch off main (data-<batch>).

- Source tag: seed:<batch> — register in docs/DATA-SOURCES.md FIRST (ADR-024).
- Author CONFIRMED entries with clean style mappings without waiting.
- Then STOP and batch all open decisions (VERIFY entries with a
  recommendation each, non-clean style mappings, non-numeric fields) before
  finishing.
- After rulings: finish the seed, add/extend the Testcontainers verification
  test against the real bundled file, add the batch to the CI smoke seed
  step, update STATE.md, commit in logical chunks (registration / seed data /
  tests+CI / STATE), push, open a PR, report CI status.
- Scope: docs + seed data only; no importer changes (flag gaps, don't build
  silently); don't touch other sources' data.
```
