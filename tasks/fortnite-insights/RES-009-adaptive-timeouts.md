# RES-009: Adaptive Timeouts Under Load

| Field         | Value                                        |
|---------------|----------------------------------------------|
| **Priority**  | P3 -- Low urgency, plan for scale            |
| **Effort**    | Low (2-3 hours)                              |
| **Impact**    | Low now, High at scale                       |
| **Origin**    | Fortnite April 2018: 200ms socket timeout caused connection storms that amplified failure |
| **Status**    | TODO                                         |
| **Execution** | See [EXECUTION.md](EXECUTION.md#res-009-adaptive-timeouts--p3) |

---

## Problem

Static timeouts are dangerous under load. Fortnite's 200ms MongoDB socket timeout seemed reasonable, but under pressure it caused rapid disconnect/reconnect churn that amplified the failure into a storm.

Cena's current timeouts are static: LlmCircuitBreakerActor has fixed `OpenDuration`, and RES-001 will add a fixed 2s Marten timeout. These should become adaptive.

## Design

### Adaptive Timeout Strategy

```
Normal load:     base timeout (e.g., 2s for Marten)
Elevated load:   base * 1.5 (e.g., 3s)
High load:       base * 2.0 (e.g., 4s)
Critical load:   base * 3.0 (e.g., 6s)
```

Load level determined by:
- Actor pool utilization (from StudentActorManager)
- Recent p99 latency (from EventPersistLatency histogram)
- Circuit breaker states (from HealthAggregator)

### Implementation

```csharp
public static class AdaptiveTimeout
{
    public static TimeSpan Calculate(TimeSpan baseTimeout, SystemHealthLevel health)
    {
        var multiplier = health switch
        {
            SystemHealthLevel.Healthy   => 1.0,
            SystemHealthLevel.Degraded  => 1.5,
            SystemHealthLevel.Critical  => 2.0,
            SystemHealthLevel.Emergency => 3.0,
            _ => 1.0
        };
        return TimeSpan.FromMilliseconds(baseTimeout.TotalMilliseconds * multiplier);
    }
}
```

This is intentionally simple. Under load, slightly slower is better than rapid failure churn.

## Dependencies

- RES-001 (Marten write timeouts) -- provides the base timeout to adapt
- RES-005 (Health Aggregator) -- provides the health level signal

## Acceptance Criteria

- [ ] `AdaptiveTimeout.Calculate` utility implemented
- [ ] StudentActor uses adaptive timeout for Marten writes
- [ ] LlmCircuitBreakerActor uses adaptive OpenDuration
- [ ] Unit test: verify multiplier at each health level
