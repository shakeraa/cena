# Actor System Code Review — Findings & Fix Tasks

**Date:** 2026-03-27
**Scope:** `src/actors/Cena.Actors/` — all actors, services, infrastructure
**Framework:** Proto.Actor v1.8 on .NET 9
**Reviewed against:** Proto.Actor best practices, Corbett & Anderson BKT, event sourcing patterns

---

## Summary

| Severity | Count | Status |
|----------|-------|--------|
| CRITICAL | 3 | C1 Fixed, C2 Fixed, C3 Fixed |
| HIGH | 6 | H1 Fixed, H2 Fixed, H3 Fixed, H4 Fixed, H5 Fixed, H6 -> ACT-019 |
| MEDIUM | 7 | M1 Fixed, M3 Fixed, M4 Fixed, M7 Fixed, M2 -> ACT-023, M5 -> ACT-025, M6 -> ACT-024 |
| LOW | 4 | L1 Fixed, L4 Fixed, L2 -> ACT-025, L3 -> ACT-025 |

**Deferred tasks created:** ACT-019 through ACT-025
**Architectural issues:** ACT-020 (stagnation signals), ACT-021 (outreach timers), ACT-022 (offline sync)

---

## CRITICAL — Won't Compile / Runtime Crash

### C1. Duplicate `IMethodologySwitchService` interface — compilation failure

**Files:**
- `Students/StudentMessages.cs:407-410` — defines `Task<DecideSwitchResponse> DecideSwitch(DecideSwitchRequest)`
- `Services/MethodologySwitchService.cs:17-19` — defines `MethodologySwitchDecision DecideSwitch(MethodologySwitchInput)`

**Problem:** Two interfaces with the same name in different namespaces, incompatible signatures and return types. `StudentActor` resolves to its own namespace's version (async, `DecideSwitchRequest`). The actual service implements the other (sync, `MethodologySwitchInput`). DI registration will fail — no service implements `Cena.Actors.Students.IMethodologySwitchService`.

**Fix:** Remove duplicate from `StudentMessages.cs`. Update `MethodologySwitchService.cs` to accept `DecideSwitchRequest`, return `Task<DecideSwitchResponse>`, add `using Cena.Actors.Students;`. Add `using Cena.Actors.Services;` to `StudentActor.cs`.

---

### C2. Message type mismatches across actor boundaries — messages go to dead letters

**Problem:** Several message records are defined in BOTH the sender's and receiver's namespaces with different shapes. Proto.Actor dispatches by CLR type identity, so the receiver's `switch` will **never** match the sender's type.

| Message | Sender (Students ns) | Receiver | Receiver's version |
|---------|----------------------|----------|-------------------|
| `ConceptMasteredNotification` | 3 params: `StudentId, ConceptId, InitialHalfLifeHours` (StudentMessages.cs:333) | OutreachSchedulerActor | 2 params: `ConceptId, InitialHalfLifeHours` (OutreachSchedulerActor.cs:232) |
| `CheckStagnation` | 2 params: `StudentId, ConceptId` (StudentMessages.cs:370) | StagnationDetectorActor | 1 param: `ConceptCluster` (StagnationDetectorActor.cs:239) |
| `ResetAfterSwitch` | 4 params: `StudentId, ConceptId, Methodology, CooldownSessions` (StudentMessages.cs:375) | StagnationDetectorActor | 1 param: `ConceptCluster` (StagnationDetectorActor.cs:244) |
| `EvaluateAnswerResponse` | 8 params (StudentMessages.cs:425) | LearningSessionActor | 6 params (LearningSessionActor.cs:304) |
| `UpdateSignals` | 10 params (StudentMessages.cs:357) | StagnationDetectorActor | expects `UpdateStagnationSignals` 6 params (StagnationDetectorActor.cs:235) |

**Impact:** Stagnation detection, outreach scheduling, and methodology switching are entirely non-functional at runtime. All cross-actor messages silently go to dead letters.

**Fix:** Remove duplicate definitions from `StudentMessages.cs`. Add `using Cena.Actors.Stagnation;`, `using Cena.Actors.Outreach;`, `using Cena.Actors.Sessions;` to `StudentActor.cs`. Update call sites to match receiver signatures. Rename Session's `EvaluateAnswerResponse` to `SessionEvaluationResult`.

---

### C3. `StudentActorManager.HandleDrainAll` — deadlock

**File:** `Management/StudentActorManager.cs:335-374`

**Problem:** The drain loop `while (_activeActors.Count > 0) { await Task.Delay(500ms) }` blocks the actor's mailbox. But `_activeActors.Count` only decreases when `StudentDeactivated` messages are processed by `HandleDeactivated`. Since the mailbox is blocked by the `DrainAll` handler, those messages queue up and are never processed. **The count never reaches zero** — always hits timeout.

**Fix:** Replace blocking poll loop with a continuation-based approach. Send a `DrainCheckTick` message to self after delay, check count in that handler, and respond to the original requester when complete (or on timeout). This lets the mailbox process `StudentDeactivated` messages between checks.

---

## HIGH — Correctness Bugs / Silent Failures

### H1. Event staging divergence — in-memory state may differ from event store

**File:** `Students/StudentActor.cs:406-460`

**Problem:** In `HandleAttemptConcept` (inline BKT path), events are constructed, staged, flushed, then **new event instances are constructed again** for `_state.Apply(...)`. If any parameter diverges between the two constructions, the in-memory state will differ from the event store.

**Fix:** Store staged events in local variables. After `FlushEvents()`, apply the **same instances** to state.

---

### H2. `context.Parent!` null-forgiving in LearningSessionActor

**File:** `Sessions/LearningSessionActor.cs:97`

**Problem:** `context.Send(context.Parent!, ...)` — NRE if ever spawned without a parent (tests, refactoring). Multiple occurrences (lines 97, 158, 249, 259, 273).

**Fix:** Guard all `context.Parent` usages: `if (context.Parent != null) context.Send(context.Parent, ...)`.

---

### H3. Captured `IContext` beyond receive scope

**Files:** `Students/StudentActor.cs:160-161, 1160-1161`

**Problem:** Timer continuation captures `context` and uses it after `ReceiveAsync` returns. In Proto.Actor, `IContext` is scoped to the current message processing.

**Fix:** Capture `context.Self` and `context.System` in local variables:
```csharp
var self = context.Self;
var system = context.System;
_ = Task.Delay(interval).ContinueWith(_ => system.Root.Send(self, new MemoryCheckTick()));
```

---

### H4. LlmGatewayActor broadcasts success/failure to ALL circuit breakers

**File:** `Topology/ActorSystemManager.cs:148-160`

**Problem:** `ReportSuccess` and `ReportFailure` are broadcast to all 3 circuit breakers. A success on "kimi" resets the failure counter on "sonnet" and "opus" too, defeating per-model isolation.

**Fix:** Add a `ModelName` field to `ReportSuccess` and `ReportFailure`. Route to the correct circuit breaker by model name lookup in `_circuitBreakers` dictionary.

---

### H5. `OutreachSchedulerActor.EnqueueOutreach` — SortedList key collision

**File:** `Outreach/OutreachSchedulerActor.cs:160-165`

**Problem:** Key formula `outreach.Priority * 1000 + _pendingQueue.Count` can produce duplicate keys after a dispatch cycle clears the queue (count resets). `SortedList` throws `ArgumentException` on duplicate keys.

**Fix:** Use a monotonically increasing counter instead of `_pendingQueue.Count`, or switch to `PriorityQueue<PendingOutreach, int>`.

---

### H6. StudentActor is 830+ lines — exceeds 500-line project limit

**File:** `Students/StudentActor.cs` — 1282 lines

**Problem:** Per CLAUDE.md: "Keep files under 500 lines." The actor is the most critical component and difficult to reason about. The inline BKT path duplicates logic from `BktService`.

**Fix:** Extract command handlers into separate handler classes (e.g., `AttemptConceptHandler`, `SessionHandler`). Remove inline BKT — always delegate to `BktService`.

---

## MEDIUM — Best Practices / Performance

### M1. Unnecessary concurrency primitives in StudentActorManager

**File:** `Management/StudentActorManager.cs:142-146`

Proto.Actor guarantees sequential message processing. `ConcurrentDictionary`, `ConcurrentQueue`, and `Interlocked` add lock contention for no benefit. Replace with `Dictionary<>`, `Queue<>`, and plain `int`.

---

### M2. Static `Meter`/`ActivitySource` instances — no `Dispose`

Every actor creates `static readonly Meter` and `static readonly ActivitySource`. These are never disposed. While not a memory leak, OpenTelemetry best practice recommends `IMeterFactory` from DI for testability.

---

### M3. Orphaned `Meter` instances

**Files:** `StagnationDetectorActor.cs:36`, `OutreachSchedulerActor.cs:43-44`, `FocusDegradationService.cs:79-80`

A `Meter` is created inline inside a field initializer but not stored — it can be GC'd, orphaning the histogram/counter. Store the `Meter` in a `static readonly` field.

---

### M4. Mutable inner lists in CurriculumGraphSnapshot

**File:** `Graph/CurriculumGraphActor.cs:75-76`

`PrerequisitesByTarget` and `DependentsBySource` are `IReadOnlyDictionary<string, List<PrerequisiteEdge>>`. The inner `List` is mutable — callers can modify the graph's internal state. Should use `IReadOnlyList<PrerequisiteEdge>`.

---

### M5. StudentProfileSnapshot uses mutable classes

**File:** `Events/StudentProfileSnapshot.cs:11`

Marten snapshot is a mutable `class` with `{ get; set; }` properties. While Marten requires mutability for deserialization, `ConceptMasteryState` should have internal setters.

---

### M6. BktService applies forgetting AFTER learning transition

**File:** `Services/BktService.cs:130`

`posterior = posterior * (1 - pForget)` is applied after the learning transition. The standard Corbett & Anderson model does NOT include forgetting in the within-trial update — HLR handles longer-term decay. With `pForget = 0.02`, mastery is depressed by 2% on every single attempt.

---

### M7. DelegateEvent from LearningSessionActor is never handled

**File:** `Sessions/LearningSessionActor.cs:319`, `Students/StudentActor.cs:104-132`

`LearningSessionActor` wraps events in `DelegateEvent` before sending to parent. But `StudentActor.ReceiveAsync` has no case for `DelegateEvent` — it falls to `_ => Task.CompletedTask`. All delegated events from session actors are silently dropped.

---

## LOW — Style / Minor

### L1. TimeZone portability — `FindSystemTimeZoneById("Israel")` throws on Linux

**File:** `Outreach/OutreachSchedulerActor.cs:29`

`TimeZoneInfo.FindSystemTimeZoneById("Israel")` throws `TimeZoneNotFoundException` on Linux (IANA ID is `"Asia/Jerusalem"`). The `??` fallback never executes because the method throws rather than returning null.

**Fix:** Use `TimeZoneInfo.FindSystemTimeZoneById("Asia/Jerusalem")` with a try-catch fallback for Windows.

---

### L2. LINQ `.Skip().Average()` on hot path

**File:** `Sessions/LearningSessionActor.cs:183`

Allocates an iterator on every question attempt. Use a simple loop or `Span<T>`.

---

### L3. Misleading supervision strategy comment

**File:** `Infrastructure/CenaSupervisionStrategies.cs:21`

Comment says "Stops child after 3 consecutive failures" but Proto.Actor **escalates to parent** after `maxNrOfRetries` is exceeded, not "stops."

---

### L4. Uncached `JsonSerializerOptions` in NatsOutboxPublisher

**File:** `Infrastructure/NatsOutboxPublisher.cs:175`

Creates a new `JsonSerializerOptions` on every event publish. Cache as a `static readonly` field.

---

## Additional Observations

### Architectural Mismatch: Per-Attempt vs Per-Session Stagnation Signals

`StudentActor` sends `UpdateSignals` after every individual attempt, but `StagnationDetectorActor` is designed to receive per-SESSION summaries (`UpdateStagnationSignals` with session accuracy, avg RT, duration, error repeat count). This is a semantic mismatch beyond just types — the detector's sliding window expects session-level aggregates, not raw per-attempt data. Requires architectural discussion on signal granularity.

### Missing Timer Scheduling for OutreachSchedulerActor

The outreach scheduler handles `CheckHlrTimers` and `CheckStreakExpiry` messages but never schedules them itself. An external component must send periodic messages. This is undocumented.

### OfflineSyncHandler is unused

`StudentActor.HandleSyncOfflineEvents` does its own inline Redis-based idempotency check (lines 807-876) rather than delegating to the dedicated `OfflineSyncHandler` class. The handler class is dead code.
