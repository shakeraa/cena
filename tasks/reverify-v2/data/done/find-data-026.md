---
id: FIND-DATA-026
task_id: t_8eec1c3c7039
severity: P1 — High
lens: data
tags: [reverify, data, perf, cross-tenant, event-schema]
status: pending
assignee: unassigned
created: 2026-04-11
---

# FIND-data-026: ExperimentAdminService - 9 unscoped full-scans, 5x per funnel request, no persisted assignments

## Summary

ExperimentAdminService - 9 unscoped full-scans, 5x per funnel request, no persisted assignments

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

GOAL
Tenant-scope every ExperimentAdminService method and replace full-event-store
scans with persisted ExperimentAssigned_V1 events + a per-arm read model.

ROOT CAUSE
ExperimentAdminService runs nine QueryAllRawEvents full-store scans across
three methods with NO ClaimsPrincipal and NO SchoolId filter. Every admin in
every tenant sees the same global experiment population. GetFunnelAsync
performs FIVE sequential full-event-store scans per request (assigned,
engaged, confused, resolved, mastered stages), with no caching.

Additionally, the experiment population is derived from session_started_v1
events and partitioned in-memory via HashCode.Combine(studentId,
experimentName) % arms.Length. There is NO persisted assignment record. If
arms.Length changes in a config file, every prior student is silently
re-bucketed and cohort comparisons become meaningless.

EVIDENCE
  $ rg "QueryAllRawEvents" src/api/Cena.Admin.Api/ExperimentAdminService.cs -c
    9

  $ rg "Task<.*> Get.*Async\(" src/api/Cena.Admin.Api/ExperimentAdminService.cs -n
    45: public async Task<ExperimentListResponse> GetExperimentsAsync()
    92: public async Task<ExperimentDetailDto?> GetExperimentDetailAsync(string experimentName)
    168: public async Task<ExperimentFunnelResponse?> GetFunnelAsync(string experimentName)

  Zero ClaimsPrincipal parameters. No tenant scope possible.

FILES TO TOUCH
  - src/api/Cena.Admin.Api/ExperimentAdminService.cs (rewrite)
  - src/api/Cena.Admin.Api/ExperimentAdminEndpoints.cs (forward ClaimsPrincipal)
  - src/actors/Cena.Actors/Events/ExperimentEvents.cs (NEW ExperimentAssigned_V1)
  - src/actors/Cena.Actors/Services/ExperimentService.cs (emit the event on
    first assignment, store in student stream)
  - src/actors/Cena.Actors/Projections/ExperimentArmRollupProjection.cs (NEW)
  - src/actors/Cena.Actors/Configuration/MartenConfiguration.cs

FILES TO READ FIRST
  - .agentdb/AGENT_CODER_INSTRUCTIONS.md
  - docs/reviews/agent-data-reverify-2026-04-11.md FIND-data-026
  - src/actors/Cena.Actors/Services/ExperimentService.cs (current hash-based
    assignment that needs to emit the event)

DEFINITION OF DONE
  - All 9 QueryAllRawEvents calls replaced.
  - Every method accepts ClaimsPrincipal and applies SchoolId via TenantScope.
  - ExperimentAssigned_V1 event registered with fields (StudentId,
    ExperimentName, Arm, AssignedAt, AlgorithmVersion, SchoolId).
  - Upcaster path in place for future schema evolution.
  - ExperimentArmRollupProjection captures (experimentName, arm, schoolId)
    → count, plus per-arm sums for the 4 cohort metrics.
  - Funnel results cached per (SchoolId, experimentName) for 5 minutes via
    an in-process IMemoryCache.
  - Integration test:
      a) Cross-tenant: admin in school A queries funnel; assert count
         matches school A's session count, NOT the global.
      b) Persistence: change arms.Length from 2→3 via configuration flag,
         replay events, assert previously-bucketed students remain in their
         original arm (proven by reading ExperimentAssigned_V1 from stream).
  - dotnet test green.

REPORTING REQUIREMENTS
  complete --result with branch, files, tests, paste of cross-tenant test
  output and assignment-stability test output.

TAGS: reverify, data, perf, cross-tenant, event-schema
LINKED REPORT: docs/reviews/agent-data-reverify-2026-04-11.md


## Evidence & context

- Lens report: `docs/reviews/agent-data-reverify-2026-04-11.md`
- Merged report: `docs/reviews/cena-review-2026-04-11-reverify.md`
- Queue ID: `t_8eec1c3c7039`

## Definition of Done

1. Root cause identified and fixed (not symptoms)
2. Regression test added and wired into CI (`.github/workflows/`)
3. Structured log emitted on the error path
4. `dotnet build` succeeds with 0 errors
5. All existing tests pass (`dotnet test`)
6. Code review by coordinator (`claude-code`)
