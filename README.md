# BarBrain

A multi-category beverage rating & discovery PWA (beer / whiskey-bourbon / wine).
Rate drinks 1–5; BarBrain builds per-category flavor profiles, recommends drinks
(including cross-category), passively matches palates, and personalizes venue
menus.

> Read **CLAUDE.md** before contributing — it carries the binding conventions and
> Hard Rules. Architecture lives in `docs/`; the current scope is the latest
> `docs/specs/sprint-N.md`.

## Stack
.NET 10 LTS · ASP.NET Core API (`src/api`) + Blazor WASM PWA (`src/web`) + shared
contracts (`src/shared`) · EF Core + PostgreSQL 16 + pgvector · Docker everywhere
(no IIS). See `docs/ARCHITECTURE.md` and `docs/ADRS.md`.

## Quickstart (local stack)

Prereqs: **Docker Desktop** (WSL2 backend), **.NET 10 SDK**, and **Node 20+**
(for the e2e suite only).

```bash
# 1) Local secrets
cp infra/.env.example infra/.env        # adjust if you like

# 2) Bring up web + api + postgres(pgvector)
docker compose -f infra/docker-compose.yml up --build
```

- Web (PWA): http://localhost:5000
- Health:    http://localhost:5000/health  → `{ "status": "ok", "version": "...", "sha": "..." }`
- Version:   http://localhost:5000/version

The API migrates the database and seeds feature flags on startup. Postgres is
bound to `127.0.0.1:5432` for local tooling only (never exposed in prod).

### Prove the feature-flag pipeline (no redeploy)
The home banner is driven by the `home.banner_text` flag. Flip it via the admin
API and reload the page:

```bash
curl -X PUT http://localhost:5000/api/admin/settings/home.banner_text \
  -H "Content-Type: application/json" \
  -d '{"value":"Flipped without a redeploy."}'
```

> Admin auth is **stubbed** in Sprint 0. With no `ADMIN_TOKEN` set, calls pass
> with a logged warning; set `ADMIN_TOKEN` in `infra/.env` to require the
> `X-Admin-Token` header.

## Develop without Docker (API only)
The API needs a Postgres reachable on `localhost:5432` (the compose `postgres`
service exposes it). Then:

```bash
dotnet run --project src/api      # API on its default Kestrel port
```

The web shell expects the API same-origin; for non-proxied dev, run the web with
`dotnet run --project src/web` and rely on the dev CORS policy.

## Tests

```bash
dotnet test                       # unit + Testcontainers integration (needs Docker)
cd tests/e2e && npm install && npm run install:browsers && npm run e2e
```

- Integration tests use **Testcontainers** (real Postgres + pgvector). Without a
  running Docker daemon they **skip** (so `dotnet test` is green locally); CI
  always runs them.
- The marquee test proves migrate-from-empty creates the schema, the pgvector
  extension, and round-trips rows.

## Database migrations

```bash
dotnet tool restore                                  # one-time: restore dotnet-ef
dotnet dotnet-ef migrations add <Name> --project src/api --output-dir Data/Migrations
```

## Repo layout
```
src/shared   DTOs / API contracts (single source of truth for both ends)
src/api      ASP.NET Core minimal API · EF Core · settings · events
src/web      Blazor WASM PWA shell · --bb-* design tokens
infra        docker compose (+ prod overlay) · Caddyfile · .env.example
tests        BarBrain.Api.Tests (xUnit + Testcontainers) · e2e (Playwright)
docs         PRD · ARCHITECTURE · ADRS · BRAND · design reference · specs · STATE
```

## Brand
All styling flows from the `--bb-*` design tokens
(`src/web/wwwroot/css/design-tokens.css`, mirroring `docs/BRAND.md`). Dark-only
MVP. Self-hosted fonts only (the WOFF2 files are a pending human deliverable —
see `src/web/wwwroot/fonts/README.md`).

## Status
Sprint 0 (foundation) in progress — see `docs/STATE.md`. CI/CD and VPS
deployment are wired up once the VPS is provisioned (HUMAN-CHECKLIST 2–5).
