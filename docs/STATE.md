# STATE — agent session handoff
> Agents: update this at the END of every session. Keep it short and factual.

## Current sprint
Sprint 0 — Foundation. Local development stack built; CI/CD + VPS deploy now
DRAFTED (workflows + scripts in repo). The PR pipeline runs on GitHub-hosted
runners with no VPS, so it gates PRs immediately; the deploy pipeline goes live
once the VPS + secrets land (HUMAN-CHECKLIST 2,3,5).

## Done
- Solution skeleton (`.slnx`): `src/shared` (contracts), `src/api` (minimal API),
  `src/web` (Blazor WASM PWA), `tests/BarBrain.Api.Tests`.
- API endpoints: `GET /health`, `GET /version` (version+sha via BuildInfo),
  `GET /api/config/home` (flag-driven), `GET|PUT /api/admin/settings/*`
  (auth STUBBED), `POST /api/events`.
- EF Core + Npgsql + pgvector. `AppDbContext` (settings, events) enables the
  `vector` extension. InitialCreate migration generated.
- Feature-flag system (ADR-006): cached typed accessor (string/bool/int, 30s TTL,
  invalidate-on-write), seed file `src/api/seed/feature-flags.json`, startup
  seeder (inserts missing keys only — never clobbers operator changes).
- First-party events table + write endpoint (ADR-017), schema-only (no dashboard).
- Web shell: dark-only, `--bb-*` design tokens + `@font-face` wiring + base layout.
  Home page renders the wordmark, the flag-driven banner, and the live API
  health line. Bootstrap and template demo pages removed.
- Docker: `src/api/Dockerfile`, `src/web/Dockerfile` (Caddy serves WASM +
  reverse-proxies the API → same-origin, no CORS), `infra/docker-compose.yml`
  (api/web/postgres+pgvector), `infra/docker-compose.prod.yml` (overlay skeleton),
  `infra/Caddyfile`, `infra/.env.example`, root `.dockerignore` + `.gitignore`.
- Tests: Testcontainers integration suite (migrate-from-empty proves schema +
  pgvector + round-trip; settings cache/typed accessors; end-to-end flag-flip;
  events). Health/version tests run without Docker. Docker tests skip gracefully
  when no daemon (SkippableFact) so `dotnet test` is green locally.
- Playwright e2e project (`tests/e2e`): smoke (page loads, health ok, screenshot
  artifact) + config targeting the compose stack.
- Docs: root `README.md` (quickstart) + `docs/RUNBOOK.md` (provisioning, deploy,
  rollback, dev-site privacy now filled in).
- CI/CD drafted: `.github/workflows/ci.yml` (PR pipeline — build+test incl.
  Testcontainers integration, dedicated contrast job, Playwright e2e w/ screenshot
  artifacts; runs on GitHub runners, NO VPS needed) and `.github/workflows/
  deploy.yml` (main — build/push GHCR images, SSH deploy, SHA health check;
  secret-gated). Contrast enforcement is a C# test (`ContrastTests`, Category
  trait) parsing design-tokens.css, incl. text-on-surface pairs — runs locally.
- Ops scripts: `infra/provision.sh` (idempotent VPS hardening: Docker, UFW
  22/80/443 only, fail2ban, unattended-upgrades) and `infra/deploy.sh` (manual
  deploy + health check). Dev-site privacy: Cloudflare Access primary, Caddy
  basic-auth fallback (commented in `infra/Caddyfile`).

## In progress
(none — clean stopping point)

## Blockers / needs founder
- HUMAN-CHECKLIST 2,3,5 (VPS, Cloudflare/dev-auth, GH secrets) gate CI + deploy.
- HUMAN-CHECKLIST 14: font WOFF2 files + logo SVG/raster icons. `@font-face` is
  wired with graceful system-ui fallback; `src/web/wwwroot` still has the
  template placeholder icons. Drop assets per `src/web/wwwroot/fonts/README.md`.

## Decisions made within spec bounds (log)
- Solution uses the new `.slnx` format (SDK 10 default).
- Web is served by Caddy, which reverse-proxies `/api`,`/health`,`/version` to
  the api container → SPA is same-origin with the API (no CORS in the prod path;
  matches `docs/ARCHITECTURE.md` "Caddy in front"). A dev-only permissive CORS
  policy covers non-proxied `dotnet run`.
- EF graph pinned to 10.0.9 (explicit `Microsoft.EntityFrameworkCore.Relational`
  ref) to resolve Npgsql 10.0.2's transitive 10.0.4 vs Design/test 10.0.9 — build
  is warning-free.
- API migrates + seeds on startup, behind `Database:MigrateOnStartup` (default on).
- Added a second seeded flag `home.show_status` (bool) so the typed bool accessor
  is exercised end-to-end alongside the string flag.
- CI/deploy drafted (founder asked to prep them while the VPS is pending). The
  PR pipeline is now VERIFIED GREEN on GitHub runners (all 3 jobs: build+test
  incl. Testcontainers integration, contrast, Playwright e2e w/ screenshot
  artifacts). First run caught a real bug — Npgsql 8+ needs EnableDynamicJson()
  for the events jsonb column (POST /api/events 500'd with properties set);
  fixed in NpgsqlConfig.UseBarBrainNpgsql(), shared by app/factory/tests.
  The deploy pipeline remains unexercised until the VPS + secrets exist.
- Image names: GHCR uses lowercase `ghcr.io/<owner>/barbrain-{api,web}`; the prod
  overlay defaults and deploy scripts derive/lowercase this automatically.

## Doc inconsistency to flag (not an ADR conflict)
- Muted-text token: `docs/BRAND.md` says `--bb-text-muted`; `docs/design/
  DESIGN-REFERENCE.md` says `--bb-muted`. Implemented `--bb-text-muted` as
  canonical with `--bb-muted` as an alias. Founder may pick one to standardize.

## Environment note (this build machine)
Docker and Node are NOT installed here, so `docker compose up`, the Testcontainers
integration tests, and Playwright were authored but not executed locally. Verified
here: `dotnet build` (solution, 0 warnings) and `dotnet test` (2 passed, 6 skipped
for absent Docker). All three run in CI / on a dev machine with Docker + Node.

## Next session should
CI is verified green on sprint-0 (PR #1). Once HUMAN-CHECKLIST 2,3,5 are done:
set the three CI jobs as required checks in branch protection, run
`infra/provision.sh` on the VPS, configure Cloudflare Access, do the first
`infra/deploy.sh prod`, and confirm `dev.barbrain.app/health` returns version+sha.
That closes the remaining Sprint 0 acceptance criteria and unblocks merging
PR #1. Then add fonts/logo assets (item 14) and start Sprint 1 (catalog schema —
the expensive-to-reverse gate).
