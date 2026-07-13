#!/usr/bin/env bash
# External exposure probe (Sprint 7 hardening; Hard Rule 8).
# Run from any machine that is NOT the VPS:  ./infra/probe.sh <host>
# Passes when exactly 80/443 (and SSH, expected) answer and Postgres/API
# container ports are unreachable from the outside.
set -euo pipefail

HOST="${1:?usage: probe.sh <host-or-ip>}"
FAIL=0

probe() { # port, expectation(open|closed), label
  local port="$1" expect="$2" label="$3"
  if timeout 5 bash -c "exec 3<>/dev/tcp/${HOST}/${port}" 2>/dev/null; then
    exec 3<&- 3>&- || true
    if [ "$expect" = closed ]; then echo "FAIL ${label}: port ${port} is OPEN (must be closed)"; FAIL=1
    else echo "ok   ${label}: port ${port} open"; fi
  else
    if [ "$expect" = open ]; then echo "FAIL ${label}: port ${port} is CLOSED (should serve)"; FAIL=1
    else echo "ok   ${label}: port ${port} closed"; fi
  fi
}

probe 80   open   "http"
probe 443  open   "https"
probe 5432 closed "postgres (Hard Rule 8)"
probe 8080 closed "api container (Caddy fronts it)"
probe 5000 closed "dev web port (local-only)"

if [ "$FAIL" -ne 0 ]; then echo "PROBE FAILED — fix exposure before launch."; exit 1; fi
echo "Probe clean: only 80/443 answer."
