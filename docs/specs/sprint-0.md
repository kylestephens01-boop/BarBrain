# Sprint 0 — Foundation
**Objective:** a deployed hello-world with the full skeleton: anyone merging a PR
sees it live at dev.barbrain.app minutes later.
**Prereqs:** HUMAN-CHECKLIST items 1–5.

## In scope
- Solution skeleton: src/api (minimal endpoint /health, /version), src/web
  (Blazor WASM PWA shell, calls /health), src/shared (first contract).
- Docker: compose for local (api, web, postgres+pgvector) and prod overlay.
- EF Core wired to Postgres; initial empty migration; Testcontainers integration
  test proving migrate-from-empty works.
- Settings/feature-flag system (ADR-006): settings table, typed accessor w/
  cached reads, admin API endpoint (auth stubbed), seed flags file.
- First-party events table + minimal write API (ADR-017) — schema only, no dashboard.
- CI (GitHub Actions): PR pipeline (build, test, Playwright smoke w/ screenshot
  artifact), main pipeline (build images → GHCR → SSH deploy → health check).
- VPS provisioning script (idempotent): docker, compose, Caddy w/ TLS, firewall
  (only 80/443/SSH), fail2ban, unattended-upgrades, Postgres NOT exposed.
- Playwright e2e project: one smoke test (page loads, health OK), screenshot upload.
- README quickstart + docs/RUNBOOK.md skeleton (deploy/rollback).
- Brand foundation: --bb-* design-tokens CSS (per docs/BRAND.md), self-hosted
  Space Grotesk + Inter WOFF2 subsets (preloaded), base layout consuming tokens.
  DARK-ONLY — no light-mode variants.
- CI contrast job: automated WCAG checks against the token file, INCLUDING
  text-on-surface pairs (e.g., muted on back-bar), not just background pairs.
- Dev-site privacy: Cloudflare Access or basic auth on dev.barbrain.app until
  the trademark knockout clears (brand gate).

## Acceptance criteria
- `docker compose up` locally → web at :5000 talking to api → green smoke e2e.
- PR triggers CI; screenshots visible as artifacts; failing test blocks merge.
- Merge to main auto-deploys; https://dev.barbrain.app/health returns version+sha.
- Settings flag flip via API changes a visible string on the home page (proof of
  the flag pipeline) without redeploy.
- Restore-from-empty migration test green in CI.
- Contrast job green in CI; dev site unreachable without auth.

## Out of scope
Any product feature. Auth. Real schema. Styling beyond a placeholder page.

## Gate (founder, ~10 min, phone)
Open dev.barbrain.app — page loads w/ version. Check a PR: screenshots present,
CI required. Flip the demo flag via admin endpoint (curl/Postman from desktop or
ask agent to demo in PR video/gif) — page text changes. Approve → Sprint 1.
