# Cena Platform — Offline Sync Protocol

> **Status:** Approved
> **Last updated:** 2026-03-26
> **Audience:** Engineering team, technical advisors
> **Related:** `docs/architecture-design.md` (Sections 4.3, 11.1, 14.3)

---

## 1. Problem Statement

The React Native client supports offline learning sessions. A student can work offline for extended periods (30-60+ minutes), completing multiple exercises while the server-side `StudentActor` continues its autonomous lifecycle — emitting `MasteryDecayed` events via Half-Life Regression timers, firing `StreakExpiring` triggers through the `OutreachSchedulerActor`, and receiving input from another device (web app) if the student logs in from multiple devices simultaneously.

When the client reconnects, it holds a queue of offline events that must reconcile with server state that has diverged independently. This document specifies the exact protocol for that reconciliation.

### The Divergence Window

During an offline period, the following server-side processes continue independently:

| Server Process | Events Emitted | Impact |
|---------------|----------------|--------|
| Half-Life Regression timers | `MasteryDecayed` | Concept mastery levels drop; spaced repetition reviews scheduled |
| `OutreachSchedulerActor` | `StreakExpiring`, `ReviewDue` | WhatsApp/Telegram messages sent based on stale activity data |
| `StagnationDetectorActor` | `StagnationDetected` | May trigger a `MethodologySwitched` event |
| Another device (web app) | `ExerciseAttempted`, `ConceptMastered`, etc. | Overlapping work on same concepts |
| Curriculum Context | `KnowledgeGraphUpdated` | Concept difficulty, prerequisites may change |

---

## 1B. Conflict Resolution Algorithm

Cena uses **operation-based conflict resolution** (a variant of operation-based CRDTs adapted for event sourcing). The algorithm is deterministic and does not require vector clocks or Last-Write-Wins (LWW), because all events are causally ordered within a single student aggregate.

**Algorithm: Three-Tier Replay Classification**

1. **Classify** each offline event into one of three tiers (see Section 2 below): Unconditional, Conditional, or Server-Authoritative.
2. **Merge** offline events into the server event stream using client-reported timestamps adjusted for clock offset (see Section 6.3).
3. **Resolve** conditional events by comparing the event's context snapshot (methodology, concept state) against current server state:
   - Context unchanged → accept at full weight
   - Context partially diverged (e.g., methodology switched) → accept at reduced weight (0.75, configurable)
   - Context fundamentally diverged (e.g., concept removed) → accept as historical record only (weight = 0)
4. **Recalculate** server-authoritative events (e.g., `ConceptMastered`) by replaying the merged event stream through BKT.
5. **Emit corrections** for any outreach messages that were sent based on stale server state (see Section 4.5).

**Why not LWW/vector clocks/CRDTs:**
- LWW is inappropriate: student work should never be silently discarded in favor of server state.
- Vector clocks add unnecessary complexity: Cena has a single writer per student (one active device at a time, enforced by session handoff).
- CRDTs: the three-tier classification achieves the same "all operations are preserved" guarantee as an operation-based CRDT, but with domain-specific weighting that a generic CRDT cannot express.

---

## 2. Event Classification

Every client-generated event type is classified into one of three replay categories. This classification determines how the event is processed during sync.

### 2.1 Classification Table

| Event Type | Classification | Rationale |
|-----------|---------------|-----------|
| `ExerciseAttempted` | **Conditional replay** | The student genuinely attempted the exercise, but the server must validate against current concept state (methodology, difficulty, prerequisites). The attempt data is always recorded, but its effect on mastery calculations depends on whether the exercise context is still valid. |
| `ExerciseCompleted` | **Conditional replay** | Completion is valid only if the parent `ExerciseAttempted` was accepted. If the exercise was generated under an outdated methodology or invalidated concept state, completion still counts for XP (see Section 5) but may not update mastery scores at full weight. |
| `ConceptMastered` | **Server-authoritative** | Mastery determination is a server-side calculation based on the Bayesian Knowledge Tracing model. The client may predict mastery locally for UI responsiveness, but the server recalculates after replaying all accepted events. The client's `ConceptMastered` event is discarded; the server emits its own if the threshold is met. |
| `AnnotationAdded` | **Unconditional replay** | A student's notes, reflections, and annotations are always valid regardless of server state. These represent the student's thinking and are never discarded. |
| `SessionStarted` | **Unconditional replay** | The fact that a session occurred is always true. Recorded for analytics, engagement tracking, and streak calculation. Timestamp is taken from the client's local clock (see Section 6.3 for clock skew handling). |
| `SessionEnded` | **Unconditional replay** | Session boundaries are factual. Always accepted. |
| `HintRequested` | **Unconditional replay** | The student asked for help — this is a factual interaction event. Always recorded. Used by the `StagnationDetectorActor` for pattern analysis. |
| `QuestionSkipped` | **Conditional replay** | Skip is accepted only if the referenced question/exercise is still valid in the current concept state. If the concept's methodology was switched server-side, the skip is still recorded (student chose not to engage) but tagged as `stale_context`. |

### 2.2 Classification Definitions

**Unconditional replay:** The event is appended to the student's event stream without validation against current server state. These events represent objective facts about the student's actions that cannot be invalidated by server-side state changes.

**Conditional replay:** The event is validated against the current server state before acceptance. If prerequisite state has changed, the event may be:
- **Accepted at full weight** — server state is compatible
- **Accepted at reduced weight** — server state has partially diverged (e.g., methodology changed but concept is still active)
- **Accepted as historical record only** — server state has fundamentally diverged; event is stored for audit but does not affect mastery calculations

In all conditional replay cases, the event is never silently discarded. The student's work is always preserved in the event stream with appropriate metadata.

**Server-authoritative:** The client's version of the event is replaced by the server's calculation. The client event is stored as a `ClientPrediction` metadata attachment on the server-authoritative event for debugging and analytics purposes.

---

## 3. Sync Handshake Protocol

### 3.1 Protocol Sequence

```
Client                                          Server (StudentActor)
  │                                                │
  │  1. SyncRequest                                │
  │  ─────────────────────────────────────────►    │
  │  {                                             │
  │    student_id,                                 │
  │    device_id,                                  │
  │    last_known_server_seq: 1847,                │
  │    client_clock_offset_ms: -230,               │
  │    queued_events: [...],                       │
  │    queue_checksum: "sha256:abc..."             │
  │  }                                             │
  │                                                │
  │  2. SyncAck + ServerDivergence                 │
  │  ◄─────────────────────────────────────────    │
  │  {                                             │
  │    current_server_seq: 1863,                   │
  │    diverged_events: [                          │
  │      { seq: 1848, type: "MasteryDecayed",      │
  │        concept: "derivatives", ... },          │
  │      { seq: 1855, type: "MethodologySwitched", │
  │        concept: "integrals", ... },            │
  │      ...                                       │
  │    ],                                          │
  │    active_methodology_map: {                   │
  │      "derivatives": "feynman",                 │
  │      "integrals": "socratic", ...              │
  │    },                                          │
  │    knowledge_graph_version: "v2026.03.15",     │
  │    sync_session_id: "uuid"                     │
  │  }                                             │
  │                                                │
  │  3. Client processes divergence locally,       │
  │     resolves what it can, flags conflicts       │
  │                                                │
  │  4. SyncCommit                                 │
  │  ─────────────────────────────────────────►    │
  │  {                                             │
  │    sync_session_id: "uuid",                    │
  │    resolved_events: [                          │
  │      { idempotency_key: "uuid-1",             │
  │        type: "ExerciseAttempted",              │
  │        offline_timestamp: "...",               │
  │        client_seq: 1,                          │
  │        resolution: "full_weight",              │
  │        ... },                                  │
  │      ...                                       │
  │    ]                                           │
  │  }                                             │
  │                                                │
  │  5. SyncResult                                 │
  │  ◄─────────────────────────────────────────    │
  │  {                                             │
  │    accepted_events: [ ... ],                   │
  │    rejected_events: [ ... ],                   │
  │    recalculated_state: {                       │
  │      mastery_overlay: { ... },                 │
  │      xp_delta: +135,                           │
  │      streak_status: "maintained",              │
  │      current_server_seq: 1875                  │
  │    },                                          │
  │    notifications: [                            │
  │      { type: "sync_summary",                   │
  │        message: "12 exercises synced...",       │
  │        details: { ... }                        │
  │      }                                         │
  │    ],                                          │
  │    outreach_corrections: [                     │
  │      { type: "streak_restored",                │
  │        original_message: "StreakExpiring sent", │
  │        correction: "streak was maintained       │
  │                     by offline session" }       │
  │    ]                                           │
  │  }                                             │
  │                                                │
  │  6. Client replaces local state with           │
  │     server-provided recalculated_state         │
  │  7. Client displays sync summary UI            │
  │                                                │
```

### 3.2 Step-by-Step Breakdown

#### Step 1: SyncRequest

The client sends its offline event queue along with metadata for conflict detection.

| Field | Type | Description |
|-------|------|-------------|
| `student_id` | UUID | The student aggregate ID |
| `device_id` | UUID | Unique device identifier (for multi-device conflict detection) |
| `last_known_server_seq` | int64 | The last event sequence number the client received from the server before going offline |
| `client_clock_offset_ms` | int32 | Difference between client clock and server clock at last sync (for timestamp correction) |
| `queued_events` | Event[] | Ordered array of offline events with client-local sequence numbers |
| `queue_checksum` | string | SHA-256 of the serialized event queue (integrity check) |

#### Step 2: SyncAck + ServerDivergence

The server responds with everything that changed since `last_known_server_seq`. This allows the client to understand the divergence before committing.

The `active_methodology_map` is included so the client can pre-classify its conditional replay events without needing the full server state.

#### Step 3: Client-Side Pre-Resolution

The client processes the divergence data and pre-classifies each queued event:
- Unconditional replay events are marked `full_weight`
- Conditional replay events are checked against the divergence data and marked appropriately
- Server-authoritative events (e.g., `ConceptMastered`) are marked `server_decides`

This step happens locally on the client, reducing server-side processing load.

#### Step 4: SyncCommit

The client sends its pre-resolved events. The server performs its own validation (the client's pre-resolution is advisory, not trusted).

#### Step 5: SyncResult

The server returns the final reconciled state. This is the authoritative result. The client must replace its local state with this response.

#### Step 6-7: Client State Update

The client atomically replaces its local state machine with the server-provided `recalculated_state` and displays the sync summary UI (see Section 7).

---

## 4. Conflict Scenarios

### Scenario A: Mastery Conflict

**Situation:** Student masters "Derivatives" offline (passes mastery threshold based on local BKT calculation). Meanwhile, the server emitted `MasteryDecayed` for "Derivatives" because the half-life timer expired.

**Resolution:**

1. The server receives the offline `ExerciseAttempted` and `ExerciseCompleted` events for Derivatives exercises (conditional replay).
2. The server replays these events **on top of the decayed state**, not the pre-decay state. The decay happened at a real point in time; the offline exercises happened after that point.
3. The server recalculates BKT parameters incorporating both the decay and the new exercise data.
4. If the recalculated mastery still exceeds the threshold, the server emits a new `ConceptMastered` event. The decay is effectively reversed by the student's offline work.
5. If the recalculated mastery falls short (e.g., the student was borderline and decay pushed them below), the concept remains in "review needed" state.
6. The client's local `ConceptMastered` event is discarded (server-authoritative classification), but the student sees a clear explanation: either "Great work! You re-mastered Derivatives" or "Almost there — one more review session on Derivatives."

**Temporal ordering rule:** Offline events are inserted into the event stream at their client-reported timestamps (adjusted for clock offset), interleaved with server events. The BKT model processes them in chronological order.

### Scenario B: XP/Streak Conflict

**Situation:** Student earned 150 XP offline across 12 exercises. The server sent a `StreakExpiring` WhatsApp message because it did not see activity within the streak window.

**Resolution:**

1. All offline `SessionStarted` and `SessionEnded` events are unconditional replay — they are always accepted.
2. The Engagement Context recalculates XP and streak status by replaying the full event stream including the newly synced offline events.
3. **Streak rule:** If the offline session's `SessionStarted` timestamp (adjusted for clock offset) falls within the streak maintenance window, the streak is retroactively maintained. The `StreakExpiring` event remains in the stream (it was factually emitted) but is annotated with `retroactively_invalidated: true`.
4. **XP rule:** Offline exercises always earn base XP (see Section 5 for detailed rules). Exercises accepted at full weight earn full XP. Exercises accepted at reduced weight earn 75% XP. Exercises accepted as historical record earn 50% XP (effort credit).
5. **Outreach correction:** The server includes an `outreach_corrections` array in the SyncResult. The Outreach Context may send a follow-up WhatsApp: "Looks like you were studying offline — your streak is safe!" This corrective message is sent only if the original StreakExpiring message was delivered less than 24 hours ago.

### Scenario C: Multi-Device Conflict

**Situation:** Student used the web app on their laptop AND the mobile app offline simultaneously. Both submit exercises for the same concept ("Integrals").

**Resolution:**

1. Each device has a unique `device_id`. The server detects multi-device conflict when two SyncRequests reference overlapping `last_known_server_seq` ranges or when events from multiple devices arrive for the same time window.
2. **All exercises from all devices are accepted.** The student did the work on both devices; none of it is discarded.
3. The BKT model processes all events in timestamp order regardless of source device. If the student attempted 5 Integrals exercises on web and 3 on mobile, all 8 feed into the mastery calculation.
4. **Deduplication rule:** If the same exercise (identified by `exercise_id`) was attempted on both devices (e.g., the same generated question appeared due to identical random seeds), only the first attempt by timestamp is counted for mastery. The duplicate is stored with `duplicate_device_attempt: true` and earns 50% XP (the student still did the work).
5. **Session merging:** Overlapping sessions from different devices are recorded as separate sessions but flagged as `concurrent_device_sessions` for analytics. The stagnation detector considers them as a single engagement period.

### Scenario D: Methodology Switch Conflict

**Situation:** The server decided to switch from Socratic to Feynman technique for "Integrals" while the student was offline (stagnation detected from previous sessions). The student's offline exercises used Socratic-style prompts generated before disconnection.

**Resolution:**

1. `MethodologySwitched` is a server-authoritative event. The switch decision stands.
2. The student's offline Socratic exercises are accepted at **reduced weight** for mastery calculation. The rationale: the exercises were generated under a methodology the server determined was not working well for this student. The learning signal is weaker but not zero.
3. Mastery weight reduction: **75% weight** for exercises completed under the previous methodology. This is generous because the student was still learning, just not optimally.
4. Full XP is awarded (see Section 5 — XP is effort-based, not methodology-dependent).
5. The client receives the updated methodology in `active_methodology_map` and switches its local session to Feynman technique for subsequent exercises.
6. **UI message:** "While you were offline, we adjusted your learning approach for Integrals to the Feynman technique based on your recent patterns. Your practice still counts! Starting your next session with the new approach."

### Scenario E: Content Update Conflict

**Situation:** The knowledge graph was updated while the student was offline — concept difficulty changed for "Derivatives" (increased from 0.6 to 0.8), and a new prerequisite ("Limits") was added to "Taylor Series."

**Resolution:**

1. The `knowledge_graph_version` in the SyncAck tells the client that the graph has changed.
2. Offline exercises referencing the old graph state are accepted at **full weight** if only difficulty changed (the concept itself is still valid; the student's performance data is still meaningful).
3. If prerequisite structure changed and the student attempted a concept for which they now lack a prerequisite (e.g., "Taylor Series" without having mastered "Limits"), the exercises are accepted as **historical record only** — stored for audit, 50% XP, no mastery update.
4. The server recalculates the student's recommended learning path based on the updated graph and syncs the new path to the client.
5. **UI message:** "Our curriculum was updated while you were offline. Most of your work synced normally. For Taylor Series, we've added Limits as a prerequisite — we'll guide you through that first."

---

## 5. XP and Streak Credit Rules

### 5.1 XP Crediting

The guiding principle is: **the student did the work; they deserve credit.** XP is effort-based, not outcome-based.

| Sync Classification | XP Awarded | Mastery Effect |
|---------------------|-----------|----------------|
| Unconditional replay | 100% XP | N/A (annotations, sessions, hints are not mastery events) |
| Conditional replay — full weight | 100% XP | Full mastery update |
| Conditional replay — reduced weight | 100% XP | 75% mastery weight |
| Conditional replay — historical record | 50% XP | No mastery update |
| Server-authoritative (discarded) | 0% XP (server recalculates) | Server determines |

**Rationale for 100% XP on reduced-weight events:** XP is a motivation mechanism. Penalizing XP for conditions outside the student's control (server-side methodology switch) would feel punitive and undermine offline learning confidence.

**Rationale for 50% XP on historical records:** The student engaged with content that turned out to be misaligned (e.g., missing prerequisite). They deserve recognition for effort but at a reduced rate because the learning benefit was limited.

### 5.2 Streak Maintenance

| Condition | Streak Effect |
|-----------|--------------|
| Offline session falls within streak window | Streak maintained retroactively |
| Offline session falls outside streak window | Streak broken; new streak starts from offline session timestamp |
| Multiple offline sessions spanning the gap | Each session's timestamp is evaluated; streak is maintained if no gap exceeds the streak window |

**Streak window:** Configurable per A/B test cohort (default: 24 hours from last qualifying activity).

**Grace period:** If the offline session started within 1 hour after the streak window expired, the streak is maintained with a "grace save" — the student was clearly intending to maintain it. Grace saves are limited to once per 7-day period per student.

### 5.3 Student-Facing Messaging

The sync summary follows a hierarchy:

1. **Best case (no conflicts):** Silent sync. A brief toast notification: "Your offline session has been synced — 12 exercises, +150 XP"
2. **Minor conflicts (methodology change, difficulty update):** Toast notification with expandable detail: "Synced! 10 of 12 exercises counted at full credit. [See details]"
3. **Significant conflicts (prerequisite changes, multi-device overlap):** A sync summary card appears in the session feed: "We synced your offline session. Here's what happened:" followed by a clear, friendly breakdown.

**Design principle:** Never make the student feel their offline work was wasted. Even in the worst case, the messaging emphasizes what was credited, not what was reduced.

---

## 6. Technical Implementation

### 6.1 Client-Side Event Queue Format

Events are stored in SQLite (via `expo-sqlite` or `react-native-sqlite-storage`) for durability. AsyncStorage is not used because it does not guarantee write ordering under crash conditions.

#### Schema: `offline_event_queue`

```sql
CREATE TABLE offline_event_queue (
  id                INTEGER PRIMARY KEY AUTOINCREMENT,
  idempotency_key   TEXT NOT NULL UNIQUE,    -- UUID v7 (time-ordered)
  student_id        TEXT NOT NULL,
  device_id         TEXT NOT NULL,
  client_seq        INTEGER NOT NULL,        -- monotonically increasing per device
  event_type        TEXT NOT NULL,
  event_payload     TEXT NOT NULL,            -- JSON serialized event data
  offline_timestamp TEXT NOT NULL,            -- ISO 8601, client local clock
  clock_offset_ms   INTEGER NOT NULL,        -- offset from last server sync
  status            TEXT NOT NULL DEFAULT 'pending',  -- pending | syncing | synced | failed
  sync_session_id   TEXT,                    -- populated during sync
  server_result     TEXT,                    -- JSON: acceptance classification from server
  created_at        TEXT NOT NULL DEFAULT (datetime('now')),
  synced_at         TEXT
);

CREATE INDEX idx_queue_status ON offline_event_queue(status);
CREATE INDEX idx_queue_student ON offline_event_queue(student_id, client_seq);
```

#### Event Payload Example

```json
{
  "idempotency_key": "019e3a4b-7c2d-7f00-8a1b-3c4d5e6f7890",
  "student_id": "stu_abc123",
  "device_id": "dev_mobile_xyz",
  "client_seq": 47,
  "event_type": "ExerciseAttempted",
  "offline_timestamp": "2026-03-26T14:32:15.123+03:00",
  "clock_offset_ms": -230,
  "payload": {
    "concept_id": "math.calculus.derivatives.chain_rule",
    "exercise_id": "ex_9f8e7d6c",
    "methodology": "socratic",
    "question_hash": "sha256:def456...",
    "student_answer": "2x * cos(x^2)",
    "is_correct": true,
    "time_spent_ms": 45200,
    "hints_used": 0,
    "knowledge_graph_version": "v2026.03.10"
  }
}
```

### 6.2 Versioning and Conflict Detection

#### Sequence Numbers

Each `StudentActor` maintains a monotonically increasing sequence number (`server_seq`) on its event stream (provided by Marten's event store). The client tracks the last `server_seq` it received.

**Divergence detection:** If `client.last_known_server_seq < server.current_seq`, the server has emitted events the client has not seen. The gap events are included in the SyncAck.

#### Vector Clocks (Multi-Device)

For multi-device scenarios, a lightweight vector clock tracks per-device causality:

```json
{
  "dev_mobile_xyz": 47,
  "dev_web_abc": 23,
  "server": 1863
}
```

Each device increments its own counter. The server increments the `server` counter for autonomous events (decay, methodology switches). During sync, the server merges vector clocks using element-wise max.

**Conflict detection:** If two devices submit events with overlapping server-seq gaps (both diverged from the same `last_known_server_seq`), the server processes them in timestamp order with tie-breaking by device_id lexicographic order.

### 6.3 Clock Skew Handling

Client clocks cannot be trusted. The protocol handles this:

1. On every successful server communication, the client records the round-trip: `client_send_time`, `server_time` (from response header), `client_receive_time`.
2. Clock offset is estimated as: `offset = server_time - (client_send_time + client_receive_time) / 2`
3. The offset is stored and sent with the SyncRequest.
4. The server adjusts all offline event timestamps by the reported offset.
5. **Sanity bound:** If the adjusted timestamp would place an event in the future or more than 8 hours in the past (beyond reasonable offline session length), the event is flagged for manual review and processed with server-receive timestamp instead.

### 6.4 Sync Endpoint Contract

#### POST `/api/v1/sync`

**Request Headers:**
```
Authorization: Bearer <jwt>
X-Device-Id: <device_id>
X-Idempotency-Key: <sync_session_uuid>
Content-Type: application/json
```

**Request Body:** (SyncRequest as defined in Section 3.2, Step 1)

**Response:** (SyncResult as defined in Section 3.2, Step 5)

**HTTP Status Codes:**

| Code | Meaning |
|------|---------|
| 200 | Sync completed successfully |
| 202 | Sync accepted, processing asynchronously (>50 events; result delivered via SignalR push) |
| 409 | Concurrent sync in progress from another device; client should retry after delay |
| 422 | Queue checksum mismatch; client should re-send |
| 429 | Rate limited; includes `Retry-After` header |

#### Async Processing (>50 events)

For large offline queues (>50 events), the sync is processed asynchronously:

1. Server returns `202 Accepted` with a `sync_session_id`.
2. The `StudentActor` processes events sequentially (maintaining event-sourcing ordering guarantees).
3. Upon completion, the result is pushed via SignalR WebSocket to the connected client.
4. If the client disconnects before receiving the result, it is stored and delivered on next connection.

### 6.5 Idempotency

Every offline event carries a UUID v7 `idempotency_key` (time-ordered for natural sorting). The server maintains an idempotency cache (Redis, 72-hour TTL) mapping `idempotency_key -> processing_result`.

**Guarantees:**
- If the client retransmits a SyncCommit (e.g., network timeout on response), the server returns the cached result without re-processing.
- The `sync_session_id` itself is an idempotency key for the entire sync operation.
- Individual event idempotency keys prevent duplicate processing if a sync is partially completed and retried.

### 6.6 Failure Modes and Recovery

| Failure | Recovery |
|---------|----------|
| Network drops during SyncCommit | Client retries with same `sync_session_id`; server returns cached result if already processed |
| Server crash during processing | Actor reactivates from event store; incomplete sync is rolled back (no partial commits to event store) |
| Client crash after SyncResult received but before local state update | On restart, client checks `offline_event_queue` for events in `syncing` status; re-requests SyncResult by `sync_session_id` |
| Queue corruption (checksum mismatch) | Client rebuilds queue from SQLite; if SQLite is corrupted, client starts fresh and server state is authoritative |

---

## 7. Client UI for Conflict Resolution

### 7.1 Design Principles

1. **The student's work is never "wasted."** Even in the worst conflict scenario, the student sees that their effort was recognized.
2. **Progressive disclosure.** Start with a simple, positive message. Details are available on tap/click but never forced.
3. **Blame the system, not the student.** Conflicts are framed as "our system caught up with your progress" — never "your work was invalid."

### 7.2 UI Components

#### Silent Sync (no conflicts)

A transient toast notification (auto-dismiss after 4 seconds):

```
✓ Synced — 12 exercises, +150 XP, streak maintained
```

#### Minor Conflict Toast

A toast notification with a "Details" link (auto-dismiss after 6 seconds):

```
✓ Synced — 10/12 exercises at full credit
  Your learning approach for Integrals was updated. Details →
```

#### Sync Summary Card (significant conflicts)

Appears as a card in the session feed / home screen:

```
┌─────────────────────────────────────────────┐
│  Offline Session Synced                      │
│                                              │
│  ✓ 12 exercises completed                    │
│  ✓ +135 XP earned                            │
│  ✓ 7-day streak maintained                   │
│                                              │
│  What happened while you were offline:       │
│  • Derivatives review was due — your          │
│    offline practice covered it                │
│  • Integrals approach updated to Feynman      │
│    technique — your Socratic exercises        │
│    still counted                              │
│  • Taylor Series now requires Limits first    │
│    — we'll add that to your path              │
│                                              │
│  [Continue Learning]                          │
└─────────────────────────────────────────────┘
```

### 7.3 Outreach Corrections

When the server sent outreach messages (WhatsApp, Telegram, push) based on stale state that offline activity would have prevented, the system sends a corrective follow-up:

- **StreakExpiring sent, but streak was maintained offline:** "Good news — turns out you were studying offline! Your streak is safe."
- **ReviewDue sent, but the review was completed offline:** No correction needed (the student already did the review; the reminder was harmless).
- **StagnationDetected sent, but student was actively working offline:** "We see you were actually studying — great progress!"

Corrections are sent only if the original message was delivered within the last 24 hours. Older messages are silently annotated in the event stream but no student-facing correction is sent.

---

## 8. Sequence Diagram Summary

```
  Student (offline)     Mobile Client        Server (StudentActor)      Outreach
       │                     │                        │                    │
       │  does 12 exercises  │                        │                    │
       │ ──────────────────► │                        │                    │
       │                     │ queues events locally   │                    │
       │                     │                        │ MasteryDecayed     │
       │                     │                        │ (derivatives)      │
       │                     │                        │                    │
       │                     │                        │ StreakExpiring ────►│
       │                     │                        │                    │── WhatsApp
       │                     │                        │                    │
       │  reconnects         │                        │                    │
       │ ──────────────────► │                        │                    │
       │                     │  SyncRequest ─────────►│                    │
       │                     │                        │                    │
       │                     │  ◄──── SyncAck +       │                    │
       │                     │        Divergence      │                    │
       │                     │                        │                    │
       │                     │  pre-resolves locally   │                    │
       │                     │                        │                    │
       │                     │  SyncCommit ──────────►│                    │
       │                     │                        │ replays events     │
       │                     │                        │ recalculates BKT   │
       │                     │                        │ recalculates XP    │
       │                     │                        │                    │
       │                     │  ◄──── SyncResult      │                    │
       │                     │                        │                    │
       │                     │  updates local state    │                    │
       │  ◄── sync summary   │                        │                    │
       │      toast/card     │                        │                    │
       │                     │                        │ streak_restored ──►│
       │                     │                        │                    │── WhatsApp
       │                     │                        │                    │   correction
```

---

## Appendix A: Event Type Reference

Complete list of client-generatable events and their classifications:

| Event Type | Classification | XP Impact | Mastery Impact | Notes |
|-----------|---------------|-----------|----------------|-------|
| `SessionStarted` | Unconditional | — | — | Streak calculation input |
| `SessionEnded` | Unconditional | — | — | Duration analytics |
| `ExerciseAttempted` | Conditional | 100%/100%/50% | Full/75%/None | Per acceptance tier |
| `ExerciseCompleted` | Conditional | 100%/100%/50% | Full/75%/None | Linked to parent attempt |
| `ConceptMastered` | Server-authoritative | Server decides | Server decides | Client prediction discarded |
| `AnnotationAdded` | Unconditional | +5 XP (bonus) | — | Always preserved |
| `HintRequested` | Unconditional | — | — | Stagnation detector input |
| `QuestionSkipped` | Conditional | — | — | Tagged `stale_context` if diverged |

## Appendix B: Configuration Parameters

| Parameter | Default | A/B Testable | Description |
|-----------|---------|-------------|-------------|
| `streak_window_hours` | 24 | Yes | Hours between qualifying activities before streak breaks |
| `streak_grace_hours` | 1 | Yes | Grace period after streak window expires |
| `grace_save_cooldown_days` | 7 | No | Minimum days between grace saves |
| `reduced_weight_factor` | 0.75 | Yes | Mastery weight for methodology-mismatched exercises |
| `historical_xp_factor` | 0.50 | Yes | XP multiplier for historical-record-only exercises |
| `max_clock_skew_hours` | 8 | No | Maximum allowed clock offset before timestamp override |
| `async_sync_threshold` | 50 | No | Event count triggering async sync processing |
| `idempotency_cache_ttl_hours` | 72 | No | Redis TTL for idempotency keys |
| `outreach_correction_window_hours` | 24 | No | Maximum age of outreach message eligible for correction |

---

## 8. Pre-Launch Load & Chaos Test Plan

Offline sync is the most complex client-server interaction in Cena. Before beta launch, the following tests must pass:

**Load tests** (run on staging with production-equivalent infrastructure):

| Test | Setup | Pass Criteria |
|------|-------|---------------|
| Sync 10 events, single student | 1 client, 10 offline events | p99 < 500ms, all events accepted |
| Sync 50 events, single student | 1 client, 50 offline events | p99 < 2s, all events validated correctly |
| Sync 100 events, single student | 1 client, 100 events (async threshold) | p99 < 5s, async processing completes < 10s via SignalR/polling |
| Concurrent sync, 100 students | 100 simultaneous sync requests, 20 events each | p99 < 3s, no PostgreSQL connection pool exhaustion, no NATS consumer lag > 100 |
| Concurrent sync, 1000 students | 1000 simultaneous sync requests, 10 events each | p99 < 5s, no OOM on actor cluster, no Marten event append failures |

**Chaos tests** (inject failures during sync):

| Test | Injection | Pass Criteria |
|------|-----------|---------------|
| Server crash mid-sync | Kill ECS task after accepting request, before persisting | Client retries with idempotency key; no duplicate events on recovery |
| PostgreSQL connection timeout | Introduce 30s latency on RDS | Circuit breaker opens; client receives 503; retry succeeds after recovery |
| NATS unavailable | Stop NATS JetStream | Sync still persists to Marten (source of truth); downstream consumers catch up when NATS recovers |
| Redis idempotency cache down | Kill ElastiCache node | Fallback to Marten-based idempotency check (slower but correct); no duplicates |

**Client-side tests:**

| Test | Setup | Pass Criteria |
|------|-------|---------------|
| SQLite corruption | Corrupt offline queue DB file | App detects corruption, creates new DB, warns user "some offline work may need to be redone" — no crash |
| Clock skew | Set device clock 6 hours ahead, then sync | Events accepted with adjusted timestamps; no mastery calculation errors |
| Large queue | Generate 500 events over 4-hour offline session | Sync completes < 30s; BKT recalculation produces correct mastery state |

**Performance baselines** (document after first test run, update quarterly):

| Metric | Target | Measured |
|--------|--------|----------|
| p50 sync latency (50 events) | < 1s | TBD |
| p95 sync latency (50 events) | < 3s | TBD |
| p99 sync latency (50 events) | < 5s | TBD |
| Max concurrent syncs before degradation | > 500 | TBD |
| PostgreSQL IOPS during 1000-student burst | < 80% provisioned | TBD |
