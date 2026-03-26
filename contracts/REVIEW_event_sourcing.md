## EVENT SOURCING REVIEW -- The Greg Young Disciple

Reviewer credentials: CQRS/ES practitioner with Marten, EventStoreDB, Axon Framework. Evaluating the Cena platform contracts against Greg Young's principles and Marten v7.x best practices.

Files reviewed:
- `contracts/data/marten-event-store.cs`
- `contracts/data/redis-contracts.ts`
- `contracts/data/neo4j-schema.cypher`
- `contracts/data/s3-export-schema.json`
- `contracts/actors/student_actor.cs`
- `contracts/actors/learning_session_actor.cs`
- `contracts/actors/stagnation_detector_actor.cs`
- `contracts/actors/outreach_scheduler_actor.cs`
- `contracts/actors/methodology_switch_service.cs`
- `contracts/actors/actor_system_topology.cs`
- `contracts/actors/cluster_config.cs`
- `contracts/actors/supervision_strategies.cs`

---

### CRITICAL (violates ES fundamentals)

1. **Apply methods use wall-clock time instead of event timestamps**: `marten-event-store.cs:334`, `marten-event-store.cs:344`, `student_actor.cs:129` -- Multiple `Apply()` methods use `DateTimeOffset.UtcNow` to set state fields like `LastAttemptedAt`, `MasteredAt`, and `Timestamp` in `AttemptRecord`. Apply methods MUST be pure functions of the event data. When Marten replays events during rehydration, `DateTimeOffset.UtcNow` will produce the REPLAY timestamp, not the ORIGINAL timestamp. This means every snapshot rebuild or rehydration produces subtly different state. Greg Young's rule: "Apply methods must be deterministic -- same events in, same state out, every time." Fix: add a `Timestamp` field to each event record and use it in Apply methods. Marten already provides `IEvent.Timestamp` metadata, but these Apply methods ignore it entirely.

2. **Dual-state divergence between StudentProfileSnapshot and StudentState**: `marten-event-store.cs:307-391` vs `student_actor.cs:49-235` -- There are TWO separate Apply method hierarchies: one in `StudentProfileSnapshot` (Marten's snapshot aggregate) and one in `StudentState` (the actor's in-memory state). These must stay perfectly synchronized, but they already diverge. `StudentState.Apply(ConceptAttempted_V1)` calls `RecalculateBaselines()` and updates `RecentAttempts`, but `StudentProfileSnapshot.Apply(ConceptAttempted_V1)` does not track recent attempts at all. `StudentState` has `HlrTimers`, `MethodologyMap`, `RecentAttempts`, `BaselineFatigueScore` -- none of which exist in the snapshot. When the actor rehydrates from snapshot in `RestoreStateFromEventStore()` (student_actor.cs:505-567), it manually maps snapshot fields to actor state, but the HLR `LastReviewAt` is approximated from `MasteredAt` (line 546-548) rather than tracking the actual review timestamps. This is a data integrity time bomb. Fix: use a SINGLE aggregate type for both Marten projection and actor state, or make StudentState the Marten aggregate directly.

3. **ForceSnapshot does not actually persist a snapshot**: `student_actor.cs:1264-1289` -- The `ForceSnapshot()` method opens a Marten `LightweightSession()`, resets the counter, but NEVER calls `session.Events.Append()` or `session.SaveChangesAsync()`. It just sets `_eventsSinceSnapshot = 0` and returns. The inline snapshot projection only fires when new events are appended. This means the "force snapshot" is a no-op. During passivation (line 444-447), if there are unpersisted events, `ForceSnapshot()` silently loses them. Fix: either append a `SnapshotCheckpoint` marker event, or use Marten's explicit snapshot API `session.Events.WriteToAggregate<T>()`.

4. **PersistAndPublish creates a new session per event -- no unit of work**: `student_actor.cs:1209-1236` -- Each call to `PersistAndPublish` creates a new `LightweightSession()` and immediately saves. In `HandleAttemptConcept` (lines 696-733), up to THREE separate events are persisted in three separate sessions/transactions: `ConceptAttempted_V1`, `XpAwarded_V1`, and potentially `ConceptMastered_V1`. If the process crashes between the first and second persistence, the stream has a `ConceptAttempted_V1` without the corresponding `XpAwarded_V1`, violating the business invariant that correct answers always award XP. Greg Young's rule: "All events from a single command must be committed atomically in a single transaction." Fix: collect all events from a command, append them all in one `session.Events.Append()` call, and call `SaveChangesAsync()` once.

5. **Event stream identity mismatch -- StudentId used as stream key but events contain StudentId redundantly**: `marten-event-store.cs:27` configures `StreamIdentity = StreamIdentity.AsString` using student UUID as the stream key, but every event record (e.g., `ConceptAttempted_V1`) embeds `StudentId` as a field. This creates a denormalization hazard: nothing prevents appending an event with `StudentId = "alice"` to Bob's stream. Marten does not validate this. Fix: remove `StudentId` from event records entirely -- the stream key IS the student identity. Use Marten's `IEvent.StreamKey` for queries.

### HIGH (will cause data integrity issues)

1. **Snapshot can become inconsistent with events due to missing Apply handlers**: `marten-event-store.cs:307-381` -- `StudentProfileSnapshot` has Apply methods for 7 event types but the system registers 20+ event types. Missing handlers: `StagnationDetected_V1`, `CognitiveLoadCooldownComplete_V1`, `ExercisePresented_V1`, `HintRequested_V1`, `QuestionSkipped_V1`, `BadgeEarned_V1`, `StreakExpiring_V1`, `ReviewDue_V1`, all Outreach events. When Marten rebuilds the snapshot, events without Apply handlers are silently skipped. If any future snapshot field depends on these events, the snapshot will diverge from full replay. This is technically fine IF those events genuinely have no state impact on the snapshot -- but `SessionEnded_V1` is also missing, and `StudentState.Apply(SessionEnded_V1)` clears `ActiveSessionId` (student_actor.cs:176-180). The snapshot has no `ActiveSessionId` field, so this specific case is "coincidentally correct" but fragile.

2. **ClassOverviewProjection is Inline but uses SingleStreamProjection on what appears to be a multi-stream concern**: `marten-event-store.cs:454-457` -- `ClassOverviewProjection` extends `SingleStreamProjection<ClassOverviewView>`, but a class-level view aggregates events from MULTIPLE student streams. A `SingleStreamProjection` can only see events from ONE stream. This projection will either (a) never populate because it expects a class-level stream that doesn't exist, or (b) only see events from one student. Fix: this must be a `MultiStreamProjection` (like `TeacherDashboardProjection` already is), or it must use a `ViewProjection` with explicit stream grouping logic.

3. **No idempotency guard in the event persistence path**: `student_actor.cs:1217-1219` -- Events are appended to Marten without any optimistic concurrency check. Marten supports expected version: `session.Events.Append(_studentId, expectedVersion, @event)`. Without this, a race condition during reactivation (e.g., two nodes briefly own the same grain during a rebalance) could append duplicate events. The Redis idempotency keys in `redis-contracts.ts` only protect the offline sync path, not the primary hot path. Fix: track `_state.EventVersion` (already maintained) and pass it as expected version to Marten.

4. **Offline sync has no idempotency enforcement**: `student_actor.cs:1006-1059` -- `HandleSyncOfflineEvents` processes offline events by calling `HandleAttemptConcept` in a loop, but never checks the Redis idempotency keys defined in `redis-contracts.ts` (lines 76-77). The idempotency infrastructure exists but is not wired into the sync handler. If a device retransmits the same batch, events will be duplicated in the stream. Fix: check `Keys.idempotency(studentId, eventId)` via SET NX before processing each offline event.

5. **Inline projection on hot path adds write latency**: `marten-event-store.cs:51` -- `StudentMasteryProjection` is registered as `ProjectionLifecycle.Inline`. Every `ConceptAttempted_V1` write triggers synchronous projection updates within the same transaction. On the hot path (student answering questions), this means every BKT update pays the cost of projection computation AND write amplification. This is the #1 latency concern. At scale with hundreds of concepts per student, the `MasteryMap` dictionary serialization in the projection will grow linearly. Fix: consider moving `StudentMasteryProjection` to `Async` since the actor's in-memory state already serves the same read queries with zero latency. The inline projection is redundant with the actor state.

6. **Event-then-apply ordering creates a window where state and store disagree**: `student_actor.cs:696-699` -- The pattern is `await PersistAndPublish(@event)` then `_state.Apply(@event)`. If the actor crashes between persist and apply, the event is in Marten but not in actor state. On reactivation, the actor rebuilds from the snapshot (which may be stale) and replays events -- so this self-heals. But the REAL problem is the reverse: if `PersistAndPublish` fails (Marten exception), the actor catches the exception (line 751-760) and responds with an error, but the NATS publish inside `PersistAndPublish` may have already fired before the Marten save. This means NATS consumers see an event that was never committed. Fix: publish to NATS AFTER `SaveChangesAsync` succeeds, or use an outbox pattern.

### MEDIUM (code smell, not idiomatic)

1. **Event version naming convention _V1 without infrastructure for V2 coexistence**: `marten-event-store.cs:97-103` -- Upcaster registration is stubbed out with comments. The V1 suffix implies version evolution is planned, but there is no upcaster infrastructure, no event type mapping, and no schema registry. When V2 events are introduced, every consumer (projections, S3 export, NATS subscribers) must be updated simultaneously. Fix: implement at least one concrete upcaster as a proof-of-concept, and define the event schema evolution strategy (weak schema, upcasting, or copy-and-replace).

2. **ConceptAttempted_V1 is too coarse -- it is a "god event"**: `marten-event-store.cs:112-130` -- This single event carries 18 fields including behavioral signals (`BackspaceCount`, `AnswerChangeCount`), mastery state (`PriorMastery`, `PosteriorMastery`), evaluation results (`IsCorrect`, `ErrorType`), and metadata (`WasOffline`, `AnswerHash`). Greg Young's guidance: "Events should record what happened, not what was computed." `PosteriorMastery` is a computed value that depends on the BKT algorithm version. If the algorithm changes, historical events carry obsolete mastery values. The event should record the RAW observation (answer, timing); mastery should be computed by the projection. Fix: split into `QuestionAnswered` (raw observation) and let projections compute mastery.

3. **XpAwarded_V1 contains TotalXp -- a running total in an event**: `marten-event-store.cs:243-248` -- Events should record deltas, not running totals. `TotalXp` is a projection concern. If events are replayed, the `TotalXp` field in each event becomes stale/incorrect because it was computed at write time from prior state. The snapshot Apply method (line 367) just overwrites `TotalXp = e.TotalXp`, which works only if events are processed in order, but creates confusion about whether `TotalXp` is authoritative. Fix: record only `XpAmount` and `Source` in the event. Compute `TotalXp` in projections and Apply methods.

4. **StreakUpdated_V1 contains absolute state rather than what happened**: `marten-event-store.cs:250-255` -- Same issue as XpAwarded_V1. The event contains `CurrentStreak`, `LongestStreak`, `LastActivityDate` -- all absolute state. An event like `DailyActivityRecorded` with just the activity timestamp would be more idiomatic. The streak logic belongs in the projection/Apply method, not baked into the event payload.

5. **StreakExpiring_V1 and ReviewDue_V1 are scheduled-state events, not domain events**: `marten-event-store.cs:264-277` -- These events represent PREDICTIONS about the future ("your streak will expire", "review is due"), not facts about what happened. They are outreach triggers, not domain events. Persisting them to the event store pollutes the student's domain stream with outreach scheduling concerns. Fix: these should be transient messages or stored in a separate outreach stream, not in the student aggregate stream.

6. **MasteryDecayed_V1 is a projection-derived event, not a true domain event**: `marten-event-store.cs:143-149` -- Mastery decay is computed by the HLR formula `p(t) = 2^(-delta/h)` -- it's a mathematical function of elapsed time, not something the student DID. Persisting it as an event means the event store now contains clock-derived synthetic events. During replay, these events would be replayed in sequence, but the decay calculations in them are frozen at the wall-clock time they were generated. Fix: decay should be computed on-the-fly by projections using the HLR formula; only actual review attempts should be events.

7. **S3 export schema methodology enum is out of sync**: `s3-export-schema.json:38` -- The S3 export schema defines methodology values as `["socratic", "spaced_repetition", "feynman", "worked_examples", "gamified_drill"]` (5 values), but the actor system defines 8 methodologies including `BloomsProgression`, `Analogy`, `RetrievalPractice`, and `DrillAndPractice` (in `methodology_switch_service.cs:141-151`). Events with the missing methodology values will fail S3 export validation.

8. **AutoCreate.CreateOrUpdate in production is risky**: `marten-event-store.cs:23` -- `opts.AutoCreateSchemaObjects = AutoCreate.CreateOrUpdate` will attempt DDL changes on every application startup. In production with multiple nodes starting simultaneously, this causes schema migration races. Fix: use `AutoCreate.None` in production and manage migrations explicitly.

9. **No tombstone/archive strategy for stream growth**: The student stream grows unboundedly. Over years of daily usage, a student could accumulate thousands of events. The snapshot-every-100 strategy helps with rehydration time, but the stream itself is never pruned. Marten does not have built-in stream archival. Fix: plan for stream archival (move old events to cold storage after N snapshots) or consider stream-per-session for high-frequency events.

10. **Stagnation detector holds state in-memory with no persistence**: `stagnation_detector_actor.cs:137` -- The `_conceptStates` dictionary is purely in-memory. When the parent StudentActor passivates and reactivates, the StagnationDetectorActor is respawned fresh with empty state. All accumulated stagnation baselines, sliding windows, and consecutive session counts are lost. This means stagnation detection only works within a single actor activation lifetime (max 30 minutes idle). Fix: either persist stagnation state as part of the student aggregate (additional events), or rebuild it from the student event stream on activation.

11. **Outreach scheduler state is also volatile**: `outreach_scheduler_actor.cs:169` -- Same issue as stagnation detector. HLR timers, pending reminders, throttle counters -- all lost on passivation. The HLR check at 15-minute intervals (`HandleHlrCheck`) will never fire if the student's actor passivates after 30 minutes of inactivity. Fix: persist HLR timer state as events on the student stream, or use a separate persistent scheduler.

12. **Event published to NATS before Marten commit can succeed -- outbox pattern missing**: `student_actor.cs:1227-1229` -- The NATS publish happens INSIDE `PersistAndPublish`, after `SaveChangesAsync` succeeds. This ordering is actually correct for the happy path. However, if NATS publish fails, the event is in Marten but not in NATS. The catch block (line 1249-1257) logs this as non-fatal and mentions a "catch-up publisher" -- but no catch-up publisher is implemented. Fix: implement an outbox pattern using Marten's outbox support (`session.Events.Append` + `IIntegrationEvent`) or a separate polling publisher.

### WHAT'S ACTUALLY GOOD

1. **Immutable event records**: All events are C# `record` types (marten-event-store.cs:112-301), guaranteeing immutability. This is textbook ES design. Records also give you structural equality for free, which helps with testing.

2. **Stream-per-aggregate with string identity**: The choice of `StreamIdentity.AsString` with student UUID as stream key (marten-event-store.cs:27) is correct for this domain. One stream per student, clear aggregate boundary.

3. **Inline snapshot every 100 events**: The snapshot strategy (marten-event-store.cs:39) is well-calibrated. 100 events means rehydration replays at most ~99 events after a snapshot, keeping activation time low while avoiding snapshot write amplification.

4. **Correct async vs inline projection taxonomy**: Analytics projections (`TeacherDashboardProjection`, `RetentionCohortProjection`, `MethodologyEffectivenessProjection`) are correctly async (marten-event-store.cs:55-58). These are cross-student aggregations that should not block the write path. The student-scoped mastery view is inline (line 51) for zero-latency reads.

5. **Event metadata enabled**: `opts.Events.MetadataConfig.EnableAll()` (marten-event-store.cs:28) captures full Marten metadata (timestamp, sequence, correlation, causation IDs). This is essential for debugging, auditing, and implementing upcasters.

6. **Explicit command validation before event emission**: The `StudentActor.HandleAttemptConcept` validates active session (lines 648-663) before creating any events. `HandleStartSession` checks for existing sessions (line 775). This is correct ES practice -- validate invariants, then emit.

7. **Separation of command and query handlers**: The StudentActor cleanly separates commands (`AttemptConcept`, `StartSession`, `EndSession`) from queries (`GetStudentProfile`, `GetReviewSchedule`). Queries are served entirely from in-memory state with zero database access -- textbook CQRS.

8. **Offline sync architecture**: The idempotency key design in Redis (redis-contracts.ts:76-77) with 72-hour TTL and SET NX semantics is a solid foundation for offline event deduplication, even though it is not yet wired into the handler.

9. **S3 export anonymization**: The export schema (s3-export-schema.json) uses HMAC-SHA256 with epoch-rotating keys for student ID anonymization. The `fields_removed` array documents stripped PII fields. This is a privacy-by-design approach.

10. **Actor supervision hierarchy**: The OneForOne strategy with failure windows and exponential backoff (supervision_strategies.cs) is well-designed for an event-sourced actor system. Crash-and-restart is safe precisely because the actor rebuilds from the event store. The poison message quarantine prevents crash loops -- a production-essential pattern.

11. **Circuit breaker for external dependencies**: The LLM circuit breaker (supervision_strategies.cs:518-685) correctly isolates the actor system from external service failures. In an ES system, this is critical because a blocked async call inside an actor would stall the entire mailbox, including event persistence.

12. **Content hashing for PII protection**: Answer text is never stored in plaintext -- only as SHA-256 hashes (student_actor.cs:1452-1457). Annotations store `ContentHash` with NLP-derived sentiment. This is excellent for an education platform under COPPA/FERPA.

---

**Summary**: The architecture demonstrates strong ES fundamentals -- immutable events, stream-per-aggregate, snapshot strategy, CQRS separation. The CRITICAL issues are all fixable without structural changes: (1) make Apply methods deterministic by using event timestamps, (2) unify the dual-state problem between snapshot and actor state, (3) fix the no-op ForceSnapshot, (4) batch events per command into atomic transactions, and (5) remove redundant StudentId from events. The HIGH issues around idempotency and inline projection latency should be addressed before production load testing.
