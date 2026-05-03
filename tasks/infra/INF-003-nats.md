# INF-003: NATS JetStream — Streams, Consumers, DLQ, and Deduplication

**Priority:** P0 — blocks all cross-context event communication
**Blocked by:** INF-001 (VPC for NAT egress to Synadia Cloud)
**Estimated effort:** 3 days
**Contract:** `contracts/backend/nats-subjects.md`, `docs/architecture-design.md` Section 5, `docs/operations.md` Section 3.2

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule. `throw UnimplementedError`, `// TODO: implement`, empty bodies, and mock returns are FORBIDDEN in source code. If you cannot implement it fully, file a blocking dependency instead.

## Context

NATS JetStream is the event backbone connecting all nine bounded contexts. The architecture uses Synadia Cloud (managed NATS) at $49-99/month. This task provisions all 11 streams, 15 consumers, the dead-letter queue infrastructure, and deduplication configuration exactly as specified in `contracts/backend/nats-subjects.md`.

Every domain event published by the Proto.Actor cluster flows through JetStream. Downstream contexts (Engagement, Outreach, Analytics, School, Curriculum) subscribe via durable pull consumers. Failed messages route to the `DEAD_LETTER` stream after exhausting retries. All streams use a 2-minute deduplication window keyed on `Nats-Msg-Id` (the domain event's UUIDv7 `event_id`).

---

## Subtasks

### INF-003.1: Synadia Cloud Account + Connection Configuration

**Files to create/modify:**
- `infra/nats/synadia-setup.md` — manual setup instructions (Synadia Cloud has no Terraform provider)
- `infra/nats/connection-config.yaml` — connection parameters per environment
- `src/Cena.Infrastructure/Nats/NatsConnectionFactory.cs` — connection factory
- `config/nats/credentials/` — `.gitignore`d credential directory

**Acceptance:**
- [ ] Synadia Cloud account created with team `cena-engineering`
- [ ] Two systems provisioned: `cena-staging` (R1 replication) and `cena-prod` (R3 replication)
- [ ] Connection URL, JWT token, and NKey seed stored in AWS Secrets Manager as `cena/nats/staging` and `cena/nats/prod`
- [ ] `NatsConnectionFactory` reads credentials from Secrets Manager at startup (not environment variables)
- [ ] Connection config per environment:
  ```yaml
  staging:
    url: "tls://connect.ngs.global"
    credentials_secret: "cena/nats/staging"
    max_reconnect_attempts: -1  # infinite reconnect
    reconnect_wait_ms: 2000
    ping_interval_ms: 20000
    max_pings_outstanding: 3
  prod:
    url: "tls://connect.ngs.global"
    credentials_secret: "cena/nats/prod"
    max_reconnect_attempts: -1
    reconnect_wait_ms: 1000
    ping_interval_ms: 10000
    max_pings_outstanding: 5
  ```
- [ ] TLS required for all connections (Synadia Cloud enforces this)
- [ ] Connection health check integrated with ASP.NET health endpoint (`/health/ready`)

**Test:**
```bash
# Verify connectivity from local dev (using NATS CLI)
nats context add cena-staging \
  --server "tls://connect.ngs.global" \
  --creds ~/.nats/cena-staging.creds

nats context select cena-staging
nats server check connection
# Expect: OK Connected to "ngs-..."

# Verify credential secret exists in Secrets Manager
aws secretsmanager describe-secret --secret-id "cena/nats/staging" \
  --query 'Name' --output text
# Expect: cena/nats/staging
```

**Edge cases:**
- Synadia Cloud outage — NATS client reconnects infinitely; actor cluster operates in degraded mode (events buffered locally, not published)
- Credential rotation on Synadia Cloud — need zero-downtime rotation: configure new credential in Secrets Manager, restart services one-by-one
- Network partition between ECS and Synadia Cloud (NAT gateway failure) — same behavior as outage; monitor via `cena.system.health.NatsConnection` subject

---

### INF-003.2: Create All 11 Streams

**Files to create/modify:**
- `infra/nats/streams/create-all-streams.sh` — idempotent stream creation script
- `infra/nats/streams/stream-configs.json` — declarative stream configuration

**Acceptance:**

Create all 11 streams exactly matching `contracts/backend/nats-subjects.md` Section 2:

**Core Streams (8):**

| Stream | Subjects | Retention | Max Age | Max Bytes | Replicas | Storage | Dedup Window |
|--------|----------|-----------|---------|-----------|----------|---------|-------------|
| `LEARNER_EVENTS` | `cena.learner.events.>` | Limits | 90d | 10 GB | 3 | File | 2m |
| `PEDAGOGY_EVENTS` | `cena.pedagogy.events.>` | Limits | 90d | 10 GB | 3 | File | 2m |
| `ENGAGEMENT_EVENTS` | `cena.engagement.events.>` | Limits | 30d | 2 GB | 3 | File | 2m |
| `OUTREACH_EVENTS` | `cena.outreach.events.>` | Limits | 30d | 2 GB | 3 | File | 2m |
| `OUTREACH_COMMANDS` | `cena.outreach.commands.>` | WorkQueue | 7d | 1 GB | 3 | File | 2m |
| `CURRICULUM_EVENTS` | `cena.curriculum.events.>` | Limits | 365d | 1 GB | 3 | File | 2m |
| `CONTENT_EVENTS` | `cena.content.events.>` | Limits | 90d | 2 GB | 3 | File | 2m |
| `SCHOOL_EVENTS` | `cena.school.events.>` | Limits | 365d | 1 GB | 3 | File | 2m |

**Infrastructure Streams (3):**

| Stream | Subjects | Retention | Max Age | Max Bytes | Replicas | Storage |
|--------|----------|-----------|---------|-----------|----------|---------|
| `SYSTEM_HEALTH` | `cena.system.health.>` | Limits | 1d | 256 MB | 1 | Memory |
| `SYSTEM_METRICS` | `cena.system.metrics.>` | Limits | 7d | 1 GB | 1 | File |
| `DEAD_LETTER` | `cena.system.dlq.>` | Limits | 30d | 5 GB | 3 | File |

- [ ] All streams use `DiscardOld` discard policy
- [ ] All file-backed streams use dedup window of 2 minutes
- [ ] `OUTREACH_COMMANDS` uses `WorkQueue` retention (exactly-once consumer semantics)
- [ ] `SYSTEM_HEALTH` is memory-backed with R1 (ephemeral, fast)
- [ ] `DEAD_LETTER` has R3 replication (never lose a DLQ message)
- [ ] Script is idempotent — running twice produces no errors or duplicates
- [ ] Staging uses R1 for all streams (cost savings); prod uses R3 as specified

**Test:**
```bash
#!/bin/bash
set -euo pipefail

# Run creation script
bash infra/nats/streams/create-all-streams.sh

# Verify all 11 streams exist
STREAM_COUNT=$(nats stream list --json | jq 'length')
[ "$STREAM_COUNT" -eq 11 ] && echo "PASS: 11 streams" || echo "FAIL: got $STREAM_COUNT streams"

# Verify each stream's configuration
for stream in LEARNER_EVENTS PEDAGOGY_EVENTS ENGAGEMENT_EVENTS OUTREACH_EVENTS \
              OUTREACH_COMMANDS CURRICULUM_EVENTS CONTENT_EVENTS SCHOOL_EVENTS \
              SYSTEM_HEALTH SYSTEM_METRICS DEAD_LETTER; do
  echo "--- $stream ---"
  nats stream info "$stream" --json | jq '{
    name: .config.name,
    subjects: .config.subjects,
    retention: .config.retention,
    max_age: .config.max_age,
    max_bytes: .config.max_bytes,
    num_replicas: .config.num_replicas,
    storage: .config.storage,
    duplicate_window: .config.duplicate_window
  }'
done

# Verify OUTREACH_COMMANDS is WorkQueue retention
RETENTION=$(nats stream info OUTREACH_COMMANDS --json | jq -r '.config.retention')
[ "$RETENTION" = "workqueue" ] && echo "PASS: WorkQueue retention" || echo "FAIL: retention=$RETENTION"

# Verify SYSTEM_HEALTH is memory-backed
STORAGE=$(nats stream info SYSTEM_HEALTH --json | jq -r '.config.storage')
[ "$STORAGE" = "memory" ] && echo "PASS: memory storage" || echo "FAIL: storage=$STORAGE"

# Verify DEAD_LETTER has 3 replicas
REPLICAS=$(nats stream info DEAD_LETTER --json | jq '.config.num_replicas')
[ "$REPLICAS" -eq 3 ] && echo "PASS: 3 replicas on DLQ" || echo "FAIL: replicas=$REPLICAS"

# Idempotency: run again — no errors
bash infra/nats/streams/create-all-streams.sh 2>&1 | grep -i "error" && echo "FAIL: errors on re-run" || echo "PASS: idempotent"
```

**Edge cases:**
- Stream already exists with different config — script should detect and report drift, not silently ignore
- Synadia Cloud quota exceeded (max streams per account) — verify limits during account setup
- R3 replication on staging wastes money — override to R1 via environment variable

---

### INF-003.3: Create All 15 Consumers

**Files to create/modify:**
- `infra/nats/consumers/create-all-consumers.sh` — idempotent consumer creation script
- `infra/nats/consumers/consumer-configs.json` — declarative consumer configuration

**Acceptance:**

Create all 15 consumers exactly matching `contracts/backend/nats-subjects.md` Section 3:

**Engagement Context (3):**

| Consumer | Stream | Filter | Ack Wait | Max Deliver | Max Ack Pending |
|----------|--------|--------|----------|-------------|-----------------|
| `engagement-xp-awards` | `LEARNER_EVENTS` | `cena.learner.events.ConceptAttempted` | 30s | 5 | 1000 |
| `engagement-streak-tracker` | `PEDAGOGY_EVENTS` | `cena.pedagogy.events.SessionStarted` | 30s | 5 | 500 |
| `engagement-mastery-badges` | `LEARNER_EVENTS` | `cena.learner.events.ConceptMastered` | 30s | 5 | 500 |

**Outreach Context (5):**

| Consumer | Stream | Filter | Ack Wait | Max Deliver | Max Ack Pending |
|----------|--------|--------|----------|-------------|-----------------|
| `outreach-streak-expiring` | `ENGAGEMENT_EVENTS` | `cena.engagement.events.StreakExpiring` | 60s | 10 | 200 |
| `outreach-review-due` | `ENGAGEMENT_EVENTS` | `cena.engagement.events.ReviewDue` | 60s | 10 | 500 |
| `outreach-stagnation` | `LEARNER_EVENTS` | `cena.learner.events.StagnationDetected` | 60s | 10 | 200 |
| `outreach-cooldown-complete` | `LEARNER_EVENTS` | `cena.learner.events.CognitiveLoadCooldownComplete` | 60s | 10 | 200 |
| `outreach-command-processor` | `OUTREACH_COMMANDS` | `cena.outreach.commands.>` | 30s | 5 | 100 |

**Analytics Context (4):**

| Consumer | Stream | Filter | Ack Wait | Max Deliver | Max Ack Pending |
|----------|--------|--------|----------|-------------|-----------------|
| `analytics-all-learner` | `LEARNER_EVENTS` | `cena.learner.events.>` | 120s | 3 | 5000 |
| `analytics-all-pedagogy` | `PEDAGOGY_EVENTS` | `cena.pedagogy.events.>` | 120s | 3 | 5000 |
| `analytics-all-engagement` | `ENGAGEMENT_EVENTS` | `cena.engagement.events.>` | 120s | 3 | 5000 |
| `analytics-all-outreach` | `OUTREACH_EVENTS` | `cena.outreach.events.>` | 120s | 3 | 5000 |

**School Context (2):**

| Consumer | Stream | Filter | Ack Wait | Max Deliver | Max Ack Pending |
|----------|--------|--------|----------|-------------|-----------------|
| `school-learner-events` | `LEARNER_EVENTS` | `cena.learner.events.>` | 30s | 5 | 1000 |
| `school-pedagogy-events` | `PEDAGOGY_EVENTS` | `cena.pedagogy.events.>` | 30s | 5 | 1000 |

**Curriculum Context (1):**

| Consumer | Stream | Filter | Ack Wait | Max Deliver | Max Ack Pending |
|----------|--------|--------|----------|-------------|-----------------|
| `curriculum-methodology-outcomes` | `LEARNER_EVENTS` | `cena.learner.events.MethodologySwitched` | 60s | 5 | 500 |

- [ ] All consumers are durable pull consumers (`--pull`)
- [ ] All consumers use `DeliverAll` policy and `Explicit` ack
- [ ] All consumers use `--replay instant`
- [ ] Outreach consumers have higher `Max Deliver` (10) than default (5) — streak reminders must get through
- [ ] Analytics consumers have high `Max Ack Pending` (5000) for batch processing
- [ ] Script is idempotent

**Test:**
```bash
#!/bin/bash
set -euo pipefail

# Run creation script
bash infra/nats/consumers/create-all-consumers.sh

# Count consumers per stream
for stream in LEARNER_EVENTS PEDAGOGY_EVENTS ENGAGEMENT_EVENTS OUTREACH_EVENTS \
              OUTREACH_COMMANDS; do
  COUNT=$(nats consumer list "$stream" --json | jq 'length')
  echo "$stream: $COUNT consumers"
done
# Expected: LEARNER_EVENTS=5, PEDAGOGY_EVENTS=3, ENGAGEMENT_EVENTS=3,
#           OUTREACH_EVENTS=1, OUTREACH_COMMANDS=1

# Verify specific consumer config
echo "--- outreach-streak-expiring ---"
nats consumer info ENGAGEMENT_EVENTS outreach-streak-expiring --json | jq '{
  filter_subject: .config.filter_subject,
  ack_wait: .config.ack_wait,
  max_deliver: .config.max_deliver,
  max_ack_pending: .config.max_ack_pending,
  deliver_policy: .config.deliver_policy,
  ack_policy: .config.ack_policy
}'
# Expect: filter=cena.engagement.events.StreakExpiring, ack_wait=60s,
#         max_deliver=10, max_ack_pending=200

# Verify analytics consumer has high ack pending
MAX_PENDING=$(nats consumer info LEARNER_EVENTS analytics-all-learner --json | jq '.config.max_ack_pending')
[ "$MAX_PENDING" -eq 5000 ] && echo "PASS: analytics max_ack_pending=5000" || echo "FAIL: got $MAX_PENDING"

# End-to-end: publish a test message and consume it
DEDUP_ID="test-$(date +%s)"
nats pub cena.learner.events.ConceptAttempted '{"test":true}' --header "Nats-Msg-Id:$DEDUP_ID"
MSG=$(nats consumer next LEARNER_EVENTS engagement-xp-awards --timeout 5s 2>/dev/null)
echo "$MSG" | grep -q "test" && echo "PASS: message received" || echo "FAIL: message not received"
```

**Edge cases:**
- Consumer created on wrong stream — the filter subject won't match any messages; detect via zero delivery count after test publish
- Consumer name typo — downstream services reference consumer names by string; maintain a constants file
- Changing `Max Deliver` on existing consumer requires consumer deletion and recreation — document this procedure

---

### INF-003.4: DLQ Handler + Deduplication Verification

**Files to create/modify:**
- `src/Cena.Infrastructure/Nats/DlqHandler.cs` — DLQ routing handler
- `src/Cena.Infrastructure/Nats/DlqHeaders.cs` — DLQ header constants
- `infra/nats/test/dedup-test.sh` — deduplication verification script
- `infra/nats/test/dlq-test.sh` — DLQ routing verification script

**Acceptance:**

**Dead Letter Queue:**
- [ ] When a consumer exhausts `Max Deliver`, an advisory triggers the DLQ handler
- [ ] DLQ handler re-publishes to `cena.system.dlq.{original_context}.{original_event_name}`
- [ ] DLQ messages include all headers from `contracts/backend/nats-subjects.md` Section 4.2:
  - `Cena-Original-Subject`
  - `Cena-Original-Stream`
  - `Cena-Consumer-Name`
  - `Cena-Delivery-Count`
  - `Cena-Last-Error`
  - `Cena-First-Attempt-At`
  - `Cena-Last-Attempt-At`
  - `Cena-Correlation-Id`
- [ ] DLQ messages are stored in the `DEAD_LETTER` stream (30d retention, R3)

**Deduplication:**
- [ ] All streams enforce 2-minute dedup window via `Nats-Msg-Id` header
- [ ] Proto.Actor publisher middleware sets `Nats-Msg-Id` to the domain event's `event_id` (UUIDv7)
- [ ] Duplicate publishes within 2 minutes are silently dropped (no error, no duplicate in stream)

**Monitoring:**
- [ ] DLQ message rate metric published to `cena.system.metrics.DlqMessageRate`
- [ ] Alert thresholds configured per `contracts/backend/nats-subjects.md` Section 4.3:
  - Warning: >10 DLQ messages/minute (any context)
  - Critical: >100 DLQ messages/minute (any context)
  - Warning: DLQ depth >1000 messages
  - Critical: oldest DLQ message >24 hours

**Test:**
```bash
#!/bin/bash
set -euo pipefail

echo "=== Deduplication Test ==="
# Publish same message ID twice
MSG_ID="dedup-test-$(date +%s)"
nats pub cena.learner.events.ConceptAttempted '{"test":"first"}' --header "Nats-Msg-Id:$MSG_ID"
nats pub cena.learner.events.ConceptAttempted '{"test":"duplicate"}' --header "Nats-Msg-Id:$MSG_ID"

# Check stream has only 1 message with this ID
STREAM_MSGS=$(nats stream info LEARNER_EVENTS --json | jq '.state.messages')
# The second publish should have been deduped

echo "=== DLQ Routing Test ==="
# Create a test consumer with max_deliver=1 for fast DLQ testing
nats consumer add LEARNER_EVENTS dlq-test-consumer \
  --filter "cena.learner.events.ConceptAttempted" \
  --deliver all --ack explicit --wait 5s \
  --max-deliver 1 --max-pending 10 --pull --replay instant

# Publish a message
nats pub cena.learner.events.ConceptAttempted '{"test":"dlq"}' \
  --header "Nats-Msg-Id:dlq-test-$(date +%s)" \
  --header "Cena-Correlation-Id:test-correlation-123"

# Fetch but don't ack (will trigger max_deliver after ack_wait expires)
nats consumer next LEARNER_EVENTS dlq-test-consumer --timeout 5s --no-ack

# Wait for ack timeout + advisory
sleep 10

# Check DLQ stream for the message
DLQ_MSG=$(nats stream get DEAD_LETTER --last-for "cena.system.dlq.learner.ConceptAttempted" 2>/dev/null)
echo "$DLQ_MSG" | grep -q "dlq" && echo "PASS: DLQ received message" || echo "WARN: DLQ may need advisory handler running"

# Verify DLQ headers
echo "$DLQ_MSG" | grep -q "Cena-Original-Subject" && echo "PASS: DLQ headers present" || echo "FAIL: missing DLQ headers"

# Cleanup
nats consumer rm LEARNER_EVENTS dlq-test-consumer --force
```

```csharp
[Fact]
public async Task DlqHandler_RoutesFailedMessage_WithCorrectHeaders()
{
    // Arrange
    var originalEvent = new ConceptAttempted_V1
    {
        EventId = Guid.CreateVersion7(),
        StudentId = "test-student",
        ConceptId = "math_5u_derivatives_basic",
        CorrelationId = "corr-123"
    };

    var advisory = new ConsumerDeliveryExceeded
    {
        Stream = "LEARNER_EVENTS",
        Consumer = "engagement-xp-awards",
        StreamSequence = 42,
        Deliveries = 5
    };

    // Act
    await _dlqHandler.HandleDeliveryExceeded(advisory);

    // Assert
    var dlqMsg = await _nats.GetLastMessage("DEAD_LETTER",
        "cena.system.dlq.learner.ConceptAttempted");

    Assert.Equal("cena.learner.events.ConceptAttempted",
        dlqMsg.Headers["Cena-Original-Subject"]);
    Assert.Equal("LEARNER_EVENTS",
        dlqMsg.Headers["Cena-Original-Stream"]);
    Assert.Equal("engagement-xp-awards",
        dlqMsg.Headers["Cena-Consumer-Name"]);
    Assert.Equal("5",
        dlqMsg.Headers["Cena-Delivery-Count"]);
    Assert.Equal("corr-123",
        dlqMsg.Headers["Cena-Correlation-Id"]);
}

[Fact]
public async Task Deduplication_RejectsDuplicateWithin2Minutes()
{
    var eventId = Guid.CreateVersion7().ToString();
    var msg1 = new NatsMsg { Data = "{\"v\":1}" };
    msg1.Headers["Nats-Msg-Id"] = eventId;

    var msg2 = new NatsMsg { Data = "{\"v\":2}" };
    msg2.Headers["Nats-Msg-Id"] = eventId;

    await _js.PublishAsync("cena.learner.events.ConceptAttempted", msg1);
    var ack2 = await _js.PublishAsync("cena.learner.events.ConceptAttempted", msg2);

    Assert.True(ack2.Duplicate); // JetStream reports duplicate
}
```

**Edge cases:**
- DLQ handler crashes while routing — the advisory is lost; NATS does not retry advisories; mitigate by monitoring DLQ depth vs. expected failure rate
- Dedup window too short for slow publishers — 2 minutes is generous; if a publisher takes >2 min between retries, it may re-publish duplicates; use unique `Nats-Msg-Id` per publish attempt
- DLQ replay of a message that still has the same root cause — re-enters the failure loop; implement replay with backoff or after root cause fix
- NATS advisory format changes between versions — pin Synadia Cloud version; test advisory parsing in CI

---

## Integration Test (all subtasks combined)

```bash
#!/bin/bash
set -euo pipefail

echo "=== INF-003 Full Integration Test ==="

# 1. Verify connection
nats server check connection && echo "PASS: connected" || exit 1

# 2. Verify all 11 streams
EXPECTED_STREAMS="CONTENT_EVENTS CURRICULUM_EVENTS DEAD_LETTER ENGAGEMENT_EVENTS LEARNER_EVENTS OUTREACH_COMMANDS OUTREACH_EVENTS PEDAGOGY_EVENTS SCHOOL_EVENTS SYSTEM_HEALTH SYSTEM_METRICS"
ACTUAL_STREAMS=$(nats stream list --names | sort | tr '\n' ' ' | sed 's/ $//')
[ "$ACTUAL_STREAMS" = "$EXPECTED_STREAMS" ] && echo "PASS: all 11 streams" || echo "FAIL: streams=$ACTUAL_STREAMS"

# 3. Verify consumer count
TOTAL_CONSUMERS=0
for stream in $(nats stream list --names); do
  COUNT=$(nats consumer list "$stream" --names 2>/dev/null | wc -l | tr -d ' ')
  TOTAL_CONSUMERS=$((TOTAL_CONSUMERS + COUNT))
done
[ "$TOTAL_CONSUMERS" -ge 15 ] && echo "PASS: $TOTAL_CONSUMERS consumers (>= 15)" || echo "FAIL: only $TOTAL_CONSUMERS consumers"

# 4. End-to-end event flow
MSG_ID="integration-$(date +%s)"
nats pub cena.learner.events.ConceptAttempted \
  '{"event_id":"'$MSG_ID'","student_id":"test","concept_id":"math_5u_test"}' \
  --header "Nats-Msg-Id:$MSG_ID" \
  --header "Cena-Correlation-Id:integration-test"

# Verify all consumers on LEARNER_EVENTS can fetch the message
for consumer in engagement-xp-awards analytics-all-learner school-learner-events; do
  MSG=$(nats consumer next LEARNER_EVENTS "$consumer" --timeout 5s 2>/dev/null || true)
  echo "$MSG" | grep -q "$MSG_ID" && echo "PASS: $consumer received" || echo "FAIL: $consumer did not receive"
done

echo "=== INF-003 Integration Test Complete ==="
```

## Rollback Criteria

If this task fails or introduces instability:
- Delete all consumers first (`nats consumer rm <stream> <consumer> --force`), then streams (`nats stream rm <stream> --force`)
- Synadia Cloud retains no state after stream deletion — clean slate
- Downstream services fail gracefully when NATS is unavailable (buffered publishing, circuit breaker)
- No data loss risk — NATS is a transit layer, not the source of truth (Marten/PostgreSQL is)

## Definition of Done

- [ ] All 11 streams created with exact configurations from the contract
- [ ] All 15 consumers created with exact configurations from the contract
- [ ] Deduplication verified: duplicate `Nats-Msg-Id` within 2 minutes is rejected
- [ ] DLQ handler routes failed messages with all 8 required headers
- [ ] DLQ monitoring alerts configured (10/min warning, 100/min critical, depth >1000, age >24h)
- [ ] Integration test passes end-to-end
- [ ] Stream creation script is idempotent
- [ ] Staging and prod configurations documented
- [ ] PR reviewed by architect
