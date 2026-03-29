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

# Core domain streams (3 replicas in prod, 1 in dev)
nats -s $NATS_URL stream add LEARNER_EVENTS \
  --subjects "cena.durable.learner.>" \
  --retention limits --max-age 90d --max-bytes 10737418240 \
  --storage file --replicas 1 --discard old \
  --dupe-window 2m --no-allow-rollup --deny-delete --deny-purge \
  2>/dev/null || echo "LEARNER_EVENTS exists"

nats -s $NATS_URL stream add PEDAGOGY_EVENTS \
  --subjects "cena.durable.pedagogy.>" \
  --retention limits --max-age 90d --max-bytes 10737418240 \
  --storage file --replicas 1 --discard old \
  --dupe-window 2m \
  2>/dev/null || echo "PEDAGOGY_EVENTS exists"

nats -s $NATS_URL stream add ENGAGEMENT_EVENTS \
  --subjects "cena.durable.engagement.>" \
  --retention limits --max-age 30d --max-bytes 5368709120 \
  --storage file --replicas 1 --discard old \
  --dupe-window 2m \
  2>/dev/null || echo "ENGAGEMENT_EVENTS exists"

nats -s $NATS_URL stream add OUTREACH_EVENTS \
  --subjects "cena.durable.outreach.>" \
  --retention limits --max-age 7d --max-bytes 1073741824 \
  --storage file --replicas 1 --discard old \
  2>/dev/null || echo "OUTREACH_EVENTS exists"

nats -s $NATS_URL stream add CURRICULUM_EVENTS \
  --subjects "cena.durable.curriculum.>" \
  --retention limits --max-age 365d --max-bytes 1073741824 \
  --storage file --replicas 1 --discard old \
  2>/dev/null || echo "CURRICULUM_EVENTS exists"

nats -s $NATS_URL stream add ANALYTICS_EVENTS \
  --subjects "cena.durable.analytics.>" \
  --retention limits --max-age 90d --max-bytes 10737418240 \
  --storage file --replicas 1 --discard old \
  2>/dev/null || echo "ANALYTICS_EVENTS exists"

nats -s $NATS_URL stream add SCHOOL_EVENTS \
  --subjects "cena.durable.school.>" \
  --retention limits --max-age 90d --max-bytes 2147483648 \
  --storage file --replicas 1 --discard old \
  2>/dev/null || echo "SCHOOL_EVENTS exists"

# System streams
nats -s $NATS_URL stream add SYSTEM_HEALTH \
  --subjects "cena.durable.system.>" \
  --retention limits --max-age 1d --max-bytes 268435456 \
  --storage memory --replicas 1 --discard old \
  2>/dev/null || echo "SYSTEM_HEALTH exists"

nats -s $NATS_URL stream add DEAD_LETTER \
  --subjects "cena.durable.dlq.>" \
  --retention limits --max-age 30d --max-bytes 1073741824 \
  --storage file --replicas 1 --discard old \
  2>/dev/null || echo "DEAD_LETTER exists"

echo ""
echo "Creating consumers..."

# Engagement context consumers
nats -s $NATS_URL consumer add LEARNER_EVENTS engagement-xp \
  --filter "cena.durable.learner.>" \
  --ack explicit --max-deliver 5 --max-pending 1000 \
  --deliver all --replay instant \
  2>/dev/null || echo "engagement-xp exists"

nats -s $NATS_URL consumer add PEDAGOGY_EVENTS engagement-session \
  --filter "cena.durable.pedagogy.>" \
  --ack explicit --max-deliver 5 --max-pending 1000 \
  --deliver all --replay instant \
  2>/dev/null || echo "engagement-session exists"

# Outreach consumers
nats -s $NATS_URL consumer add LEARNER_EVENTS outreach-triggers \
  --filter "cena.durable.learner.>" \
  --ack explicit --max-deliver 3 --max-pending 500 \
  --deliver all --replay instant \
  2>/dev/null || echo "outreach-triggers exists"

# Analytics consumer (all events)
nats -s $NATS_URL consumer add LEARNER_EVENTS analytics-all \
  --filter "cena.durable.learner.>" \
  --ack explicit --max-deliver 10 --max-pending 5000 \
  --deliver all --replay instant \
  2>/dev/null || echo "analytics-all exists"

echo ""
echo "✅ NATS JetStream setup complete"
nats -s $NATS_URL stream ls
