# EPIC-E2E-J — Resilience & failure modes

**Status**: Proposed
**Priority**: P1 (graceful degradation; a broken peripheral shouldn't take the app down)
**Related**: [EPIC-PRR-L](../pre-release-review/EPIC-PRR-L-observability-completion.md), RATE-001, RDY-045, RDY-064

---

## Why this exists

The product integrates with many peripherals (Stripe, Firebase, SymPy sidecar, LLM providers, NATS, Redis, SMTP, WhatsApp). Each has its own failure mode. We ship "graceful-disabled" defaults (SmtpEmailSender → Null, Sentry → Null, etc.) but **testing that the disable path actually works** is the only way to know it will when you need it at 3 AM.

## Workflows

### E2E-J-01 — SymPy sidecar down → CAS gate shadow mode fails open (configurable)

**Journey**: sidecar stopped mid-suite → next LLM response triggers CAS verify → circuit breaker trips → admin UI shows `CasOracleDegraded` warning → SUBJECT-matter depends on `CENA_CAS_GATE_MODE`:
  - `enforce`: student sees "I need to double-check this" fallback; no math surfaces
  - `shadow`: student sees the answer + a shadow-warning in admin only

**Boundaries**: DOM student-side (correct fallback copy), admin surface shows degraded-state badge, bus (`CasOracleDegradedV1`).

**Regression caught**: enforce mode silently falls open; shadow mode doesn't warn admin.

### E2E-J-02 — LLM provider 429 → cost circuit breaker trips

**Journey**: LLM provider returns 429 → ICostCircuitBreaker trips for N seconds → subsequent requests fail fast with quota-message UI → breaker auto-recovers after cooldown.

**Boundaries**: DOM (quota UI), admin surface (breaker state visible), bus (`LlmQuotaTrippedV1`), metrics (tripped duration).

**Regression caught**: breaker doesn't recover; breaker trips for wrong reason (mistook 500 for 429); cost dashboard doesn't reflect the 429s.

### E2E-J-03 — NATS down → outbox buffers events

**Journey**: NATS stopped → student completes a session → answers logged → NATS-outbox queues events → NATS restarts → outbox drains → SignalR / parent-digest catches up.

**Boundaries**: DB NatsOutbox rows accumulate during outage, zero on restart, no duplicate deliveries, eventual-consistency window < 30s.

**Regression caught**: outbox bypass on some write path (lost event); outbox duplicates on restart; NATS-down surfaces as a hard 500 to students.

### E2E-J-04 — Firebase Auth emu down → existing sessions continue

**Journey**: Firebase emulator stopped → existing signed-in session continues (cached token still valid) → new sign-in attempts fail fast with a friendly "sign-in temporarily unavailable" → emulator restart → new sign-ins work.

**Boundaries**: DOM (distinct UI for retryable vs permanent errors), existing signed-in user's `/home` keeps rendering, new sign-ins queue retry with exponential backoff.

**Regression caught**: existing session gets signed out on Firebase outage (terrible UX); sign-in retries hammer the emulator (denial-of-self).

### E2E-J-05 — Stripe webhook replay idempotency

**Journey**: see EPIC-E2E-B-10. Moved here for the multi-webhook-burst variant: 5 webhook deliveries in 100ms of the same event → DB shows exactly 1 activation event; Stripe retry strategy respected.

### E2E-J-06 — SMTP down → parent digest goes to WhatsApp-only or holding queue

**Journey**: SMTP unreachable → parent-digest channel dispatcher → falls back to WhatsApp if opted in, else holds + retries with exponential backoff → SMTP recovers → held digests deliver.

**Boundaries**: DB DigestHoldingQueue row, bus (`DigestDeliveryDelayedV1`), after recovery zero rows.

**Regression caught**: digest silently dropped (lost communication); fallback picks wrong channel; holding queue grows unbounded.

### E2E-J-07 — Redis down → rate limiter fail-closed

**Journey**: Redis stopped → rate-limit check call fails → policy decision: fail-closed (no reads/writes). Students see "service temporarily unavailable" not an infinite spinner.

**Boundaries**: backend returns 503 with Retry-After, not 500 or hanging; ops alert fired.

**Regression caught**: fail-open (unlimited requests until Redis restored — DoS vector); silent hang until timeout.

### E2E-J-08 — Per-student rate limit kicks in (RATE-001)

**Journey**: student fires >N requests / 10s → rate limiter rejects with 429 + Retry-After → UI shows "Slow down" toast → student waits → requests succeed again.

**Boundaries**: DB rate-limit bucket state, DOM toast, admin dashboard reflects the tripping.

**Regression caught**: limit not enforced per-student (global limiter could DoS one student for the whole tenant); limiter leaks across tenants.

### E2E-J-09 — SignalR reconnection (hub drop mid-session)

**Journey**: student mid-session → SignalR connection drops (simulate via network-blocker) → SPA auto-reconnects with last-seen message id → missed events replay → session continues without state loss.

**Boundaries**: DOM (brief "reconnecting..." toast, then back to normal), client's last-seen-message-id honored server-side, no duplicate events.

**Regression caught**: session hangs after reconnect; duplicate events cause UI thrash; reconnect without token refresh → 401 loop.

## Out of scope

- Hard kernel-level failures (OOM, disk full) — belongs in infra chaos tests, not E2E
- Cross-region failover — we're single-region

## Definition of Done

- [ ] 9 workflows green
- [ ] J-01 (CAS down) and J-05 (webhook replay) tagged `@resilience @p0`
- [ ] Chaos-inject helper (`probes/chaos.ts`) stops/starts compose services cleanly between specs
- [ ] Each spec verifies **recovery**, not just the outage state
