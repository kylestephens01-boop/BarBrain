# RUNBOOK

Operational playbook for BarBrain. Skeleton as of Sprint 0; deploy/rollback
sections are filled in when the VPS is provisioned (HUMAN-CHECKLIST 2–5) and CI
is wired up.

## Topology (target)
Single Hetzner VPS running docker compose: `web` (Caddy → Blazor WASM static +
API reverse-proxy, TLS), `api` (ASP.NET Core), `postgres` (16 + pgvector),
`backup` (nightly pg_dump). Cloudflare in front (DNS, proxy, TLS).
See `docs/ARCHITECTURE.md`.

## Local stack
```bash
cp infra/.env.example infra/.env
docker compose -f infra/docker-compose.yml up --build
```
Health: http://localhost:5000/health · stop: `docker compose -f infra/docker-compose.yml down`
(add `-v` to wipe the Postgres volume).

## Health checks
- `GET /health` → `{ status, version, sha }`. Liveness; no DB dependency, so it
  stays green during a DB blip (distinguishes "app down" from "db down").
- `GET /version` → `{ version, sha }`. Confirms which build is live.
- The deploy gate polls `/health` and asserts the expected `sha`.

## Deploy (preview / prod) — TODO (pending VPS + CI)
> Filled in with the CI pipeline. Intended shape:
> 1. CI builds `api` + `web` images, pushes to GHCR tagged with the commit SHA.
> 2. SSH to the VPS; `docker compose pull && docker compose -f infra/docker-compose.yml -f infra/docker-compose.prod.yml up -d`.
> 3. Wait for `/health` to report the new SHA; fail the deploy if it doesn't.
> Manual escape hatch: `./infra/deploy.sh preview` (script TODO).

## Rollback — TODO (pending VPS + CI)
> Intended shape: re-point the `api`/`web` image tags to the previous good SHA
> and `up -d` again; `/health` confirms the rollback SHA. Database: migrations
> are additive; a rollback of code does NOT auto-revert a migration. If a
> migration must be undone, do it deliberately (see below) — never on a whim.

## Migrations
```bash
dotnet tool restore
dotnet dotnet-ef migrations add <Name> --project src/api --output-dir Data/Migrations
```
- The API applies migrations on startup (`Database:MigrateOnStartup`, default on).
- Migrations must apply cleanly to an empty DB AND to the previous sprint's
  schema (Definition of Done). The Testcontainers test proves migrate-from-empty.
- To apply manually: `dotnet dotnet-ef database update --project src/api`.

## Secrets
- Local: `infra/.env` (gitignored). Never commit secrets (Hard Rule 8).
- Prod: VPS `.env` + GitHub Actions secrets (`VPS_HOST`, `VPS_SSH_KEY`, GHCR
  token). Postgres is never exposed publicly.

## Feature flags
DB-backed (`settings` table, ADR-006). Read/flip via `GET|PUT /api/admin/settings`.
A flip takes effect on the next read (cache invalidated on write) — no redeploy.
Auth is stubbed in Sprint 0; gate with `ADMIN_TOKEN`.

## Backup / restore — TODO (Sprint 7)
Nightly encrypted `pg_dump` → object storage, 30-day retention. Restore drill is
a Sprint 7 acceptance criterion; the step-by-step restore goes here then.

## Incident basics
- Logs: `docker compose logs -f api` (structured JSON via Serilog once added).
- DB down but app up: `/health` stays `ok`; API calls touching the DB fail.
- Roll back to the last known-good SHA (see Rollback) before deep debugging.
