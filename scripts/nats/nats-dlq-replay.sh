#!/usr/bin/env bash
# =============================================================================
# RDY-017: NATS DLQ Replay Tool
# Replays dead-lettered events from the CENA_DLQ stream back to their
# original durable stream subjects.
#
# Prerequisites: nats CLI (https://github.com/nats-io/natscli)
#
# Usage:
#   ./scripts/nats/nats-dlq-replay.sh                    # replay all
#   ./scripts/nats/nats-dlq-replay.sh --filter learner    # filter by subject
#   ./scripts/nats/nats-dlq-replay.sh --dry-run           # preview only
# =============================================================================

set -euo pipefail

STREAM="CENA_DLQ"
CONSUMER="dlq-replay-$$"
DRY_RUN=false
FILTER=""
MAX_MSGS=100

while [[ $# -gt 0 ]]; do
  case $1 in
    --dry-run)  DRY_RUN=true; shift ;;
    --filter)   FILTER="$2"; shift 2 ;;
    --max)      MAX_MSGS="$2"; shift 2 ;;
    --help|-h)
      echo "Usage: $0 [--dry-run] [--filter <subject-pattern>] [--max <N>]"
      echo ""
      echo "Replays dead-lettered events from CENA_DLQ back to original streams."
      echo ""
      echo "Options:"
      echo "  --dry-run   Preview events without replaying"
      echo "  --filter    Only replay events matching subject pattern (e.g., 'learner')"
      echo "  --max       Maximum events to replay (default: 100)"
      exit 0
      ;;
    *) echo "Unknown option: $1"; exit 1 ;;
  esac
done

echo "=== NATS DLQ Replay Tool ==="
echo "Stream:  $STREAM"
echo "Dry run: $DRY_RUN"
echo "Filter:  ${FILTER:-<all>}"
echo "Max:     $MAX_MSGS"
echo ""

# Check nats CLI is available
if ! command -v nats &>/dev/null; then
  echo "ERROR: 'nats' CLI not found. Install from https://github.com/nats-io/natscli"
  exit 1
fi

# Get stream info
echo "Stream info:"
nats stream info "$STREAM" --json 2>/dev/null | grep -E '"messages"|"bytes"' || echo "  (stream may not exist yet)"
echo ""

# Create ephemeral consumer
SUBJECT="cena.durable.dlq.>"
if [[ -n "$FILTER" ]]; then
  SUBJECT="cena.durable.dlq.*${FILTER}*"
fi

echo "Fetching up to $MAX_MSGS messages from $SUBJECT..."
echo ""

REPLAYED=0
SKIPPED=0

# Use nats sub to consume messages
nats stream get "$STREAM" --count "$MAX_MSGS" --raw 2>/dev/null | while IFS= read -r line; do
  # Parse the original subject from the DLQ payload
  ORIGINAL_SUBJECT=$(echo "$line" | python3 -c "import sys,json; d=json.load(sys.stdin); print(d.get('originalSubject',''))" 2>/dev/null || echo "")

  if [[ -z "$ORIGINAL_SUBJECT" ]]; then
    echo "  SKIP: could not parse originalSubject"
    SKIPPED=$((SKIPPED + 1))
    continue
  fi

  EVENT_TYPE=$(echo "$line" | python3 -c "import sys,json; d=json.load(sys.stdin); print(d.get('eventType','?'))" 2>/dev/null || echo "?")
  SEQ=$(echo "$line" | python3 -c "import sys,json; d=json.load(sys.stdin); print(d.get('sequence',0))" 2>/dev/null || echo "0")

  if $DRY_RUN; then
    echo "  [DRY RUN] Would replay seq=$SEQ type=$EVENT_TYPE -> $ORIGINAL_SUBJECT"
  else
    # Extract original data and republish
    DATA=$(echo "$line" | python3 -c "import sys,json; d=json.load(sys.stdin); print(json.dumps(d.get('data',{})))" 2>/dev/null || echo "{}")
    echo "$DATA" | nats pub "$ORIGINAL_SUBJECT" --stdin 2>/dev/null
    echo "  REPLAYED seq=$SEQ type=$EVENT_TYPE -> $ORIGINAL_SUBJECT"
    REPLAYED=$((REPLAYED + 1))
  fi
done

echo ""
echo "Done. Replayed: $REPLAYED, Skipped: $SKIPPED"
