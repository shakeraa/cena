#!/bin/sh
# ═══════════════════════════════════════════════════════════════════════
# Cena NATS JetStream — Stream & Consumer Setup
# Runs once on docker compose up to create all streams and consumers
# Matches: contracts/backend/nats-subjects.md
# ═══════════════════════════════════════════════════════════════════════

set -e

NATS_URL="nats://nats:4222"

echo "Waiting for NATS..."
sleep 3

echo "Creating JetStream streams..."

# Core domain streams (3 replicas in prod, 1 in dev)
nats -s $NATS_URL stream add LEARNER_EVENTS \
  --subjects "cena.learner.events.>" \
  --retention limits --max-age 90d --max-bytes 10737418240 \
  --storage file --replicas 1 --discard old \
  --dupe-window 2m --no-allow-rollup --deny-delete --deny-purge \
  2>/dev/null || echo "LEARNER_EVENTS exists"

nats -s $NATS_URL stream add PEDAGOGY_EVENTS \
  --subjects "cena.pedagogy.events.>" \
  --retention limits --max-age 90d --max-bytes 10737418240 \
  --storage file --replicas 1 --discard old \
  --dupe-window 2m \
  2>/dev/null || echo "PEDAGOGY_EVENTS exists"

nats -s $NATS_URL stream add ENGAGEMENT_EVENTS \
  --subjects "cena.engagement.events.>" \
  --retention limits --max-age 30d --max-bytes 5368709120 \
  --storage file --replicas 1 --discard old \
  --dupe-window 2m \
  2>/dev/null || echo "ENGAGEMENT_EVENTS exists"

nats -s $NATS_URL stream add OUTREACH_EVENTS \
  --subjects "cena.outreach.events.>" \
  --retention limits --max-age 7d --max-bytes 1073741824 \
  --storage file --replicas 1 --discard old \
  2>/dev/null || echo "OUTREACH_EVENTS exists"

nats -s $NATS_URL stream add CURRICULUM_EVENTS \
  --subjects "cena.curriculum.events.>" \
  --retention limits --max-age 365d --max-bytes 1073741824 \
  --storage file --replicas 1 --discard old \
  2>/dev/null || echo "CURRICULUM_EVENTS exists"

nats -s $NATS_URL stream add ANALYTICS_EVENTS \
  --subjects "cena.analytics.events.>" \
  --retention limits --max-age 90d --max-bytes 10737418240 \
  --storage file --replicas 1 --discard old \
  2>/dev/null || echo "ANALYTICS_EVENTS exists"

nats -s $NATS_URL stream add SCHOOL_EVENTS \
  --subjects "cena.school.events.>" \
  --retention limits --max-age 90d --max-bytes 2147483648 \
  --storage file --replicas 1 --discard old \
  2>/dev/null || echo "SCHOOL_EVENTS exists"

# System streams
nats -s $NATS_URL stream add SYSTEM_HEALTH \
  --subjects "cena.system.health.>" \
  --retention limits --max-age 1d --max-bytes 268435456 \
  --storage memory --replicas 1 --discard old \
  2>/dev/null || echo "SYSTEM_HEALTH exists"

nats -s $NATS_URL stream add DEAD_LETTER \
  --subjects "cena.system.dlq.>" \
  --retention limits --max-age 30d --max-bytes 1073741824 \
  --storage file --replicas 1 --discard old \
  2>/dev/null || echo "DEAD_LETTER exists"

echo ""
echo "Creating consumers..."

# Engagement context consumers
nats -s $NATS_URL consumer add LEARNER_EVENTS engagement-xp \
  --filter "cena.learner.events.>" \
  --ack explicit --max-deliver 5 --max-pending 1000 \
  --deliver all --replay instant \
  2>/dev/null || echo "engagement-xp exists"

nats -s $NATS_URL consumer add PEDAGOGY_EVENTS engagement-session \
  --filter "cena.pedagogy.events.>" \
  --ack explicit --max-deliver 5 --max-pending 1000 \
  --deliver all --replay instant \
  2>/dev/null || echo "engagement-session exists"

# Outreach consumers
nats -s $NATS_URL consumer add LEARNER_EVENTS outreach-triggers \
  --filter "cena.learner.events.>" \
  --ack explicit --max-deliver 3 --max-pending 500 \
  --deliver all --replay instant \
  2>/dev/null || echo "outreach-triggers exists"

# Analytics consumer (all events)
nats -s $NATS_URL consumer add LEARNER_EVENTS analytics-all \
  --filter "cena.learner.events.>" \
  --ack explicit --max-deliver 10 --max-pending 5000 \
  --deliver all --replay instant \
  2>/dev/null || echo "analytics-all exists"

echo ""
echo "✅ NATS JetStream setup complete"
nats -s $NATS_URL stream ls
