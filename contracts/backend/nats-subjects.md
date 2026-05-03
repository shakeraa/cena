# NATS JetStream Subject Hierarchy & Consumer Configuration

> **Status:** Specification
> **Last updated:** 2026-03-26
> **Applies to:** All bounded contexts communicating via NATS JetStream
> **Companion to:** `docs/architecture-design.md` Section 5, `docs/event-schemas.md`

---

## 1. Subject Hierarchy

All subjects follow the pattern: `cena.{context}.{type}.{event_name}` where:
- **context** = bounded context (lowercase, singular)
- **type** = `events` (domain events), `commands` (cross-context commands), `queries` (read requests)
- **event_name** = PascalCase event name matching the `_V1` suffix types in `marten-event-store.cs`

Wildcards: `>` matches one or more tokens; `*` matches exactly one token.

```
cena.>                                          # Everything (admin/debug only)
│
├── cena.learner.events.>                       # Learner bounded context events
│   ├── cena.learner.events.ConceptAttempted     # Student attempted an exercise
│   ├── cena.learner.events.ConceptMastered      # Mastery threshold reached
│   ├── cena.learner.events.MasteryDecayed       # Predicted recall dropped below threshold
│   ├── cena.learner.events.MethodologySwitched  # Teaching approach changed
│   ├── cena.learner.events.StagnationDetected   # Learning plateau identified
│   ├── cena.learner.events.AnnotationAdded      # Student added a note/reflection
│   └── cena.learner.events.CognitiveLoadCooldownComplete  # Fatigue cooldown ended
│
├── cena.pedagogy.events.>                      # Pedagogy bounded context events
│   ├── cena.pedagogy.events.SessionStarted      # Learning session began
│   ├── cena.pedagogy.events.SessionEnded        # Learning session ended
│   ├── cena.pedagogy.events.ExercisePresented   # Question shown to student
│   ├── cena.pedagogy.events.HintRequested       # Student asked for a hint
│   └── cena.pedagogy.events.QuestionSkipped     # Student skipped a question
│
├── cena.engagement.events.>                    # Engagement bounded context events
│   ├── cena.engagement.events.XpAwarded         # XP earned
│   ├── cena.engagement.events.StreakUpdated      # Streak count changed
│   ├── cena.engagement.events.BadgeEarned        # Achievement unlocked
│   ├── cena.engagement.events.StreakExpiring      # Streak at risk of breaking
│   └── cena.engagement.events.ReviewDue           # Spaced repetition review due
│
├── cena.outreach.events.>                      # Outreach bounded context events
│   ├── cena.outreach.events.MessageSent          # Outreach message dispatched
│   ├── cena.outreach.events.MessageDelivered     # Delivery confirmed by channel
│   └── cena.outreach.events.ResponseReceived     # Student responded to outreach
│
├── cena.outreach.commands.>                    # Cross-context commands TO outreach
│   ├── cena.outreach.commands.SendReminder       # Request to send a reminder
│   └── cena.outreach.commands.CancelReminder     # Request to cancel a pending reminder
│
├── cena.analytics.events.>                     # Analytics context (downstream, read-only)
│   └── cena.analytics.events.ProjectionRebuilt   # CQRS projection was rebuilt
│
├── cena.curriculum.events.>                    # Curriculum context (rare, upstream)
│   ├── cena.curriculum.events.GraphPublished     # Knowledge graph version published
│   └── cena.curriculum.events.McmUpdated         # MCM graph confidence scores updated
│
├── cena.content.events.>                       # Content authoring context
│   ├── cena.content.events.ContentPublished      # New content artifact published
│   ├── cena.content.events.QuestionRetired       # Question removed from pool
│   └── cena.content.events.DifficultyRecalibrated # Question difficulty adjusted
│
├── cena.school.events.>                        # School context (B2B2C)
│   ├── cena.school.events.StudentEnrolled        # Student added to a school
│   └── cena.school.events.ClassAssigned          # Student assigned to a class
│
└── cena.system.>                               # Infrastructure & monitoring
    ├── cena.system.health.>                     # Health checks
    │   ├── cena.system.health.ActorCluster       # Proto.Actor cluster health
    │   ├── cena.system.health.EventStore         # Marten/PostgreSQL health
    │   └── cena.system.health.LlmAcl             # LLM ACL availability
    ├── cena.system.metrics.>                    # Operational metrics
    │   ├── cena.system.metrics.TokenBudgetExhausted  # Student hit daily LLM budget
    │   ├── cena.system.metrics.ActorPassivated       # Actor went idle
    │   ├── cena.system.metrics.ActorReactivated      # Actor woke up
    │   └── cena.system.metrics.EventStoreLatency     # Event persistence latency
    └── cena.system.dlq.>                        # Dead letter queues
        ├── cena.system.dlq.learner.>             # Failed learner event processing
        ├── cena.system.dlq.pedagogy.>            # Failed pedagogy event processing
        ├── cena.system.dlq.engagement.>          # Failed engagement event processing
        ├── cena.system.dlq.outreach.>            # Failed outreach event processing
        └── cena.system.dlq.analytics.>           # Failed analytics event processing
```

---

## 2. Stream Configurations

Each bounded context gets its own JetStream stream. Streams are the durable storage layer; consumers read from streams.

### 2.1 Core Streams

| Stream Name | Subjects | Retention | Max Age | Max Bytes | Replicas | Storage | Discard Policy | Notes |
|---|---|---|---|---|---|---|---|---|
| `LEARNER_EVENTS` | `cena.learner.events.>` | Limits | 90 days | 10 GB | 3 | File | DiscardOld | Core domain — highest durability. 90 days covers quarterly retraining. |
| `PEDAGOGY_EVENTS` | `cena.pedagogy.events.>` | Limits | 90 days | 10 GB | 3 | File | DiscardOld | Core domain — session lifecycle events. |
| `ENGAGEMENT_EVENTS` | `cena.engagement.events.>` | Limits | 30 days | 2 GB | 3 | File | DiscardOld | Supporting — gamification state changes. |
| `OUTREACH_EVENTS` | `cena.outreach.events.>` | Limits | 30 days | 2 GB | 3 | File | DiscardOld | Delivery tracking for audit trail. |
| `OUTREACH_COMMANDS` | `cena.outreach.commands.>` | WorkQueue | 7 days | 1 GB | 3 | File | DiscardOld | Commands are consumed once. WorkQueue retention ensures exactly-once processing semantics. |
| `CURRICULUM_EVENTS` | `cena.curriculum.events.>` | Limits | 365 days | 1 GB | 3 | File | DiscardOld | Rare changes — keep a full year for audit. |
| `CONTENT_EVENTS` | `cena.content.events.>` | Limits | 90 days | 2 GB | 3 | File | DiscardOld | Content lifecycle tracking. |
| `SCHOOL_EVENTS` | `cena.school.events.>` | Limits | 365 days | 1 GB | 3 | File | DiscardOld | Enrollment/class data — keep a full year. |

### 2.2 Infrastructure Streams

| Stream Name | Subjects | Retention | Max Age | Max Bytes | Replicas | Storage | Notes |
|---|---|---|---|---|---|---|---|
| `SYSTEM_HEALTH` | `cena.system.health.>` | Limits | 1 day | 256 MB | 1 | Memory | Ephemeral — memory-backed for speed. Single replica. |
| `SYSTEM_METRICS` | `cena.system.metrics.>` | Limits | 7 days | 1 GB | 1 | File | Operational metrics for dashboards. |
| `DEAD_LETTER` | `cena.system.dlq.>` | Limits | 30 days | 5 GB | 3 | File | Failed messages for investigation. 3 replicas — never lose a DLQ message. |

---

## 3. Consumer Groups

Consumers are grouped by bounded context. Each consumer group processes messages independently. Within a group, messages are load-balanced across instances (queue group semantics).

### 3.1 Engagement Context Consumers

| Consumer Name | Stream | Filter Subject | Deliver Policy | Ack Policy | Ack Wait | Max Deliver | Max Ack Pending | Notes |
|---|---|---|---|---|---|---|---|---|
| `engagement-xp-awards` | `LEARNER_EVENTS` | `cena.learner.events.ConceptAttempted` | DeliverAll | Explicit | 30s | 5 | 1000 | Awards XP on correct answers. |
| `engagement-streak-tracker` | `PEDAGOGY_EVENTS` | `cena.pedagogy.events.SessionStarted` | DeliverAll | Explicit | 30s | 5 | 500 | Updates streak on session start. |
| `engagement-mastery-badges` | `LEARNER_EVENTS` | `cena.learner.events.ConceptMastered` | DeliverAll | Explicit | 30s | 5 | 500 | Awards mastery badges. |

### 3.2 Outreach Context Consumers

| Consumer Name | Stream | Filter Subject | Deliver Policy | Ack Policy | Ack Wait | Max Deliver | Max Ack Pending | Notes |
|---|---|---|---|---|---|---|---|---|
| `outreach-streak-expiring` | `ENGAGEMENT_EVENTS` | `cena.engagement.events.StreakExpiring` | DeliverAll | Explicit | 60s | 10 | 200 | High retry — must deliver streak reminders. |
| `outreach-review-due` | `ENGAGEMENT_EVENTS` | `cena.engagement.events.ReviewDue` | DeliverAll | Explicit | 60s | 10 | 500 | Spaced repetition outreach triggers. |
| `outreach-stagnation` | `LEARNER_EVENTS` | `cena.learner.events.StagnationDetected` | DeliverAll | Explicit | 60s | 10 | 200 | Re-engagement nudges on stagnation. |
| `outreach-cooldown-complete` | `LEARNER_EVENTS` | `cena.learner.events.CognitiveLoadCooldownComplete` | DeliverAll | Explicit | 60s | 10 | 200 | "Ready to resume?" nudge. |
| `outreach-command-processor` | `OUTREACH_COMMANDS` | `cena.outreach.commands.>` | DeliverAll | Explicit | 30s | 5 | 100 | Processes cross-context outreach commands. |

### 3.3 Analytics Context Consumers

| Consumer Name | Stream | Filter Subject | Deliver Policy | Ack Policy | Ack Wait | Max Deliver | Max Ack Pending | Notes |
|---|---|---|---|---|---|---|---|---|
| `analytics-all-learner` | `LEARNER_EVENTS` | `cena.learner.events.>` | DeliverAll | Explicit | 120s | 3 | 5000 | Consumes ALL learner events for dashboards. High ack-pending for batch processing. |
| `analytics-all-pedagogy` | `PEDAGOGY_EVENTS` | `cena.pedagogy.events.>` | DeliverAll | Explicit | 120s | 3 | 5000 | Consumes ALL pedagogy events. |
| `analytics-all-engagement` | `ENGAGEMENT_EVENTS` | `cena.engagement.events.>` | DeliverAll | Explicit | 120s | 3 | 5000 | Consumes ALL engagement events. |
| `analytics-all-outreach` | `OUTREACH_EVENTS` | `cena.outreach.events.>` | DeliverAll | Explicit | 120s | 3 | 5000 | Consumes ALL outreach events for delivery analytics. |

### 3.4 School Context Consumers

| Consumer Name | Stream | Filter Subject | Deliver Policy | Ack Policy | Ack Wait | Max Deliver | Max Ack Pending | Notes |
|---|---|---|---|---|---|---|---|---|
| `school-learner-events` | `LEARNER_EVENTS` | `cena.learner.events.>` | DeliverAll | Explicit | 30s | 5 | 1000 | Filters by school tenant at application level. |
| `school-pedagogy-events` | `PEDAGOGY_EVENTS` | `cena.pedagogy.events.>` | DeliverAll | Explicit | 30s | 5 | 1000 | Session data for teacher dashboards. |

### 3.5 Curriculum Context Consumers

| Consumer Name | Stream | Filter Subject | Deliver Policy | Ack Policy | Ack Wait | Max Deliver | Max Ack Pending | Notes |
|---|---|---|---|---|---|---|---|---|
| `curriculum-methodology-outcomes` | `LEARNER_EVENTS` | `cena.learner.events.MethodologySwitched` | DeliverAll | Explicit | 60s | 5 | 500 | Feeds MCM graph confidence updates (Flywheel 1). |

---

## 4. Dead Letter Queue Configuration

Messages that exceed `Max Deliver` attempts are automatically moved to the dead letter stream. The DLQ preserves the original subject, headers, and payload for investigation.

### 4.1 DLQ Routing

When a consumer exhausts its retry budget (`Max Deliver`), the NATS server advisory triggers a custom handler that re-publishes the message to the corresponding DLQ subject:

```
Original subject:     cena.learner.events.ConceptAttempted
DLQ subject:          cena.system.dlq.learner.ConceptAttempted
```

**Pattern:** `cena.system.dlq.{original_context}.{original_event_name}`

### 4.2 DLQ Message Envelope

Each DLQ message includes additional headers:

| Header | Description |
|---|---|
| `Cena-Original-Subject` | The original NATS subject |
| `Cena-Original-Stream` | The stream the message came from |
| `Cena-Consumer-Name` | The consumer that failed to process it |
| `Cena-Delivery-Count` | How many times delivery was attempted |
| `Cena-Last-Error` | The error message from the last processing attempt |
| `Cena-First-Attempt-At` | Timestamp of the first delivery attempt |
| `Cena-Last-Attempt-At` | Timestamp of the final failed attempt |
| `Cena-Correlation-Id` | End-to-end correlation ID from the original event |

### 4.3 DLQ Monitoring & Alerting

| Metric | Alert Threshold | Severity | Action |
|---|---|---|---|
| DLQ message rate (any context) | > 10 messages/minute | Warning | Investigate consumer health |
| DLQ message rate (any context) | > 100 messages/minute | Critical | Page on-call; likely systemic failure |
| DLQ depth (accumulated unprocessed) | > 1000 messages | Warning | Schedule manual review |
| DLQ message age (oldest unprocessed) | > 24 hours | Critical | Mandatory investigation |

### 4.4 DLQ Replay

DLQ messages can be replayed back to the original subject using the admin CLI:

```bash
# Replay all DLQ messages for a specific context
nats consumer msg next DEAD_LETTER dlq-replay \
  --filter "cena.system.dlq.learner.>" \
  --count 100 \
  | cena-dlq-replay --target-stream LEARNER_EVENTS

# Replay a single message by sequence number
cena-dlq-replay --stream DEAD_LETTER --seq 42 --target-stream LEARNER_EVENTS
```

---

## 5. Monitoring Subjects

### 5.1 Health Check Protocol

Health check messages are published every 30 seconds by each subsystem. Absence of a health message for 90 seconds triggers an alert.

```json
{
  "component": "ActorCluster",
  "status": "healthy",
  "timestamp": "2026-03-26T14:30:00Z",
  "details": {
    "active_actors": 1523,
    "passivated_actors": 8201,
    "cluster_members": 3,
    "event_store_latency_p99_ms": 12
  }
}
```

### 5.2 Metric Subjects

| Subject | Published By | Frequency | Payload |
|---|---|---|---|
| `cena.system.metrics.TokenBudgetExhausted` | LLM ACL | On occurrence | `{student_id, tokens_used, budget_limit}` |
| `cena.system.metrics.ActorPassivated` | Proto.Actor cluster | On occurrence | `{actor_kind, actor_identity, idle_duration_ms}` |
| `cena.system.metrics.ActorReactivated` | Proto.Actor cluster | On occurrence | `{actor_kind, actor_identity, rehydration_duration_ms, events_replayed}` |
| `cena.system.metrics.EventStoreLatency` | Marten middleware | Every 10s | `{p50_ms, p95_ms, p99_ms, events_per_second}` |
| `cena.system.metrics.NatsStreamLag` | Consumer monitor | Every 30s | `{stream, consumer, pending_count, ack_pending_count}` |
| `cena.system.metrics.LlmRouting` | LLM ACL | Per request | `{model_tier, model_id, input_tokens, output_tokens, ttft_ms, cache_hit}` |

---

## 6. Operational Runbook: Stream Setup

### 6.1 Initial Stream Creation (NATS CLI)

```bash
# Core streams — 3 replicas, file storage, 90-day retention
nats stream add LEARNER_EVENTS \
  --subjects "cena.learner.events.>" \
  --retention limits --max-age 90d --max-bytes 10737418240 \
  --replicas 3 --storage file --discard old --dupe-window 2m

nats stream add PEDAGOGY_EVENTS \
  --subjects "cena.pedagogy.events.>" \
  --retention limits --max-age 90d --max-bytes 10737418240 \
  --replicas 3 --storage file --discard old --dupe-window 2m

nats stream add ENGAGEMENT_EVENTS \
  --subjects "cena.engagement.events.>" \
  --retention limits --max-age 30d --max-bytes 2147483648 \
  --replicas 3 --storage file --discard old --dupe-window 2m

nats stream add OUTREACH_EVENTS \
  --subjects "cena.outreach.events.>" \
  --retention limits --max-age 30d --max-bytes 2147483648 \
  --replicas 3 --storage file --discard old --dupe-window 2m

nats stream add OUTREACH_COMMANDS \
  --subjects "cena.outreach.commands.>" \
  --retention work --max-age 7d --max-bytes 1073741824 \
  --replicas 3 --storage file --discard old --dupe-window 2m

nats stream add CURRICULUM_EVENTS \
  --subjects "cena.curriculum.events.>" \
  --retention limits --max-age 365d --max-bytes 1073741824 \
  --replicas 3 --storage file --discard old --dupe-window 2m

nats stream add CONTENT_EVENTS \
  --subjects "cena.content.events.>" \
  --retention limits --max-age 90d --max-bytes 2147483648 \
  --replicas 3 --storage file --discard old --dupe-window 2m

nats stream add SCHOOL_EVENTS \
  --subjects "cena.school.events.>" \
  --retention limits --max-age 365d --max-bytes 1073741824 \
  --replicas 3 --storage file --discard old --dupe-window 2m

# Infrastructure streams
nats stream add SYSTEM_HEALTH \
  --subjects "cena.system.health.>" \
  --retention limits --max-age 1d --max-bytes 268435456 \
  --replicas 1 --storage memory --discard old

nats stream add SYSTEM_METRICS \
  --subjects "cena.system.metrics.>" \
  --retention limits --max-age 7d --max-bytes 1073741824 \
  --replicas 1 --storage file --discard old

nats stream add DEAD_LETTER \
  --subjects "cena.system.dlq.>" \
  --retention limits --max-age 30d --max-bytes 5368709120 \
  --replicas 3 --storage file --discard old
```

### 6.2 Consumer Creation Example

```bash
# Engagement context: XP awards on correct answers
nats consumer add LEARNER_EVENTS engagement-xp-awards \
  --filter "cena.learner.events.ConceptAttempted" \
  --deliver all --ack explicit --wait 30s \
  --max-deliver 5 --max-pending 1000 \
  --pull --replay instant

# Outreach context: streak expiring notifications (high retry)
nats consumer add ENGAGEMENT_EVENTS outreach-streak-expiring \
  --filter "cena.engagement.events.StreakExpiring" \
  --deliver all --ack explicit --wait 60s \
  --max-deliver 10 --max-pending 200 \
  --pull --replay instant

# Analytics: all learner events (batch processing, high pending)
nats consumer add LEARNER_EVENTS analytics-all-learner \
  --filter "cena.learner.events.>" \
  --deliver all --ack explicit --wait 120s \
  --max-deliver 3 --max-pending 5000 \
  --pull --replay instant
```

---

## 7. Message Deduplication

All JetStream streams are configured with a **2-minute deduplication window** (`--dupe-window 2m`). Publishers must set the `Nats-Msg-Id` header to the domain event's `event_id` (UUIDv7). This provides exactly-once publishing semantics within the dedup window.

The Proto.Actor NATS publisher middleware sets this header automatically:

```csharp
// Pseudo-code — actual implementation in Cena.Infrastructure.Nats
msg.Header["Nats-Msg-Id"] = domainEvent.EventId;
msg.Header["Cena-Correlation-Id"] = domainEvent.CorrelationId;
msg.Header["Cena-Causation-Id"] = domainEvent.CausationId;
msg.Header["Cena-Schema-Version"] = domainEvent.SchemaVersion.ToString();
```
