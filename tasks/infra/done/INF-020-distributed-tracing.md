# INF-020: End-to-End Distributed Tracing Across NATS Hops

**Priority:** P2 — observability for production debugging
**Blocked by:** INF-009 (Grafana/OTEL setup)
**Estimated effort:** 2 days

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic.

## Context

Individual components have OpenTelemetry spans (StudentActor has `ActivitySource`, LLM calls are traced), but the full request lifecycle is **not connected**:

```
Student App → NATS command → NatsBusRouter → Proto.Actor activation
  → Marten event flush → NATS event publish → SignalR push → Browser
```

NATS doesn't propagate W3C `traceparent` headers by default. Each NATS hop starts a new trace. This makes it impossible to debug "student X waited 5 seconds for their answer" — you'd need to correlate 4 separate traces manually.

## Subtasks

### INF-020.1: Trace Context Propagation in NATS Messages

**Files:**
- `src/actors/Cena.Actors/Bus/NatsTraceContext.cs` — new: W3C traceparent inject/extract helpers
- `src/actors/Cena.Actors/Bus/NatsBusRouter.cs` — extract traceparent from inbound, set Activity.Current
- `src/actors/Cena.Actors/Sessions/SessionNatsPublisher.cs` — inject traceparent in outbound events

**Acceptance:**
- [ ] Outbound NATS messages include `traceparent` header (W3C format)
- [ ] Outbound NATS messages include `tracestate` header (if present)
- [ ] Inbound NATS handler extracts headers and creates `Activity` with correct parent
- [ ] Full trace visible: `NATS publish → NatsBusRouter → StudentActor → Marten flush → NATS event`
- [ ] If no traceparent in message: start a new trace (backward compatible)

### INF-020.2: Emulator Trace Injection

**Files:**
- `src/emulator/` — inject traceparent in NATS commands from emulator

**Acceptance:**
- [ ] Each emulated student session gets a unique trace ID
- [ ] All commands within a session share the same trace ID (different span IDs)
- [ ] Trace spans: `emulator.start_session`, `emulator.attempt_concept`, `emulator.end_session`

### INF-020.3: Admin API → NATS Request/Reply Tracing

**Files:**
- `src/api/Cena.Admin.Api/AdminDashboardService.cs` — propagate trace context in NATS requests
- `src/actors/Cena.Actors/Bus/NatsBusRouter.cs` — extract/respond with trace context

**Acceptance:**
- [ ] Admin API HTTP request → NATS request → Actor response → HTTP response is ONE trace
- [ ] Grafana Tempo shows full waterfall: `HTTP GET /api/admin/dashboard/student/{id}` → `NATS request.student.profile` → `StudentActor.GetProfile` → `Marten.QuerySession`
- [ ] Latency breakdown visible per hop

## Definition of Done
- [ ] `dotnet build` + `dotnet test` pass
- [ ] End-to-end trace visible in Grafana Tempo for a student session lifecycle
- [ ] NATS hops are connected (single trace ID across publish/subscribe boundaries)
