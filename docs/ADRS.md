# Architecture Decision Records
Short form. Status: all ACCEPTED (founder-confirmed, June 2026). Do not reverse
without a new ADR and founder sign-off.

**ADR-001 — .NET 10 LTS, Blazor WASM PWA + separate ASP.NET Core API.**
Max founder-stack reuse; cleanest separation; Razor components lift into MAUI
Blazor Hybrid later. PWA-first sidesteps app-store alcohol review during validation.

**ADR-002 — Single PostgreSQL 16 + pgvector for everything.**
Relational + vector similarity in one engine; HNSW covers far beyond MVP scale.
No separate vector DB, no ML infra.

**ADR-003 — Docker everywhere; no IIS.**
Dev/prod parity (Win11+WSL2 compose ↔ VPS compose). pgvector friction disappears;
deploy story is one set of compose files.

**ADR-004 — Hetzner VPS from day one as CI deploy target.**
Stable preview URL enables phone-first gate reviews; no personal-machine exposure;
~€5/mo. Cloudflare in front.

**ADR-005 — Private monorepo, personal account → org transfer pre-launch.
Branch protection; agents merge via PR only; CI required.**

**ADR-006 — DB-backed feature-flag/settings system in the foundation.**
Phase-dependent behavior (match-% display, thresholds, prompts) is config, not
code. Origin: founder's "eager now, conservative at scale" requirement.

**ADR-007 — Hand-rolled CF v1 (Pearson, mean-centered, nightly batch).**
Transparent, sufficient at MVP scale. Upgrade path: ML.NET Matrix Factorization.
Deep learning deferred indefinitely.
*Amended by ADR-025 (July 2026): CF is DEFERRED in sequencing — pgvector
attribute similarity ships first; this ADR's design stands for when CF lands.*

**ADR-008 — Canonical drink = (producer, product, category).**
Package format = rating metadata. Wine vintage = rating metadata, single entity.
ABV = metadata + dedup signal, not identity. Minimizes dedup surface.

**ADR-009 — 8 attribute dims per category; 6-dim cross-category bridge**
(sweetness, bitterness/tannin, body, smoke, fruit, acidity). Stored 0–1,
displayed 0–10. Style-baseline inheritance + per-drink overrides w/ provenance
+ confidence.

**ADR-010 — Pseudonymous identity; birth-year-only persistence.**
No real-name fields. Full DOB captured transiently for the 21+ gate, never stored.

**ADR-011 — Auth: email+password, Google, Apple; soft verification.**
Apple now avoids native-wrap rework (store rule). OAuth paths capture DOB
post-auth before activation. Rate immediately; verify ≤7 days.

**ADR-012 — Ratings pseudonymous-public by default w/ per-rating private toggle;
history kept** (re-rating appends; engine uses most recent).

**ADR-013 — Sectioned rec feed:** Up Your Alley / Stretch a Little / Wildcard
(+ Loved by Your Matches). No hidden blend dial; confidence-adaptive wildcards;
every rec carries its "because." Mirrors venue four-shelf vocabulary.

**ADR-014 — Match score = blended (attribute similarity + co-rating agreement,
density-weighted); named matches** (handle + %, hide-me toggle, one-way only).
Eager-labeled display behind a flag with conservative mode for scale.

**ADR-015 — Check-in is the session primitive; Home Bar is a private virtual
venue** auto-created per user, default rating location, excluded from discovery.
Venue menus personalize on check-in (teaser before). No GPS proximity v1.
Backlog: Home Bar inventory/library.

**ADR-016 — No consumption-volume/frequency incentives, ever.**
Weekly streaks only; distinct-drink counts only. Ethics + Apple 1.4.3 alignment.

**ADR-017 — First-party analytics only:** events table in our Postgres + admin
retention dashboard. No third-party trackers.

**ADR-018 — Deletion: user chooses full delete vs anonymize-contributions;
PII deleted either way.** Self-serve JSON export.

**ADR-019 — Weekly email digest only in MVP** (no web push; iOS PWA push is
install-gated/flaky). Blocks config-flagged.

**ADR-020 — Seeding strategy: local-depth.** Corridor-priority product coverage
before national breadth. Open Brewery DB = producers only (it has no products).

**ADR-021 — Brand system v1; dark-only MVP.**
Tokens (--bb-*) single styling source from Sprint 0; two-temperature grammar
(amber=beverage, teal=intelligence); Space Grotesk + Inter self-hosted; light
mode deferred pending its own contrast pass; dev site behind auth until the
trademark gate clears. Wordmark amber-"Brain" = documented exception.

**ADR-022 — Moat posture: data asset first, venues as revenue proof.**
The cross-category attribute dataset is the only compounding asset (grows
without founder hours); corridor venue density is the defensible revenue
proof. Sequence: M1–4 maximize data machinery; M4–8 founder hours to venues.
Data-asset metrics dashboarded beside retention. Untappd cross-category launch
triggers RE-EVALUATION, not auto-fold, if venue renewals or dataset depth hold
value. Home Bar library deliberately remains distant backlog (founder, 2026-06-11).
*Note: a moat REFRAME (operational corridor moat primary, dataset supporting)
is PROPOSED in docs/project-charter.md and awaits founder review; this ADR is
unchanged until that is adjudicated.*

**ADR-023 — Licensing-safe style modeling (founder decision, July 2026).**
No BJCP (or other) guideline prose is ingested or stored — copyright-
incompatible with a revenue app. Styles carry ONLY: name, style/category code,
and structural numeric parameters (ABV/IBU/SRM/OG/FG ranges), plus BarBrain's
own original flavor-attribute vocabulary and editorial baseline values. The
`styles` table deliberately has no description column; rich descriptive text
may be added later ONLY under explicit permission (additive migration).

**ADR-024 — Data-source license gate (founder decision, July 2026).**
docs/DATA-SOURCES.md is a binding registry: every external dataset gets an
entry (exact upstream URL, license, quoted wording, capture date) BEFORE any
ingestion. Share-alike (ODbL etc.) or ambiguous licensing → STOP and flag the
founder. Known trap: openbeerdb.com (ODbL, prohibited) is a DIFFERENT project
from github.com/openbeer aka geraldb beer.db (public domain, permitted).
Open Brewery DB (MIT, producers only) and TTB COLA (US-gov PD) pre-approved
with attribution. Competitor databases never (Hard Rule 1).

**ADR-025 — pgvector attribute similarity is the PRIMARY rec mechanism; CF
deferred (founder decision, July 2026).** Per-drink attribute vectors (8-dim
per category + 6-dim cross-category bridge, ADR-009) power recommendations
from day one: relational `drink_attributes` is the auditable source of truth;
derived `vector` columns (HNSW, cosine) serve similarity. Collaborative
filtering (ADR-007) is deferred — no CF tables/jobs now. The upgrade path CF
needs is preserved by design: append-only rating history (ADR-012) and the
first-party events table (ADR-017) supply the co-rating matrix later without
schema rework.

**ADR-026 — Ownership + visibility columns from day one (founder decision,
July 2026).** Every user-ownable entity ships in its INITIAL schema with
`created_by_user_id` (FK to `users`) and `visibility`
('public'|'private'), enforced by DB constraints — not app checks alone —
including `CHECK (created_by_user_id IS NOT NULL OR visibility = 'public')`
(ownerless/imported rows cannot be private). A minimal pseudonymous `users`
stub table (id, nullable unique handle, created_at — zero PII, zero auth
columns) exists so these FKs are real; Sprint 2 extends it additively and
enforces authz against these exact columns.

**ADR-027 — Cross-category bridge recs are REQUIRED in the first rec release;
rec quality is gated by a golden-set eval harness (founder decision, July 2026).**
The 6-dim bridge (ADR-009) informing recs ACROSS categories is the moat
mechanic, promoted from stretch to a Sprint 3 deliverable with an explicit
eval test (single-category smoky palate must surface smoky drinks in another
category). CF stays deferred per ADR-025 — no CF tables or user-user matrices;
the append-only ratings history remains the upgrade path. Because rec quality
is invisible to screenshot review, a golden-set eval suite (fixed synthetic
personas with known preference vectors → asserted rec ordering + threshold
metrics) runs in CI and BLOCKS merge on regression. Every recommendation
carries a human-readable "because" (hard product requirement, ADR-013).
