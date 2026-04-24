#!/usr/bin/env bash
# ═══════════════════════════════════════════════════════════════════════
# Cena Platform — NATS JetStream Messaging Stream Provisioning
# Idempotent: safe to run multiple times.
# Usage: bash scripts/nats/provision-messaging-streams.sh [NATS_URL]
# ═══════════════════════════════════════════════════════════════════════

set -euo pipefail

NATS_URL="${1:-nats://localhost:4222}"
export NATS_URL

echo "Provisioning Messaging NATS streams at ${NATS_URL}..."

# ── MESSAGING_EVENTS: 365-day audit trail ──
nats stream add MESSAGING_EVENTS \
  --subjects "cena.messaging.events.>" \
  --retention limits --max-age 365d --max-bytes 5368709120 \
  --replicas 3 --storage file --discard old --dupe-window 2m \
  --defaults 2>/dev/null || \
  echo "MESSAGING_EVENTS stream already exists (or updated)"

# ── MESSAGING_COMMANDS: work queue ──
nats stream add MESSAGING_COMMANDS \
  --subjects "cena.messaging.commands.>" \
  --retention work --max-age 7d --max-bytes 1073741824 \
  --replicas 3 --storage file --discard old --dupe-window 2m \
  --defaults 2>/dev/null || \
  echo "MESSAGING_COMMANDS stream already exists (or updated)"

# ── Consumers ──

echo "Creating consumers..."

# Process send requests
nats consumer add MESSAGING_COMMANDS messaging-send-processor \
  --filter "cena.messaging.commands.SendMessage" \
  --deliver all --ack explicit --wait 30s \
  --max-deliver 5 --max-pending 200 --pull --replay instant \
  --defaults 2>/dev/null || true

# Route inbound replies (high retry)
nats consumer add MESSAGING_COMMANDS messaging-inbound-router \
  --filter "cena.messaging.commands.RouteInboundReply" \
  --deliver all --ack explicit --wait 30s \
  --max-deliver 10 --max-pending 100 --pull --replay instant \
  --defaults 2>/dev/null || true

# Fan-out class broadcasts
nats consumer add MESSAGING_COMMANDS messaging-broadcast-processor \
  --filter "cena.messaging.commands.BroadcastToClass" \
  --deliver all --ack explicit --wait 60s \
  --max-deliver 5 --max-pending 50 --pull --replay instant \
  --defaults 2>/dev/null || true

# Analytics: all messaging events (batch processing)
nats consumer add MESSAGING_EVENTS analytics-all-messaging \
  --filter "cena.messaging.events.>" \
  --deliver all --ack explicit --wait 120s \
  --max-deliver 3 --max-pending 5000 --pull --replay instant \
  --defaults 2>/dev/null || true

# Archival: message events for S3 cold storage
nats consumer add MESSAGING_EVENTS archival-message-events \
  --filter "cena.messaging.events.MessageSent" \
  --deliver all --ack explicit --wait 120s \
  --max-deliver 3 --max-pending 10000 --pull --replay instant \
  --defaults 2>/dev/null || true

echo "Messaging NATS provisioning complete."
echo "  Streams:   MESSAGING_EVENTS, MESSAGING_COMMANDS"
echo "  Consumers: messaging-send-processor, messaging-inbound-router,"
echo "             messaging-broadcast-processor, analytics-all-messaging,"
echo "             archival-message-events"
