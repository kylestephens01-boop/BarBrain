# RECONCILIATION NOTES — 2026-07-10
Repo @ 6f2d7a7 (branch sprint-6). One line per discrepancy. Read DECISION
NEEDED first.

## DECISION NEEDED (your call — nothing guessed)
1. **v6 charter text is not available anywhere I can read** — repo
   project-charter.md holds ONLY the P1–P3 proposals and forbids agents from
   reconstructing settled text, and the v6 copy wasn't attached to this task.
   Deliverable 1 is therefore a **v7 UPDATE PACK** (drop-in blocks per
   section), not a full rewritten charter. Merge it into your v6, or re-run
   this task with v6 attached for a true full v7.
2. **Task said ".app registration is still a standing action" — repo says the
   opposite**: barbrain.app was UNAVAILABLE to acquire (charter P1,
   HUMAN-CHECKLIST 4, both dated). Launch TLD is a post-knockout decision.
   Update pack follows the repo; confirm.
3. **"Precision@10 check against 0.71 baseline" isn't runnable and 0.71 isn't
   in the repo.** Golden-set eval is fixture-only (CI threshold ≥0.7,
   sprint-3 spec); a live-catalog eval verb is explicitly unbuilt backlog
   (STATE.md). Where does 0.71 come from, and do you want the verb built
   (STATE's trigger: rec-quality complaint or pre-launch gate)?
4. **"No proof-as-potency editorializing" is not written down anywhere in the
   repo.** Nearest bindings: BRAND.md prohibited-language, DATA-INTAKE §2f.
   If it's a standing rule, pick its home (BRAND.md? DATA-INTAKE?) — left out
   of the v7 pack pending that.
5. **Charter proposals P1–P3 remain unadjudicated** (STATE.md carries them
   every sprint). P1 is folded into the update pack as the domain correction —
   accepting the pack adjudicates P1. P2 (moat reframe) conflicts with
   ADR-022's first sentence and needs a founder-signed ADR amendment. P3 is
   already ADR-025 in substance; closing it is bookkeeping.

## CORRECTED (fixed in the update pack / CURRENT-STATE)
6. "Sprints 0–4.8 complete, Sprint 5 next" → **Sprint 5 MERGED** (PR #12,
   2026-07-10); **Sprint 6 code-complete, PR #13 OPEN, CI currently RED**;
   Sprint 7 is next after the S6 gate.
7. Beer-national "20 drinks / 13 producers" → **24 drinks / 15 producers**
   (seed file count + PR #11 STATE handoff agree).
8. dev.barbrain.app → **dev.barbrain.co** (charter P1, HUMAN-CHECKLIST,
   ARCHITECTURE all agree).
9. Whiskey research report 57 expressions / 18 producer groups → shipped seed
   is **55 drinks / 20 producers** (groups split into distinct producers;
   2 researched expressions not shipped). CURRENT-STATE.md replaces the
   report in project knowledge; the research doc stays in-repo as
   source-of-record.
10. Charter said last ADR era ~015 → ADRs now run **001–029**; 023–029 are
    all post-charter (indexed in CURRENT-STATE.md).

## DRIFT (repo evolved past charter/knowledge without an explicit decision)
11. Task's "beerdb --dir producers import" conflates two sources: the
    PRODUCERS-ONLY import is **openbrewerydb** (MIT); **beerdb** imports
    producers+drinks, is ~2012–13 stale, and is an explicit founder
    judgment call per DATA-SOURCES.md — it's a decision, not a to-do.
12. VPS bulk seed run outstanding since Sprint 1 (RUNBOOK) — whiskey-national
    + beer-national + OBDB have never been imported to the live DB.
13. Sprint 6 assets (15-badge set, moderation, provenance weighting, PWA
    manifest/SW) exist on branch sprint-6 only until PR #13 merges — CI red
    (Build&test + Playwright) as of 2026-07-10; fixing it is the next
    session's first job per STATE.md.
14. Token-name mismatch carried in STATE.md: BRAND.md `--bb-text-muted` vs
    DESIGN-REFERENCE `--bb-muted` (alias in place; standardize when
    convenient).
15. Cloudflare TLS is "Flexible" (dev-only acceptable); ARCHITECTURE.md
    mandates "Full" + origin cert before any public launch — matches your
    deferred-items list, now recorded in CURRENT-STATE.md.
16. Lagunitas IPNA hold is recorded in the PR #11 handoff ("no NA style
    taxonomy — ADR territory if wanted") — deferral confirmed, no ADR exists.

## UNCHANGED
17. **BRAND.md: UNCHANGED — no replacement needed.** Repo copy has exactly one
    commit (1a94de2, 2026-06-17 initial docs pack), zero edits since. Caveat:
    the project-knowledge copy itself wasn't provided to diff byte-for-byte —
    if it predates the docs pack, replace it with repo docs/BRAND.md.

## VERIFY (not fabricated, not resolvable from the repo)
18. v6 changelog-line FORMAT (pack includes the line; format it to match).
19. Source of the 0.71 Precision@10 figure (see item 3).
