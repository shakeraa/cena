# REV-003: PostgreSQL Backup Strategy for Marten Event Store

**Priority:** P0 -- CRITICAL (event store is the single source of truth; volume loss = unrecoverable data loss)
**Blocked by:** None
**Blocks:** Production deployment
**Estimated effort:** 1 day
**Source:** System Review 2026-03-28 -- Cyber Officer 2 (F-BKP-01/02), DevOps Engineer (Finding #10)

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule.

## Purpose

The Marten event store in PostgreSQL is the authoritative source for ALL domain state -- every student interaction, mastery update, question lifecycle event, and tutoring session. Event sourcing means there is no secondary data source; if the event store is lost, the entire system state is unrecoverable. Currently there is zero backup: no pg_dump, no WAL archiving, no point-in-time recovery.

## Architect's Decision

Implement a two-tier backup strategy:
1. **Development**: Scheduled `pg_dump` via a Docker sidecar (simple, self-contained)
2. **Production**: WAL-G continuous archiving to S3 (enables point-in-time recovery)

This task covers development. Production backup (WAL-G + S3) is deferred to INF-002 (RDS) which handles managed PostgreSQL.

## Subtasks

### REV-003.1: Add pg_dump Backup Sidecar to Docker Compose

**Files to create:**
- `config/backup/backup.sh` -- pg_dump script with rotation
- `config/backup/restore.sh` -- restore script

**Files to modify:**
- `docker-compose.yml` -- add backup service

**backup.sh:**
```bash
#!/bin/bash
set -euo pipefail

BACKUP_DIR="/backups"
RETENTION_DAYS=7
TIMESTAMP=$(date +%Y%m%d_%H%M%S)
BACKUP_FILE="${BACKUP_DIR}/cena_${TIMESTAMP}.sql.gz"

# Custom format for selective restore capability
pg_dump -h postgres -U cena -d cena \
  --format=custom \
  --compress=6 \
  --file="${BACKUP_FILE}"

echo "[$(date)] Backup created: ${BACKUP_FILE} ($(du -h ${BACKUP_FILE} | cut -f1))"

# Rotate: delete backups older than retention period
find "${BACKUP_DIR}" -name "cena_*.sql.gz" -mtime +${RETENTION_DAYS} -delete
echo "[$(date)] Rotation complete. Retained backups:"
ls -lh "${BACKUP_DIR}"/cena_*.sql.gz 2>/dev/null || echo "  (none)"
```

**docker-compose.yml addition:**
```yaml
postgres-backup:
  image: postgres:16-alpine
  depends_on:
    postgres:
      condition: service_healthy
  volumes:
    - ./config/backup:/scripts:ro
    - postgres_backups:/backups
  environment:
    PGPASSWORD: ${POSTGRES_PASSWORD:-cena_dev_password}
  entrypoint: ["/bin/sh", "-c"]
  command:
    - |
      echo "Backup scheduler started. Running every 6 hours."
      while true; do
        /scripts/backup.sh
        sleep 21600
      done
  restart: unless-stopped

volumes:
  postgres_backups:
```

**Acceptance:**
- [ ] `docker compose up` starts backup sidecar alongside postgres
- [ ] First backup runs within 10 seconds of container start
- [ ] Subsequent backups run every 6 hours
- [ ] Backups older than 7 days are automatically deleted
- [ ] Backup file is `pg_dump --format=custom` (supports selective restore)

### REV-003.2: Create Restore Script & Document

**File to create:** `config/backup/restore.sh`

```bash
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
```

**Acceptance:**
- [ ] `restore.sh` restores from any backup file
- [ ] Restore is transactional (all-or-nothing via `--single-transaction`)
- [ ] 5-second safety delay before destructive operation

### REV-003.3: Verify Round-Trip Integrity

**Test procedure:**
1. Run the system with emulator for 2 minutes (generate events)
2. Query event count: `SELECT count(*) FROM mt_events;`
3. Run backup
4. Drop and restore
5. Query event count again -- must match
6. Start Actor Host -- actors must recover state from replayed events

**Acceptance:**
- [ ] Event count matches pre/post restore
- [ ] Marten document projections rebuild correctly after restore
- [ ] Actor Host starts and serves requests after restore
