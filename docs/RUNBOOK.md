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

# beer.db: REJECTED (founder, 2026-07-10 — stale data fails the quality bar;
# see DATA-SOURCES.md "Rejected sources"). Do NOT run `import beerdb`.

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

# Live-catalog rec-quality eval (Sprint 7): Precision@10 with synthetic
# golden-set personas against the LIVE catalog. Strictly read-only — all
# synthetic rows run inside a rolled-back transaction; there is no commit
# path. Prints one comparable number (Gate C1 fixture baseline: 0.71 —
# compare the trend, don't equate; different catalogs).
… eval recs --out rec-eval.md
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

## Backup / restore (Sprint 7)
The compose `backup` service dumps nightly at `BACKUP_HOUR_UTC` (default 3):
`pg_dump | gzip | openssl AES-256` (passphrase `BACKUP_PASSPHRASE` — REQUIRED
in prod, keep a copy OFF the VPS) into the `backups` volume, pruning past
`BACKUP_RETENTION_DAYS` (30). Off-box copies upload automatically once
`RCLONE_REMOTE` is set (HUMAN-CHECKLIST 10); until then every nightly log
notes the backup is on-box only.

```bash
# Manual dump right now (the service entrypoint is already `bash /backup.sh`,
# so the arg is just `once` — passing `/backup.sh once` would loop-and-sleep)
docker compose -f infra/docker-compose.yml run --rm backup once

# Restore drill: newest dump → scratch container → smoke checks → teardown.
# Never touches the live DB. Writes restore-drill.log (CI runs this per PR).
BACKUP_PASSPHRASE=… ./infra/restore-drill.sh            # newest from volume
BACKUP_PASSPHRASE=… ./infra/restore-drill.sh <file>     # specific dump
```

**Real restore (incident):** stop the api, drill-restore the chosen dump to a
scratch container FIRST to prove it's good, then restore the same SQL into the
live `postgres` service (`psql -U barbrain -d barbrain < dump.sql`), start the
api, check `/health` + `/version`.

**External exposure probe** (run from any non-VPS machine; Gate E item):
```bash
./infra/probe.sh dev.barbrain.co   # expects 80/443 open; 5432/8080/5000 closed
```

## Monitoring (Sprint 7)
- **Uptime:** external monitor on `/health`, rule "down > 2 min" → founder
  (HUMAN-CHECKLIST 15 — must live OUTSIDE the box).
- **Errors:** every unhandled API exception lands as an `error` event
  (first-party, PII-scrubbed, no userId). `ErrorRateAlertService` checks every
  `monitoring.check_minutes` (15) and emails the founder at
  `monitoring.error_spike_threshold` (10) errors/window — throttled to one
  alert/hour; composed-and-logged until SMTP + `monitoring.alert_email` exist.
- **Logs:** Production logs are structured JSON on the container console —
  `docker compose logs api | jq` works.

## Weekly ops (founder, ~10 minutes)
1. Uptime monitor dashboard: any incidents this week? (HUMAN-CHECKLIST 15)
2. `/admin/analytics`: signups, WAU, D30 vs the kill/excellent thresholds.
3. `/admin` moderation tabs: open reports + anomaly flags actioned or cleared.
4. Backups: `docker compose … exec backup ls -lh /backups | tail -5` —
   nightly files present and recent. Once a month, run the restore drill.
5. GitHub: Security workflow green (weekly CVE sweep runs Mondays);
   dependabot PRs reviewed/merged.
6. `docker compose logs api --since 168h | grep -c '"error"'` sanity glance.

## Incident basics
- Logs: `docker compose logs -f api` (structured JSON in Production).
- DB down but app up: `/health` stays `ok`; API calls touching the DB fail.
- Roll back to the last known-good SHA (see Rollback) before deep debugging.
