---
id: FIND-QA-003
task_id: t_4243ac31521f
severity: P0 — Critical
lens: qa
tags: [reverify, qa, test, regression, security]
status: pending
assignee: unassigned
created: 2026-04-11
type: regression
---

# FIND-qa-003: FocusAnalytics tenant filter has zero regression test (FIND-sec-005)

## Summary

FocusAnalytics tenant filter has zero regression test (FIND-sec-005)

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

Goal: Lock the FocusAnalyticsService tenant filter into a regression test so dropping any of the 9 .Where clauses fails CI.

Background:
FIND-sec-005 (FocusAnalytics tenant bypass) was fixed in commit fc3abdb (kimi bundle 3) by adding 9 `.Where(r => r.SchoolId == schoolId)` clauses to FocusAnalyticsService. The same commit added zero test files. A regression dropping any one of the 9 .Where clauses would silently expose another school's analytics data.

Files to read first:
  - src/api/Cena.Admin.Api/FocusAnalyticsService.cs (lines 58, 75, 107, 118, 137, 188, 206, 224, 245)
  - src/api/Cena.Admin.Api.Tests/FocusAnalyticsServiceEventNamingTests.cs (pattern for Marten testing)
  - src/api/Cena.Admin.Api.Tests/IdorTests.cs (pattern for tenant scoping tests)

Files to touch:
  - src/api/Cena.Admin.Api.Tests/FocusAnalyticsServiceTenantScopeTests.cs (NEW)

Definition of Done:
  - [ ] Test seeds FocusScoreUpdated_V1 + related events under TWO different SchoolIds
  - [ ] Test calls each of the 9 FocusAnalyticsService methods as a user with school_id=A
  - [ ] Test asserts NO row from school B leaks into the result for any of the 9 paths
  - [ ] Tests fail on a synthetic regression that drops any single .Where clause
  - [ ] Tests pass on cc3f702
  - [ ] Wired into backend.yml (Cena.Admin.Api.Tests is already in CI)
  - [ ] If a Marten in-memory store is not feasible, document the integration fixture under tests/README.md (NOT repo root)

Reference: FIND-qa-003 in docs/reviews/agent-qa-reverify-2026-04-11.md
Related prior finding: FIND-sec-005


## Evidence & context

- Lens report: `docs/reviews/agent-qa-reverify-2026-04-11.md`
- Merged report: `docs/reviews/cena-review-2026-04-11-reverify.md`
- Queue ID: `t_4243ac31521f`

## Definition of Done

1. Root cause identified and fixed (not symptoms)
2. Regression test added and wired into CI (`.github/workflows/`)
3. Structured log emitted on the error path
4. `dotnet build` succeeds with 0 errors
5. All existing tests pass (`dotnet test`)
6. Code review by coordinator (`claude-code`)
