---
id: FIND-SEC-015
task_id: t_ba8a2aa46b59
severity: P1 — High
lens: sec
tags: [reverify, sec, cost]
status: pending
assignee: unassigned
created: 2026-04-11
---

# FIND-sec-015: No global or per-tenant cap on AI tutor cost (per-user rate limit only)

## Summary

No global or per-tenant cap on AI tutor cost (per-user rate limit only)

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

# FIND-sec-015 (P1): No global / per-tenant cap on AI tutor cost

**Severity**: P1 (cost exposure — per-user rate limit only; no global or per-tenant ceiling)

**Files**:
- src/api/Cena.Student.Api.Host/Program.cs (rate limiter section line 159-219)
- src/api/Cena.Student.Api.Host/Endpoints/TutorEndpoints.cs
- src/shared/Cena.Infrastructure/Ai/AiTokenBudgetService.cs (new)
- src/api/Cena.Admin.Api.Tests/AiBudgetTests.cs (new)
- appsettings.json (new Cena:LlmBudget section)

**Goal**: bound LLM cost per minute and per day at the global and per-tenant level. Per-user limits remain as UX guardrails.

**Background**: The "tutor" rate-limit policy is per-user (10 messages/min/student) but there is NO global cap and NO per-tenant cap. With 10K active students at peak the platform can fire 100K LLM calls/min against Claude. At Anthropic Sonnet pricing (~$3/M input, ~$15/M output, ~1.5K-token tutor turn) that is roughly $4-7/min and scales linearly with student count. The admin "ai" rate limiter has the same per-user-only design.

**Scope**:
1. Add a global "tutor-global" fixed-window limiter at the host level (not per-user) with a configurable PermitLimit (`Cena:LlmBudget:GlobalTutorPerMinute`, default 1000).
2. Add a per-tenant "tutor-tenant" sliding-window limiter partitioned by school_id claim, default 200/min.
3. Plumb both limiters via `RequireRateLimiting` chain on the tutor endpoints (ASP.NET Core RateLimiterMiddleware composes them in sequence).
4. Add a Redis-backed daily token-budget service: `IAiTokenBudgetService.TryReserveAsync(tenantId, estimatedTokens)` that increments a `cena:llm:budget:{tenant}:{yyyymmdd}` key against a configurable cap. Fail the request with 429 + a clear message if the cap is hit.
5. Wire AiGenerationService._tokensTotal and _costUsd counters (or the new SecurityMetrics meter from FIND-sec-014) into the tutor path so cost is tracked per student / per tenant in Prometheus.

**Definition of Done**:
- [ ] tutor-global and tutor-tenant policies registered in Program.cs and chained on the tutor endpoints
- [ ] AiTokenBudgetService registered as singleton, backed by Redis
- [ ] Tutor SendMessage / StreamMessage call TryReserveAsync before the LLM call
- [ ] AiBudgetTests covers the 4 cases (per-user limit, global limit, per-tenant limit, daily token cap)
- [ ] Branch: `<worker>/<task-id>-sec-015-ai-cost-budgeting`

**Files to read first**:
- src/api/Cena.Student.Api.Host/Program.cs (especially rate limiter section line 159-219)
- src/api/Cena.Student.Api.Host/Endpoints/TutorEndpoints.cs
- src/api/Cena.Admin.Api/AiGenerationService.cs (existing token counter pattern)
- src/actors/Cena.Actors/Tutor/ClaudeTutorLlmService.cs


## Evidence & context

- Lens report: `docs/reviews/agent-sec-reverify-2026-04-11.md`
- Merged report: `docs/reviews/cena-review-2026-04-11-reverify.md`
- Queue ID: `t_ba8a2aa46b59`

## Definition of Done

1. Root cause identified and fixed (not symptoms)
2. Regression test added and wired into CI (`.github/workflows/`)
3. Structured log emitted on the error path
4. `dotnet build` succeeds with 0 errors
5. All existing tests pass (`dotnet test`)
6. Code review by coordinator (`claude-code`)
