# RUNBOOK

Operational playbook for BarBrain. CI and deploy/rollback procedures are drafted
(workflows + scripts in the repo); they go live once the VPS is provisioned and
secrets are set (HUMAN-CHECKLIST 2–5).

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

## Catalog seeding (Sprint 1)
The api binary doubles as the importer CLI (license gate: every source MUST
have an entry in `docs/DATA-SOURCES.md` first — ADR-024). All commands are
idempotent; re-runs create no duplicates.

Local (`dotnet run --project src/api -- <cmd>`) or in the container
(`docker compose -f infra/docker-compose.yml exec -T api dotnet BarBrain.Api.dll <cmd>`;
add the prod overlay `-f` on the VPS):

```bash
# Bundled, offline, license-safe seeds: attribute vocabulary → styles → corridor list
… import bundled

# Open Brewery DB (MIT; PRODUCERS ONLY — ADR-020). Download the CSV first:
curl -L -o /tmp/obdb.csv https://raw.githubusercontent.com/openbrewerydb/openbrewerydb/master/breweries.csv
… import openbrewerydb --file /tmp/obdb.csv

# beer.db (github.com/openbeer — public domain; NOT openbeerdb.com/ODbL).
# License-cleared but 2012–2013 vintage; founder judgment call (DATA-SOURCES.md).
git clone --depth 1 https://github.com/openbeer/us-united-states /tmp/openbeer-us
… import beerdb --dir /tmp/openbeer-us

# TTB COLA sample batch (public domain). Full extraction is deferred background work.
… import ttb-sample --file <sample.csv>

# Generic product-seed file (docs/SEED-FORMAT.md; ADR-028). The file declares
# its own provenance source tag, which MUST already be registered in
# docs/DATA-SOURCES.md — the importer refuses unregistered tags (fail-closed).
… import products --file /data/<batch>.json

# National American whiskey catalog (bundled with the api build; sprint 4.7)
… import products --file seed/whiskey-national.json

# Correct a wrong editorial override (SEED-FORMAT.md § Correcting a wrong
# override): deletes the drink's source='moderator' attribute row for one key
# and resyncs vectors so the dim reverts to style-baseline inheritance.
# Idempotent; refuses non-moderator provenance. --key is the short key.
… import products --clear-attribute --source <seed-tag> --drink-ref <ref> --key <attribute-key>

# Near-duplicate fixtures for the merge-queue demo (CI/e2e use this)
… import demo-dupes

# Seed verification report (counts, coverage %, duplicate-rate estimate)
… report --out seed-report.md
```

Merge review: `/admin/merge-queue` in the web app (admin token). Thresholds
and inherited-value confidence are settings flags (`catalog.*`), editable via
the admin settings API without a deploy.

## VPS provisioning (one-time)
Run on a fresh Ubuntu 24.04 box as root (idempotent — safe to re-run):
```bash
REPO_URL=https://github.com/<owner>/BarBrain.git ./infra/provision.sh
```
Installs Docker + compose, locks the firewall to **22/80/443 only**, enables
fail2ban + unattended security upgrades, and checks the repo out to
`/opt/barbrain`. Then `cp infra/.env.example infra/.env` and fill secrets.

## Dev-site privacy (brand gate)
The dev hostname stays private until the trademark knockout clears. **Primary:**
Cloudflare Access on the dev hostname (HUMAN-CHECKLIST 3) — gates at the edge,
doesn't affect origin `localhost` health checks. **Fallback:** uncomment the
`basic_auth` block in `infra/Caddyfile` (it exempts `/health` + `/version`).

## Deploy (auto on merge to main)
`.github/workflows/deploy.yml`:
1. Builds `api` + `web` images, pushes to GHCR tagged `:latest` and `:<sha>`.
2. SSHes to the VPS, checks out the SHA, `docker compose -f infra/docker-compose.yml -f infra/docker-compose.prod.yml pull && up -d`.
3. Polls `http://localhost/version` on the VPS until the deployed SHA is live;
   fails the deploy if it never appears.

Requires secrets: `VPS_HOST`, `VPS_USER`, `VPS_SSH_KEY`, `GHCR_TOKEN`.

Manual deploy / first deploy / hotfix (run on the VPS in `/opt/barbrain`):
```bash
./infra/deploy.sh prod      # or: preview
```

## Rollback
Re-point the image tags to the previous good SHA and bring the stack back up:
```bash
cd /opt/barbrain
git checkout -q <previous-good-sha>
API_IMAGE=ghcr.io/<owner>/barbrain-api:<previous-good-sha> \
WEB_IMAGE=ghcr.io/<owner>/barbrain-web:<previous-good-sha> \
  ./infra/deploy.sh prod
```
`/health` confirms the rollback SHA. **Database:** migrations are additive; a
code rollback does NOT auto-revert a migration. Undo a migration deliberately
(see below) — never on a whim.

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
