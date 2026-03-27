# RES-006: Graceful Degradation Tiers

| Field         | Value                                        |
|---------------|----------------------------------------------|
| **Priority**  | P1 -- High impact, builds on RES-005         |
| **Effort**    | Medium (6-8 hours)                           |
| **Impact**    | High -- Fortnite was all-or-nothing, system either worked 100% or was fully down |
| **Origin**    | Fortnite had zero degradation path at 3.4M CCU. When things failed, everything failed. |
| **Status**    | TODO (depends on RES-005)                    |

---

## Problem

Cena currently has no defined degradation path. If LLMs are unavailable, sessions fail. If Marten is slow, actors block. There's no middle ground between "fully operational" and "broken."

## Design

### Degradation Tier Responses

| Tier | Health Level | What Degrades                          | What Still Works                          |
|------|-------------|----------------------------------------|-------------------------------------------|
| 0    | Healthy     | Nothing                                | Everything                                |
| 1    | Degraded    | LLM-generated questions unavailable    | Pre-built question pools, BKT tracking, session management |
| 2    | Critical    | No new event persistence               | Read from Redis cache, serve cached state, continue active sessions |
| 3    | Emergency   | No new sessions                        | Active sessions finish gracefully, cached dashboards |

### Implementation Points

#### StudentActor: LLM Fallback (Tier 1)

When `HealthAggregatorActor` reports Degraded:
- `LearningSessionActor` switches from LLM-generated questions to pre-built question pool
- BKT and fatigue tracking continue normally
- Student experience is slightly worse but uninterrupted

#### StudentActor: Cache-Only Mode (Tier 2)

When health is Critical:
- Skip Marten writes (buffer events in memory, flush when recovered)
- Serve state from Redis cache
- Accept potential data loss of buffered events (bounded by buffer size)

#### StudentActorManager: Reject New Sessions (Tier 3)

When health is Emergency:
- Reject `ActivateStudent` with friendly error: "System under maintenance"
- Allow active sessions to complete naturally
- Aggressive passivation of idle actors

### LearningSessionActor Changes

```csharp
// In HandleNextQuestion:
if (_healthLevel >= SystemHealthLevel.Degraded)
{
    // Fallback: serve pre-built question from curriculum graph
    var fallbackQuestion = await _curriculumGraph.GetPreBuiltQuestion(_subject, _currentConcept);
    context.Respond(new QuestionPresented(fallbackQuestion));
}
else
{
    // Normal: generate via LLM
    var llmQuestion = await GenerateLlmQuestion(...);
    context.Respond(new QuestionPresented(llmQuestion));
}
```

## Affected Files

- `src/actors/Cena.Actors/Sessions/LearningSessionActor.cs` -- fallback question source
- `src/actors/Cena.Actors/Students/StudentActor.cs` -- cache-only mode, event buffering
- `src/actors/Cena.Actors/Management/StudentActorManager.cs` -- reject at Emergency
- `src/actors/Cena.Actors/Graph/CurriculumGraphActor.cs` -- pre-built question pool
- New: `src/actors/Cena.Actors/Infrastructure/DegradationMode.cs` -- shared enum/state

## Dependencies

- RES-005 (Health Aggregator Actor) must be implemented first
- Pre-built question pool must exist in curriculum data

## Acceptance Criteria

- [ ] Tier 1: LLM unavailable → sessions continue with pre-built questions
- [ ] Tier 2: Marten slow → events buffered in memory, reads from cache
- [ ] Tier 3: Emergency → new sessions rejected, active sessions drain
- [ ] Recovery: when health improves, buffered events flushed to Marten
- [ ] Integration test: simulate each tier, verify correct behavior
- [ ] No data loss during Tier 1 degradation
- [ ] Bounded data loss during Tier 2 (max N buffered events)
