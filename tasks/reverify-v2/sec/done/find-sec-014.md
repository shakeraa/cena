---
id: FIND-SEC-014
task_id: t_c449d12b59af
severity: P1 — High
lens: sec
tags: [reverify, sec, observability]
status: pending
assignee: unassigned
created: 2026-04-11
---

# FIND-sec-014: No custom metrics or alert rules on the security-critical fix paths

## Summary

No custom metrics or alert rules on the security-critical fix paths

## Severity

**P1 — High**

## Requirements

The fix for this task MUST be production-grade:

- **No stubs, no canned data, no hardcoded objects, no `NotImplementedException`**
- **Labels must match actual data** — if a button says "Save", it must persist; if a metric says "tokens", it must count real tokens
- **Verify E2E** — query the DB, call the API, render the UI, compare field names
- **Include a CI-wired regression test** that fails on the current (buggy) commit and passes on the fix
- **Add a structured log line** on the error path so a re-regression is detectable in production

## Task body

# FIND-sec-014 (P1): No custom metrics on the security-critical fix paths (silent regression risk)

**Severity**: P1 (observability gap — fixed bugs cannot be alerted on if they re-regress)

**Files**:
- src/shared/Cena.Infrastructure/Observability/SecurityMetrics.cs (new)
- src/shared/Cena.Infrastructure/Tenancy/TenantScope.cs (call counter on throw)
- src/api/Cena.Admin.Api.Host/Program.cs (register meter)
- src/api/Cena.Student.Api.Host/Program.cs (register meter)
- src/actors/Cena.Actors.Host/Program.cs (register meter)
- config/prom-rules/cena-security.yml (new)
- src/shared/Cena.Infrastructure.Tests/Observability/SecurityMetricsTests.cs (new)

**Goal**: ensure every security-critical regression class introduced by FIND-sec-001..011 has a metric and an alerting rule, so a silent re-regression in prod is detectable inside one alerting window.

**Background**: Cena's three .NET hosts wire OpenTelemetry with Prometheus + OTLP exporters, but only `AiGenerationService` (admin AI generation) defines custom counters/histograms via IMeterFactory. There are NO domain-level metrics on the paths the v1 sec lens fixed: leaderboard SQL parse failures (sec-001), tenant-isolation rejections (sec-005, sec-008, sec-011), Firebase ID-token verification failures, SignalR connection auth rejections, rate-limit rejections per policy, tutor LLM call count, privileged action audit. There are also no Prometheus alerting rules in the repo.

**Scope**: Add a `Cena.Infrastructure.Observability.SecurityMetrics` static class that registers a single `Meter("cena.security")` with the following instruments and is wired into all 3 hosts:

- Counter `cena.security.tenant_rejection.count` (tags: service, endpoint, caller_role) — incremented every time TenantScope.GetSchoolFilter throws or a cross-school check returns 404
- Counter `cena.security.auth_rejection.count` (tags: service, reason={token_invalid, token_expired, token_missing, signature_failed})
- Counter `cena.security.rate_limit_rejection.count` (tags: policy_name)
- Counter `cena.security.privileged_action.count` (tags: action={assign_role, gdpr_export, gdpr_erasure, suspend_user, force_reset, revoke_session})
- Histogram `cena.security.firebase_token_validation.duration_ms`
- Counter `cena.security.signalr_connect_rejection.count`

Plus a Prometheus alerting rules YAML under `config/prom-rules/cena-security.yml` with at minimum:
- `cena.security.tenant_rejection.count rate > 5/min for 5m`
- `cena.security.auth_rejection.count rate > 50/min for 5m`
- `cena.security.privileged_action.count{action="assign_role"} > 0` (paged immediately)

**Definition of Done**:
- [ ] `Meter("cena.security")` registered in all 3 hosts
- [ ] All 6 instruments emit on the right code paths (verified by tests)
- [ ] Prometheus rules YAML committed and referenced from docker-compose observability config
- [ ] `dotnet test src/shared/Cena.Infrastructure.Tests` green
- [ ] Branch: `<worker>/<task-id>-sec-014-security-metrics`

**Files to read first**:
- src/api/Cena.Admin.Api/AiGenerationService.cs:243-256 (reference IMeterFactory pattern)
- src/api/Cena.Admin.Api.Tests/AiGenerationServiceTests.cs (TestMeterFactory pattern)
- src/shared/Cena.Infrastructure/Tenancy/TenantScope.cs
- config/docker-compose.observability.yml


## Evidence & context

- Lens report: `docs/reviews/agent-sec-reverify-2026-04-11.md`
- Merged report: `docs/reviews/cena-review-2026-04-11-reverify.md`
- Queue ID: `t_c449d12b59af`

## Definition of Done

1. Root cause identified and fixed (not symptoms)
2. Regression test added and wired into CI (`.github/workflows/`)
3. Structured log emitted on the error path
4. `dotnet build` succeeds with 0 errors
5. All existing tests pass (`dotnet test`)
6. Code review by coordinator (`claude-code`)
