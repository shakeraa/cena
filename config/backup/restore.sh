#!/bin/bash
set -euo pipefail

BACKUP_FILE=${1:?"Usage: restore.sh <backup_file>"}

if [ ! -f "$BACKUP_FILE" ]; then
  echo "ERROR: Backup file not found: $BACKUP_FILE"
  exit 1
fi

echo "WARNING: This will drop and recreate the 'cena' database."
echo "Press Ctrl+C within 5 seconds to abort..."
sleep 5

pg_restore -h postgres -U cena -d cena \
  --clean --if-exists \
  --single-transaction \
  "$BACKUP_FILE"

echo "[$(date)] Restore complete from: $BACKUP_FILE"
