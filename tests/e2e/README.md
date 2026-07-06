# BarBrain e2e (Playwright)

Smoke suite that runs against the running stack.

## Run locally
```bash
# 1) Bring the stack up (from repo root)
docker compose -f infra/docker-compose.yml up --build -d

# 2) Install deps + browser (first time)
cd tests/e2e
npm install
npm run install:browsers

# 3) Run
npm run e2e            # headless
npm run e2e:headed     # watch it
npm run report         # open the HTML report
```

Target a different host with `BASE_URL`, e.g. `BASE_URL=https://dev.barbrain.co npm run e2e`.

## What it checks (Sprint 0)
- The web shell loads (Blazor WASM boots, wordmark renders).
- The flag-driven home banner is present.
- `/health` (proxied same-origin via Caddy) reports `ok` + version + sha.
- A full-page screenshot is attached to the report and written to
  `test-results/` for CI to upload as an artifact.

CI brings the stack up, runs this suite, and uploads screenshots; a failure
blocks merge.
