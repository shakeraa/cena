# ADR-0012: StudentActor Aggregate Decomposition

- **Status**: Accepted (implementation deferred to post-pilot)
- **Date**: 2026-04-14
- **Decision Makers**: Dina (Architecture), Oren (API), Shaker (Lead)
- **Source**: Expert panel audit (RDY-008)

## Context

`StudentActor` handles 65+ event types across learner profile, sessions, pedagogy, engagement, outreach, focus, social, challenges, and assignments — all in one Marten event stream per student. This is a "god aggregate" that:

1. **Makes state transitions impossible to reason about** — a single `StudentState.cs` must handle events from 7+ domains
2. **Forces all projections to handle all event types** — even session-only projections must subscribe to profile events
3. **Creates write contention at scale** — all writes to one stream per student serialize
4. **Prevents independent deployment** — session logic cannot be deployed without profile logic

**Panel agreement**: Acceptable for the 1-2 school pilot (≤200 students). Mandatory split before production scale (10K+ students).

## Decision

Decompose `StudentActor` into 3 bounded contexts with separate event streams.

### Target Bounded Contexts

| Context | Aggregate Root | Stream Key | Purpose |
|---------|---------------|------------|---------|
| **StudentProfile** | `StudentProfileAggregate` | `student-profile-{studentId}` | Identity, enrollment, preferences, consent, account status |
| **LearningSession** | `LearningSessionAggregate` | `session-{sessionId}` | Session lifecycle, attempts, hints, scaffolding, misconceptions |
| **StudentMetrics** | `StudentMetricsAggregate` | `student-metrics-{studentId}` | Mastery, XP, badges, streaks, decay, IRT ability, focus |

### Event-to-Context Mapping

#### StudentProfile (identity + enrollment)
| Event | Current File |
|-------|-------------|
| `AccountStatusChanged_V1` | LearnerEvents.cs |
| `MethodologySwitched_V1` | LearnerEvents.cs |
| `MethodologyConfidenceReached_V1` | (StudentState) |
| `MethodologySwitchDeferred_V1` | (StudentState) |
| `TeacherMethodologyOverride_V1` | (StudentState) |
| `EnrollmentCreated_V1` | EnrollmentEvents.cs |
| `EnrollmentStatusChanged_V1` | EnrollmentEvents.cs |
| `NotificationDeleted_V1` / `Snoozed_V1` | NotificationEvents.cs |
| `WebPushSubscribed_V1` / `Unsubscribed_V1` | NotificationEvents.cs |

#### LearningSession (session lifecycle)
| Event | Current File |
|-------|-------------|
| `SessionStarted_V1` | PedagogyEvents.cs |
| `SessionEnded_V1` | PedagogyEvents.cs |
| `ConceptAttempted_V1/V2/V3` | LearnerEvents.cs |
| `ExercisePresented_V1` | PedagogyEvents.cs |
| `HintRequested_V1` | PedagogyEvents.cs |
| `QuestionSkipped_V1` | PedagogyEvents.cs |
| `AnnotationAdded_V1` | LearnerEvents.cs |
| `StepAttempted_V1` / `StepVerified_V1` | StepSolverEvents.cs |
| `MisconceptionDetected_V1` | MisconceptionEvents.cs |
| `MisconceptionRemediated_V1` | MisconceptionEvents.cs |
| `SessionMisconceptionsScrubbed_V1` | MisconceptionEvents.cs |
| `MentorChatMessageSent_V1` / `Read_V1` | MentorChatEvents.cs |
| `ExamSimulationStarted_V1` / `Submitted_V1` | ExamSimulationEvents.cs |

#### StudentMetrics (mastery + engagement)
| Event | Current File |
|-------|-------------|
| `ConceptMastered_V1/V2` | LearnerEvents.cs |
| `MasteryDecayed_V1` | LearnerEvents.cs |
| `MasterySeepageApplied_V1` | LearnerEvents.cs |
| `XpAwarded_V1` | EngagementEvents.cs |
| `StreakUpdated_V1` | EngagementEvents.cs |
| `BadgeEarned_V1` | EngagementEvents.cs |
| `ChallengeCompleted_V1` | EngagementEvents.cs |
| `ChallengeStarted_V1` / `BossAttemptConsumed_V1` | ChallengeEvents.cs |
| `FocusScoreUpdated_V1` | FocusEvents.cs |
| `MindWanderingDetected_V1` | FocusEvents.cs |
| `MicrobreakSuggested_V1` / `Taken_V1` / `Skipped_V1` | FocusEvents.cs |
| `StagnationDetected_V1` | LearnerEvents.cs |
| `AnomalyFlagRaised_V1` | AnomalyEvents.cs |

#### Cross-Context (handled by sagas/process managers)
| Event | From → To | Mechanism |
|-------|-----------|-----------|
| `ConceptAttempted` | Session → Metrics | Saga: update mastery after attempt |
| `SessionEnded` | Session → Metrics | Saga: finalize XP, check badge triggers |
| `ConceptMastered` | Metrics → Profile | Saga: check methodology switch criteria |
| `MethodologySwitched` | Profile → Session | Query: session reads current methodology |

## Migration Strategy

### Phase 1: Shadow-Write (post-pilot, 2 sprints)
1. Keep existing `student-{id}` stream as primary
2. Add shadow-write to new streams on every event append
3. New projections read from new streams; old projections unchanged
4. Validate: new streams produce identical read models

### Phase 2: Cutover (1 sprint)
1. Flip primary writes to new streams
2. Old stream becomes read-only (for historical replay)
3. Rebuild all projections from new streams
4. Remove dual-write code

### Phase 3: Cleanup (1 sprint)
1. Archive old `student-{id}` streams (don't delete — audit trail)
2. Remove old `StudentState` monolith
3. Split `StudentActor` into 3 actor types with separate Props/supervision

### Snapshot Migration
- `StudentProfileSnapshot` → per-context snapshots
- Each context snapshots independently at its own cadence
- Profile: snapshot every 50 events (low volume)
- Session: snapshot every 100 events (high volume, short-lived)
- Metrics: snapshot every 200 events (medium volume, long-lived)

## Risk Assessment

### 1. Event Ordering Across Contexts
**Risk**: Events that previously had guaranteed ordering in one stream lose that guarantee across streams.
**Mitigation**: Use causal timestamps (`DateTimeOffset`) for cross-context correlation. Session events carry `SessionId`; metrics events carry `SessionId` as correlation.

### 2. Saga/Process Manager Complexity
**Risk**: Cross-context workflows (e.g., "attempt → mastery update → methodology check") require explicit sagas.
**Mitigation**: Start with 3 simple sagas (attempt→mastery, session→xp, mastery→methodology). Proto.Actor supports saga patterns via `IContext.SpawnNamed`.

### 3. Proto.Actor Supervision
**Risk**: Current `StudentActor` supervision tree assumes one actor per student. Split requires 3 actors per student.
**Mitigation**: Create a `StudentSupervisor` actor that spawns and supervises the 3 child aggregates. Router pattern unchanged from caller's perspective.

### 4. Projection Rebuild Time
**Risk**: Rebuilding projections from split streams takes time during cutover.
**Mitigation**: Pre-build projections from shadow-write streams before cutover. Zero-downtime switchover.

### 5. Marten Stream Limits
**Risk**: Marten event store partitioning may need adjustment for 3x stream count.
**Mitigation**: Already addressed by EVENT-SCALE-001 (async projections + partitioning).

## Implementation Timeline (Post-Pilot)

| Sprint | Work | Estimated Effort |
|--------|------|-----------------|
| Sprint 1 | Define aggregate boundaries + interface contracts | 1 week |
| Sprint 2-3 | Shadow-write implementation + new projections | 2 weeks |
| Sprint 4 | Saga/process manager implementation | 1 week |
| Sprint 5 | Cutover + projection rebuild + validation | 1 week |
| Sprint 6 | Cleanup + old code removal | 1 week |

**Total**: ~6 weeks post-pilot

## Consequences

### Positive
- Independent scalability per context (sessions scale differently than profiles)
- Smaller, testable aggregates with clear boundaries
- Session streams are short-lived (archived after session end) — reduces stream sizes
- Enables future microservice extraction if needed

### Negative
- 3 saga/process managers to maintain
- Cross-context queries require read-model joins (not stream joins)
- Migration effort is non-trivial (~6 weeks)

### Neutral
- Event count per student unchanged (just distributed across streams)
- API contracts unchanged (DTOs aggregate from multiple read models already)

## References
- Vaughn Vernon, "Implementing Domain-Driven Design" (2013) — aggregate boundaries
- Greg Young, "Event Sourcing" — stream-per-aggregate pattern
- Proto.Actor documentation — supervision and saga patterns
- ADR-0001 (Multi-Institute Enrollment) — enrollment context informs profile split
- EVENT-SCALE-001 — event store scaling already addresses partitioning
