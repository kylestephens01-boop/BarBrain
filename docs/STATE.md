# STATE — agent session handoff
> Agents: update this at the END of every session. Keep it short and factual.

## Current sprint
Sprint 1 — Catalog & Data Foundation (branch `sprint-1`, PR pending founder's
Gate A review). Schema + migration + importers + search + merge queue + seed
data are BUILT and CI-tested; the BULK seed run (Open Brewery DB ≥1k
producers, beer.db products) happens on the VPS post-merge per RUNBOOK — the
≥1,000-producers / ≥2,000-beer-products acceptance numbers are gated on that
run, not on this PR. Founder decisions A–E applied throughout: no BJCP text
(ADR-023), DATA-SOURCES.md license gate (ADR-024; openbeerdb.com = ODbL =
prohibited, openbeer/geraldb = PD = cleared w/ staleness caveat), pgvector
recs first / CF deferred (ADR-025), ownership+visibility constraints day one
(ADR-026), charter proposals drafted (docs/project-charter.md).

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
- Sprint 0.5 cleanup: deploy pipeline now exports GIT_SHA so compose doesn't
  clobber the baked image SHA with infra/.env's old `GIT_SHA=local` (root cause
  of the false-failing deploy health check); prod overlay defaults GIT_SHA to
  empty → BuildInfo falls back to the SHA baked at image build; `GIT_SHA=local`
  removed from .env.example; deploy.sh exports HEAD for manual deploys;
  provision.sh seeds infra/.env from .env.example when missing (never
  overwrites); docs: domain reality (barbrain.co dev host, .app unavailable) +
  Cloudflare "Flexible" TLS note in ARCHITECTURE.md (must be "Full" + origin
  cert before public launch).

- Sprint 1 catalog: schema (users stub, producers, styles, attribute_definitions,
  style_attributes, drinks, drink_attributes, merge_queue) in migration
  `Sprint1Catalog` — CHECK constraints + composite category-coherence FKs +
  partial uniques + HNSW/trgm indexes (docs/SCHEMA.md = annotated ERD, the
  Gate A artifact). AttributeVectorService (relational truth → derived
  vectors; inheritance materialized w/ provenance), NameNormalizer,
  MergeService (trgm candidates, approve=redirect, reject=remembered),
  CatalogQueryService (trgm search, browse, style trees, redirect-following
  detail). CLI importers (api binary: `import bundled|openbrewerydb|beerdb|
  ttb-sample|demo-dupes`, `report`) — license-gated per DATA-SOURCES.md.
  Seed data: 24-term attribute vocabulary; 140 beer styles (full BJCP 2021
  list, names/codes/ranges only + our baselines), 20 whiskey + 35 wine
  styles; corridor list (~35 producers, ~70 drinks, all 3 categories, 100%
  vector coverage via inheritance). Catalog + admin-merge APIs; /admin/
  merge-queue web stub (dark tokens, keyboard-operable); e2e admin-merge
  spec + CI seeds the stack and uploads the seed report artifact. Tests:
  migrate-from-empty (updated), migrate-FROM-SPRINT-0 (new), importer
  idempotency, corridor coverage, constraint rejections, cosine sanity,
  4 search acceptance queries, merge approve/reject/redirect.

## In progress
(none — clean stopping point; Gate A review is the blocker by design)

## Blockers / needs founder (Sprint 0 era — mostly cleared)
- ~~HUMAN-CHECKLIST 2,3,5~~ DONE: VPS live, Cloudflare Access on, deploys green.
- HUMAN-CHECKLIST 14 (still open): font WOFF2 files + logo SVG/raster icons.
  `@font-face` is wired with graceful system-ui fallback; `src/web/wwwroot`
  still has template placeholder icons. Drop assets per
  `src/web/wwwroot/fonts/README.md`.

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

## Blockers / needs founder (Sprint 1)
- **Gate A review** (the long sitting): docs/SCHEMA.md + migration + the
  attribute baseline numbers in `src/api/seed/styles.*.json` (10 drinks you
  know: do the numbers smell right?) + search queries + merge-queue demo
  (e2e screenshot artifact in CI).
- **beer.db judgment call**: license cleared (PD) but data is 2012–2013;
  ingest for bulk or skip for quality? (DATA-SOURCES.md.)
- **Charter proposals** P1–P3 in docs/project-charter.md (domain reality,
  moat reframe, rec sequencing); P2 would also amend ADR-022 on approval.
- Settled charter text was never committed — paste it into
  docs/project-charter.md above the marker when convenient.

## Next session should
After Gate A approval + merge: run the bulk seed on the VPS per RUNBOOK
("Catalog seeding") — `import bundled`, Open Brewery DB CSV (→ ≥1k producers),
founder-decided beer.db, then `report` to verify the acceptance numbers; check
the Deploy health gate matches the SHA. Remaining from 0.5: delete stale
`GIT_SHA=local` from `/opt/barbrain/infra/.env`; fonts/logo assets
(HUMAN-CHECKLIST 14). Then Sprint 2 (identity/auth — enforce authz against
the ADR-026 ownership/visibility columns). Pre-public-launch (post-knockout):
Cloudflare SSL to "Full" + origin cert (see ARCHITECTURE.md).
