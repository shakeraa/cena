---
id: FIND-QA-004
task_id: t_644582f3aea7
severity: P0 — Critical
lens: qa
tags: [reverify, qa, test, regression, security]
status: pending
assignee: unassigned
created: 2026-04-11
type: regression
---

# FIND-qa-004: QueryAllRawEvents anti-pattern needs analyzer + tests (FIND-data-009)

## Summary

QueryAllRawEvents anti-pattern needs analyzer + tests (FIND-data-009)

## Severity

**P0 — Critical** — REGRESSION

## Requirements

The fix for this task MUST be production-grade:

- **No stubs, no canned data, no hardcoded objects, no `NotImplementedException`**
- **Labels must match actual data** — if a button says "Save", it must persist; if a metric says "tokens", it must count real tokens
- **Verify E2E** — query the DB, call the API, render the UI, compare field names
- **Include a CI-wired regression test** that fails on the current (buggy) commit and passes on the fix
- **Add a structured log line** on the error path so a re-regression is detectable in production

## Task body

Goal: Lock the QueryAllRawEvents anti-pattern under either a Roslyn analyzer or a CI bash check, AND add per-callsite tenant tests for the top 5 hot paths.

Background:
FIND-data-009 (QueryAllRawEvents tenant leakage) was fixed for the analytics endpoints called out in v1, but the broader anti-pattern survives across 55 callsites in 18 files with no test guarding any of them. The Phase-0 preflight already flagged this for the data lens. From a QA lens, the pattern CANNOT be reliably guarded without tooling: each call needs a per-call tenant scoping check, and there is no convention or analyzer that catches a forgotten filter.

Files to read first:
  - src/actors/Cena.Actors/Projections/StudentLifetimeStatsProjection.cs (the only file added in fix commit 9d39eaf)
  - rg `QueryAllRawEvents` src/  (gets you the 55 callsites in 18 files)
  - src/api/Cena.Admin.Api/AdminDashboardService.cs (one of the top callers)

Files to touch:
  - scripts/lint-query-all-raw-events.sh (NEW) — bash check for `QueryAllRawEvents` without nearby `SchoolId`/`TenantId` filter (within 5 lines below the call)
  - .github/workflows/backend.yml — wire the lint as a job
  - src/api/Cena.Admin.Api.Tests/QueryAllRawEventsTenantTests.cs (NEW) — covers the top 5 hottest callsites

Definition of Done:
  - [ ] CI fails on a synthetic regression that calls QueryAllRawEvents without a SchoolId/TenantId filter within 5 lines
  - [ ] Tests cover the 5 highest-volume callsites — pick from analytics, leaderboard, mastery, focus, ingestion
  - [ ] Each test seeds events under two SchoolIds and asserts no leakage
  - [ ] Lint script does NOT false-positive on legitimate uses (e.g. event metadata-only queries that don't return tenant data — those need an explicit `// LINT:tenant-safe` annotation)

Reference: FIND-qa-004 in docs/reviews/agent-qa-reverify-2026-04-11.md
Related prior finding: FIND-data-009


## Evidence & context

- Lens report: `docs/reviews/agent-qa-reverify-2026-04-11.md`
- Merged report: `docs/reviews/cena-review-2026-04-11-reverify.md`
- Queue ID: `t_644582f3aea7`

## Definition of Done

1. Root cause identified and fixed (not symptoms)
2. Regression test added and wired into CI (`.github/workflows/`)
3. Structured log emitted on the error path
4. `dotnet build` succeeds with 0 errors
5. All existing tests pass (`dotnet test`)
6. Code review by coordinator (`claude-code`)
