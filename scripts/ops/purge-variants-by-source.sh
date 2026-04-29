#!/usr/bin/env bash
# =============================================================================
# Cena Platform — Per-source-paper-code variant purge tool
#
# Part of the Ministry takedown response runbook
# (docs/ops/runbooks/ministry-takedown.md §4). Selects every
# QuestionDocument whose Provenance.Source matches the structured
# slash-delimited form `ministry-bagrut/{paperCode}/{year}/{season}/{moed}/q{n}`
# and a SQL-LIKE pattern, then either:
#   * dry-run: print the candidate list with provenance excerpts
#   * live: set Status='Withdrawn' + WithdrawnReason on each
#
# Does NOT delete LearningSessionAttempt rows for the variants — those
# stay in the audit trail under the existing 180-day retention so
# counsel can answer "what did students see during the affected
# window".
#
# Usage:
#   ./purge-variants-by-source.sh --pattern 'ministry-bagrut/035582/*' --dry-run
#   ./purge-variants-by-source.sh --pattern 'ministry-bagrut/035582/*' \
#       --reason "Ministry-takedown-2026-04-29"
#
# Connection:
#   - Production: env CENA_POSTGRES_CONNECTION must be set (admin-api uses
#     this same var). Read the bucket ARN + DSN from the host's secrets store.
#   - Dev/test: defaults to the cena-postgres dev DSN if env is unset.
#
# Counsel-sign-off gate: live mode REQUIRES --confirm-counsel-approved.
# The dry-run + counsel review + live run is the documented sequence
# per the runbook.
# =============================================================================

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DSN="${CENA_POSTGRES_CONNECTION:-postgresql://cena:cena_dev_password@localhost:5433/cena}"

PATTERN=""
REASON=""
DRY_RUN=false
COUNSEL_APPROVED=false

while [ $# -gt 0 ]; do
  case "$1" in
    --pattern)
      PATTERN="$2"; shift 2 ;;
    --reason)
      REASON="$2"; shift 2 ;;
    --dry-run)
      DRY_RUN=true; shift ;;
    --confirm-counsel-approved)
      COUNSEL_APPROVED=true; shift ;;
    --dsn)
      DSN="$2"; shift 2 ;;
    -h|--help)
      grep '^#' "$0" | head -50
      exit 0 ;;
    *)
      echo "Unknown arg: $1" >&2
      exit 2 ;;
  esac
done

if [ -z "$PATTERN" ]; then
  echo "ERROR: --pattern is required (e.g. 'ministry-bagrut/035582/*')" >&2
  exit 2
fi

if [ "$DRY_RUN" = false ] && [ -z "$REASON" ]; then
  echo "ERROR: --reason is required for live runs (e.g. 'Ministry-takedown-2026-04-29')" >&2
  exit 2
fi

if [ "$DRY_RUN" = false ] && [ "$COUNSEL_APPROVED" = false ]; then
  echo "ERROR: live runs require --confirm-counsel-approved" >&2
  echo "       (dry-run + counsel review + live run is the documented sequence)" >&2
  exit 3
fi

# Translate glob-ish '*' to SQL '%' for the LIKE pattern. '?' -> '_'.
SQL_PATTERN="${PATTERN//\*/%}"
SQL_PATTERN="${SQL_PATTERN//\?/_}"

echo "── Cena variant purge ──"
echo "  pattern (input):  $PATTERN"
echo "  pattern (SQL):    $SQL_PATTERN"
echo "  dsn:              ${DSN%%password=*}***"
echo "  mode:             $([ "$DRY_RUN" = true ] && echo 'DRY-RUN' || echo 'LIVE')"
[ "$DRY_RUN" = false ] && echo "  reason:           $REASON"
echo

# ── Candidate selection ──
# Selects rows from cena.mt_doc_questiondocument whose JSON 'data'
# column has SourceProvenance.Source matching the LIKE pattern AND
# is currently Status='Active' (or whatever non-Withdrawn we have).
# We use jsonb operators so the selection is index-friendly.

CANDIDATE_SQL=$(cat <<SQL
SELECT
  id,
  data->>'Subject'        AS subject,
  data->>'Topic'          AS topic,
  data->>'Status'         AS status,
  data #>> '{SourceProvenance,Source}' AS provenance_source
FROM cena.mt_doc_questiondocument
WHERE data #>> '{SourceProvenance,Source}' LIKE '$SQL_PATTERN'
  AND COALESCE(data->>'Status', 'Active') <> 'Withdrawn'
ORDER BY id;
SQL
)

echo "── Candidate set ──"
psql "$DSN" -c "$CANDIDATE_SQL" || {
  echo "ERROR: candidate query failed; check DSN" >&2
  exit 4
}

CANDIDATE_COUNT=$(psql "$DSN" -t -c "SELECT COUNT(*) FROM cena.mt_doc_questiondocument WHERE data #>> '{SourceProvenance,Source}' LIKE '$SQL_PATTERN' AND COALESCE(data->>'Status', 'Active') <> 'Withdrawn';" | tr -d ' ')
echo
echo "── Candidate count: $CANDIDATE_COUNT ──"

if [ "$CANDIDATE_COUNT" = "0" ]; then
  echo "No candidates match. No-op."
  exit 0
fi

if [ "$DRY_RUN" = true ]; then
  echo
  echo "Dry-run complete. Re-run without --dry-run + with --reason + --confirm-counsel-approved to apply."
  exit 0
fi

# ── Live purge ──
# Atomic per-row jsonb_set updates. Marten's mt_version optimistic
# concurrency would normally apply; we bypass it intentionally because
# this is an ops-mandated forced state change, not an application
# write. The arch-test asserts this is the ONLY caller of the bypass.

NOW_ISO=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
PURGE_SQL=$(cat <<SQL
UPDATE cena.mt_doc_questiondocument
SET data = jsonb_set(
       jsonb_set(
         jsonb_set(data, '{Status}', '"Withdrawn"'),
         '{WithdrawnReason}', '"$REASON"'
       ),
       '{WithdrawnAt}', '"$NOW_ISO"'
     ),
    mt_last_modified = transaction_timestamp(),
    mt_version = gen_random_uuid()
WHERE data #>> '{SourceProvenance,Source}' LIKE '$SQL_PATTERN'
  AND COALESCE(data->>'Status', 'Active') <> 'Withdrawn';
SQL
)

echo
echo "── Applying purge ──"
psql "$DSN" -c "$PURGE_SQL" || {
  echo "ERROR: purge transaction failed" >&2
  exit 5
}

# ── Cascade: remove from active session queues ──
# LearningSessionQueueProjection rows reference question ids; we need
# to drop the affected ids from any in-flight session queue so a
# student doesn't get served a withdrawn variant on next pull.

CASCADE_SQL=$(cat <<SQL
WITH affected AS (
  SELECT id FROM cena.mt_doc_questiondocument
  WHERE data #>> '{SourceProvenance,Source}' LIKE '$SQL_PATTERN'
)
UPDATE cena.mt_doc_learningsessionqueueprojection q
SET data = jsonb_set(
       q.data,
       '{QueuedQuestionIds}',
       (SELECT COALESCE(jsonb_agg(elem), '[]'::jsonb)
        FROM jsonb_array_elements_text(q.data->'QueuedQuestionIds') elem
        WHERE elem.value NOT IN (SELECT id FROM affected))
     ),
    mt_last_modified = transaction_timestamp()
WHERE EXISTS (
  SELECT 1
  FROM jsonb_array_elements_text(q.data->'QueuedQuestionIds') elem
  WHERE elem.value IN (SELECT id FROM affected)
);
SQL
)

echo
echo "── Cascading queue removal ──"
psql "$DSN" -c "$CASCADE_SQL" || {
  echo "WARNING: cascade transaction failed; queues may still serve withdrawn variants until next reload" >&2
  # Non-fatal: kill-switch is the primary defense; queue cascade is belt-and-suspenders.
}

# ── Audit log ──
# Append a structured event so the SIEM exporter forwards it to the
# legal-hold log bucket. The runbook's §3b 'audit-log the flip'
# requirement reads this trail.

AUDIT_SQL=$(cat <<SQL
INSERT INTO cena.mt_doc_auditeventdocument (id, data, mt_dotnet_type, mt_version, mt_last_modified)
VALUES (
  'ministry-takedown-$NOW_ISO',
  jsonb_build_object(
    'Id', 'ministry-takedown-$NOW_ISO',
    'Kind', 'MinistryTakedownPurge',
    'PurgePattern', '$PATTERN',
    'Reason', '$REASON',
    'CandidateCount', $CANDIDATE_COUNT,
    'PerformedBy', '$(whoami)',
    'PerformedAt', '$NOW_ISO',
    'Severity', 'SEV1'
  ),
  'Cena.Infrastructure.Compliance.AuditEventDocument',
  gen_random_uuid(),
  transaction_timestamp()
)
ON CONFLICT (id) DO NOTHING;
SQL
)

echo
echo "── Audit-log entry ──"
psql "$DSN" -c "$AUDIT_SQL" || {
  echo "WARNING: audit-log insert failed; manually log the purge in #incident-ministry-takedown" >&2
}

echo
echo "✓ Purge complete: $CANDIDATE_COUNT variants withdrawn under reason '$REASON'."
echo "  Next: bump Cena:ReferenceLibrary:CacheBuster + invalidate CDN if applicable."
echo "  Next: confirm Cena:ReferenceLibrary:Enabled is still false until counsel re-enables."
