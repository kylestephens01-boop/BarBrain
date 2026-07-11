# Project Charter — BarBrain — v7 UPDATE PACK
**2026-07-10 · prepared for merge into charter v6 (2026-06-11)**

> **Why an update pack, not a full v7:** the settled v6 charter text is not in
> the repo — docs/project-charter.md carries ONLY the P1–P3 proposals and
> explicitly instructs agents not to reconstruct settled text, and the v6 copy
> was not provided to this session. Rather than fabricate structure and voice,
> this pack gives drop-in replacement blocks for each section named in the
> reconciliation brief. Every fact below is verified against repo state as of
> commit 6f2d7a7 (branch sprint-6). Merge each block into v6 where indicated,
> bump the version line, append the changelog line.

---

## 0. Version + changelog line (append)

v7 — 2026-07-10 — Reconciliation: Sprints 0–5 shipped, Sprint 6 in PR review;
domain corrected to barbrain.co; seeding section rewritten (national catalogs +
provenance separation); data-assets line added under moat; sourcing constraints
hardened into standing rules (ADRs 023/024/028); open questions closed per
ADRs 023–029.

*(Format the line to match v6's changelog convention — VERIFY, format not
visible from the repo.)*

## 1. Status line — REPLACE

Status: BUILD. Sprints 0–5 merged (foundation → catalog+seeding →
identity/auth → palate engine + recs → matching + digest → venues/check-in).
A 4.x mini-sprint series (4.5–4.8) shipped between S4 and S5. Sprint 6
(gamification, moderation, PWA polish) is code-complete in PR review (PR #13,
CI red as of 2026-07-10). Next: Sprint 6 gate, then Sprint 7 (pre-launch
hardening).

## 2. Domain — REPLACE the domain-portfolio line

BarBrain operates on **barbrain.co** (owned; dev host `dev.barbrain.co` behind
Cloudflare Access). **barbrain.app was unavailable to acquire.** The
public-launch TLD — staying on .co vs acquiring an alternative
(.io/.ai/getbarbrain.com) — is a post-trademark-knockout decision, deliberately
deferred until the name itself is cleared.

*(This is proposal P1 in docs/project-charter.md, verbatim in substance. It is
a factual correction, but it formally awaits founder adjudication alongside
P2–P3 — merging it here IS the adjudication of P1 if you accept it.)*

## 3. Database seeding — REPLACE section

Corridor-priority local depth (ADR-020) remains the spine: what is actually
poured in the Cedar Rapids–Iowa City corridor seeds first. National breadth
now exists ALONGSIDE it — founder-authored, first-party-sourced catalog
batches (whiskey-national, beer-national) flowing through one generic importer
(ADR-028) under the research→seed playbook (docs/DATA-INTAKE.md).

Standing constraint — provenance-tag separation: every batch declares its own
`seed:*` source tag; the catalog is the union of seeds; cross-source overlap
resolves in the merge queue, never by editing or deleting another source's
rows. The license gate (docs/DATA-SOURCES.md, ADR-024) is machine-enforced:
the registry is embedded in the api binary and unregistered source tags refuse
to import, fail-closed.

## 4. Development plan — EDIT

- Mark Sprints 0–4 DONE. Also mark Sprint 5 (venues & check-in — Gate D) DONE
  (merged 2026-07-10).
- Add one line: *A 4.x mini-sprint series (4.5 rapid-rate surface, 4.6 generic
  importer + license-gate hardening, 4.7 whiskey-national catalog +
  override-clear verb, 4.8 CI/demo seed parity) shipped between S4 and S5
  without disturbing the sprint sequence.*
- Sprint 6 (gamification/moderation/PWA) in PR review; Sprint 7 (pre-launch
  hardening) next.

## 5. Moat strategy — ADD "Data assets" line item

Data assets (bundled seed files, counted 2026-07-10):
`seed:corridor` 62 drinks / 35 producers · `seed:whiskey-national` 55 drinks /
20 producers · `seed:beer-national` 24 drinks / 15 producers — 141 drinks /
70 producer entries total (cross-source producer overlap merges in-app).
Style taxonomy (`seed:barbrain-styles`, facts-only per ADR-023): 140 beer /
21 whiskey / 36 wine styles. External producer sets (Open Brewery DB, MIT) are
import-ready but not yet run — the VPS bulk seed run is still outstanding.

*(Do not touch the moat ARGUMENT here: the P2 reframe — operational corridor
moat primary, dataset supporting — is still pending adjudication and conflicts
with ADR-022's first sentence until you rule.)*

## 6. Standing constraints — ADD (hardened into rules since v6)

- **License gate (ADR-024/028):** docs/DATA-SOURCES.md is a binding,
  machine-enforced registry — entry with exact URL, license, quoted wording,
  capture date BEFORE any ingestion; share-alike/ambiguous = stop.
- **First-party-only sourcing (docs/DATA-INTAKE.md):** producer sites, official
  press releases, label/regulatory statements only; three-tier confidence
  (CONFIRMED / VERIFY / UNCONFIRMED); UNCONFIRMED numerics are never imported.
- **BJCP names/codes/numeric ranges only (ADR-023):** zero guideline prose is
  ingested or stored; the styles table has no description column; attribute
  baselines are BarBrain-original editorial data.
- **Taxonomy is closed to designation creep (ADR-028 addendum):**
  bottled-in-bond / single-barrel / barrel-proof are name facts or per-drink
  attribute overrides, never new style codes.
- ~~"No proof-as-potency editorializing"~~ — **VERIFY before adding.** This
  rule is not recorded verbatim anywhere in the repo. Nearest bindings:
  BRAND.md prohibited-language (no intoxication framing) and DATA-INTAKE §2f
  (non-numeric label statements get explicit per-field decisions). If it is a
  standing rule, it needs a written home first.

## 7. Open questions — CLOSE / UPDATE

Closed since v6, each with a recorded decision:
- Rec-engine sequencing → CLOSED: pgvector attribute similarity primary, CF
  deferred (ADR-025); CF subsequently landed in S4 as a density-gated blend
  partner, not a standalone recommender (ADR-007/014 realized). *(= proposal
  P3; merging this closes P3.)*
- Cross-category recs → CLOSED: bridge recs required in the first rec release;
  golden-set eval harness gates CI (ADR-027).
- Style-guideline licensing → CLOSED: facts-only, no prose (ADR-023).
- External-dataset licensing → CLOSED: binding registry + fail-closed gate
  (ADR-024).
- Product-seed architecture → CLOSED: one generic importer, per-file
  provenance, moderator-sourced editorial overrides, CLI clear verb (ADR-028
  + addendum).
- QR one-pager tooling → CLOSED: QuestPDF Community license; revisit before
  $1M revenue (ADR-029).

Still open:
- Charter proposals P1–P3 adjudication (P2 conflicts with ADR-022's first
  sentence — founder-signed amendment required if accepted).
- Public-launch TLD (post-knockout decision).
- beer.db ingestion worth-it call (license-cleared, ~2012–13 vintage —
  founder judgment, DATA-SOURCES.md).
- Trademark knockout → coined-mark fallback (Likli/Savry) if it fails.
- Live-catalog rec-quality eval verb (backlog; trigger = rec-quality complaint
  or pre-launch gate — STATE.md).
