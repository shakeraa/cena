# DISTRIBUTED SYSTEMS REVIEW -- Viktor Klang's Angry Ghost

**Reviewer:** A hostile distributed systems architect who has shipped Akka, Erlang/OTP, and Orleans at 10M+ user scale.

**Scope:** All contracts in `contracts/actors/` and `contracts/backend/`.

**Date:** 2026-03-26

**Verdict:** This is a well-designed system with serious thought behind it. But I've found enough landmines to ruin your on-call rotation for the next two years. Here they are.

---

## CRITICAL (will cause outages)

### C1. Task.Delay Timer Leak + Actor Lifetime Mismatch
**Files:** `learning_session_actor.cs:244`, `outreach_scheduler_actor.cs:307`, `outreach_scheduler_actor.cs:401`, `outreach_scheduler_actor.cs:808`

Every one of your timer patterns is the same anti-pattern:
```csharp
_ = Task.Delay(timeout, _timeoutCts.Token).ContinueWith(t =>
{
    if (!t.IsCanceled) context.Send(context.Self, new SessionTimeoutTick());
});
```

**The bug:** The `ContinueWith` closure captures `context` -- a Proto.Actor `IContext` that is **only valid during the current ReceiveAsync call**. By the time the timer fires 45 minutes later, that `IContext` reference is stale. In Proto.Actor, `IContext` is pooled and recycled between messages. Sending to `context.Self` via a stale context reference may deliver to the wrong actor or throw.

**The fix:** Capture `context.Self` (a `PID`, which is stable) in the closure, and use `system.Root.Send(pid, msg)` from the timer continuation. Alternatively, use Proto.Actor's built-in `context.ReenterAfter()` or scheduler abstractions.

**Severity:** This will corrupt actor state or deliver timer messages to wrong actors under load. Silent data corruption.

### C2. Mutable State Shared Across Actor Boundaries
**File:** `learning_session_actor.cs:165`, `student_actor.cs:614-615`

```csharp
private readonly StudentState _studentState; // read-only reference to parent's state
```

The `LearningSessionActor` holds a **direct reference** to the parent `StudentActor`'s mutable state object. This violates the fundamental actor model invariant: actors must not share mutable state. The parent continues to mutate `_state` (via `Apply()` methods) while the child reads it concurrently from different message handlers.

This is a **data race**. Proto.Actor actors run on the thread pool. Even though each individual actor processes one message at a time, the parent and child are **separate actors** processing messages **concurrently**. The child reading `_studentState.MasteryMap` while the parent is in the middle of `Apply(ConceptAttempted_V1)` is a classic torn read.

**The fix:** Deep-clone the relevant state (or pass an immutable snapshot) when spawning the session actor. If the session needs updated state, request it from the parent via a message.

### C3. No Fencing on Event Persistence + State Apply Order
**File:** `student_actor.cs:686-699`

```csharp
await PersistAndPublish(context, @event);
_state.Apply(@event);
```

You persist the event to Marten, **then** apply it to local state. If the actor crashes between persist and apply (or if persist succeeds but the actor restarts), you get:
- Event in Marten (durable)
- State not updated (transient, lost on restart)
- On restart, Marten replays the event, so the state catches up... **unless** the NATS publish inside `PersistAndPublish` fails and you retry the whole command, in which case you now have a **duplicate event** in Marten.

Worse: you do multiple `PersistAndPublish` calls per command (attempt + XP + mastery). If the actor crashes after the first persist but before the second, your event stream is now in an **inconsistent partial state**. On restart, replaying events gives you an attempt without its XP award.

**The fix:** Use Marten's `AppendEvents` to batch all events for a single command into one `SaveChangesAsync` call. This is what Marten is designed for. One command = one atomic event batch = one Marten transaction.

### C4. Graceful Shutdown Is a Lie
**File:** `cluster_config.cs:238-263`

Your graceful shutdown has a 30-second timeout and calls `ShutdownAsync(graceful: true)`. But look at what actually needs to happen:
1. Every `StudentActor` needs to `ForceSnapshot()` (a Marten write per actor)
2. Every `OutreachSchedulerActor` needs to cancel and persist pending timers
3. Every `LearningSessionActor` needs to publish session summaries

At 10K active actors, each doing a Marten write, you need ~10K PostgreSQL round-trips. At 5ms each, that's 50 seconds minimum. Your 30-second timeout will fire, you'll force-shutdown, and **hundreds of actors will lose their unpersisted events**.

The `OnStopping` handler in `student_actor.cs:417-451` sequentially stops each child with `await context.StopAsync()`. That's 3 sequential awaits per student actor. Multiply by thousands of actors and you'll never finish in 30 seconds.

**The fix:**
- Implement a two-phase shutdown: Phase 1 blocks new activations + drains in-flight requests. Phase 2 snapshots in parallel (not sequential per-actor).
- Use `Task.WhenAll` for parallel snapshot persistence.
- Scale the timeout based on active actor count, not a fixed 30s.
- The load balancer should stop routing to this node **before** the shutdown timer starts.

---

## HIGH (will cause data loss or inconsistency)

### H1. NATS "Exactly-Once" Is an Illusion
**Files:** `nats-subjects.md:344`, `outreach_scheduler_actor.cs:766-796`

The NATS subjects doc claims:
> "exactly-once publishing semantics within the dedup window"

This is JetStream deduplication, which provides **exactly-once publishing** (not exactly-once processing). Your consumers use `Explicit` ack with `MaxDeliver: 5-10`. This means if the consumer processes a message, does its work, but crashes before sending the ack, the message will be re-delivered and processed again. You have **at-least-once processing**, not exactly-once.

For outreach (sending WhatsApp messages), this means a student can receive duplicate reminder messages. For the engagement context (XP awards), a student can get double XP.

**The fix:** Consumers must be idempotent. Use the `Nats-Msg-Id` (event ID) as an idempotency key in a PostgreSQL `processed_events` table. Check before processing, ack after commit.

### H2. Stagnation Detector State Is Not Persisted
**File:** `stagnation_detector_actor.cs:137`

The `StagnationDetectorActor` holds all its state in a `Dictionary<string, ConceptStagnationState>` with no persistence. It is a classic (non-virtual) child actor. When the parent `StudentActor` passivates (after 30 min idle), this child is stopped and **all stagnation tracking data is lost**.

On reactivation, the stagnation detector starts with empty state. The sliding window, consecutive stagnation counts, cooldown trackers -- all gone. A student who was 2 sessions into a 3-session stagnation trigger will have their counter reset to zero.

**The fix:** The stagnation state should be included in the StudentActor's event-sourced state (derive it from domain events on replay), or persisted as part of the Marten snapshot.

### H3. Outreach Scheduler State Is Not Persisted
**File:** `outreach_scheduler_actor.cs:169`

Same problem as H2 but worse. The `OutreachSchedulerActor` holds `HlrTimers`, `PendingReminders`, `MessagesSentToday`, `StreakExpiryReminderSent` all in transient memory. When the parent passivates:
- All pending reminder timers are lost
- HLR timer state (half-lives, last review times) is lost
- Throttle counters reset (student could get re-spammed on reactivation)
- Streak expiry reminders that were already sent might be re-sent

A student who is offline for 31 minutes will have all their spaced repetition schedules silently dropped.

**The fix:** HLR timer state must be part of the persistent StudentState. Pending reminders should be reconstructed from HLR state on activation (compute what's overdue, schedule accordingly). The `StudentActor` already has `HlrTimers` in its state -- use that as the source of truth and let the outreach actor be a pure function of that state.

### H4. Event Sourcing Snapshot Corruption on Concurrent Commands
**File:** `student_actor.cs:688-741`

The `HandleAttemptConcept` handler calls `PersistAndPublish` multiple times in sequence (ConceptAttempted, XpAwarded, optionally ConceptMastered). Between these calls, the Marten session may have been committed. If another command arrives at the same actor between these calls (Proto.Actor guarantees single-threaded access, so this specific scenario is prevented), BUT the `_eventsSinceSnapshot` counter increments per-event, not per-command. If the snapshot fires between the ConceptAttempted and XpAwarded persists of the same command, and the actor crashes, the snapshot will be inconsistent -- it will have the attempt but not the XP.

Marten inline snapshots need to see all events from a single command batch atomically.

**The fix:** Accumulate events per-command, persist as a single Marten `SaveChangesAsync`, then apply all to state.

### H5. SortedDictionary Key Collision in Reminder Scheduling
**File:** `outreach_scheduler_actor.cs:54`, `outreach_scheduler_actor.cs:295`

```csharp
public SortedDictionary<DateTimeOffset, PendingReminder> PendingReminders { get; set; } = new();
```

You use `DateTimeOffset` as the dictionary key. If two reminders are scheduled for the exact same time (entirely possible with HLR timers that compute the same review time), the second `PendingReminders[fireTime] = reminder` will **silently overwrite** the first. One reminder is lost forever.

**The fix:** Use a `SortedSet<PendingReminder>` with a custom comparer that breaks ties on `ReminderId`, or use a `PriorityQueue<PendingReminder, DateTimeOffset>`.

---

## MEDIUM (will cause operational pain)

### M1. Unbounded FatigueWindow List
**File:** `learning_session_actor.cs:71`

```csharp
public List<FatigueDataPoint> FatigueWindow { get; set; } = new();
```

The `ComputeFatigueScore` method uses a "sliding window of last 5" by doing `.Skip(Count - 5)`. But the list itself **never has elements removed**. In a 45-minute session with fast responses (every 10 seconds), you accumulate 270 fatigue data points. Harmless here, but it's a code smell that signals nobody is thinking about memory budgets.

**The fix:** Trim the list after every update: `if (FatigueWindow.Count > 10) FatigueWindow.RemoveRange(0, FatigueWindow.Count - 10);`

### M2. Circuit Breaker Half-Open Thundering Herd
**File:** `supervision_strategies.cs:582-630`

Your `ActorCircuitBreaker` transitions from Open to HalfOpen and allows calls through. But **every concurrent call** that arrives during HalfOpen will pass through the `lock` check, see HalfOpen, and proceed. If 50 calls are queued behind the open circuit, they'll all rush through the probe simultaneously.

In the actor model, this is partially mitigated because actors are single-threaded. But the `ActorCircuitBreaker` is a shared object used from `ExecuteAsync` which can be called from multiple actors. The lock only protects the state check, not the actual execution.

Similarly, in `actor_system_topology.cs:586-589`, the `LlmCircuitBreakerActor` in HalfOpen lets every request through -- there's no "single probe" logic.

**The fix:** Add a `_halfOpenPermit` boolean. Only the first call in HalfOpen gets through. All others are rejected until the probe completes.

### M3. Timer Drift in HLR Spaced Repetition
**File:** `outreach_scheduler_actor.cs:477`

Your HLR timer scheduling computes `hoursUntilReview` and schedules a `Task.Delay`. But `Task.Delay` resolution is ~15ms on Windows and ~1ms on Linux. More importantly, timers accumulate drift. If the first review is at T+5.57h, and you schedule the next from the review callback, the next fires at T+5.57+computed_new_delay. The error compounds over reviews.

Additionally, if the actor passivates and reactivates, all timers are lost (see H3), and the HLR check runs every 15 minutes as a safety net. This means reviews can be delayed by up to 15 minutes. For a half-life of 24 hours, this is a <1% error. For short half-lives (2 hours after a failed review), a 15-minute delay is a 12.5% error in recall prediction.

**The fix:** Store the absolute review-due time, not the delay. On activation, compute which reviews are overdue from the durable state and schedule immediately. Use a single periodic tick (you already have the 15-min HLR check) as the canonical timer, not individual `Task.Delay` per concept.

### M4. ResponseTimesMs List Grows Unbounded
**File:** `learning_session_actor.cs:62`

`List<int> ResponseTimesMs` grows for the entire session. The `GetMedian` call on line 580 sorts the entire list every time. In a 45-minute session with ~270 questions, you sort 270 elements after every question. O(n log n) per question becomes O(n^2 log n) per session. Not a crisis, but sloppy.

**The fix:** Use a running median algorithm (two heaps) or just keep a fixed-size sliding window.

### M5. FailureWindow ConcurrentDictionary Leak
**File:** `supervision_strategies.cs:79`

```csharp
private static readonly ConcurrentDictionary<string, FailureWindow> FailureWindows = new();
```

This is a **static** dictionary keyed by actor PID string. Entries are only removed when an actor hits max failures and is stopped. Actors that fail once or twice and then recover will have their `FailureWindow` entries persist in memory forever. At 100K actor activations with 10% failure rate, you leak 10K entries that are never cleaned up.

**The fix:** Add a periodic cleanup that evicts entries older than the window duration. Or use `ConditionalWeakTable` tied to actor lifecycle.

### M6. Passivation Calls Cluster API From Actor Context
**File:** `student_actor.cs:489-493`

```csharp
await context.Cluster().RequestAsync<Passivate>(
    context.ClusterIdentity()!.Identity,
    context.ClusterIdentity()!.Kind,
    new Passivate(),
    CancellationToken.None);
```

This sends a `Passivate` message through the cluster to itself. It is a network round-trip to the partition identity lookup and back. If the cluster is under partition pressure, this call can hang for the full `GetPidTimeout` (5 seconds from your config). During that time, the actor is blocked, unable to process messages, and will not respond to any commands.

**The fix:** Use the `context.Self.Stop()` or `context.Stop(context.Self)` pattern. Proto.Actor virtual actors have built-in passivation support -- you don't need to round-trip through the cluster.

### M7. No Back-Pressure on Marten Event Persistence
**File:** `student_actor.cs:696` (inferred `PersistAndPublish`)

If PostgreSQL is slow (connection pool exhausted, disk pressure, vacuum running), every AttemptConcept command blocks on the Marten write. The actor's mailbox fills up. Proto.Actor has no built-in mailbox pressure valve. At 1000 concurrent students each sending 1 attempt/second, you queue 1000 Marten writes. If Postgres latency spikes to 100ms, each actor is blocked for 100ms. Mailbox depth grows linearly.

No circuit breaker protects the Marten write path. The LLM ACL has circuit breakers, but the **most critical write path** (event persistence) has none.

**The fix:** Add a circuit breaker on Marten writes. When open, fail fast with a retryable error to the client. Better: batch events per-actor and write on a timer (every 100ms or 10 events, whichever comes first).

### M8. DynamoDB Cluster Provider 3-Second Poll Interval
**File:** `cluster_config.cs:137`

```csharp
PollInterval = TimeSpan.FromSeconds(3)
```

DynamoDB cluster membership polls every 3 seconds. If a node crashes, other nodes won't detect it for up to 3 seconds. During that window, messages routed to the dead node will fail. Combined with `HeartbeatExpiration` of 30 seconds and `GetPidTimeout` of 5 seconds, a node failure creates a **35-second window** where student actors on the dead node are unreachable.

For a real-time tutoring app, 35 seconds of unresponsiveness is an eternity.

**The fix:** Reduce heartbeat expiration to 10-15 seconds. Add client-side retry with exponential backoff on `DeadLetterException`. Consider a faster cluster provider (Consul, etcd) for sub-second failure detection.

### M9. Memory Budget Estimation Is Wildly Inaccurate
**File:** `student_actor.cs:226-234`

```csharp
return (MasteryMap.Count * 200L) + (RecentAttempts.Count * 100L) + ...
```

These are guess-estimated constants. A `Dictionary<string, double>` entry in .NET is ~80 bytes (key string ~40 bytes for UUID + 8 bytes double + hash bucket overhead ~32 bytes). You estimate 200 bytes. But the `MethodAttemptHistory` is `Dictionary<string, List<MethodologyAttemptRecord>>` -- each record is a string + string + double + DateTimeOffset (~120 bytes), and the List has internal array overhead. Your 400-byte estimate per history entry may undercount by 2x when the strings are long.

The real risk: you alert at 500KB but actual usage is 1MB. You think you can fit 10K actors on a node but can only fit 5K.

**The fix:** Use `GC.GetAllocatedBytesForCurrentThread()` before/after actor activation for empirical measurement. Or use `ObjectLayoutInspector` in testing to get real object sizes. Update the constants.

---

## WHAT'S ACTUALLY GOOD

### G1. BKT Inline in Session Actor
The decision to compute BKT updates inline (not via a service call) on the hot path is correct. Sub-microsecond, no allocations, no I/O. The math is textbook-correct with proper clamping. This is how you keep p99 latency low.

### G2. OneForOne Supervision with Failure Windows
The supervision strategy is well-designed. OneForOne prevents cascading sibling failures. The sliding window failure tracker with time-based pruning is better than a simple counter. Exponential backoff prevents restart storms. The poison message quarantine is a mature pattern.

### G3. Five-Signal Stagnation Detection Model
The composite scoring model with configurable weights, sigmoid normalization on accuracy plateau, and 3-session consecutive threshold is pedagogically sound and operationally tunable. The cooldown period after methodology switch prevents oscillation. This shows domain expertise.

### G4. NATS Subject Hierarchy and DLQ Design
The subject naming convention is clean and predictable. DLQ routing with full envelope headers (original subject, consumer name, delivery count, correlation ID) is production-grade. The replay CLI design is thoughtful. Stream configurations with appropriate retention and replica counts show operational awareness.

### G5. Privacy Annotations on gRPC Proto
The `privacy_level` field option with enforcement at the ACL boundary is a solid architectural decision. Distinguishing PII vs. sensitive data, and stripping PII before Kimi routing, shows data governance maturity.

### G6. Circuit Breaker Per LLM Model
Having independent circuit breakers per model tier (Kimi, Sonnet, Opus) with different failure thresholds and open durations is correct. A Kimi outage shouldn't block Sonnet usage. The fallback chain in `RoutingMetadata` provides full observability.

### G7. Event-Sourced Aggregate with Marten
Using Marten's `AggregateStreamAsync` with inline snapshots every 100 events is a solid pattern. The `Apply()` methods are pure and allocation-conscious. The separation between command handling (actor) and event application (state) is clean.

### G8. Telemetry Coverage
Every actor has structured logging, OpenTelemetry activity sources, and Prometheus-style meters. The metric naming follows conventions (`cena.{component}.{metric}`). Correlation IDs flow end-to-end. This codebase was built by someone who has been paged at 3 AM.

---

## SUMMARY

| Severity | Count | Impact |
|----------|-------|--------|
| CRITICAL | 4 | Outages, data corruption, silent state loss |
| HIGH | 5 | Data loss, duplicate processing, inconsistent state |
| MEDIUM | 9 | Performance degradation, memory leaks, operational surprises |
| GOOD | 8 | Solid patterns that should be preserved |

**Top 3 actions before going to production:**
1. Fix the `Task.Delay` + stale `IContext` capture pattern (C1). This is a ticking time bomb in every timer-based actor.
2. Stop sharing mutable state between parent and child actors (C2). Deep-clone or use messages.
3. Batch event persistence per-command in Marten (C3/H4). One command = one atomic transaction.

**Top 3 actions before scaling past 10K users:**
1. Persist stagnation and outreach scheduler state (H2/H3). Your child actors are stateful but not durable.
2. Fix the SortedDictionary key collision (H5). You will lose reminders.
3. Implement consumer-side idempotency for NATS (H1). At-least-once delivery + non-idempotent consumers = duplicate messages to students.
