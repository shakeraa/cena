# RDY-064: Production Error Aggregator (Sentry / App Insights)

- **Status**: Proposed — not started
- **Priority**: Medium-High — visible gap in production observability
- **Source**: Shaker question 2026-04-19 during peripherals audit — "are they in?
  configurable?" surfaced that SMS, email, metrics, tracing, structured logs,
  and health checks are all wired, but grouped exception aggregation is not.
- **Tier**: 2 (quality / operability — not a ship-blocker, but significantly
  slower incident response without it)
- **Effort**: 2-4 days end-to-end (scope-dependent; see §Decision gate)
- **Depends on**: Serilog (in), OpenTelemetry (in). Both already carry
  exception data; this task wires a product-layer aggregator on top.

## Problem

The platform today captures exceptions in three places:

1. **Serilog** — structured logs include `ex.ToString()` at the line-level,
   but raw log sinks don't group identical stack traces, don't track
   regression / deploy correlation, don't own alerting.
2. **OpenTelemetry traces** — activity spans mark failures via
   `SetStatus(Error)`, but traces are sampled (default 10–50%) and don't
   carry a first-class "new exception since last deploy" signal.
3. **Health checks** — binary up/down at `/health` and
   `/health/admin-signalr`. No exception-body visibility.

What's **missing** is the grouping + alerting layer that on-call expects:

- "Same exception fingerprinted across 47 requests in the last 10 min"
- "This exception is NEW since release v1.14.3"
- "User X hit this error 3 times today" (for debugging without sifting logs)
- Breadcrumb trail of the 10 preceding log lines
- Release-health / crash-free sessions on the two SPAs
- Release correlation so ops can tell whether a deploy broke something

The two canonical options:

| Option | Fit | Cost | Notes |
|---|---|---|---|
| **Sentry** (self-hosted or SaaS) | Strong Vue/.NET SDKs, source-map upload, release tracking, crash-free-session KPI for SPAs. Self-host is on-prem-friendly. | SaaS: $26–80/mo team tier; self-host: free + infra. | Industry default for this category. |
| **Azure Application Insights** | Strong .NET integration (auto-instrumentation), integrates with Azure Monitor + Grafana. Weak Vue support. | Usage-based; free tier 5 GB/mo. | Good if the stack is already on Azure. |
| **Custom (self-hosted ELK + Elastic APM)** | Built from parts we already almost have (Serilog + OTel). Heavier ops overhead. | Infra only. | Re-implements what Sentry gives for free. Not recommended. |

## Decision gate (before implementation)

This ADR-worthy decision needs operator input before code:

1. **Hosted vs self-hosted** — Cena's COPPA/GDPR-K posture may favour
   self-hosted (no third-party data processor agreement needed for
   exception data that might contain student identifiers). Self-host
   Sentry on the cluster vs trusting Sentry's DPA.
2. **Exception-scrubbing policy** — exceptions from the tutor path can
   contain student free-text. PII scrubbing MUST happen before the
   payload reaches the aggregator. This is the architecturally hardest
   part; reuse `TutorPromptScrubber` pattern.
3. **Release correlation** — need a reliable "release version" string
   across admin-api / student-api / actor-host / emulator + both SPAs.
   The `1.0.0` serviceVersion in OTel is a placeholder; we'd wire this
   to git SHA on docker build.

## Scope

### 1. ADR-0037: Error-aggregator choice

Short ADR documenting the choice (Sentry self-hosted recommended),
exception-scrubbing policy, and release-version plumbing. No code.

### 2. Shared Cena.Infrastructure wiring

`src/shared/Cena.Infrastructure/Observability/ErrorAggregator/`:

- `IErrorAggregator` interface — thin abstraction so the concrete impl
  (Sentry, AppInsights) doesn't leak into application code.
- `SentryErrorAggregator` — wraps `Sentry.SentrySdk` with before-send
  hook that runs PII scrubbing on exception messages + breadcrumbs.
- `NullErrorAggregator` — no-op fallback when `ErrorAggregator:Enabled=false`
  or no DSN configured. Graceful-disabled like SMS/Email.
- `AddCenaErrorAggregator(IServiceCollection, IConfiguration)` extension
  — registered in all three .NET hosts + the emulator.

### 3. Serilog integration

- `Serilog.Sinks.Sentry` (or equivalent AppInsights sink) configured in
  each host's `UseSerilog(...)` block when aggregator is enabled.
- Exceptions at `LogLevel.Error` or higher flow to the aggregator
  automatically; `LogLevel.Warning` does NOT by default (noise control).

### 4. OTel bridge

OpenTelemetry Activity exceptions (set via `activity.RecordException`)
also forward to the aggregator with the same scrubbing pass. Single
code path, two observables in different tools.

### 5. SPA side — admin + student

- `@sentry/vue` (or AppInsights JS SDK) in `src/admin/full-version` +
  `src/student/full-version`.
- Scrubbing: before-send hook strips tutor free-text, identifiers, and
  any `user_input` breadcrumb.
- Source-map upload in Vite build step (behind a CI env var so local
  dev doesn't upload).
- Release-health enabled — crash-free sessions metric on the dashboard.

### 6. Release correlation

- Docker build args inject `CENA_GIT_SHA` into each container
- `appsettings.json` → `Cena:Release:Version` = build arg or "dev"
- OTel + aggregator both tag every event with this release

### 7. Exception-scrubbing hardening

Reuse `TutorPromptScrubber` patterns; add a dedicated
`ExceptionScrubber` that runs over:
  - Exception.Message
  - Exception.StackTrace (file paths only; argument values stripped)
  - Breadcrumbs (request body, headers)
  - User context (never include `studentId`; use `studentAnonId` from
    the RDY-063 anonymiser)

## Acceptance criteria

- [ ] ADR-0037 drafted + approved (Sentry self-hosted vs SaaS decision made)
- [ ] `IErrorAggregator` + `SentryErrorAggregator` + `NullErrorAggregator`
  in `Cena.Infrastructure`
- [ ] All 4 .NET hosts (admin-api, student-api, actor-host, emulator)
  register the aggregator; graceful-disabled when `ErrorAggregator:Enabled=false`
- [ ] Both SPAs initialise the client SDK with before-send scrubbing
- [ ] Exception with a simulated `studentId` in its message is scrubbed
  to `<redacted:student>` before it leaves the process (unit test)
- [ ] Release version tag appears on every aggregated event, sourced
  from docker build args / `CENA_GIT_SHA`
- [ ] Runbook: `docs/ops/runbooks/error-aggregator-degraded.md`
- [ ] Architecture test: no code path calls `Console.Error` / raw throw
  outside of the aggregator-wrapped paths in production code (regression
  guard — exceptions must flow through the aggregator in Prod)

## Out of scope

- **Log aggregation itself** — Serilog sinks to wherever the cluster is
  configured (Elasticsearch, Loki). Separate concern from exception
  aggregation.
- **APM / performance monitoring** — Sentry has it but this task does
  not wire it. OTel tracing already covers most of the same ground.
- **User feedback widget** — Sentry's crash-report prompt is
  user-facing and out of scope for the pilot.
- **Multiple aggregator sinks** — one aggregator at a time. Multi-sink
  splits responsibility and adds complexity without benefit.

## Open questions

1. **Self-hosted Sentry**: who owns the infra? Needs Postgres +
   ClickHouse + Redis; not trivial to stand up. If SaaS, who owns the
   DPA negotiation?
2. **PII in exceptions from the tutor path**: CAS-gated questions should
   never allow student free-text into an exception message (TutorPromptScrubber
   runs before the LLM call), but stack traces can carry argument values.
   Do we strip or rely on the scrubber?
3. **Under-13 users**: COPPA rule on exception data from minors —
   likely treatable as "operational data necessary to provide the
   service" which is permitted, but worth a Ran sign-off before ship.
4. **Emulator opt-out**: should emulator errors flow to the aggregator
   or skip? If the aggregator is fed by emulator noise, incident triage
   gets harder. Default: emulator sends to a separate project/env tag
   so a filter can exclude it.

## Links

- Serilog config: `src/actors/Cena.Actors.Host/Program.cs:68` (example)
- PII scrubber pattern to reuse: `src/actors/Cena.Actors/Tutor/TutorPromptScrubber.cs`
- Anonymiser pattern to reuse: `src/actors/Cena.Actors/Diagnosis/StuckAnonymizer.cs`
- OTel config: `src/actors/Cena.Actors.Host/Program.cs:515`
- Runbook directory: `docs/ops/runbooks/`
