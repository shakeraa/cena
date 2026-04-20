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

---

## Schedule Lock — 2026-04-20

Implementation schedule locked by user decision during pre-release review triage 2026-04-20 (see [`/pre-release-review/reviews/SYNTHESIS.md`](../../pre-release-review/reviews/SYNTHESIS.md), task [prr-002](../../tasks/pre-release-review/TASK-PRR-002-adr-0012-studentactor-split-gate-pedagogy-srl-features.md), [EPIC-PRR-A](../../tasks/pre-release-review/EPIC-PRR-A-studentactor-decomposition.md)). Deferral cost is compounding: `StudentActor.Commands.cs` grew from ~1,000L on 2026-04-14 to **1,036L by 2026-04-20**. Further delay makes the split strictly harder.

### Locked sprint schedule

| Sprint | Dates | Work | Status |
|---|---|---|---|
| **1** | 2026-04-27 → 2026-05-03 | Aggregate boundaries + interface contracts; **extract LearningSession first**; TZ fix (prr-157) parallel track | Planned |
| **2-3** | 2026-05-04 → 2026-05-17 | Shadow-write implementation + new projections for LearningSession | Planned |
| **4** | 2026-05-18 → 2026-05-24 | Saga/process manager implementation (attempt→mastery, session→xp) | Planned |
| **5** | 2026-05-25 → 2026-05-31 | Cutover + projection rebuild + validation | Planned |
| **6** | 2026-06-01 → 2026-06-07 | Cleanup + remove old StudentActor code | Planned |

**Total: 6 weeks. Completion: 2026-06-07.**

### First-aggregate decision: LearningSession

Rationale (persona-enterprise recommendation, confirmed by user 2026-04-20):

- **Lowest coupling** — session events are short-lived and scoped; less cross-context integration work in Sprint 1
- **Highest new-feature magnet** — [prr-148](../../tasks/pre-release-review/TASK-PRR-148-student-input-ui-for-adaptivescheduler-deadline-weekly-ti.md) (scheduler inputs), [prr-149](../../tasks/pre-release-review/TASK-PRR-149-live-caller-for-adaptivescheduler-at-session-start.md) (live caller), [prr-013](../../tasks/pre-release-review/TASK-PRR-013-retire-redesign-at-risk-student-alert-under-adr-0003-session.md) (at-risk redesign), [prr-003b](../../tasks/pre-release-review/TASK-PRR-003b-implement-crypto-shredding-for-misconception-events.md) (erasure implementation) all target session scope — extracting LearningSession first lets these features land on the new aggregate, not the god-aggregate
- Extracting **Profile** first would leave the session firehose mixed inside the remaining StudentActor
- Extracting **Metrics** first would strand IRT/BKT work that depends on session events

Profile vs Metrics ordering for Sprint 2+ will be re-picked based on actual Sprint-1 extraction pain. **Default**: Profile second (smaller, lower-risk), Metrics third.

### Enforcement — two architecture tests effective 2026-04-27

1. **`tests/architecture/FileSize500LocTest.cs`** — file-size gate with grandfather whitelist.
   - Grandfathered at 2026-04-20 LOC: `StudentActor.Commands.cs` (1,036L), `StudentActor.cs` (764L), `StudentState.cs` (453L — at limit), `StudentMessages.cs` (430L), `StudentActor.Queries.cs` (363L), `StudentActor.Methodology.cs` (353L), `StudentActor.Mastery.cs` (133L)
   - CI fails if a PR pushes any grandfathered file above its baseline **or** any non-grandfathered file above 500 LOC
   - Grandfathered baselines decrease monotonically — the whitelist value may only ever be lowered by a PR
   - Over time the entire StudentActor tree is expected to disappear from the whitelist as its contents migrate to successor aggregates

2. **`tests/architecture/NoNewStudentActorStateTest.cs`** — "no new event-handler state" gate.
   - Scans `src/actors/Cena.Actors/Students/StudentActor*.cs` for event-apply methods (pattern: `Apply(<Event>)` or `On<Event>`)
   - Baseline event-handler count captured at 2026-04-20; CI fails if PR increases the count
   - Equivalent outcome: no new event type may be handled inside StudentActor; new handlers must land in `LearningSessionActor` (once extracted) or a temporary `StudentActor.Pending` seam documented in PR description

### Cross-epic coordination

- **[prr-155](../../tasks/pre-release-review/TASK-PRR-155-design-consentaggregate-events.md) ConsentAggregate** is designed inside this epic (shares aggregate-design substrate with StudentActor successors). Event schema reviewed by [EPIC-PRR-C](../../tasks/pre-release-review/EPIC-PRR-C-parent-aggregate-consent.md) owner before commit; parent-consent semantics must match.
- **[prr-157](../../tasks/pre-release-review/TASK-PRR-157-fix-tz-infra-before-calendar-features.md) TZ fix** (`FindSystemTimeZoneById("Israel")` throws on Linux per actor-system-review L1) moved to Sprint 1 parallel track — ships before LearningSession extraction so downstream session-start code isn't building on a broken primitive.

### Kill switch

If Sprint 1 reveals the LearningSession extraction fights the 500-LOC rule (e.g. extracted aggregate itself >500L, or splitting reveals a deeper god-object further in), **pause absorbed-feature work** on this epic until the split is re-planned. Escalate to user-architect for re-scope. Do not "make it work" by stubbing or by relaxing the LOC rule — the rule is a non-negotiable, the design is the variable.
