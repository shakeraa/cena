# Fortnite-Insights Execution Plan & Review Notes

> Reviewed: 2026-03-27
> Source: `research/fortnite-akka/` | Architecture: `docs/resilience-architecture.md`

---

## Execution Order

```
Phase 1 (P0):  RES-001 + RES-002   (parallel, no dependencies)
Phase 2 (P1):  RES-003 + RES-008   (parallel, narrowed scope for RES-008)
Phase 3 (P1):  RES-005 → RES-006   (sequential, RES-006 blocked on RES-005)
Phase 4 (P2):  RES-004 + RES-007   (parallel, both need design spikes first)
Phase 5 (P3):  RES-009 + RES-010   (parallel, lowest priority)
```

## Dependency Graph

```
RES-001 ─────────────────────────────┐
RES-002 (standalone)                 │
RES-003 (standalone) ──→ RES-005 ──→ RES-006
RES-008 (standalone)        │             │
                            ├─────→ RES-009
RES-004 (standalone, needs design spike)
RES-007 (standalone, needs design spike)
RES-010 (standalone)
```

Hidden dependency: RES-006 Tier 1 requires a pre-built question pool in `CurriculumGraphActor` that does not yet exist.

---

## Per-Task Review Notes

### RES-001: Marten Write Timeouts — P0

- **Status:** Not started, confirmed gap
- **Scope:** Two bare `SaveChangesAsync()` calls in `StudentActor.cs` — `FlushEvents()` and `ForceSnapshot()`
- **Spec fix needed:** Task only mentions `FlushEvents()` — `ForceSnapshot()` also needs the timeout
- **Already done:** `EventPersistLatency` histogram already exists (`_eventPersistLatency`, line 112-113). Only `PersistTimeoutCounter` is missing.
- **Effort:** Low. Wrap two call sites, add one counter, one test.

### RES-002: Observability Stack — P0

- **Status:** Partially implemented — different direction than spec
- **What exists:** OTLP exporter already wired in `Program.cs` via `AddOtlpExporter()` targeting `http://localhost:4317`. All 6 meters registered. OTel packages already referenced.
- **What's missing:** `OpenTelemetry.Exporter.Prometheus.AspNetCore` package, `MapPrometheusScrapingEndpoint()`, observability docker-compose file
- **Decision needed:** Add Prometheus as a second exporter alongside OTLP, or use OTEL Collector → Prometheus? Both are valid. Adding directly is simpler for local dev.
- **Spec fix needed:** Acknowledge existing OTLP pipeline. Don't replace it — add Prometheus alongside.

### RES-003: Redis Circuit Breaker — P1

- **Status:** Not started
- **Implementation path:** `LlmCircuitBreakerActor` is already generic via `CircuitBreakerConfig`. Just add `public static CircuitBreakerConfig Redis => new("redis", 5, TimeSpan.FromSeconds(30));` and register it.
- **Spec fix needed:** Missing detail on how Redis CB PID gets injected into `StudentActor`. Also, activation already loads from Marten (not Redis), so the "Marten fallback on activation" path is already the existing behavior. Real protection needed is on Redis write paths (`InvalidateRedisCache`).

### RES-004: PostgreSQL Partitioning — P2

- **Status:** Not started, correctly deferred
- **Risk:** Marten's `AutoCreateSchemaObjects` may conflict with externally partitioned tables. Must set `AutoCreate = None` post-migration to prevent Marten from recreating/altering the table.
- **Spec fix needed:** Address Marten AutoCreate conflict. Rollback plan must also cover snapshot and projection tables, not just `mt_events`.
- **Gate:** Benchmark single-table first. Only implement when approaching 10K+ active students.

### RES-005: Health Aggregator Actor — P2

- **Status:** Not started, blocked on RES-003
- **Integration point:** `GetCircuitStatus` message already defined and handled in `LlmCircuitBreakerActor` (line 71), so the polling interface is ready for LLM CBs.
- **Existing health:** `ProtoActorHealthCheck` in `Program.cs` (lines 294-322) only checks cluster membership — not CB states.
- **Spec fix needed:** Missing NATS subject name and serialization format for `SystemHealthChanged`. Missing detail on how downstream actors subscribe (NATS subscription vs direct actor messaging).

### RES-006: Graceful Degradation — P1, Effort Underestimated

- **Status:** Not started, blocked on RES-005
- **Effort reality:** Spec says Medium (6-8h), actual is closer to High (12-16h)
- **Tier 2 complexity:** Event buffering across command boundaries requires changing flush semantics in `StudentActor`. The existing `_pendingEvents` list flushes on every command handler — holding events in memory while Marten is slow requires a new flush trigger on recovery. This is architecturally non-trivial.
- **Tier 1 gap:** Pre-built question pool does not exist in `CurriculumGraphActor`. Must be populated before Tier 1 fallback works.
- **Spec fix needed:** Re-estimate effort. Add a sub-task for building the question pool. Detail the event buffering flush-on-recovery mechanism.

### RES-007: Profile Multiplexing — P2, Breaking Change

- **Status:** Not started
- **Migration risk:** No migration path from single-stream to sub-streams. Students with existing single-stream event history need either replay+re-emit into sub-streams, or dual-read support during transition.
- **Field mapping gaps:** Spec adds `FatigueBaseline` but `BaselineFatigueScore` already exists. Spec's `SubjectProfile` omits `MethodAttemptHistory` and `HlrTimers` that are currently per-concept. Silent data loss risk.
- **Spec fix needed:** Design spike required. Add migration plan. Fix field mapping to align with actual `StudentState` fields.

### RES-008: NATS Outbox Sweep — P1, Mostly Solved

- **Status:** Substantially implemented via `NatsOutboxPublisher` (high-water mark polling pattern)
- **What exists:** Events persist to Marten first, catch-up publish to NATS every 5s using `NatsOutboxCheckpoint`. On NATS failure, retries next cycle. Metrics: `cena.outbox.published_total`, `cena.outbox.errors_total`, `cena.outbox.cycles_total`.
- **Remaining gaps:** (1) No dead-letter handling after N retries. (2) Missing `cena.outbox.republished_total` and `cena.outbox.dead_lettered_total` metrics.
- **Spec fix needed:** Do NOT build the separate `cena_outbox` table — the existing HWM approach is simpler and correct. Narrow scope to adding dead-letter after 10 retries + 2 missing metrics.

### RES-009: Adaptive Timeouts — P3

- **Status:** Not started, correctly low-priority
- **Dependency chain:** RES-001 (base timeout) → RES-005 (health level signal) → RES-009
- **Implementation issue:** `LlmCircuitBreakerActor.OpenDuration` is set at construction time from `CircuitBreakerConfig` and is not mutable. Making it adaptive requires either injecting health level into the actor or making `OpenDuration` a computed property.
- **Spec fix needed:** Address the constructor-time `OpenDuration` constraint.

### RES-010: Feature Flag Service — P3

- **Status:** Not started, correctly low-priority
- **Existing kill-switch:** `LlmCircuitBreakerActor.ForceReset` already provides a functional LLM kill-switch. Feature flags add convenience, not a new capability.
- **Spec fix needed:** Specify how `FeatureFlagActor` PID is passed to consuming actors (DI, well-known cluster name, or root-level singleton). Specify admin API controller/route namespace. Address that `session.max_minutes` is currently a compile-time constant — making it a runtime flag adds a synchronous dependency on actor activation.

---

## Summary

| Task    | Status            | Adjusted Effort | Spec Changes Needed |
|---------|-------------------|-----------------|---------------------|
| RES-001 | Not started       | Low             | Add `ForceSnapshot()` to scope |
| RES-002 | Partial (OTLP exists) | Medium      | Acknowledge OTLP, add Prometheus alongside |
| RES-003 | Not started       | Low             | Add PID wiring detail, fix fallback description |
| RES-004 | Not started       | Medium          | Address Marten AutoCreate conflict |
| RES-005 | Not started       | Medium          | Add NATS subject/serialization detail |
| RES-006 | Not started       | High (was Medium) | Re-estimate, add question pool sub-task |
| RES-007 | Not started       | High (was Medium) | Needs design spike, migration plan, field mapping |
| RES-008 | Mostly done       | Low (narrowed)  | Drop table redesign, narrow to dead-letter + metrics |
| RES-009 | Not started       | Low             | Address OpenDuration constructor constraint |
| RES-010 | Not started       | Medium          | Add PID injection, admin API detail |
