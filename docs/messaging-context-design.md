# Messaging Context — Bidirectional Communication Architecture

> **Status:** Approved
> **Last updated:** 2026-03-27
> **Audience:** Engineering team, backend lead, security reviewer
> **Companion to:** `docs/architecture-design.md` Section 3.2, `contracts/backend/nats-subjects.md`

---

## 1. Problem Statement

Cena distills learning interactions into a concept memory graph (mastery, BKT, HLR timers) but lacks a channel for human-to-human communication. Three critical gaps exist:

| Gap | Impact |
|-----|--------|
| Teacher cannot send feedback to students | No direct pedagogical intervention outside learning sessions |
| Parent cannot encourage their child | No accountability or emotional support channel |
| No class-wide announcements | No way to coordinate group activities, assign homework, or schedule reviews |
| Inbound replies not processed | WhatsApp/Telegram outreach is one-directional; students cannot respond |

The Outreach context handles **system-to-student push** (streak reminders, review nudges). This design introduces the **Messaging context** for **human-initiated, bidirectional communication**.

---

## 2. Architecture Decision: Why a Separate Bounded Context

Messaging is a **peer context to Outreach**, not a sub-module, because:

| Criterion | Outreach | Messaging |
|-----------|----------|-----------|
| **Aggregate root** | Timer-based scheduling (no persistent aggregate) | `ConversationThread` (event-sourced) |
| **Trigger** | System events (StreakExpiring, ReviewDue) | Human action (teacher types a message) |
| **Access control** | Per-student preferences | Role-based per-thread authorization |
| **Lifecycle** | Transient — items dispatched and forgotten | Permanent records with audit trail |
| **Compliance** | Operational logs (30-day retention) | COPPA/GDPR audit trail (365-day retention) |
| **Volume** | Low (max 3/student/day, throttled) | Potentially high (class broadcasts, parent threads) |

**Integration point:** The `MessageClassifier` service sits at the boundary. When an inbound WhatsApp/Telegram reply arrives, it classifies the content:
- **Learning signal** (quiz answer, confirmation) → routes to `StudentActor` via `cena.learner.events.>`
- **Communication** (greeting, encouragement, resource) → routes to `ConversationThreadActor` via `cena.messaging.commands.>`

---

## 3. Storage Architecture: Hot/Warm/Cold Tiering

PostgreSQL (Marten event store) is the wrong fit for chat messages at scale. Different access patterns and volume profiles demand a tiered storage strategy.

### 3.1 Why Not PostgreSQL for Messages

| Concern | Mastery Events (PostgreSQL) | Chat Messages |
|---------|---------------------------|---------------|
| Volume | ~50-200/student/day | Thousands/day across threads |
| Access pattern | Replay full stream per aggregate | Paginate recent, rarely read old |
| Retention | 90 days (model retraining) | 365 days (audit) but cold after 30 |
| Query shape | Sequential replay by student ID | Reverse-chronological by thread ID |
| Write pattern | Single writer (StudentActor) | Multi-writer (teacher, parent, webhooks) |

Storing every "good morning" alongside mastery events would bloat the event store, degrade replay performance, and conflate audit requirements.

### 3.2 Tiered Storage Design

```
┌─────────────────────────────────────────────────────────────────────┐
│                     Message Write Path                               │
│                                                                     │
│  ConversationThreadActor                                            │
│    │                                                                │
│    ├──► Redis Streams (HOT)                                         │
│    │    XADD cena:thread:{threadId} * ...                          │
│    │    TTL: 30 days auto-evict                                    │
│    │                                                                │
│    ├──► NATS JetStream (AUDIT)                                     │
│    │    cena.messaging.events.MessageSent                          │
│    │    Retention: 365 days, 3 replicas                            │
│    │                                                                │
│    └──► PostgreSQL/Marten (METADATA ONLY)                          │
│         ThreadSummary projection: threadId, participants,           │
│         lastMessageAt, unreadCounts                                │
│                                                                     │
│                     Message Read Path                               │
│                                                                     │
│  GET /api/messaging/threads/:id/messages                           │
│    │                                                                │
│    ├── before > 30 days ago? ──► S3 (COLD)                        │
│    │                             Gzipped JSON-lines                │
│    │                             s3://cena-messages/{year}/{month} │
│    │                                                                │
│    └── before <= 30 days? ──► Redis Streams (HOT)                 │
│                               XREVRANGE cena:thread:{threadId}     │
│                                                                     │
│                     Archival Path (Nightly)                         │
│                                                                     │
│  Archival Worker (ECS Scheduled Task, 02:00 UTC)                   │
│    ├── Scans Redis keys with TTL < 24h                             │
│    ├── XRANGE → gzip → S3 PUT                                     │
│    ├── Verifies S3 object integrity (SHA-256)                      │
│    └── Logs to cena.messaging.events.MessagesArchived              │
└─────────────────────────────────────────────────────────────────────┘
```

### 3.3 Storage Tier Summary

| Tier | Technology | Data | TTL | Access Pattern | Cost Profile |
|------|-----------|------|-----|----------------|--------------|
| **Hot** | Redis Streams (ElastiCache) | Recent messages (0-30 days) | 30 days auto-evict | `XREVRANGE` for paginated reads, `XADD` for writes | ~$15-25/month (shared cluster) |
| **Audit** | NATS JetStream (`MESSAGING_EVENTS`) | All message events | 365 days | Consumer groups for analytics | ~$5-10/month (included in NATS cluster) |
| **Metadata** | PostgreSQL (Marten projection) | Thread summaries, unread counts | Permanent | SQL queries for thread list | ~$0 (rows in existing RDS) |
| **Cold** | S3 | Archived messages (30+ days) | 365 days (lifecycle policy) | On-demand fetch, rare | ~$0.50/month (S3 Standard-IA) |

### 3.4 Redis Stream Key Schema

```
cena:thread:{threadId}              # Message stream per thread
cena:thread:{threadId}:meta         # Hash: threadType, participantIds, createdAt
cena:thread:{threadId}:unread:{uid} # Counter: unread count per participant
cena:user:{userId}:threads          # Sorted set: threadIds scored by lastMessageAt
cena:webhook:dedup:{source}:{msgId} # String with 5min TTL: idempotency key
```

**Message entry fields (Redis Stream):**
```
messageId:    UUIDv7
senderId:     string
senderRole:   Teacher|Parent|Student|System
senderName:   string
contentText:  string (max 2000 chars)
contentType:  text|resource-link|encouragement
resourceUrl:  string|null
replyTo:      messageId|null
channel:      InApp|WhatsApp|Telegram|Push
sentAt:       ISO 8601 UTC
```

### 3.5 S3 Archive Format

```
s3://cena-messages/
├── 2026/
│   ├── 03/
│   │   ├── thread-{threadId}-20260301-20260331.jsonl.gz
│   │   └── manifest.json  # Lists all archived threads + SHA-256 checksums
│   └── 04/
│       └── ...
```

Each `.jsonl.gz` file contains one JSON object per line matching the Redis Stream entry schema. The manifest enables integrity verification and selective retrieval.

---

## 4. Actor Design: ConversationThreadActor

### 4.1 Actor Position in Hierarchy

```
Proto.Actor Cluster
├── StudentActor [virtual, event-sourced]         ← existing
│   ├── LearningSessionActor [classic, child]     ← existing
│   ├── StagnationDetectorActor [classic, child]  ← existing
│   └── OutreachSchedulerActor [classic, child]   ← existing
│
├── ConversationThreadActor [virtual, stateful]   ← NEW
│   └── ClusterIdentity(kind: "conversation-thread", identity: threadId)
│   └── State: in-memory from Redis Stream replay (not Marten)
│
└── ClassroomActor [virtual, lightweight]         ← future (School context)
```

### 4.2 State Management

Unlike `StudentActor` (which replays from Marten event store), `ConversationThreadActor` reconstructs state from **Redis Streams** on activation:

```csharp
// Pseudocode — actor activation
public async Task OnActivateAsync()
{
    // 1. Load thread metadata from Redis hash
    var meta = await _redis.HashGetAllAsync($"cena:thread:{_threadId}:meta");
    _threadType = meta["threadType"];
    _participantIds = meta["participantIds"].Split(',');

    // 2. Load last N messages from Redis Stream for working state
    var recent = await _redis.StreamRangeAsync(
        $"cena:thread:{_threadId}", count: 50, order: Order.Descending);
    _lastMessageAt = recent.FirstOrDefault()?.Values["sentAt"];
    _messageCount = await _redis.StreamLengthAsync($"cena:thread:{_threadId}");
}
```

**Why not Marten for this actor?**
- Chat threads don't need full event replay — latest state is sufficient
- Redis Stream `XLEN` / `XREVRANGE` is O(1) / O(log N) — faster than Marten replay
- Actor passivation is cheap (no snapshot needed — Redis is the store)

### 4.3 Message Flow

```
Teacher clicks "Send Feedback"
    │
    ▼
SignalR Hub (MessagingHub)
    │ authorize: teacher.classIds.contains(student.classId)
    ▼
ConversationThreadActor.SendMessage(...)
    │
    ├──► Redis XADD cena:thread:{threadId}     (hot store)
    ├──► Redis INCR cena:thread:{threadId}:unread:{recipientId}
    ├──► NATS publish cena.messaging.events.MessageSent  (audit)
    ├──► Marten upsert ThreadSummary projection  (metadata)
    └──► SignalR broadcast to recipient connection(s)
         │
         ├── In-app? → direct SignalR push
         ├── WhatsApp? → NATS cena.outreach.commands.SendReminder
         └── Telegram? → NATS cena.outreach.commands.SendReminder
```

---

## 5. Message Classification: Learning Signal vs Communication

### 5.1 Classifier Design

The `MessageClassifier` is a **stateless, deterministic service** (no LLM call) that runs at the inbound webhook boundary.

```
Inbound WhatsApp/Telegram message
    │
    ▼
MessageClassifier.Classify(text, locale)
    │
    ├── IsNumericAnswer(text)?  → LearningSignal(intent: "quiz-answer")
    ├── IsConfirmation(text)?   → LearningSignal(intent: "confirmation")
    │   Patterns: "yes","no","כן","לא","نعم","لا","1","2","3","4","a","b","c","d"
    ├── IsConceptQuestion(text)? → LearningSignal(intent: "concept-question")
    │   Patterns: starts with "what is", "how do", "מה זה", "كيف", "explain"
    │
    └── else → Communication(intent: inferred)
        ├── HasUrl(text)?       → "resource-share"
        ├── IsGreeting(text)?   → "greeting"
        ├── HasEmoji > 50%?     → "reaction"
        └── default             → "general"
```

### 5.2 Classification Rules

| Pattern | Language | Intent | Route |
|---------|----------|--------|-------|
| `^\d+(\.\d+)?$` | Any | quiz-answer | `cena.learner.events.>` |
| `^[a-d]$` (case-insensitive) | Any | quiz-answer | `cena.learner.events.>` |
| `yes\|no\|כן\|לא\|نعم\|لا` | he/ar/en | confirmation | `cena.learner.events.>` |
| `^(what is\|how do\|explain\|מה זה\|كيف\|اشرح)` | he/ar/en | concept-question | `cena.learner.events.>` |
| Everything else | Any | communication | `cena.messaging.commands.>` |

**Confidence threshold:** If a message matches multiple patterns, the highest-confidence match wins. Ambiguous messages (confidence < 0.7) default to **communication** (safe default — better to log a quiz answer as chat than to feed chat into the concept graph).

---

## 6. NATS Integration

### 6.1 New Subjects

```
cena.messaging.events.>                          # Messaging bounded context events
├── cena.messaging.events.MessageSent            # Human message dispatched
├── cena.messaging.events.MessageRead            # Message marked as read
├── cena.messaging.events.ThreadCreated          # New conversation thread
├── cena.messaging.events.ThreadMuted            # Thread muted by participant
├── cena.messaging.events.MessageBlocked         # Content moderation blocked a message
├── cena.messaging.events.InboundReceived        # Webhook received (audit trail)
└── cena.messaging.events.MessagesArchived       # Batch archived to S3

cena.messaging.commands.>                        # Cross-context commands TO messaging
├── cena.messaging.commands.SendMessage          # Request to send a message
├── cena.messaging.commands.RouteInboundReply    # Webhook handler routes an inbound reply
└── cena.messaging.commands.BroadcastToClass     # Teacher broadcasts to all students in class
```

### 6.2 Stream Configurations

| Stream | Subjects | Retention | Max Age | Max Bytes | Replicas | Storage | Notes |
|--------|----------|-----------|---------|-----------|----------|---------|-------|
| `MESSAGING_EVENTS` | `cena.messaging.events.>` | Limits | 365 days | 5 GB | 3 | File | Audit trail — longest retention of any stream |
| `MESSAGING_COMMANDS` | `cena.messaging.commands.>` | WorkQueue | 7 days | 1 GB | 3 | File | Commands consumed once |

### 6.3 Consumer Groups

| Consumer | Stream | Filter | Ack Wait | Max Deliver | Max Pending | Purpose |
|----------|--------|--------|----------|-------------|-------------|---------|
| `messaging-send-processor` | `MESSAGING_COMMANDS` | `...commands.SendMessage` | 30s | 5 | 200 | Process send requests |
| `messaging-inbound-router` | `MESSAGING_COMMANDS` | `...commands.RouteInboundReply` | 30s | 10 | 100 | High retry — must route replies |
| `messaging-broadcast-processor` | `MESSAGING_COMMANDS` | `...commands.BroadcastToClass` | 60s | 5 | 50 | Fan-out to class members |
| `analytics-all-messaging` | `MESSAGING_EVENTS` | `...events.>` | 120s | 3 | 5000 | Analytics projection |
| `archival-message-events` | `MESSAGING_EVENTS` | `...events.MessageSent` | 120s | 3 | 10000 | Feeds nightly archival worker |

---

## 7. Access Control

### 7.1 Authorization Matrix

| Action | Teacher | Parent | Student | System |
|--------|---------|--------|---------|--------|
| Send DirectMessage to own students | Yes | - | - | - |
| Send DirectMessage to own child | - | Yes | - | - |
| Broadcast to own class | Yes | - | - | - |
| Send system notification | - | - | - | Yes |
| Read thread (participant) | Yes | Yes | Yes | Yes |
| Read thread (non-participant) | No | No | No | No |
| Mark message as read | Yes | Yes | Yes | - |
| Mute thread | Yes | Yes | Yes | - |
| Initiate new thread | Yes | Yes | No (MVP) | Yes |

### 7.2 JWT Claims for Authorization

```json
{
  "sub": "user-id",
  "role": "Teacher",
  "class_ids": ["class-1", "class-2"],
  "child_ids": [],
  "school_id": "school-1"
}
```

- **Teacher sends to student:** `student.classId IN teacher.class_ids`
- **Parent sends to child:** `child.id IN parent.child_ids`
- **Student reads thread:** `student.id IN thread.participantIds`

---

## 8. Throttling & Content Safety

### 8.1 Per-Role Rate Limits

| Role | Max/Day | Max/Hour | Max Threads/Day | Rationale |
|------|---------|----------|-----------------|-----------|
| Teacher | 100 | 30 | 20 | Active classroom communication |
| Parent | 10 | 5 | 3 | Encouragement, not conversation |
| Student | 0 | 0 | 0 | Receive-only in MVP |
| System | Unlimited | Unlimited | Unlimited | Outreach-generated |

### 8.2 Content Moderation Rules

| Rule | Action | Reason |
|------|--------|--------|
| Phone number detected (`\+?\d{7,15}`) | Block | Prevent off-platform contact exchange |
| Email address detected | Block | Prevent off-platform contact exchange |
| URL not in allowlist | Block | Safety — allowlist: `*.youtube.com`, `*.khanacademy.org`, `*.desmos.com`, `*.geogebra.org` |
| Excessive caps (>50% uppercase, >20 chars) | Flag (don't block) | Possible aggression indicator — logged for review |

Blocked messages emit `cena.messaging.events.MessageBlocked` with the moderation reason for audit.

---

## 9. Bounded Context Map Update

The Messaging context should be added to `docs/architecture-design.md` as Section 3.2.9:

```markdown
#### 3.2.9 Messaging Context (supporting)

- **Owns:** Bidirectional human-to-human communication (teacher↔student, parent↔student, teacher→class)
- **Storage:**
  - Hot messages (0-30 days): Redis Streams (ElastiCache)
  - Cold messages (30+ days): S3 (gzipped JSON-lines)
  - Thread metadata: PostgreSQL (Marten projection)
  - Audit trail: NATS JetStream (365-day retention)
- **Aggregate root:** ConversationThreadActor (virtual, stateful via Redis)
- **Subscribes to:** Inbound webhook events (WhatsApp, Telegram)
- **Publishes:** MessageSent, MessageRead, ThreadCreated, MessageBlocked
- **Integrates with:**
  - Outreach Context: outbound channel dispatch (WhatsApp/Telegram/Push send)
  - Learner Context: inbound learning signals routed via MessageClassifier
  - School Context: class membership for broadcast authorization
- **Domain events:** MessageSent_V1, MessageRead_V1, ThreadCreated_V1, ThreadMuted_V1, MessageBlocked_V1
```

---

## 10. Persistence Layer Update

Update Section 12 of `docs/architecture-design.md`:

| Store | Technology | Purpose | Tier |
|-------|-----------|---------|------|
| **Event Store** | PostgreSQL + Marten | Student actor event sourcing, CQRS read model projections, user accounts | Primary |
| **Hot State** | Redis (ElastiCache) | Active session state, **message streams (0-30 days)**, sub-millisecond reads | Cache |
| **Knowledge Graph** | Neo4j AuraDB | Domain graph source of truth, admin/authoring, cross-student analytics | Primary |
| **Object Storage** | S3 | Generated videos, analytics archive, **archived messages (30+ days)**, static curriculum artifacts | Archive |
| **Cluster State** | DynamoDB | Proto.Actor cluster discovery and membership | Infrastructure |
| **Thread Metadata** | PostgreSQL (Marten projection) | Thread summaries, participant lists, unread counts | Read Model |

---

## 11. Failure Modes & Mitigations

| Failure | Severity | Mitigation |
|---------|----------|------------|
| Redis cluster down | High | Actor falls back to NATS replay for recent messages; SignalR delivers real-time but history unavailable until recovery |
| S3 archive read fails | Low | Return 503 for old messages only; hot messages unaffected |
| NATS publish fails | Medium | Actor retries 3x with backoff; if exhausted, message is in Redis (hot) and logged locally — NATS catches up on recovery |
| Archival worker fails | Low | Next run picks up expired keys; Redis TTL provides grace period |
| MessageClassifier misroutes | Medium | Safe default: ambiguous → communication (never pollutes concept graph). Weekly accuracy review against labeled corpus |
| Content moderation false positive | Low | Sender receives `MESSAGE_BLOCKED` with reason; can rephrase. Weekly false positive review |

---

## Appendix A: Related Documents

- `docs/architecture-design.md` — Bounded context map, persistence layer, deployment architecture
- `contracts/backend/nats-subjects.md` — NATS subject hierarchy and consumer configuration
- `contracts/frontend/signalr-messages.ts` — SignalR message envelope and type definitions
- `contracts/backend/actor-contracts.cs` — Proto.Actor message records and grain interfaces
- `src/actors/Cena.Actors/Outreach/OutreachSchedulerActor.cs` — Outreach context actor (peer context)
- `tasks/backend/MSG-001-messaging-context.md` — Implementation task (original, being superseded)
