#!/usr/bin/env bash
# Restore drill (Sprint 7 acceptance): prove the newest encrypted backup
# actually restores. Spins a SCRATCH postgres container, restores into it,
# smoke-verifies, tears it down. Never touches the live database.
#
#   ./infra/restore-drill.sh [backup-file]
#
# With no argument, pulls the newest dump out of the compose backups volume
# (barbrain_backups). Needs BACKUP_PASSPHRASE in the environment. Output is
# both stdout and restore-drill.log (the CI/PR artifact).
set -euo pipefail

: "${BACKUP_PASSPHRASE:?BACKUP_PASSPHRASE is required}"
LOG="restore-drill.log"
SCRATCH="barbrain-restore-drill-$$"
VOLUME="${BACKUP_VOLUME:-barbrain_backups}"
START=$(date -u +%s)

log() { echo "$(date -u +%H:%M:%S) $*" | tee -a "${LOG}"; }
: > "${LOG}"

WORK=$(mktemp -d)
cleanup() { docker rm -f "${SCRATCH}" >/dev/null 2>&1 || true; rm -rf "${WORK}"; }
trap cleanup EXIT

# --- 1. locate the dump ------------------------------------------------------
if [ -n "${1:-}" ]; then
  cp "$1" "${WORK}/dump.sql.gz.enc"
  log "using backup file: $1"
else
  NEWEST=$(docker run --rm -v "${VOLUME}:/backups" alpine \
    sh -c "ls -1t /backups/barbrain-*.sql.gz.enc 2>/dev/null | head -1")
  [ -n "${NEWEST}" ] || { log "FAIL: no backups found in volume ${VOLUME}"; exit 1; }
  docker run --rm -v "${VOLUME}:/backups" -v "${WORK}:/out" alpine \
    cp "${NEWEST}" /out/dump.sql.gz.enc
  log "using newest backup from ${VOLUME}: ${NEWEST}"
fi

# --- 2. decrypt --------------------------------------------------------------
openssl enc -d -aes-256-cbc -pbkdf2 -pass env:BACKUP_PASSPHRASE \
  -in "${WORK}/dump.sql.gz.enc" | gunzip > "${WORK}/dump.sql"
log "decrypted: $(wc -c < "${WORK}/dump.sql") bytes of SQL"

# --- 3. scratch container ----------------------------------------------------
docker run -d --name "${SCRATCH}" -e POSTGRES_PASSWORD=drill \
  -e POSTGRES_DB=barbrain pgvector/pgvector:pg16 >/dev/null
log "scratch container started"
for i in $(seq 1 30); do
  docker exec "${SCRATCH}" pg_isready -U postgres -d barbrain >/dev/null 2>&1 && break
  sleep 1
  [ "$i" = 30 ] && { log "FAIL: scratch postgres never became ready"; exit 1; }
done

# --- 4. restore ---------------------------------------------------------------
docker exec -i "${SCRATCH}" psql -U postgres -d barbrain -q -v ON_ERROR_STOP=1 \
  < "${WORK}/dump.sql" >/dev/null
log "restore completed"

# --- 5. smoke-verify ----------------------------------------------------------
check() { # query, label, minimum
  local n
  n=$(docker exec "${SCRATCH}" psql -U postgres -d barbrain -tAc "$1")
  log "  $2 = ${n}"
  [ "${n}" -ge "$3" ] || { log "FAIL: $2 below minimum $3"; exit 1; }
}
check 'SELECT count(*) FROM "__EFMigrationsHistory"' "migrations applied" 1
check 'SELECT count(*) FROM settings' "settings rows" 1
check 'SELECT count(*) FROM drinks' "drinks rows" 0
check 'SELECT count(*) FROM users' "users rows" 0

ELAPSED=$(( $(date -u +%s) - START ))
log "RESTORE DRILL PASSED in ${ELAPSED}s (dump → decrypt → scratch restore → smoke green)"
