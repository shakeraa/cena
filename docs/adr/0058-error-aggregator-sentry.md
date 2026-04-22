# ADR-0058 — Production error aggregator: Sentry SaaS with source-layer PII scrubbing

- **Status**: Accepted
- **Date**: 2026-04-22
- **Decision Makers**: Shaker (project owner), Architecture
- **Task**: [RDY-064](../../tasks/readiness/done/RDY-064-error-aggregator.md) (§Decision gate)
- **Related**: [ADR-0003](0003-misconception-session-scope.md) (session-scope PII), [ADR-0038](0038-event-sourced-right-to-be-forgotten.md) (RTBF), [ADR-0042](0042-consent-aggregate-bounded-context.md) (observability consent), [ADR-0047](0047-no-pii-in-llm-prompts.md) (source-layer scrubbing precedent)

---

## Context

[RDY-064](../../tasks/readiness/done/RDY-064-error-aggregator.md) landed the `IErrorAggregator` abstraction + `ExceptionScrubber` + `NullErrorAggregator` default + locked-down `sentry.config.ts` on the student SPA. The gated `switch` case in [ServiceCollectionExtensions.cs:53-54](../../src/shared/Cena.Infrastructure/Observability/ErrorAggregator/ServiceCollectionExtensions.cs#L53-L54) explicitly waits on this ADR before instantiating a real Sentry backend; the Vue plugin at [sentry.ts:99-113](../../src/student/full-version/src/plugins/sentry.ts#L99-L113) defers real `@sentry/vue` init to a follow-up ("STU-W-OBS-SENTRY") — which this ADR unblocks.

Three decisions block rollout:

1. **Hosted SaaS vs self-host** — affects COPPA/GDPR-K data-processor posture.
2. **PII scrubbing policy** — exceptions from the tutor path can contain student free-text. Where scrubbing happens is architecturally load-bearing.
3. **Release correlation** — Sentry's regression-detection + crash-free-session KPI both need a reliable release string across 4 .NET hosts + 2 SPAs.

## Decisions

### 1. Hosted SaaS (Sentry.io EU region), not self-hosted

**Sentry SaaS, `de` region, Team plan.**

Rejected alternatives:

- **Self-hosted Sentry on own cluster** — considered first for COPPA/GDPR-K reasons. Rejected: Sentry's self-hosted stack is 20+ containers (Snuba, Relay, Kafka, ClickHouse, Redis, PostgreSQL), needs dedicated ops + continuous security patching. Adds a high-value intrusion target to the cluster. The PII risk that justifies self-hosting is **eliminated at the source** by the scrubbing policy in §2, so the infra overhead buys nothing.
- **Azure Application Insights** — strong .NET, weak Vue. The two SPAs would need a second aggregator; splitting fingerprinting across tools defeats the grouping value.
- **Custom ELK + Elastic APM** — re-implements what Sentry ships, at higher ops cost.

Sentry SaaS posture:

- **Data region**: EU (`de.sentry.io`) — keeps events in Germany, covered by Sentry's GDPR DPA (signed as part of this ADR rollout task).
- **Data processor agreement**: Sentry's standard DPA covers Art 28 GDPR. COPPA-side: no student-identifiable data ever leaves the process (§2 scrubbing) → Sentry never becomes a COPPA-covered data processor.
- **Retention**: 90 days on Team plan. Sufficient for incident correlation; no long-term analytics.

### 2. PII scrubbing runs at the source — `ExceptionScrubber` is the contract

Scrubbing is **non-negotiable source-layer** (same pattern as [ADR-0047](0047-no-pii-in-llm-prompts.md) for LLM prompts). The aggregator transport never sees raw PII; Sentry's `beforeSend` is the last line of defence, not the first.

**.NET path:**

1. Every `IErrorAggregator.Capture(...)` call in application code passes through `SentryErrorAggregator`, which invokes `IExceptionScrubber.ScrubException(...)` **before** handing the event to `SentrySdk.CaptureException`.
2. The scrubber wraps the original exception in `ScrubbedException` — type name preserved (for fingerprinting), message + stack trace redacted.
3. Sentry `options.BeforeSend` installs a **second-line** pass that scrubs `user.email`, `user.ip_address`, HTTP query strings, request bodies, and any breadcrumb containing strings matching the `ExceptionScrubber` patterns. If both layers redact the same field, no harm; if the source layer misses something, the transport catches it.
4. `SentrySdk.Options.SendDefaultPii = false` (hard-coded, not config-driven — prevents accidental override).

**SPA path:**

1. `getSentryConfig(dsn)` at [sentry.config.ts](../../src/student/full-version/src/plugins/sentry.config.ts) is authoritative. `defaultPii: false`, session-replay disabled (both sample rates 0), `beforeSend: scrubEvent`.
2. Admin SPA ships the same config file (copy — both SPAs carry identical privacy contract; drift detected by a CI test that hashes the two `scrubEvent` functions).
3. `setUser` only accepts `{ id_hash }` — raw `studentId` / `email` / `username` rejected by the TypeScript type.

**Forbidden fields** (scrubbed on both legs):

| Field | .NET | SPA |
|---|---|---|
| `studentId` (raw) | `ExceptionScrubber.StudentIdMarkerPattern` | type-level: `SentryUser.id_hash` only |
| Email address | `EmailPattern` | `scrubEvent` strips `user.email` |
| IP address | `IPv4Pattern` + `IPv6Pattern` | `defaultPii: false` |
| Bearer / JWT tokens | `BearerTokenPattern` + `JwtPattern` | Header scrubbing in `scrubEvent` |
| Israeli ID (9-digit) | `IsraeliIdPattern` | Form data not serialised to Sentry |
| Phone numbers | `PhonePattern` | Same |
| Tutor free-text (`message_content` breadcrumb) | Dropped entirely in `SentryErrorAggregator.BeforeSend` | Breadcrumb categories `nav`, `ui` only; `input` category dropped |
| Raw URL query strings | N/A | `scrubEvent` empties `request.query_string` |

**Scrubbing failure mode**: the scrubber must never throw. `ExceptionScrubber.Scrub` already returns `<scrub-failed>` on pattern-engine error; `SentryErrorAggregator.BeforeSend` wraps the whole thing in a try/catch that drops the event if any downstream scrub step fails. Losing an exception report is preferable to leaking PII.

### 3. Release correlation — container-level `CENA_GIT_SHA`

One canonical release string across all runtimes.

**.NET hosts** (admin-api, student-api, actor-host, emulator):

- Docker build arg `CENA_GIT_SHA` injected at image build (from `git rev-parse --short=12 HEAD`).
- Propagated to container env var `CENA__ErrorAggregator__Release`.
- Consumed by `ErrorAggregatorOptions.Release` (already scaffolded at [ErrorAggregatorOptions.cs:41](../../src/shared/Cena.Infrastructure/Observability/ErrorAggregator/ErrorAggregatorOptions.cs#L41)).
- Passed to `SentryOptions.Release` on init.
- Also flows to OpenTelemetry `service.version` (currently hard-coded `"1.0.0"` placeholder — fix in the wiring task).

**SPAs** (student, admin):

- Vite env var `VITE_CENA_RELEASE` (same `git rev-parse --short=12 HEAD` at build time).
- Source-map upload step in Vite build invokes `sentry-cli releases` with that SHA (only when `SENTRY_AUTH_TOKEN` is present — keeps local dev silent).
- `Sentry.init({ release: import.meta.env.VITE_CENA_RELEASE })`.

**Local dev**: env var absent → release defaults to `"unknown"`; events still fire but regression-detection won't cross-correlate. Acceptable; local dev isn't shipped.

**CI**: GitHub Actions (or equivalent) sets both env vars in the build job from `${{ github.sha }}`.

## Consequences

### Positive

- On-call gets first-class exception grouping ("same error 47× in 10 min"), regression detection ("new since v1.14.3"), and crash-free-session KPI on both SPAs.
- Source-layer scrubbing means the data-processor posture is minimal: Sentry processes only type names, redacted messages, and release metadata.
- The existing `IErrorAggregator` abstraction is preserved — if Sentry falls out of favour in 12 months, a new backend drops in behind the same interface with no call-site churn.
- `NullErrorAggregator` remains the default for local dev + CI — zero Sentry quota burn on every test run.

### Negative

- Team plan cost: ~$26–$80/month depending on event volume (expect low end given source-layer scrubbing drops many events).
- Self-hosting revisit is possible if volume grows past Team plan economics; the abstraction makes this a config+infra change, not a code change.
- Source-map uploads require a Sentry auth token in CI secrets. One more secret to rotate; documented in the deployment runbook.

### Risk — scrubber drift

The two-layer scrubbing (source + transport) means a developer could add a new PII-carrying breadcrumb and forget to update the pattern catalogue. Mitigations:

1. CI test harness asserts every `ExceptionScrubber` pattern has a positive test + a negative (false-positive-avoidance) test.
2. PR template adds a checkbox: "If this PR adds new breadcrumb categories or exception messages, did you update the scrubber catalogue?" — enforced by a custom `danger-js` rule.
3. Monthly dry-run: replay a week of production events against the current scrubber in staging, audit the diff.

## Compliance

- **COPPA** — no student identifiers reach Sentry (§2 source-layer scrubbing). Sentry is not a "service provider" under 16 CFR 312 because it never receives PI.
- **GDPR-K** — Sentry is a joint processor under Art 28. DPA executed as part of rollout. EU data residency. Right-to-be-forgotten: 90-day retention is shorter than our own event-source retention, so RTBF is auto-honoured by TTL; no deletion pipeline needed.
- **Ship-gate** — this ADR does not touch student-facing behaviour. No ship-gate banned-term risk.

## Implementation Plan

Scaffolded pieces already in place (no work needed):

- [IErrorAggregator.cs](../../src/shared/Cena.Infrastructure/Observability/ErrorAggregator/IErrorAggregator.cs) — abstraction
- [ExceptionScrubber.cs](../../src/shared/Cena.Infrastructure/Observability/ErrorAggregator/ExceptionScrubber.cs) — pattern catalogue + `ScrubbedException`
- [NullErrorAggregator.cs](../../src/shared/Cena.Infrastructure/Observability/ErrorAggregator/NullErrorAggregator.cs) — default
- [sentry.config.ts](../../src/student/full-version/src/plugins/sentry.config.ts) — locked-down SPA config
- [sentry.ts](../../src/student/full-version/src/plugins/sentry.ts) — no-op shim

Remaining wiring (split into 3 parallel queue tasks):

1. **RDY-064-impl-backend**: `SentryErrorAggregator : IErrorAggregator` in `Cena.Infrastructure`; replace the gated switch case; wire into all 4 host `Program.cs` files; add `Sentry.Serilog` sink; tests.
2. **RDY-064-impl-spa**: Install `@sentry/vue`; real `Sentry.init(getSentryConfig(dsn))` in both SPAs; replace the shim export; delete the STU-W-OBS-SENTRY deferral comment; add the `scrubEvent`-function hash CI test that catches drift between the two SPAs.
3. **RDY-064-impl-release**: Docker build arg `CENA_GIT_SHA` plumbed through all 4 Dockerfiles + CI workflow; Vite `VITE_CENA_RELEASE` + sourcemap upload step (behind `SENTRY_AUTH_TOKEN`); OTel `service.version` hard-coded placeholder replaced.

Full sln build gate ([feedback memory 2026-04-13](../../CLAUDE.md)): each task builds the whole solution before merge.
