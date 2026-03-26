# Cena Platform — Proto.Actor Cluster Failure Mode Analysis

> **Status:** Active
> **Last updated:** 2026-03-26
> **Severity:** Critical architectural gap — all scenarios below are unmitigated in the current design
> **Audience:** Engineering team, on-call engineers, SRE

This document analyzes seven failure scenarios specific to Cena's Proto.Actor (.NET 9) cluster running on ECS/Fargate with DynamoDB-based cluster discovery, PostgreSQL/Marten event sourcing, NATS JetStream event backbone, and the Python FastAPI LLM Anti-Corruption Layer.

Each scenario includes detection, automated response, manual recovery, and prevention measures.

---

## 1. Split-Brain / Network Partition

### Scenario

Two Fargate nodes (Node A and Node B) lose network connectivity to each other but both remain healthy from ECS's perspective. Both nodes believe they own the same virtual actor (e.g., `StudentActor:student-4821`). Both accept writes. The student's event stream forks — two conflicting versions of reality exist simultaneously.

### Why This Is Dangerous

Event sourcing assumes a single writer per aggregate. A forked event stream violates this invariant. If Student 4821 completes exercises on both partitions, the mastery vector, XP, streak, and spaced repetition schedule all diverge. When the partition heals, the event store contains contradictory history.

### Detection

- **DynamoDB cluster discovery heartbeat:** Each node writes a heartbeat to DynamoDB with a TTL. Configure the heartbeat interval to 2 seconds, with a TTL of 8 seconds. If a node cannot reach DynamoDB to renew its heartbeat, it must assume it is partitioned and stop accepting actor activations.
- **Proto.Actor gossip protocol:** Configure gossip intervals and failure detection:
  ```
  GossipInterval:    500ms    — how often nodes exchange membership state
  SuspectTimeout:    3000ms   — node marked "suspect" after missing 6 gossip rounds
  DeadTimeout:       8000ms   — node marked "dead" and its actors redistributed
  ```
- **Metric:** `protoactor_cluster_partition_detected` counter. Alert on any increment.
- **Marten concurrency guard:** Enable optimistic concurrency on the event stream. If two nodes attempt to append to the same stream, the second write fails with a `StreamConcurrencyException`. This is the last line of defense — it does not prevent the split-brain, but it prevents silent corruption.

### Automated Response

1. **Self-fencing via DynamoDB:** If a node fails to write its heartbeat to DynamoDB for 2 consecutive intervals (4 seconds), it must voluntarily drain all virtual actors and stop accepting new activations. This is the "fence-or-die" pattern — a node that cannot prove it is in the majority partition must assume it is not.
2. **Proto.Actor `MemberLeft` event:** When gossip detects a dead node, the cluster publishes `MemberLeft`. The remaining nodes activate orphaned virtual actors on demand.
3. **Marten `StreamConcurrencyException` handler:** If an actor receives this exception on event persistence, it must:
   - Immediately stop processing messages
   - Reload its state from the event store (full replay from last snapshot)
   - Re-apply any in-flight commands against the corrected state
   - Log a `SPLIT_BRAIN_RECOVERY` structured event with both the expected and actual stream version

### Manual Recovery

1. **Identify the fork:** Query Marten for streams where the same sequence number appears twice (this should be impossible under normal operation — its presence confirms a split-brain occurred despite safeguards).
   ```sql
   SELECT stream_id, version, COUNT(*)
   FROM mt_events
   GROUP BY stream_id, version
   HAVING COUNT(*) > 1;
   ```
2. **Determine authoritative branch:** The branch with the most recent Marten-confirmed writes (lowest gap in sequence numbers) is typically authoritative. If both branches have confirmed writes, manual domain-level conflict resolution is required.
3. **Replay and compensate:** For affected students, generate compensating events to reconcile the two branches. In the worst case (conflicting mastery assessments), the safer option is to accept the higher mastery estimate — a student who demonstrated mastery on either branch likely does possess it.
4. **Notify affected students:** If outreach messages (WhatsApp) were sent from the wrong branch, there is no recall mechanism. Log the incident and accept the duplicate.

### Prevention

- **Minimum cluster size of 3 nodes** for partition tolerance (2 nodes cannot form a majority).
- **DynamoDB conditional writes** for actor placement: before activating a virtual actor, the node must win a conditional write to a DynamoDB "actor lease" table. This ensures only one node owns a given actor identity at any time.
- **Network topology:** Place all Fargate tasks in the same availability zone to minimize partition probability (accept the AZ-failure tradeoff at this scale).

---

## 2. Actor Activation Storm

### Scenario

A rolling deployment restarts all Proto.Actor nodes. At 7pm (peak study time in Israel), 10,000 students open the app simultaneously. Every `StudentActor` needs to reactivate, which means replaying its event stream from PostgreSQL/Marten. Each student has ~500 events on average (with snapshots every 100 events, that is ~100 events to replay from the last snapshot). That is 10,000 concurrent snapshot loads + event replays hitting PostgreSQL simultaneously.

### Why This Is Dangerous

PostgreSQL under 10,000 concurrent event stream reads will saturate connection pools and IOPS. Actor activation latency spikes from <100ms to 10+ seconds. Students see loading spinners. The system appears down even though every component is technically healthy.

### Detection

- **Metric:** `protoactor_actor_activation_duration_ms` histogram. Alert when p99 > 2000ms.
- **Metric:** `protoactor_actor_activation_queue_depth` gauge. Alert when > 500.
- **Metric:** `postgresql_active_connections` gauge. Alert when > 80% of `max_connections`.
- **Marten query duration:** Alert when `mt_events` SELECT p99 > 500ms.
- **ECS task health checks:** If actor activations block the health check endpoint, ECS will kill "unhealthy" tasks, causing a cascading restart loop. The health check endpoint MUST NOT depend on actor activation.

### Automated Response

1. **Activation rate limiter:** Implement a `SemaphoreSlim`-based activation gate in the Proto.Actor `IClusterProvider` implementation. Maximum 200 concurrent actor activations per node. Activations beyond the limit are queued with a FIFO policy.
   ```csharp
   // In custom ActivationThrottle middleware
   private static readonly SemaphoreSlim _gate = new(200, 200);

   public async Task OnActivating(ActivationContext ctx)
   {
       if (!await _gate.WaitAsync(TimeSpan.FromSeconds(30)))
           throw new ActivationThrottledException(ctx.Identity);
       try { await next(ctx); }
       finally { _gate.Release(); }
   }
   ```
2. **PostgreSQL connection pool ceiling:** Configure Npgsql with `MaxPoolSize=100` per node. With 3 nodes, that is 300 max connections against a PostgreSQL instance configured for 400 `max_connections` (leaving headroom for admin and Marten async daemon).
3. **Snapshot pre-warming:** On node startup, before the node joins the cluster, pre-load the 500 most recently active student snapshots into a local cache. This eliminates the snapshot fetch from the hot path for the most likely activations.
4. **Client-side retry with exponential backoff:** The React Native app retries failed requests with jittered exponential backoff (1s, 2s, 4s, max 16s). The client displays a "connecting..." state, not an error.

### Manual Recovery

1. **Scale out PostgreSQL read replicas:** If the activation storm saturates the primary, promote a read replica and point Marten snapshot reads to it.
2. **Increase Fargate node count temporarily:** Scale to 5-6 nodes to distribute the activation load. Proto.Actor's virtual actor placement will automatically spread actors across the larger cluster.
3. **If cascading restart loop:** Temporarily increase the ECS health check grace period to 300 seconds to give nodes time to stabilize.

### Prevention

- **Blue-green deployment, not rolling restart:** Deploy new task definition to a parallel target group. Shift traffic via ALB weight. Old nodes continue serving until new nodes are warm. Only then drain the old target group. Actor state naturally migrates as new requests hit new nodes.
- **Staggered traffic shift:** Shift 10% of traffic to the new deployment, wait 5 minutes, then 50%, then 100%. Monitor activation latency at each step.
- **Deploy outside peak hours:** Schedule deployments for 2-4am Israel time. If a deployment must happen during peak hours, use the blue-green strategy.
- **Snapshot interval tuning:** If average event count grows significantly, reduce the snapshot interval from 100 to 50 events to bound replay time.

---

## 3. Poison Message / Corrupt Event

### Scenario

A malformed event exists in Student 7392's event stream. Perhaps a bug in a previous deployment serialized a `ConceptAttempted` event with a null `ConceptId`, or an event schema migration left a field in an incompatible state. Every time `StudentActor:student-7392` activates, it replays its events, hits the corrupt event, throws a deserialization exception, and crashes. Proto.Actor's supervision strategy restarts the actor. It replays again, hits the same event, crashes again. The student is permanently locked out.

### Why This Is Dangerous

This is a permanent, self-inflicted denial of service for a single student. The actor will never recover without intervention. If the bug that produced the corrupt event affected multiple students, the blast radius could be dozens or hundreds of locked-out users.

### Detection

- **Proto.Actor supervision metrics:** Track `protoactor_actor_restart_count` per actor identity. Alert when any single actor restarts > 3 times within 60 seconds.
- **Dead-letter logging:** Configure a dead-letter handler that logs every message that causes an actor to crash:
  ```csharp
  system.EventStream.Subscribe<DeadLetterEvent>(evt =>
  {
      logger.LogError("Dead letter: Actor={Actor} Message={Message} Reason={Reason}",
          evt.Pid, evt.Message?.GetType().Name, evt.Reason);
      metrics.IncrementCounter("actor_dead_letter", evt.Pid.Id);
  });
  ```
- **Structured alert:** `actor_restart_loop` — fires when the same actor PID restarts N times (configurable, default 5) within M seconds (default 120). This pattern is the signature of a poison message.
- **Marten event stream health check:** Nightly batch job that attempts to deserialize every event in every stream without applying business logic. Corrupt events are flagged before they cause runtime failures.

### Automated Response

1. **Circuit-breaker supervision strategy:** Replace Proto.Actor's default restart strategy with a circuit-breaker:
   ```
   Restart policy:
     - First 3 failures within 60s: restart with exponential backoff (1s, 2s, 4s)
     - After 3rd failure: stop the actor, mark it as "quarantined"
     - Return a QuarantinedActorResponse to any subsequent messages for this identity
   ```
2. **Quarantine registry:** Maintain an in-memory `ConcurrentDictionary<string, QuarantineRecord>` of quarantined actor identities. When a message arrives for a quarantined actor, return immediately with a structured error (HTTP 503 with `Retry-After` and `X-Quarantine-Reason` headers).
3. **Client handling:** The React Native app receives the quarantine response and shows: "We're fixing something with your account. You'll be back shortly." with a support contact link.

### Manual Recovery

1. **Identify the corrupt event:**
   ```sql
   SELECT seq_id, stream_id, version, type, data
   FROM mt_events
   WHERE stream_id = 'student-7392'
   ORDER BY version;
   ```
   Attempt to deserialize each event programmatically. The one that throws is the poison.

2. **Recovery options (in order of preference):**
   - **Patch the event:** If the corruption is a missing or malformed field, update the event data in-place. This violates event sourcing immutability but is acceptable for data recovery.
     ```sql
     UPDATE mt_events
     SET data = jsonb_set(data, '{conceptId}', '"RECOVERED_CONCEPT_ID"')
     WHERE seq_id = <corrupt_event_seq_id>;
     ```
   - **Skip the event:** Add a `PoisonEventFilter` to the actor's event replay pipeline that skips events marked as poisoned in a `poison_events` table.
   - **Rebuild from snapshot:** If a valid snapshot exists before the corrupt event, delete events after the snapshot and accept the data loss.

3. **Unquarantine the actor:** Remove the identity from the quarantine registry. The next message for that student will trigger a fresh activation with the repaired stream.

4. **Root cause:** Identify the code path that produced the corrupt event. Deploy a fix. Run the nightly deserialization health check against all streams to find other affected students.

### Prevention

- **Event schema validation:** Validate every event against a JSON schema before persisting to Marten. Reject invalid events at write time, not replay time.
- **Event versioning with upcasters:** When event schemas change, write Marten upcasters that transform old event formats to new ones during replay. Never change the semantics of an existing event type.
- **Integration tests:** Every event type must have a round-trip serialization test: serialize, deserialize, assert equality.
- **Immutable event types:** Event classes are `record` types with `init`-only properties. No mutable state that could be corrupted in-memory.

---

## 4. Timer Skew on Actor Migration

### Scenario

`OutreachSchedulerActor` (a classic child actor of `StudentActor:student-2104`) has scheduled a Proto.Actor reminder to fire in 10 minutes: "Send WhatsApp streak reminder." At minute 7, the Fargate node hosting this actor fails. The actor is deactivated. Three minutes later, the student sends a message, causing `StudentActor:student-2104` to reactivate on a different node. The `OutreachSchedulerActor` child is recreated.

**Questions:** Does the reminder fire? Does it fire on time? Does it fire twice?

### Why This Is Dangerous

- **If the timer is lost:** The student's streak expires without a reminder. Engagement drops. The student may never come back.
- **If the timer fires twice:** The student receives two identical WhatsApp messages. This feels spammy, erodes trust, and may violate WhatsApp Business API rate limits.
- **If the timer fires late:** The streak may have already expired by the time the reminder arrives. The message content is stale ("Your streak expires in 2 hours!" when it already expired).

### Proto.Actor Timer/Reminder Semantics

Proto.Actor's built-in timers (`Context.ScheduleOnce`, `Context.ScheduleRepeat`) are **in-memory only**. They are NOT persisted. When a node fails:

- All scheduled timers on that node are lost silently.
- When the actor reactivates on a new node, no timers are restored.
- Proto.Actor does NOT have a built-in durable reminder system (unlike Orleans' persistent reminders).

This means **Cena must build its own durable timer mechanism.**

### Detection

- **Metric:** `outreach_reminder_scheduled` vs `outreach_reminder_fired` counters. If `scheduled - fired` grows over time, timers are being lost.
- **Audit:** On actor activation, compare the current time against the actor's persisted schedule. If a scheduled reminder's fire time has passed and no corresponding `ReminderFired` event exists, the timer was lost.
- **NATS monitoring:** If the Outreach service stops receiving `StreakExpiring` events during a period when streaks should be expiring, timers are being dropped.

### Automated Response — Durable Timer Design

1. **Persist timer state in the event stream:** When `OutreachSchedulerActor` schedules a reminder, emit a domain event:
   ```
   ReminderScheduled {
       ReminderId: guid,
       StudentId: "student-2104",
       FireAt: DateTime(UTC),
       Type: "StreakExpiring",
       Payload: { ... },
       IdempotencyKey: "streak-reminder-2104-2026-03-26"
   }
   ```

2. **On actor activation, rebuild timers:** During `StudentActor.OnStarted()`, scan the event stream for `ReminderScheduled` events that have not been followed by a corresponding `ReminderFired` or `ReminderCancelled` event. For each outstanding reminder:
   - If `FireAt` is in the future: schedule an in-memory timer for `FireAt - now`
   - If `FireAt` is in the past: fire immediately (the timer was lost during migration)

3. **On timer fire, emit `ReminderFired`:**
   ```
   ReminderFired {
       ReminderId: guid,
       FiredAt: DateTime(UTC),
       IdempotencyKey: "streak-reminder-2104-2026-03-26"
   }
   ```

4. **Idempotency at the Outreach service:** The NATS message for a streak reminder includes the `IdempotencyKey`. The Outreach service maintains a Redis set of recently processed idempotency keys (TTL: 24 hours). If the key already exists, the message is acknowledged but not acted upon. This prevents duplicate WhatsApp sends even if the actor fires the reminder twice (once on the old node before it died, once on the new node during recovery).

5. **Staleness check:** Before sending, the Outreach service checks whether the reminder is still relevant:
   ```
   if (event.FireAt < DateTime.UtcNow - TimeSpan.FromMinutes(30))
   {
       // Reminder is stale — the streak already expired or was renewed
       // Check current streak status before sending
       var currentStreak = await studentApi.GetStreakStatus(event.StudentId);
       if (currentStreak.Status != StreakStatus.Expiring)
       {
           logger.LogInfo("Dropping stale reminder {Id}", event.ReminderId);
           return;
       }
   }
   ```

### Manual Recovery

1. **Identify students with lost timers:** Query the event store for students with `ReminderScheduled` events that have no corresponding `ReminderFired` within a reasonable window after `FireAt`.
2. **Force-reactivate affected actors:** Send a no-op `Ping` message to each affected student actor. On activation, the timer rebuild logic (step 2 above) will recover the lost timers.

### Prevention

- **Event-source all timer state** (as designed above). Never rely on Proto.Actor's in-memory timers for anything that has real-world consequences.
- **Idempotency keys on all outbound messages** to WhatsApp, Telegram, voice, and push. The messaging layer must be idempotent regardless of how many times the actor fires.
- **Health check timer audit:** On every actor activation, log the count of recovered timers. A spike in recovered timers correlates with infrastructure instability.

---

## 5. NATS JetStream Consumer Lag

### Scenario

The Outreach service (NATS JetStream consumer) crashes and stays down for 30 minutes. During this window, 500 `StreakExpiring` events accumulate in the NATS JetStream stream. When the service restarts, it begins processing the backlog. Problem: 300 of those 500 streaks have already expired. Sending "Your streak expires in 2 hours!" to a student whose streak expired 25 minutes ago is worse than sending nothing — it is confusing and demonstrates that the system is broken.

### Detection

- **NATS consumer lag metric:** `nats_consumer_pending_messages` gauge per consumer group. Alert when > 100 for the Outreach consumer.
- **Consumer last activity:** Alert when the Outreach consumer has not acknowledged a message in > 5 minutes during peak hours.
- **Service health check:** ECS health check on the Outreach service Fargate task. If the task is not running, ECS should restart it automatically.
- **End-to-end latency:** Track `event_published_at` vs `event_processed_at` for Outreach events. Alert when the delta exceeds 5 minutes.

### Automated Response

1. **Event freshness gate:** Every NATS message includes a `published_at` timestamp. The Outreach consumer checks freshness before processing:
   ```csharp
   var age = DateTime.UtcNow - message.PublishedAt;
   var maxAge = GetMaxAge(message.EventType); // e.g., 15 min for StreakExpiring

   if (age > maxAge)
   {
       logger.LogWarning("Dropping stale event {Type} age={Age}s",
           message.EventType, age.TotalSeconds);
       message.Ack(); // Acknowledge to advance the consumer position
       metrics.Increment("outreach_stale_event_dropped");
       return;
   }
   ```

2. **Domain state verification:** Even for fresh events, verify the current state before acting:
   ```
   StreakExpiring event received
     → Query student actor (or Redis cache): Is the streak still expiring?
       → Yes: Send WhatsApp reminder
       → No (already expired or renewed): Drop the event, log it
   ```

3. **Backpressure on consumer restart:** When the consumer detects a large backlog (> 100 pending messages), process messages at a throttled rate (50/second) to avoid overwhelming WhatsApp API rate limits and the student actor cluster with verification queries.

4. **NATS JetStream `MaxDeliver` and `AckWait`:**
   ```
   AckWait:      30s     — if a message is not acknowledged in 30s, redeliver
   MaxDeliver:   5       — after 5 failed deliveries, move to dead-letter
   MaxAckPending: 100    — backpressure: max 100 unacknowledged messages
   ```

### Manual Recovery

1. **Inspect the dead-letter stream:** Messages that exceeded `MaxDeliver` land in a dead-letter stream (`cena.outreach.events.dlq`). Review them for patterns.
2. **Replay with filtering:** If the service was down during a critical window and many events were dropped as stale, manually trigger streak checks for all students who had active streaks during the outage window:
   ```sql
   SELECT student_id FROM streak_status
   WHERE status = 'expiring'
   AND expiry_time BETWEEN @outage_start AND @outage_end;
   ```
3. **For each affected student:** Send a fresh, accurate outreach message based on current state, not the stale event.

### Prevention

- **Outreach service high availability:** Run 2 instances of the Outreach consumer in a NATS JetStream "queue group" (consumer group). If one fails, the other continues processing. NATS JetStream distributes messages across group members.
- **Watchdog process:** A lightweight Lambda function (or ECS task) that checks NATS consumer lag every 60 seconds and pages on-call if the Outreach consumer is offline.
- **Graceful shutdown:** On SIGTERM, the Outreach service finishes processing in-flight messages before exiting, preventing partial processing.

---

## 6. LLM Service Unavailability

### Scenario

The Python FastAPI LLM Anti-Corruption Layer is down, or all upstream LLM providers (Kimi K2.5, Claude Sonnet, Claude Opus) are simultaneously rate-limited or experiencing outages. A student is mid-session — they just answered a question and are waiting for the next one. The `LearningSessionActor` sends a gRPC call to the LLM ACL and gets... nothing.

### Why This Is Dangerous

- **Session hang:** The student stares at a loading spinner. After 30 seconds, they leave. Session abandonment is the #1 engagement killer.
- **Actor resource leak:** If the actor awaits the LLM response indefinitely, it holds memory and blocks its mailbox. Other messages for this student queue up.
- **Cascade:** If many students are mid-session, hundreds of actors are blocked, consuming memory and thread pool resources. The cluster may become unresponsive.

### Detection

- **gRPC call latency:** `llm_acl_request_duration_ms` histogram. Alert when p99 > 5000ms.
- **gRPC error rate:** `llm_acl_error_rate` counter. Alert when > 5% of calls fail over a 2-minute window.
- **Circuit breaker state:** `llm_circuit_breaker_state` gauge (0=closed, 1=half-open, 2=open). Alert on transition to open.
- **Python FastAPI health check:** `/health` endpoint on the LLM ACL service, checked by ECS every 10 seconds.
- **Upstream provider status:** Monitor Anthropic and Moonshot (Kimi) status pages via webhook integration.

### Automated Response

1. **Timeout on LLM calls:** Every gRPC call from the actor cluster to the LLM ACL has a hard timeout:
   ```
   GenerateSocraticQuestion:  8 seconds
   EvaluateAnswer:            5 seconds
   ClassifyError:             3 seconds
   DecideMethodologySwitch:  10 seconds
   ```
   On timeout, the call returns a `LlmUnavailableResult`, not an exception.

2. **Circuit breaker (Polly):** Implement a circuit breaker on the gRPC channel to the LLM ACL:
   ```
   Failure threshold:  5 failures in 30 seconds → open circuit
   Open duration:      30 seconds
   Half-open:          Allow 1 probe request. If it succeeds, close. If it fails, re-open.
   ```
   When the circuit is open, calls return `LlmUnavailableResult` immediately without attempting the network call.

3. **Graceful degradation — cached/pre-generated content:**
   - **Exercise bank:** Pre-generate 10-20 exercises per concept using Claude during content authoring. Store in PostgreSQL. When LLM is unavailable, serve from the bank.
   - **Evaluation fallback:** For multiple-choice and numeric-answer questions, the actor can evaluate correctness without an LLM (exact match or option comparison). Only free-text Socratic evaluation requires the LLM.
   - **Hint fallback:** Pre-generated hints stored with each exercise. Serve static hints when the LLM cannot generate dynamic ones.
   - **Classification fallback:** Use rule-based error classification (regex patterns for common error types) as a degraded alternative to LLM classification.

4. **Session state machine — degraded mode:**
   ```
   Normal mode:
     Question → Student answers → LLM evaluates → LLM generates next question

   Degraded mode:
     Pre-generated question → Student answers → Rule-based evaluation →
     Pre-generated follow-up → [Banner: "Running in offline mode, some features limited"]
   ```

5. **Actor-level timeout protection:** The `LearningSessionActor` uses `Task.WhenAny` with a timeout future. If the LLM call does not return within the timeout, the actor transitions to degraded mode for the remainder of the session (or until the circuit breaker closes).

### Manual Recovery

1. **If Python FastAPI is down:** Check ECS task status, CloudWatch logs. Restart the task. If it crash-loops, check for dependency issues (Python package, environment variable, secrets).
2. **If all LLM providers are down:** Nothing to do except wait. The system degrades gracefully. Post an incident in the student-facing status page.
3. **If rate-limited:** Check cost tracking in the LLM ACL. Identify if a specific task type is consuming excessive tokens. Temporarily route expensive tasks to degraded mode to reduce API load.

### Prevention

- **Pre-generate content aggressively:** For every concept in the knowledge graph, pre-generate a pool of exercises, hints, and explanations during content authoring (batch mode). The LLM is a real-time enhancement, not a hard dependency for core learning.
- **Multi-provider redundancy:** The fallback chain (Opus -> Sonnet -> Kimi) already provides redundancy. Add a fourth tier: local heuristic-based generation (template-based questions with variable substitution) for when all LLM providers are down.
- **Token budget management:** Per-student and per-hour token budgets in the LLM ACL to prevent a single runaway session from consuming rate limit headroom.
- **LLM ACL autoscaling:** Scale the Python FastAPI service on request queue depth, not just CPU. A sudden influx of LLM requests should trigger horizontal scaling before the service becomes a bottleneck.

---

## 7. Database Failure

### Scenario

PostgreSQL (the event store) becomes unavailable — RDS failover, network partition to the database subnet, or storage full. Actors currently in memory continue operating (their state is in-memory), but they cannot persist new events. Meanwhile:

- Redis (ElastiCache) may also fail independently, losing hot session state.
- Neo4j AuraDB may become unreachable, though this only affects admin operations since the domain graph is cached in-memory.

### Sub-Scenario 7a: PostgreSQL Unavailable

#### Detection

- **Marten write failure metric:** `marten_event_append_error_rate` counter. Alert on any increment.
- **RDS CloudWatch alarms:** `DatabaseConnections`, `FreeStorageSpace`, `CPUUtilization`, `ReplicaLag`.
- **Actor-level:** The `StudentActor` catches `NpgsqlException` on event persistence and emits a structured log: `EVENT_PERSIST_FAILED`.
- **Health check:** The `/health` endpoint includes a lightweight PostgreSQL connectivity check (`SELECT 1`).

#### Automated Response — Fail-Fast, Not Write-Ahead Buffer

**Design decision: fail-fast.** When PostgreSQL is unavailable, the actor MUST NOT continue accepting writes with a promise to persist later. Reasons:

1. A write-ahead buffer in the actor creates a split-brain between in-memory state and the event store. If the node fails before the buffer is flushed, events are lost permanently.
2. Other consumers (NATS subscribers, CQRS projections) will not see the buffered events, causing inconsistency across bounded contexts.
3. Event ordering guarantees are violated if buffered events are flushed out of order.

**Fail-fast behavior:**
```
1. Actor attempts to persist event via Marten
2. Marten throws NpgsqlException
3. Actor catches the exception
4. Actor returns an error to the client: "Unable to save your progress. Please try again in a moment."
5. Actor state is NOT updated (the event was not persisted, so the in-memory state must not reflect it)
6. The client retries the command on the next attempt
7. Actor remains alive and responsive — it can still serve read queries from its in-memory state
```

**What still works during PostgreSQL outage:**
- Reading current actor state (in-memory)
- Serving the knowledge graph (in-memory cache from Neo4j)
- Timer-based operations (reminders are in-memory, will persist when PostgreSQL returns)
- Client can display current state — just cannot make progress

**What stops working:**
- Any state-mutating operation (exercise attempts, concept mastery updates)
- New actor activations (cannot replay event stream)
- CQRS projection updates
- Snapshot writes

#### Manual Recovery

1. **If RDS failover:** Wait for automatic failover (typically 1-2 minutes for Multi-AZ). The connection pool will reconnect automatically.
2. **If storage full:** Increase allocated storage in RDS console (online operation for GP3 volumes). Identify and archive old event streams if needed.
3. **Post-recovery:** All actors that were alive during the outage resume normal operation automatically. Actors that failed to activate during the outage will activate on the next request.
4. **Verify event stream integrity:** Run the nightly deserialization health check immediately after recovery.

### Sub-Scenario 7b: Redis Unavailable

#### Detection

- **ElastiCache CloudWatch alarms:** `CurrConnections`, `EngineCPUUtilization`, `Evictions`.
- **Redis connection error metric:** `redis_connection_error_rate`. Alert on any increment.

#### Automated Response

Redis is a cache, not a source of truth. When Redis is unavailable:

1. **Hot state reads fall back to PostgreSQL replay:** The actor activates by replaying from Marten instead of loading from Redis. This is slower (100-500ms vs 1-5ms) but correct.
2. **Session state:** If an active session was cached in Redis and Redis fails, the `LearningSessionActor` reloads session state from the parent `StudentActor`'s in-memory state (which includes the current session).
3. **Idempotency keys:** If the Outreach service uses Redis for idempotency key storage and Redis fails, fall back to a PostgreSQL-based idempotency check (slower but durable). Accept the possibility of a duplicate message as the lesser evil compared to not sending at all.

#### Manual Recovery

1. **If ElastiCache node failure:** Automatic failover to replica (if Multi-AZ enabled). No action needed.
2. **If cluster-wide failure:** Redis will come back empty. All actors fall back to PostgreSQL replay. Performance degrades but correctness is maintained. Redis repopulates as actors are accessed.

### Sub-Scenario 7c: Neo4j AuraDB Unavailable

#### Detection

- **Neo4j AuraDB status page** webhook.
- **Domain graph cache load failure** at node startup. If a new node cannot reach Neo4j, it cannot populate its in-memory domain graph cache.

#### Automated Response

- **Existing nodes:** No impact. The domain graph is fully loaded into memory at startup and does not query Neo4j at runtime.
- **New nodes joining the cluster:** Cannot load the domain graph. Mitigation: persist the domain graph snapshot to S3 as a fallback. On startup, try Neo4j first; if unavailable, load from S3.
  ```
  Startup sequence:
    1. Try Neo4j AuraDB → success? Load into memory. Snapshot to S3.
    2. Neo4j unavailable? → Load from latest S3 snapshot.
    3. S3 also unavailable? → Node fails to start. Do NOT join cluster without domain graph.
  ```
- **Admin operations** (curriculum editing, graph updates) are unavailable during the outage. This is acceptable — curriculum changes are infrequent (weekly at most).

#### Manual Recovery

1. Check Neo4j AuraDB console for service status.
2. If Neo4j is down for an extended period and a new deployment is needed, ensure the S3 snapshot is recent.
3. After Neo4j recovers, trigger a cache refresh across all nodes (send a `RefreshDomainGraphCache` cluster-wide message).

### Prevention (All Database Sub-Scenarios)

- **PostgreSQL:** RDS Multi-AZ deployment with automatic failover. Automated backups with 7-day retention. Monitor `FreeStorageSpace` and auto-scale storage.
- **Redis:** ElastiCache Multi-AZ with automatic failover. Since Redis is a cache, data loss on failure is acceptable.
- **Neo4j:** AuraDB Professional includes automatic backups. S3 snapshot as secondary fallback.
- **Connection pool tuning:** Size connection pools for peak load + 20% headroom. Use connection pool metrics to detect exhaustion early.
- **Chaos testing:** Periodically inject database failures in staging to verify fallback behavior works as documented.

---

## Summary Matrix

| # | Failure Mode | Blast Radius | Detection Time | Auto-Recovery | Human Required? |
|---|-------------|-------------|---------------|--------------|----------------|
| 1 | Split-Brain | All students on affected partition | 3-8s (gossip timeout) | Self-fencing via DynamoDB | Yes — event stream reconciliation |
| 2 | Activation Storm | All students during peak | Immediate (queue depth) | Rate limiting + snapshot pre-warming | If PostgreSQL saturated |
| 3 | Poison Message | Single student (per corrupt event) | < 2 min (restart loop detection) | Actor quarantine | Yes — event repair |
| 4 | Timer Skew | Single student (per lost timer) | On next actor activation | Timer rebuild from event stream | Only if mass timer loss |
| 5 | NATS Consumer Lag | Outreach for all students | < 60s (consumer lag metric) | Stale event dropping + freshness gate | If extended outage |
| 6 | LLM Unavailable | Learning session quality | < 30s (circuit breaker) | Degraded mode with cached content | Only if crash-loop |
| 7 | Database Failure | Varies by sub-scenario | < 60s (CloudWatch + health checks) | Fail-fast (PG), fallback (Redis), S3 snapshot (Neo4j) | If failover fails |

---

## Appendix: Monitoring Dashboard Requirements

The following Grafana dashboard panels are required for failure mode visibility:

1. **Cluster Health:** Node count, gossip state per node, partition events
2. **Actor Activation:** Activation rate, queue depth, p50/p95/p99 latency, quarantined actor count
3. **Event Store:** Marten write latency, error rate, active connections, events per second
4. **NATS Consumers:** Pending messages per consumer, ack rate, dead-letter count
5. **LLM ACL:** Request rate, latency percentiles, circuit breaker state, degraded mode active count
6. **Outreach:** Messages sent, idempotency-deduplicated count, stale events dropped
7. **Timers:** Reminders scheduled vs fired, recovered timers on activation, lost timer rate

---

## Appendix: Related Documents

- `docs/architecture-design.md` — System architecture and technology choices
- `docs/architecture-audit.md` — Architecture review and gap analysis
- `docs/llm-routing-strategy.md` — LLM model routing and cost analysis
