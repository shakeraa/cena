# ADR-0053 — External-integration adapter pattern

- **Status**: Accepted
- **Date**: 2026-04-21
- **Decision Makers**: Shaker (project owner), persona-enterprise, persona-sre
- **Task**: prr-024
- **Related**: [ADR-0001](0001-multi-institute-enrollment.md) (tenancy),
  [ADR-0038](0038-event-sourced-right-to-be-forgotten.md) (RTBF),
  [ADR-0046](0046-httponly-cookie-session.md) (session transport)

---

## Context

Cena integrates with several third-party systems that are outside our trust
and control boundary:

| System | Kind | Failure modes |
|---|---|---|
| **Mashov** (IL schools' grades portal) | REST + auth handshake, rate-limited | Flaky DNS, 5xx spikes, auth token expiry mid-flight |
| **Google Classroom** | REST + OAuth2 per-school | Scope revocation, webhook replay storms |
| **Firebase Auth** | SSO token issuance | Clock skew, service outages |
| **Twilio** (SMS parent digest) | REST + webhook | Phone number churn, regional outages |
| **Anthropic / OpenRouter** (LLM) | REST, token metering | Soft rate limits, billable error paths |
| **Apple/Google IAP** (future) | webhook + signed receipts | Replay attacks, server-to-server deltas |

Today each integration has its own in-line code with its own retry policy
(or none), its own error propagation (or none), and its own logging. This
violates three project rules:

1. **No stubs in prod** (banned 2026-04-11): "Phase 1 stub → Phase 1b real"
   is how Mashov sync shipped, and it still has incomplete retry. Retire.
2. **Labels match data**: when a Mashov call fails and the UI renders
   "grades not synced", the DB column still reads `last_sync_success=true`.
   Root cause: the adapter does not have a single source of truth for its
   own health.
3. **Runbook-ready**: at 03:00 on a Bagrut exam morning, ops needs one
   place to see "which third parties are down". Today ops pages per
   integration folder.

## Decision

Every external integration lives behind a typed **adapter** that implements
a canonical `IExternalAdapter<TRequest, TResponse>` contract and applies a
**policy bundle** of:

1. **Timeout** (per-call hard ceiling)
2. **Retry** (bounded, with exponential backoff + jitter)
3. **Circuit breaker** (open / half-open / closed)
4. **Dead-letter queue** (NATS topic `cena.dlq.ext.<adapter-name>`)
5. **Staleness badge** (for data-read adapters; see below)
6. **Structured logging** (tenant, institute, correlation id, adapter name)
7. **Metrics** (latency histogram, success/failure counters, circuit state)

### Adapter contract

```csharp
public interface IExternalAdapter<TRequest, TResponse>
{
    string Name { get; }                    // "mashov", "classroom", "twilio", etc.
    ExternalAdapterHealth Health { get; }   // Closed | HalfOpen | Open
    Task<ExternalResult<TResponse>> InvokeAsync(
        TRequest request,
        AdapterContext ctx,
        CancellationToken ct);
}

public sealed record AdapterContext(
    string TenantId,       // school_id or institute_id (ADR-0001)
    string? CorrelationId, // request-scoped correlation
    string? OperatorId);   // who-triggered; null if system

public sealed record ExternalResult<TResponse>(
    bool IsSuccess,
    TResponse? Value,
    AdapterError? Error,
    DateTimeOffset? DataAsOfUtc);   // for read adapters: when was the value fresh
```

### Policy bundle: default + override

```csharp
public sealed record AdapterPolicy(
    TimeSpan RequestTimeout       = default, // default 10s
    int MaxRetries                = 3,
    TimeSpan BaseBackoff          = default, // default 500 ms
    TimeSpan MaxBackoff           = default, // default 30 s
    int CircuitBreakerThreshold   = 5,       // failures before open
    TimeSpan CircuitBreakerWindow = default, // default 60 s
    TimeSpan HalfOpenProbeDelay   = default, // default 30 s
    TimeSpan StalenessWarnAfter   = default, // default 5 min
    TimeSpan StalenessDangerAfter = default); // default 15 min
```

Each adapter declares its policy at registration time; the defaults are only
the fallback. Mashov (slow, rate-limited) overrides to `RequestTimeout=30s`,
`MaxRetries=5`. Twilio (SMS — don't retry, don't double-charge) overrides to
`MaxRetries=0`.

### Retry + backoff

**Bounded exponential backoff with full jitter** (Amazon 2015 recipe):

```
delay_i = min(MaxBackoff, random(0, BaseBackoff * 2^i))
```

Retries ONLY on:

- Network failures (DNS, TCP reset, TLS handshake)
- HTTP 502, 503, 504, 408, 429 (with `Retry-After` honoured when present)
- Provider-specific "transient" classifier (each adapter declares which
  provider error codes are transient)

**Never retry on** 4xx (client error), auth failures, `IsSuccess=false` with
structured "permanent" class.

### Circuit breaker

Per-adapter, per-tenant. State:

- **Closed**: requests flow normally.
- **Open**: N consecutive failures within window → reject with
  `AdapterError(kind=CircuitOpen)` without making the call. Records an
  immediate telemetry tick.
- **Half-open**: after `HalfOpenProbeDelay`, a single probe request is
  allowed. Success → Closed. Failure → Open with reset timer.

Per-tenant breakers prevent one misconfigured institute from poisoning
Mashov for everyone.

### Dead-letter queue

Adapters that exhaust retries publish the failing request + error envelope to
`cena.dlq.ext.<adapter-name>`. The DLQ consumer is the **ops queue** (see
prr-034 for the review-board pattern; integration DLQ reuses the same UI
shell). Fields captured:

- `AdapterName`, `TenantId`, `CorrelationId`, `OperatorId`
- Request payload (redacted per-adapter redaction policy — PII, tokens)
- Full retry trace (attempt count, delays, error per attempt)
- `EnqueuedAt`, `AdapterVersion`

DLQ alerts page ops on depth > `AdapterPolicy.DlqPageThreshold` per minute
(default 10).

### Staleness badge (for read adapters)

A read adapter returns `DataAsOfUtc` on every `ExternalResult`. The UI / API
consumer MUST surface:

- `DataAsOfUtc < now - StalenessDangerAfter` → red badge, "data may be
  stale" banner, **actions requiring fresh data blocked**.
- `DataAsOfUtc < now - StalenessWarnAfter` → yellow badge, warning banner,
  actions permitted.
- Otherwise → no badge.

Current Mashov reality: we sometimes serve yesterday's grades and the UI
lies that they are current. Staleness badge is non-negotiable for any
read-through adapter.

### Observability

Every adapter emits, per call:

| Dimension | Why |
|---|---|
| Adapter name | Which integration |
| Tenant id (school/institute) | Which tenant |
| Correlation id | Cross-hop tracing |
| Operator id (if any) | Who triggered |
| Outcome (`success`, `retry`, `dlq`, `circuit-open`) | State machine |
| Latency (ms) | SLO |
| Error kind (if any) | Classification |

Prometheus: `cena_external_adapter_calls_total{adapter,tenant,outcome}`,
`cena_external_adapter_latency_ms_bucket{adapter,tenant}`,
`cena_external_adapter_circuit_state{adapter,tenant}`.

### Tenancy (ADR-0001) at the adapter

The adapter MUST not accept tenant-ambiguous requests. `AdapterContext`
requires a `TenantId`. If an adapter implementation needs a per-tenant auth
token (Classroom OAuth), the token is loaded via a `IAdapterSecretStore`
keyed by `(TenantId, AdapterName)`. Secrets are NEVER inlined in request
payloads.

### PII handling

Adapter payloads may contain PII (Mashov: grades, student names; Twilio:
phone numbers). The DLQ redaction policy is per-adapter and MUST be
declared at registration. The arch-test `AdapterDlqHasRedactionPolicyTest`
fails build if any registered adapter has no declared redaction policy.

### RTBF (ADR-0038)

Adapter DLQ entries are indexed by `StudentId` where applicable. RTBF
deletes the student's DLQ entries by tombstoning (same as event store).
Redacted entries retain the adapter-level shape (for SLO stats) but not the
student identity.

## Rationale

### Why one pattern instead of per-provider SDKs?

Per-provider SDKs drift. Mashov has its own retry; Twilio its own. Three
years in, we would have three different circuit-breaker semantics. One
pattern + typed wrappers around the SDKs keeps ops runbook and alerting
symmetrical.

### Why Polly under the hood (or equivalent)?

`src/shared/Cena.Infrastructure/Resilience/HttpPolicies.cs` already uses
Polly. The adapter base class wraps Polly; individual adapters do not
re-implement backoff/breaker logic. This ADR formalises the current pattern
so future contributors do not reinvent.

### Why DLQ over a database retry queue?

NATS DLQ is the same transport as every other Cena bus message. One
observability surface (NATS consumer lag) alerts on it. Database retry
queues require a second durability story.

### 03:00 Bagrut morning runbook

Symptom: grades-missing banners everywhere.

Steps:

1. `GET /api/admin/external/mashov/health` — circuit state, last N call
   outcomes, DLQ depth.
2. If circuit open: check Mashov's published status page. If Mashov is
   down, staleness-danger banner fires automatically and student-facing
   actions that need fresh grades block. No ops action needed — communicate
   externally.
3. If circuit closed but DLQ growing: `GET
   /api/admin/external/mashov/dlq?tenant=<school>` — inspect redacted
   payloads for per-tenant config problems (wrong school code, expired
   token).
4. `POST /api/admin/external/mashov/circuit/force-close` — operator reset
   if breaker tripped on a transient that has recovered.
5. Never reach into the DLQ manually with SQL; use the admin endpoints so
   RTBF and audit stay correct.

## Consequences

### Positive

- One retry model, one breaker model, one DLQ pattern across all third
  parties.
- Staleness badges are mandated for read adapters — UI cannot silently lie.
- Tenant-scoped breakers protect cross-tenant blast radius.
- Ops queue reuses saga-DLQ + cultural-context-DLQ UI.

### Negative

- Migration work: existing inline integrations (Mashov, Twilio, Firebase)
  need to be rewrapped. One adapter per week cadence is realistic.
- Adapter policy defaults require per-adapter tuning; wrong defaults mask
  real failures.

### Neutral

- Adds `Cena.Infrastructure.ExternalAdapters` namespace.
- Adapter tests run against sandbox endpoints + recorded VCR cassettes, not
  mocks.

## Implementation seams

- **Base class**: `Cena.Infrastructure.ExternalAdapters.ExternalAdapterBase<TReq, TRes>`
  (to be added — not in this ADR's scope).
- **Policy**: `AdapterPolicy` record (above).
- **DLQ producer**: shared helper that publishes to
  `cena.dlq.ext.<name>` with redacted payload.
- **Reference adapter**: Mashov (highest-criticality) is the first
  migration. The reference implementation lives in
  `src/shared/Cena.Infrastructure/ExternalAdapters/Mashov/` and becomes
  the template.

## Open items

- Per-adapter redaction policies: one task per adapter, enforced by
  arch-test.
- Retire in-line Mashov retry code once adapter is live (task follow-up).
- Ops queue UI for DLQ review — shared with prr-034 + prr-023.

## References

- Netflix Hystrix docs — circuit breaker semantics ([archived](https://github.com/Netflix/Hystrix/wiki)).
- Amazon Architecture Blog, *Exponential Backoff and Jitter*, 2015.
- Release It! 2nd ed., Michael T. Nygard — Stability patterns.
- [Polly](https://github.com/App-vNext/Polly) — .NET resilience library,
  already a project dependency.
