#!/usr/bin/env bash
# Nightly encrypted pg_dump (Sprint 7). Runs as the compose `backup` service:
# default mode loops forever, dumping once a day at BACKUP_HOUR_UTC; `once`
# runs a single dump and exits (CI drill + manual use).
#
#   dump → gzip → openssl AES-256 (pbkdf2, BACKUP_PASSPHRASE) → /backups
#   prune anything older than BACKUP_RETENTION_DAYS (default 30)
#   optional off-box upload when RCLONE_REMOTE is set (HUMAN-CHECKLIST 10 —
#   object-storage creds; until then backups are on-box only, and the log
#   says so every night rather than pretending otherwise)
set -euo pipefail

: "${PGHOST:=postgres}"
: "${PGDATABASE:=barbrain}"
: "${BACKUP_DIR:=/backups}"
: "${BACKUP_RETENTION_DAYS:=30}"
: "${BACKUP_HOUR_UTC:=3}"
: "${BACKUP_PASSPHRASE:?BACKUP_PASSPHRASE is required (never commit it — Hard Rule 8)}"

dump_once() {
  local stamp file
  stamp="$(date -u +%Y%m%d-%H%M%S)"
  file="${BACKUP_DIR}/barbrain-${stamp}.sql.gz.enc"
  mkdir -p "${BACKUP_DIR}"

  echo "[backup] dumping ${PGDATABASE} → ${file}"
  pg_dump --format=plain --no-owner --no-privileges "${PGDATABASE}" \
    | gzip \
    | openssl enc -aes-256-cbc -pbkdf2 -salt -pass env:BACKUP_PASSPHRASE -out "${file}"

  # Refuse to call a tiny dump a success — a broken pipe can produce one.
  local bytes
  bytes=$(stat -c%s "${file}")
  if [ "${bytes}" -lt 1024 ]; then
    echo "[backup] FAILED: dump is only ${bytes} bytes"; rm -f "${file}"; exit 1
  fi
  echo "[backup] wrote ${bytes} bytes"

  echo "[backup] pruning older than ${BACKUP_RETENTION_DAYS} days"
  find "${BACKUP_DIR}" -name 'barbrain-*.sql.gz.enc' -mtime "+${BACKUP_RETENTION_DAYS}" -delete

  if [ -n "${RCLONE_REMOTE:-}" ] && command -v rclone >/dev/null 2>&1; then
    echo "[backup] uploading to ${RCLONE_REMOTE}"
    rclone copy "${file}" "${RCLONE_REMOTE}"
  else
    echo "[backup] NOTE: no RCLONE_REMOTE configured — backup is ON-BOX ONLY (HUMAN-CHECKLIST 10)"
  fi
}

# `docker compose run backup …` APPENDS args to the entrypoint, so a caller
# writing `run backup /backup.sh once` hands us our own path as $1 — shift it
# off instead of silently falling into loop mode (CI hung 6h on exactly this).
if [ "${1##*/}" = "backup.sh" ]; then shift; fi

if [ "${1:-loop}" = "once" ]; then
  dump_once
  exit 0
fi

echo "[backup] loop mode: nightly at ${BACKUP_HOUR_UTC}:00 UTC, retention ${BACKUP_RETENTION_DAYS}d"
while true; do
  now=$(date -u +%s)
  next=$(date -u -d "today ${BACKUP_HOUR_UTC}:00" +%s)
  if [ "${next}" -le "${now}" ]; then next=$(date -u -d "tomorrow ${BACKUP_HOUR_UTC}:00" +%s); fi
  sleep $(( next - now ))
  dump_once || echo "[backup] nightly dump FAILED — will retry tomorrow"
done
