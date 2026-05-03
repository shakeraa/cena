#!/bin/bash
set -euo pipefail

BACKUP_DIR="/backups"
RETENTION_DAYS=${RETENTION_DAYS:-7}
TIMESTAMP=$(date +%Y%m%d_%H%M%S)
BACKUP_FILE="${BACKUP_DIR}/cena_${TIMESTAMP}.sql.gz"

# Custom format for selective restore capability
pg_dump -h postgres -U cena -d cena \
  --format=custom \
  --compress=6 \
  --file="${BACKUP_FILE}"

echo "[$(date)] Backup created: ${BACKUP_FILE} ($(du -h "${BACKUP_FILE}" | cut -f1))"

# Rotate: delete backups older than retention period
find "${BACKUP_DIR}" -name "cena_*.sql.gz" -mtime +${RETENTION_DAYS} -delete
echo "[$(date)] Rotation complete. Retained backups:"
ls -lh "${BACKUP_DIR}"/cena_*.sql.gz 2>/dev/null || echo "  (none)"
