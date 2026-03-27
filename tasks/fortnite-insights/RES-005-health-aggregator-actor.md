# RES-005: Global Health Aggregator Actor

| Field         | Value                                        |
|---------------|----------------------------------------------|
| **Priority**  | P2 -- Next milestone                         |
| **Effort**    | Medium (4-6 hours)                           |
| **Impact**    | Medium -- enables coordinated degradation    |
| **Origin**    | Fortnite had no coordinated response when multiple systems failed simultaneously |
| **Status**    | TODO                                         |

---

## Problem

Cena has per-model circuit breakers (LLM) and activation backpressure (StudentActorManager), but no global health signal. If Redis, NATS, and an LLM model all degrade simultaneously, each component handles failure independently. No coordinated degradation response.

Fortnite's cascading failures in 2018 showed that independent circuit breakers are not enough -- you need a system-level health coordinator.

## Design

### HealthAggregatorActor

A singleton actor that polls all circuit breakers and infrastructure health:

```
HealthAggregatorActor (singleton)
  ├── polls every 5s:
  │     LlmCB(kimi).GetCircuitStatus
  │     LlmCB(sonnet).GetCircuitStatus
  │     LlmCB(opus).GetCircuitStatus
  │     RedisCB.GetCircuitStatus          (RES-003)
  │     NatsCB.GetCircuitStatus           (future)
  │     StudentActorManager.GetManagerMetrics
  │
  ├── computes: SystemHealthLevel (enum)
  │     Healthy      = 0 CBs open, metrics normal
  │     Degraded     = 1 CB open OR pool >70%
  │     Critical     = 2+ CBs open OR pool >90%
  │     Emergency    = 3+ CBs open OR Marten unreachable
  │
  └── publishes: SystemHealthChanged event to NATS
        (consumed by StudentActorManager, API gateway, dashboards)
```

### Degradation Tiers (from Fortnite research)

| Tier | Level     | Trigger                    | System Response                                  |
|------|-----------|----------------------------|--------------------------------------------------|
| 0    | Healthy   | Normal                     | Full features                                    |
| 1    | Degraded  | 1 LLM CB open              | Fallback to simpler pedagogy (pre-built items)   |
| 2    | Critical  | DB latency >500ms          | Read-only mode (serve from Redis cache)          |
| 3    | Emergency | Memory >80% OR 3+ CBs open | Aggressive passivation, reject new sessions      |

### Messages

```csharp
public sealed record GetSystemHealth;
public sealed record SystemHealthResponse(
    SystemHealthLevel Level,
    Dictionary<string, CircuitState> CircuitBreakers,
    int ActiveActors,
    int MaxActors,
    double MemoryUtilizationPercent);

public enum SystemHealthLevel { Healthy, Degraded, Critical, Emergency }
```

## Affected Files

- New: `src/actors/Cena.Actors/Infrastructure/HealthAggregatorActor.cs`
- Modify: `src/actors/Cena.Actors/Management/StudentActorManager.cs` -- respect health level
- Modify: `src/actors/Cena.Actors.Host/Program.cs` -- register singleton

## Acceptance Criteria

- [ ] `HealthAggregatorActor` polls all CBs every 5 seconds
- [ ] `SystemHealthLevel` computed correctly from CB states + metrics
- [ ] `SystemHealthChanged` event published to NATS on level transitions
- [ ] `StudentActorManager` rejects new activations at Emergency level
- [ ] Grafana panel showing system health level over time
- [ ] Unit test: mock 3 open CBs → verify Emergency level
- [ ] Unit test: all CBs closed → verify Healthy level
