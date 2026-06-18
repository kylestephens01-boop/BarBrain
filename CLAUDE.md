# CLAUDE.md — BarBrain Agent Conventions

A multi-category beverage rating & discovery PWA (beer / whiskey-bourbon / wine).
Users rate drinks 1–5; the system builds per-category flavor profiles, recommends
drinks (including cross-category), passively matches palates, and personalizes
venue menus. Solo founder + AI agents. Read this file fully before any work.

## Stack (settled — do not relitigate)
- .NET 10 LTS. ASP.NET Core API (`src/api`) + standalone Blazor WASM PWA (`src/web`)
  + shared contracts (`src/shared`). EF Core + PostgreSQL 16 + pgvector.
- Docker everywhere. Local dev = `docker compose up`. Prod = same compose on a
  Hetzner VPS. There is NO IIS anywhere in this project.
- CI/CD = GitHub Actions: build → tests → Playwright e2e (screenshots as artifacts)
  → deploy to preview on merge to main.

## Repo layout
- `src/api` ASP.NET Core API · `src/web` Blazor WASM PWA · `src/shared` DTOs/contracts
- `infra/` compose files, deploy scripts · `docs/` PRD, ARCHITECTURE, ADRS, STATE
- `docs/specs/sprint-N.md` — the spec for each sprint. The CURRENT spec is your scope.

## Workflow (every session)
1. Read `docs/STATE.md`, then the current `docs/specs/sprint-N.md`.
2. Work on branch `sprint-N` (or `sprint-N-fix-*`). NEVER commit to main.
   Main is branch-protected; all merges via PR.
3. Conventional commits. Small, coherent commits as you go.
4. Tests accompany features. CI must be green before you consider work done.
5. End of session: update `docs/STATE.md` (done / in-progress / blockers / next).
6. PR description: what shipped, decisions made within spec bounds, screenshots,
   anything deferred and why.

## Definition of done (per sprint)
- All acceptance criteria in the spec demonstrably met.
- `dotnet test` green; Playwright e2e green with screenshots attached in CI.
- Migrations apply cleanly to an empty database AND to the previous sprint's schema.
- No out-of-scope work. The spec's OUT OF SCOPE list is binding.
- STATE.md updated; PR opened with summary + screenshots.
- Accessibility baseline holds: semantic HTML, labeled inputs, sane contrast,
  keyboard-operable core flows (signup, rate, check-in). Not optional polish.

## Hard rules (NEVER — no exceptions, no creative interpretations)
1. NEVER scrape, import, or reference competitor databases or curated content
   (Untappd, Vivino, Distiller, RateBeer, etc.). Seed sources are listed in specs.
2. NEVER store full date of birth. Birth year + attestation timestamp only.
3. NEVER store IP addresses beyond transient abuse-control needs; no long-term IP.
4. NEVER build mechanics that reward consumption volume or frequency (no daily
   streaks, no per-session counts, no "drink more" framing in any copy).
5. NEVER add real-name fields. Identity is pseudonymous handle + optional display name.
6. NEVER export, sell, or log user-level behavioral data externally. Aggregate only.
7. NEVER weaken the 21+ gate. Every signup path (incl. OAuth) must capture DOB
   before account activation.
8. NEVER expose Postgres publicly or commit secrets. Secrets live in env/GH secrets.
9. NEVER push to main or alter branch protection / CI gates.
10. Anything phase-dependent (display modes, thresholds, prompts) ships as a
    config flag in the settings system, not hardcoded.
11. The brand prohibited-language list (docs/BRAND.md) is binding on ALL copy —
    UI, badge names, notifications, store listings, marketing: no volume/
    intoxication framing or jokes, no health/benefit claims about alcohol,
    no "drink more" CTAs. Badges reward breadth and discovery, never volume.

## Brand (binding — see docs/BRAND.md)
- **Design reference: docs/design/DESIGN-REFERENCE.md is the visual contract.**
  Build toward the approved screen specs and component inventory. When a sprint
  spec says WHAT to build, the design reference shows WHAT IT LOOKS LIKE.
  Screenshot PNGs in docs/design/screens/ are the acceptance targets.
- All styling flows from the --bb-* design tokens file. No hardcoded colors/fonts.
- Two-temperature grammar: amber (--bb-pour) accents BEVERAGE objects only;
  teal (--bb-synapse) accents RECOMMENDATION/INTELLIGENCE objects only. Never
  swapped, never decorative.
- MVP is DARK-ONLY (--bb-ink base). Light mode is deferred until it gets its
  own contrast pass — do not build light variants.
- Fonts: Space Grotesk (display) + Inter (UI), self-hosted WOFF2 only. Never
  load fonts from a CDN. Two weights each (400/500).
- The wordmark's amber "Brain" is a documented deliberate exception to the
  color grammar. Do not "fix" it.

## Commands
- `docker compose up -d` — full local stack (API, web, Postgres+pgvector)
- `dotnet test` — unit + integration suites
- `dotnet ef migrations add <Name> -p src/api` — schema changes
- `npm run e2e` (in `tests/e2e`) — Playwright suite
- `./infra/deploy.sh preview` — manual preview deploy (CI normally does this)

## When uncertain
Spec ambiguity → choose the simplest interpretation consistent with the PRD and
ADRs, note it in the PR. Conflict with an ADR → STOP and flag in STATE.md; do not
override architecture decisions. Anything touching the Hard Rules → stop and ask.
