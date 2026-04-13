# RDY-008: Aggregate Decomposition ADR (StudentActor → 3 Bounded Contexts)

- **Priority**: Critical — architectural debt, blocks production scale
- **Complexity**: Architect — ADR authoring (implementation deferred to post-pilot)
- **Source**: Expert panel audit — Dina (Architecture), Oren (API)
- **Tier**: 1 (ADR now, implementation post-pilot)
- **Effort**: 3 days (ADR only)

## Problem

`StudentActor` handles 65+ event types across learner profile, sessions, pedagogy, engagement, outreach, focus, and challenges — all in one event stream. This is a "god aggregate" that:
- Makes state transitions impossible to reason about
- Forces all projections to handle all event types
- Creates contention at scale (all writes to one stream per student)
- Prevents independent deployment of session vs. profile concerns

**Panel agreement**: Acceptable for 1-2 school pilot. Mandatory split before production scale (10K+ students).

## Scope

### 1. Author ADR documenting target decomposition

Proposed bounded contexts:

| Context | Events | Aggregate Root | Stream |
|---------|--------|----------------|--------|
| **StudentProfile** | Enrolled, ProfileUpdated, PreferencesChanged, ConsentGranted/Revoked, MethodologySwitched | StudentProfileAggregate | `student-profile-{id}` |
| **LearningSession** | SessionStarted, ConceptAttempted, HintRequested, AnswerRevealed, SessionEnded, ScaffoldingLevelChanged | LearningSessionAggregate | `session-{sessionId}` |
| **StudentMetrics** | MasteryUpdated, DecayApplied, XpAwarded, BadgeUnlocked, StreakUpdated, FatigueAssessed | StudentMetricsAggregate | `student-metrics-{id}` |

### 2. Document migration strategy

- Event stream splitting approach (shadow-write to new streams, cutover)
- Projection migration (rebuild from split streams)
- Backward compatibility (read from both old and new streams during transition)
- Snapshot migration (StudentProfileSnapshot → per-context snapshots)

### 3. Document risk assessment

- Risk of event ordering guarantees across contexts
- Need for sagas/process managers for cross-context workflows
- Proto.Actor implications (separate actor types, separate supervision trees)

## Files to Create

- New: `docs/adr/0012-aggregate-decomposition.md` — ADR with decision, consequences, migration plan
- Update: `docs/tasks/readiness/READINESS-PANEL-AUDIT.md` — link to ADR

## Acceptance Criteria

- [ ] ADR authored with target decomposition (3 contexts)
- [ ] Migration strategy documented (shadow-write → cutover)
- [ ] Risk assessment completed (ordering, sagas, supervision)
- [ ] Implementation timeline estimated (post-pilot, sprint-by-sprint)
- [ ] Current StudentActor event types mapped to target contexts
- [ ] ADR reviewed and approved
