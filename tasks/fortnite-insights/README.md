# Fortnite-Insights Task Tracker

Tasks derived from researching Fortnite's Akka backend architecture and mapping lessons to Cena's Proto.Actor system.

> Research: `research/fortnite-akka/`
> Architecture doc: `docs/resilience-architecture.md`
> Execution plan & review notes: [EXECUTION.md](EXECUTION.md)

---

## Task Summary

| ID      | Task                           | Priority | Effort | Status | Tests | Key Files |
|---------|--------------------------------|----------|--------|--------|-------|-----------|
| RES-001 | Marten write timeouts          | P0       | Low    | DONE   | 3     | `StudentActor.cs` — 2s CancellationToken on SaveChangesAsync, persist_timeout_total metric |
| RES-002 | Observability (Prometheus/Grafana) | P0    | Medium | DONE   | --    | `Program.cs` /metrics endpoint, `config/` Prometheus + Grafana stack (8-panel dashboard) |
| RES-003 | Redis circuit breaker          | P1       | Low    | DONE   | 6     | `CircuitBreakerConfig.Redis` (5/30s), StudentActor CB-aware cache fallback |
| RES-004 | PostgreSQL partitioning        | P2       | Medium | PLAN   | --    | Migration SQL + rollback documented; implement before 10K students |
| RES-005 | Health aggregator actor        | P2       | Medium | DONE   | 8     | `HealthAggregatorActor.cs` — polls CBs + manager, computes SystemHealthLevel |
| RES-006 | Graceful degradation tiers     | P1       | Medium | DONE   | 20    | `DegradationMode.cs` — 4-tier behavior mapping (fallback/buffer/reject/passivate) |
| RES-007 | Profile multiplexing           | P2       | Medium | PLAN   | --    | Sub-stream design documented; implement when adding second subject |
| RES-008 | NATS outbox sweep              | P1       | Low    | DONE   | 4     | `NatsOutboxPublisher.cs` — retry counting, dead-letter after 10 failures |
| RES-009 | Adaptive timeouts              | P3       | Low    | DONE   | 5     | `AdaptiveTimeout.cs` — 1x/1.5x/2x/3x multiplier by health level |
| RES-010 | Feature flag service           | P3       | Medium | DONE   | 6     | `FeatureFlagActor.cs` — 8 default flags, SHA256 rollout bucketing |

**Totals:** 8/10 implemented, 2 planned. 52 new unit tests. 559 total tests passing.

## Execution Order (completed 2026-03-27)

```
Phase 1 (P0):  RES-001 + RES-002  (parallel)  -- DONE  commit 8762d3e
Phase 2 (P1):  RES-003 + RES-008  (parallel)  -- DONE  commit 3baf757
Phase 3 (P1+): RES-005 + RES-006 + RES-009 + RES-010  -- DONE  commit cb1eb7b
Phase 4 (P2):  RES-004 + RES-007  -- PLAN (scale preparation, not needed yet)
```
