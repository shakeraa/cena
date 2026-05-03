#!/bin/sh
# ═══════════════════════════════════════════════════════════════════════
# Cena NATS JetStream — Stream & Consumer Setup
# Runs once on docker compose up to create all streams and consumers
# Matches: contracts/backend/nats-subjects.md
# ═══════════════════════════════════════════════════════════════════════

set -e

# REV-002: Authenticate with nats-setup user for JetStream admin operations
NATS_URL="nats://nats-setup:${NATS_SETUP_PASSWORD:-dev_setup_pass}@nats:4222"

echo "Waiting for NATS..."
sleep 3

echo "Creating JetStream streams..."

# Helper — nats CLI ≥0.2 requires a unit suffix on --max-bytes (previously
# raw integers were accepted). We pass SI-suffixed values here to stay
# compatible with current nats-box images.

# Core domain streams (3 replicas in prod, 1 in dev)
nats -s $NATS_URL stream add LEARNER_EVENTS \
  --subjects "cena.durable.learner.>" \
  --retention limits --max-age 90d --max-bytes 10GB \
  --storage file --replicas 1 --discard old \
  --dupe-window 2m --no-allow-rollup --deny-delete --deny-purge \
  --defaults \
  || echo "LEARNER_EVENTS add failed or exists"

nats -s $NATS_URL stream add PEDAGOGY_EVENTS \
  --subjects "cena.durable.pedagogy.>" \
  --retention limits --max-age 90d --max-bytes 10GB \
  --storage file --replicas 1 --discard old \
  --dupe-window 2m \
  --defaults \
  || echo "PEDAGOGY_EVENTS add failed or exists"

nats -s $NATS_URL stream add ENGAGEMENT_EVENTS \
  --subjects "cena.durable.engagement.>" \
  --retention limits --max-age 30d --max-bytes 5GB \
  --storage file --replicas 1 --discard old \
  --dupe-window 2m \
  --defaults \
  || echo "ENGAGEMENT_EVENTS add failed or exists"

nats -s $NATS_URL stream add OUTREACH_EVENTS \
  --subjects "cena.durable.outreach.>" \
  --retention limits --max-age 7d --max-bytes 1GB \
  --storage file --replicas 1 --discard old \
  --defaults \
  || echo "OUTREACH_EVENTS add failed or exists"

nats -s $NATS_URL stream add CURRICULUM_EVENTS \
  --subjects "cena.durable.curriculum.>" \
  --retention limits --max-age 365d --max-bytes 1GB \
  --storage file --replicas 1 --discard old \
  --defaults \
  || echo "CURRICULUM_EVENTS add failed or exists"

nats -s $NATS_URL stream add ANALYTICS_EVENTS \
  --subjects "cena.durable.analytics.>" \
  --retention limits --max-age 90d --max-bytes 10GB \
  --storage file --replicas 1 --discard old \
  --defaults \
  || echo "ANALYTICS_EVENTS add failed or exists"

nats -s $NATS_URL stream add SCHOOL_EVENTS \
  --subjects "cena.durable.school.>" \
  --retention limits --max-age 90d --max-bytes 2GB \
  --storage file --replicas 1 --discard old \
  --defaults \
  || echo "SCHOOL_EVENTS add failed or exists"

# System streams — file-backed so a NATS restart doesn't wipe the
# default-route stream. Previously memory-backed which silently dropped
# every lowercase-prefixed event (e.g. focus_score_updated__v1,
# concept_attempted__v1) into a black hole after the first restart.
nats -s $NATS_URL stream add SYSTEM_HEALTH \
  --subjects "cena.durable.system.>" \
  --retention limits --max-age 1d --max-bytes 256MB \
  --storage file --replicas 1 --discard old \
  --defaults \
  || echo "SYSTEM_HEALTH add failed or exists"

nats -s $NATS_URL stream add DEAD_LETTER \
  --subjects "cena.durable.dlq.>" \
  --retention limits --max-age 30d --max-bytes 1GB \
  --storage file --replicas 1 --discard old \
  --defaults \
  || echo "DEAD_LETTER add failed or exists"

echo ""
echo "Creating consumers..."

# Engagement context consumers
# INF-018: Backpressure settings per consumer type:
#   --max-pending: max unacknowledged messages (backpressure trigger)
#   --max-deliver: dead-letter after N retries
#   --ack-wait:    time before redelivery (30s commands, 60s events)

# Engagement context consumers
nats -s $NATS_URL consumer add LEARNER_EVENTS engagement-xp \
  --filter "cena.durable.learner.>" \
  --pull \
  --ack explicit --max-deliver 5 --max-pending 500 \
  --wait 60s --deliver all --replay instant \
  --defaults \
  || echo "engagement-xp add failed or exists"

nats -s $NATS_URL consumer add PEDAGOGY_EVENTS engagement-session \
  --filter "cena.durable.pedagogy.>" \
  --pull \
  --ack explicit --max-deliver 5 --max-pending 500 \
  --wait 60s --deliver all --replay instant \
  --defaults \
  || echo "engagement-session add failed or exists"

# Outreach consumers (lower throughput, tighter limits)
nats -s $NATS_URL consumer add LEARNER_EVENTS outreach-triggers \
  --filter "cena.durable.learner.>" \
  --pull \
  --ack explicit --max-deliver 3 --max-pending 100 \
  --wait 30s --deliver all --replay instant \
  --defaults \
  || echo "outreach-triggers add failed or exists"

# Analytics consumer (high throughput, relaxed limits)
nats -s $NATS_URL consumer add LEARNER_EVENTS analytics-all \
  --filter "cena.durable.learner.>" \
  --pull \
  --ack explicit --max-deliver 10 --max-pending 1000 \
  --wait 60s --deliver all --replay instant \
  --defaults \
  || echo "analytics-all add failed or exists"

echo ""
echo "✅ NATS JetStream setup complete"
nats -s $NATS_URL stream ls
