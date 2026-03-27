# Fortnite-Insights Task Tracker

Tasks derived from researching Fortnite's Akka backend architecture and mapping lessons to Cena's Proto.Actor system.

> Research: `research/fortnite-akka/`
> Architecture doc: `docs/resilience-architecture.md`
> Execution plan & review notes: [EXECUTION.md](EXECUTION.md)

---

## Task Summary

| ID      | Task                           | Priority | Effort | Status | Depends On |
|---------|--------------------------------|----------|--------|--------|------------|
| RES-001 | Marten write timeouts          | P0       | Low    | TODO   | --         |
| RES-002 | Observability (Prometheus/Grafana) | P0    | Medium | TODO   | --         |
| RES-003 | Redis circuit breaker          | P1       | Low    | TODO   | --         |
| RES-004 | PostgreSQL partitioning        | P2       | Medium | TODO   | --         |
| RES-005 | Health aggregator actor        | P2       | Medium | TODO   | RES-003    |
| RES-006 | Graceful degradation tiers     | P1       | Medium | TODO   | RES-005    |
| RES-007 | Profile multiplexing           | P2       | Medium | TODO   | --         |
| RES-008 | NATS outbox sweep              | P1       | Low    | TODO   | --         |
| RES-009 | Adaptive timeouts              | P3       | Low    | TODO   | RES-001, RES-005 |
| RES-010 | Feature flag service           | P3       | Medium | TODO   | --         |

## Execution Order

```
Phase 1 (P0):  RES-001 + RES-002  (parallel)
Phase 2 (P1):  RES-003 + RES-008  (parallel)
Phase 3 (P1):  RES-005 → RES-006  (sequential)
Phase 4 (P2):  RES-004 + RES-007  (parallel)
Phase 5 (P3):  RES-009 + RES-010  (parallel)
```
