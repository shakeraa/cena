# Cena Resilience Architecture

> Derived from Fortnite/Akka backend research (2026-03-27).
> Task tracker: `tasks/fortnite-insights/RES-*.md`

---

## Current State

Cena's actor system already implements several resilience patterns that Fortnite lacked during its 2018 outages:

### What We Have

| Pattern                     | Implementation                                  | File                                    |
|-----------------------------|-------------------------------------------------|-----------------------------------------|
| Per-model circuit breaker   | `LlmCircuitBreakerActor` (Closed→Open→HalfOpen)| `Gateway/LlmCircuitBreakerActor.cs`     |
| Activation backpressure     | 200/sec rate limiter, queue depth 1000          | `Management/StudentActorManager.cs`     |
| Actor pool cap              | 10,000 concurrent actors, ~500KB each           | `Management/StudentActorManager.cs`     |
| Supervision isolation       | OneForOne -- child failure doesn't affect siblings | `Infrastructure/CenaSupervisionStrategies.cs` |
| Event sourcing              | Marten snapshots + event replay                 | `Students/StudentActor.cs`              |
| Distributed messaging       | NATS JetStream (no SPOF)                        | `Infrastructure/NatsOutboxPublisher.cs` |
| Graceful shutdown            | DrainAll with timeout                            | `Management/StudentActorManager.cs`     |

### What We Need (from Fortnite's lessons)

```
┌─────────────────────────────────────────────────────────────────┐
│                    CENA RESILIENCE ROADMAP                       │
│                                                                  │
│  P0 (Immediate)                                                  │
│  ├── RES-001: Marten write timeouts (2s CancellationToken)      │
│  └── RES-002: Observability stack (Prometheus + Grafana)         │
│                                                                  │
│  P1 (Next Sprint)                                                │
│  ├── RES-003: Redis circuit breaker                              │
│  ├── RES-006: Graceful degradation tiers                         │
│  └── RES-008: NATS outbox sweep                                  │
│                                                                  │
│  P2 (Next Milestone)                                             │
│  ├── RES-004: PostgreSQL partitioning by student                 │
│  ├── RES-005: Global health aggregator actor                     │
│  └── RES-007: Profile multiplexing per subject                   │
│                                                                  │
│  P3 (Plan for Scale)                                             │
│  ├── RES-009: Adaptive timeouts under load                       │
│  └── RES-010: Global feature flag service                        │
└─────────────────────────────────────────────────────────────────┘
```

---

## Target Architecture

```
                        CENA RESILIENCE LAYERS
                        ══════════════════════

                    ┌─────────────────────────────┐
                    │   API Gateway / Endpoints    │
                    │   (health check, rate limit) │
                    └──────────────┬───────────────┘
                                   │
                    ┌──────────────▼───────────────┐
                    │    HealthAggregatorActor      │  ← RES-005
                    │    (polls all CBs, computes   │
                    │     system health level)      │
                    └──────────────┬───────────────┘
                                   │ SystemHealthChanged
              ┌────────────────────┼────────────────────┐
              │                    │                     │
   ┌──────────▼──────────┐  ┌─────▼──────┐  ┌──────────▼──────────┐
   │ StudentActorManager  │  │FeatureFlag │  │  Grafana Dashboard  │
   │ (activation control, │  │  Actor     │  │  (alerts, panels)   │
   │  degradation tiers)  │  │ (RES-010)  │  │  (RES-002)          │
   └──────────┬───────────┘  └────────────┘  └─────────────────────┘
              │
   ┌──────────▼───────────────────────────────────────┐
   │              StudentActor (virtual)               │
   │  ┌─────────────┐  ┌──────────┐  ┌─────────────┐ │
   │  │ Marten Write │  │  Redis   │  │  NATS Pub   │ │
   │  │ (2s timeout) │  │  (CB)    │  │  (outbox)   │ │
   │  │  RES-001     │  │  RES-003 │  │  RES-008    │ │
   │  └──────┬───────┘  └────┬─────┘  └──────┬──────┘ │
   │         │               │               │        │
   │  ┌──────▼───────┐  ┌────▼─────┐  ┌──────▼──────┐ │
   │  │ Adaptive     │  │ Fallback │  │ Dead Letter │ │
   │  │ Timeout      │  │ to Marten│  │ after 10    │ │
   │  │ (RES-009)    │  │ on CB    │  │ retries     │ │
   │  └──────────────┘  └──────────┘  └─────────────┘ │
   └───────────────────────────────────────────────────┘
              │
   ┌──────────▼──────────────────────────────┐
   │         LLM Gateway Layer               │
   │  ┌────────┐  ┌────────┐  ┌────────┐    │
   │  │Kimi CB │  │Sonnet  │  │Opus CB │    │
   │  │(5/60s) │  │CB      │  │(2/120s)│    │
   │  │        │  │(3/90s) │  │        │    │
   │  └────────┘  └────────┘  └────────┘    │
   └─────────────────────────────────────────┘
```

---

## Degradation Tiers (RES-006)

| Tier | Health Level | Trigger                         | Response                                               |
|------|-------------|---------------------------------|--------------------------------------------------------|
| 0    | Healthy     | All systems normal              | Full LLM-powered pedagogy                              |
| 1    | Degraded    | 1 LLM CB open                  | Pre-built question pools, BKT still tracks             |
| 2    | Critical    | DB latency >500ms OR 2+ CBs    | Read-only from Redis, buffer events in memory           |
| 3    | Emergency   | Memory >80% OR 3+ CBs OR Marten down | Reject new sessions, drain active, cached dashboards |

---

## Dependency Graph

```
RES-001 (Marten timeouts)     ← standalone, do first
RES-002 (Observability)       ← standalone, do first
RES-003 (Redis CB)            ← standalone
RES-008 (NATS outbox)         ← standalone
RES-005 (Health aggregator)   ← depends on RES-003 (watches Redis CB)
RES-006 (Degradation tiers)   ← depends on RES-005 (reads health level)
RES-009 (Adaptive timeouts)   ← depends on RES-001 + RES-005
RES-004 (PG partitioning)     ← standalone, benchmark first
RES-007 (Profile multiplex)   ← standalone, architecture change
RES-010 (Feature flags)       ← standalone
```

Recommended execution order:
1. RES-001 + RES-002 (parallel, P0)
2. RES-003 + RES-008 (parallel, P1)
3. RES-005 (needs RES-003)
4. RES-006 (needs RES-005)
5. RES-004 + RES-007 + RES-009 + RES-010 (parallel, P2/P3)

---

## Key Metrics to Monitor

| Metric                                | Source                    | Alert Threshold         |
|---------------------------------------|--------------------------|------------------------|
| `cena.student.event_persist_ms` p99   | StudentActor             | >100ms                 |
| `cena.student.persist_timeout_total`  | StudentActor (RES-001)   | Any increment          |
| `cena.llm.circuit_opened_total`       | LlmCircuitBreakerActor   | Any increment          |
| `cena.llm.requests_rejected_total`    | LlmCircuitBreakerActor   | >10/min                |
| `cena.redis.circuit_opened_total`     | Redis CB (RES-003)       | Any increment          |
| `cena.outbox.republished_total`       | OutboxSweep (RES-008)    | >100/min (NATS issues) |
| `cena.outbox.dead_lettered_total`     | OutboxSweep (RES-008)    | Any increment          |
| Active actor count                    | StudentActorManager      | >8,000 (80% cap)       |
| System health level                   | HealthAggregator (RES-005)| >= Degraded            |
