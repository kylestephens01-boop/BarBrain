# Architecture

## Topology
Single VPS (Hetzner CX22) running Docker Compose: `api` (ASP.NET Core, Kestrel),
`web` (Blazor WASM static assets served via Caddy/nginx container with TLS),
`postgres` (16 + pgvector), `backup` (nightly pg_dump → object storage).
Cloudflare in front (DNS, proxy, TLS). Local dev = identical compose on
Win11 + WSL2 + Docker Desktop.

## Projects
- `src/shared`: DTOs, API contracts, enums — single source of truth for both ends.
- `src/api`: minimal-API or controller endpoints; EF Core; FluentValidation;
  background jobs (nightly CF batch, digest sender, badge evaluator) via hosted
  services — no external queue at MVP scale.
- `src/web`: Blazor WASM standalone PWA. Razor components organized for later
  reuse in .NET MAUI Blazor Hybrid (no browser-only assumptions in components;
  platform services behind interfaces).

## Data layer
PostgreSQL 16 + pgvector (HNSW). Core tables: producers, drinks, styles,
style_attributes, drink_attributes (value+source+confidence), users, ratings
(score, note, visibility, location_context, provenance), venues (type: public|
home_bar), venue_menu_items, checkins, matches (nightly materialized), badges,
badge_awards, events (first-party analytics), settings (feature flags), 
merge_queue, reports. Attribute vectors stored both relationally (auditable) and
as pgvector columns (fast similarity).

## Recommendation/matching jobs
Nightly hosted-service batch: recompute palate profiles (incremental), user-user
CF neighborhoods, match scores, badge awards, digest queue. All pure C# + SQL;
no ML infra. Upgrade path: ML.NET Matrix Factorization if CF quality plateaus.

## Feature flags / settings
DB-backed `settings` table + admin UI + in-memory cache w/ short TTL. All
phase-dependent behavior (match-% display mode, thresholds, quiz prompts,
wildcard aggressiveness, digest blocks) reads flags.

## CI/CD (GitHub Actions)
PR: restore→build→unit+integration (Testcontainers Postgres)→Playwright e2e
against compose stack→upload screenshots+coverage. Merge to main: build images
→ push GHCR → SSH deploy to VPS → health check → smoke e2e. Branch protection:
PRs only, CI required.

## Environments
local (compose) · preview/prod (same VPS, dev.barbrain.app → prod domain at
launch; promote by tag). Secrets: GH Actions secrets + .env on VPS (never in repo).

## Observability
Structured JSON logs (Serilog) · error tracking w/ PII scrubbing · uptime ping ·
alerts on down/error-spike only. First-party events table powers product metrics.

## Backup/DR
Nightly pg_dump → object storage (encrypted), 30-day retention; restore runbook
in docs/RUNBOOK.md; restore drill = Sprint 7 acceptance criterion.
