# INF-018: NATS Backpressure & Flow Control

**Priority:** P1 — prevents command flood on actor host restart
**Blocked by:** Nothing
**Estimated effort:** 1 day

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic.

## Context

The `NatsBusRouter` has a `MaxConcurrentActivations = 50` semaphore gating NATS-routed commands. But if the actor host restarts while 10,000 NATS messages have accumulated (school peak), all buffered messages flood in simultaneously. The semaphore limits concurrent _processing_ but not _subscription buffer depth_. NATS Core subscriptions have no built-in backpressure — messages arrive as fast as the server sends them.

Additionally, Proto.Actor cluster requests from `Cena.Api.Host` (admin queries, profile lookups) bypass the NATS gate entirely, adding uncontrolled actor activations.

## Subtasks

### INF-018.1: Bounded Subscription Buffers

**Files:**
- `src/actors/Cena.Actors/Bus/NatsBusRouter.cs`

**Acceptance:**
- [ ] Set `NatsSubOpts.PendingMsgsLimit` to 500 per command subscription
- [ ] Set `NatsSubOpts.PendingBytesLimit` to 10MB per subscription
- [ ] When buffer fills, NATS drops oldest messages (slow consumer eviction)
- [ ] Log warning when pending count exceeds 80% of limit
- [ ] Metric: `cena.nats.pending_messages` gauge per subject

### INF-018.2: JetStream Durable Consumers for Commands

**Files:**
- `src/actors/Cena.Actors/Bus/NatsBusRouter.cs` — migrate from Core sub to JetStream consumer
- `src/actors/Cena.Actors.Host/Program.cs` — ensure JetStream streams exist

**Acceptance:**
- [ ] Create JetStream stream `CENA_COMMANDS` with subjects `cena.session.>`, `cena.mastery.>`
- [ ] Retention: `WorkQueue` (each message processed once)
- [ ] Max messages: 50,000 (discard old when full)
- [ ] Consumer: `cena-actor-host` durable push consumer
- [ ] `MaxAckPending = 50` (matches existing semaphore — natural backpressure)
- [ ] AckWait: 30 seconds (re-deliver if not acked)
- [ ] On restart: consumer resumes from last ack position (no message loss, no flood)

### INF-018.3: Proto.Actor Activation Rate Limiter

**Files:**
- `src/actors/Cena.Actors.Host/Program.cs` — configure `ActorSystemConfig`
- `src/actors/Cena.Actors/Infrastructure/ActivationThrottle.cs` — new middleware

**Acceptance:**
- [ ] Limit: max 20 actor activations per second (configurable)
- [ ] Excess activations queued, not rejected
- [ ] Applies to ALL activation paths (NATS-routed + direct cluster requests)
- [ ] Metric: `cena.actor.activations_throttled_total` counter
- [ ] Log warning when throttle queue depth > 50

## Definition of Done
- [ ] `dotnet build` + `dotnet test` pass
- [ ] Simulated restart with 1000 buffered messages: no connection pool exhaustion
- [ ] Actor activation rate stays under configured limit during burst
