---
id: FIND-DATA-025
task_id: t_d460e84481fa
severity: P1 — High
lens: data
tags: [reverify, data, perf, cross-tenant]
status: pending
assignee: unassigned
created: 2026-04-11
---

# FIND-data-025: StudentInsightsService - 13 global event-store scans, no tenant scope, sample-truncation bug

## Summary

StudentInsightsService - 13 global event-store scans, no tenant scope, sample-truncation bug

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
Add tenant scoping to all 8 per-student insight endpoints AND replace the
global Take(N).Where(in-memory-studentId) pattern with per-student stream
/ per-student rollup queries. Eliminate the cross-tenant leak and the
sample-truncation bug.

ROOT CAUSE
StudentInsightsService has 13 separate QueryAllRawEvents calls each doing
`Take(500|2000|5000)` globally then filtering in memory by studentId. Two
compounding bugs:
1. Cross-tenant leak: the method takes a ClaimsPrincipal but never resolves
   the caller's SchoolId. An admin from school A can request insights for a
   student in school B and gets the data back.
2. Sample-truncation: with 100k students generating ~50 focus events per
   session, the most-recent 2000 events globally cover at most ~40 active
   students. Every other student's heatmap silently returns empty. The label
   says "last 30 days of focus scores"; the data is "the most recent 2000
   globally".

EVIDENCE
  $ rg "QueryAllRawEvents" src/api/Cena.Admin.Api/StudentInsightsService.cs -c
    13

  $ sed -n '85,93p' src/api/Cena.Admin.Api/StudentInsightsService.cs
    85: var focusEvents = await session.Events.QueryAllRawEvents()
    86:     .Where(e => e.EventTypeName == "focus_score_updated_v1")
    87:     .OrderByDescending(e => e.Timestamp)
    88:     .Take(2000)             ← global cap
    89:     .ToListAsync();
    90:
    91: var studentEvents = focusEvents
    92:     .Where(e => ExtractString(e, "studentId") == studentId)  ← in-memory filter
    93:     .ToList();

  No SchoolId lookup anywhere in the service's 553 lines.

  FocusAnalyticsService.cs:150 does the same pattern in its fallback path
  (comment "school isolation via student lookup" but no lookup).

FILES TO TOUCH
  - src/api/Cena.Admin.Api/StudentInsightsService.cs (rewrite all 13 sites)
  - src/api/Cena.Admin.Api/FocusAnalyticsService.cs:150 (same anti-pattern)
  - src/actors/Cena.Actors/Projections/StudentDailyStatsRollupProjection.cs
    (NEW — feeds heatmap + degradation curve from real data)
  - src/actors/Cena.Actors/Configuration/MartenConfiguration.cs

FILES TO READ FIRST
  - .agentdb/AGENT_CODER_INSTRUCTIONS.md
  - docs/reviews/agent-data-reverify-2026-04-11.md FIND-data-025
  - src/api/Cena.Admin.Api/TutoringAdminService.cs:105-113
    (the existing TenantScope pattern to copy — resolves SchoolId from
    ClaimsPrincipal and 404s cross-school studentIds)

DEFINITION OF DONE
  - Every per-student insight endpoint resolves SchoolId from the caller's
    claim via TenantScope.GetSchoolFilter.
  - Cross-school studentId requests return 403 (or 404 if you prefer
    IDOR-style silence).
  - SUPER_ADMIN bypass (schoolId is null) still works.
  - Zero Take(2000) / Take(5000) global event scans in StudentInsightsService.
  - Per-day rollups (heatmap, degradation curve) read from the new
    StudentDailyStatsRollupProjection keyed by (StudentId, Date).
  - Integration test: two students with different SchoolIds. Admin in
    school A requests student B's heatmap, asserts 403/404. Then requests
    student A's heatmap, asserts 200 with non-empty cells.
  - dotnet test green.

REPORTING REQUIREMENTS
  complete --result with branch, files, tests, proof the cross-tenant test
  fails on the pre-fix branch and passes on the fix branch.

TAGS: reverify, data, perf, cross-tenant, label-drift
RELATED PRIOR FINDING: FIND-data-009 (sister unbounded-query pattern in
  student-facing endpoints)
LINKED REPORT: docs/reviews/agent-data-reverify-2026-04-11.md


## Evidence & context

- Lens report: `docs/reviews/agent-data-reverify-2026-04-11.md`
- Merged report: `docs/reviews/cena-review-2026-04-11-reverify.md`
- Queue ID: `t_d460e84481fa`

## Definition of Done

1. Root cause identified and fixed (not symptoms)
2. Regression test added and wired into CI (`.github/workflows/`)
3. Structured log emitted on the error path
4. `dotnet build` succeeds with 0 errors
5. All existing tests pass (`dotnet test`)
6. Code review by coordinator (`claude-code`)
