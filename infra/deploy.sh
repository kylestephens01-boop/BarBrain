#!/usr/bin/env bash
# Manual deploy of the BarBrain stack on the VPS. CI normally does this
# (.github/workflows/deploy.yml); use this for the first deploy or a hotfix.
# Run from the repo root on the VPS (e.g. /opt/barbrain).
#
# Usage:  ./infra/deploy.sh [preview|prod]   (default: prod)
#
# Image source: $API_IMAGE / $WEB_IMAGE if set, else GHCR :latest derived from
# the git origin (lowercased). Set GHCR_USER + GHCR_TOKEN to log in for private
# image pulls.
set -euo pipefail

ENVIRONMENT="${1:-prod}"
cd "$(dirname "$0")/.."

# Derive the lowercased ghcr image base from the origin remote, unless overridden.
if [[ -z "${API_IMAGE:-}" || -z "${WEB_IMAGE:-}" ]]; then
  origin="$(git config --get remote.origin.url || true)"
  slug="$(echo "$origin" | sed -E 's#(git@[^:]+:|https?://[^/]+/)##; s#\.git$##' | tr '[:upper:]' '[:lower:]')"
  if [[ -z "$slug" ]]; then
    echo "Cannot derive image names; set API_IMAGE and WEB_IMAGE." >&2; exit 1
  fi
  API_IMAGE="ghcr.io/${slug}-api:latest"
  WEB_IMAGE="ghcr.io/${slug}-web:latest"
fi
export API_IMAGE WEB_IMAGE

# Same trap as the images: compose interpolates GIT_SHA into the api container
# env, and a stale GIT_SHA=local in infra/.env would override the SHA baked into
# the image. Export the checkout's HEAD so manual deploys report the real SHA.
if [[ -z "${GIT_SHA:-}" ]]; then
  GIT_SHA="$(git rev-parse HEAD 2>/dev/null || true)"
fi
export GIT_SHA

echo "==> Deploying ($ENVIRONMENT)"
echo "    api: $API_IMAGE"
echo "    web: $WEB_IMAGE"

if [[ -n "${GHCR_TOKEN:-}" && -n "${GHCR_USER:-}" ]]; then
  echo "$GHCR_TOKEN" | docker login ghcr.io -u "$GHCR_USER" --password-stdin
fi

COMPOSE="docker compose -f infra/docker-compose.yml -f infra/docker-compose.prod.yml"
$COMPOSE pull
$COMPOSE up -d

echo "==> Health check (localhost; bypasses Cloudflare Access)"
for i in $(seq 1 30); do
  if curl -fsS http://localhost/health > /dev/null; then
    sha="$(curl -fsS http://localhost/version | sed -n 's/.*"sha":"\([^"]*\)".*/\1/p')"
    echo "Healthy. Live SHA: ${sha:-unknown}"
    exit 0
  fi
  sleep 5
done

echo "Health check failed." >&2
$COMPOSE logs --tail=100
exit 1
