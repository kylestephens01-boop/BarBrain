#!/usr/bin/env bash
# Idempotent VPS provisioning for BarBrain (targets Ubuntu 24.04/noble, run as
# root; the Docker apt repo is pinned to noble for forward compat with 26.04,
# which Docker has no repo for yet).
# Hardens the host and installs Docker so the compose stack can be deployed.
# Safe to re-run. Does NOT deploy the app — that's deploy.sh / the CI pipeline.
#
# Usage:  REPO_URL=https://github.com/<owner>/BarBrain.git ./provision.sh
#
# Invariants (Sprint 0 spec / Hard Rules):
#   - Firewall allows ONLY 22 (SSH), 80, 443. Everything else denied.
#   - Postgres is never exposed (compose prod overlay drops the port; UFW blocks).
set -euo pipefail

APP_DIR=/opt/barbrain
REPO_URL="${REPO_URL:-}"

if [[ $EUID -ne 0 ]]; then
  echo "Run as root (sudo)." >&2; exit 1
fi

echo "==> apt base packages"
export DEBIAN_FRONTEND=noninteractive
apt-get update -y
apt-get install -y ca-certificates curl gnupg git jq ufw fail2ban unattended-upgrades

echo "==> Docker (engine + compose plugin)"
if ! command -v docker >/dev/null 2>&1; then
  install -m 0755 -d /etc/apt/keyrings
  curl -fsSL https://download.docker.com/linux/ubuntu/gpg | gpg --dearmor -o /etc/apt/keyrings/docker.gpg
  chmod a+r /etc/apt/keyrings/docker.gpg
  echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] https://download.docker.com/linux/ubuntu noble stable" \
    > /etc/apt/sources.list.d/docker.list
  apt-get update -y
  apt-get install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
fi
systemctl enable --now docker

echo "==> Firewall (UFW): allow only SSH/80/443"
ufw default deny incoming
ufw default allow outgoing
ufw allow OpenSSH
ufw allow 80/tcp
ufw allow 443/tcp
ufw --force enable

echo "==> fail2ban"
systemctl enable --now fail2ban

echo "==> Unattended security upgrades"
cat > /etc/apt/apt.conf.d/20auto-upgrades <<'EOF'
APT::Periodic::Update-Package-Lists "1";
APT::Periodic::Unattended-Upgrade "1";
EOF
systemctl enable --now unattended-upgrades || true

echo "==> App directory + checkout at ${APP_DIR}"
mkdir -p "${APP_DIR}"
if [[ ! -d "${APP_DIR}/.git" ]]; then
  if [[ -n "${REPO_URL}" ]]; then
    git clone "${REPO_URL}" "${APP_DIR}"
  else
    echo "    (no REPO_URL set — clone the repo into ${APP_DIR} manually, or re-run with REPO_URL=...)"
  fi
fi

cat <<EOF

==> Provisioning complete.
Next:
  1) cd ${APP_DIR} && cp infra/.env.example infra/.env   # then fill secrets
  2) Configure Cloudflare Access on the dev hostname (dev-site privacy gate),
     or enable Caddy basic-auth (see infra/Caddyfile).
  3) First deploy:  ./infra/deploy.sh prod
  Thereafter, pushes to main auto-deploy via .github/workflows/deploy.yml.
EOF
