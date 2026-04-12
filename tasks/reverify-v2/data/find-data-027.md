---
id: FIND-DATA-027
task_id: t_7cc75a600edf
severity: P1 — High
lens: data
tags: [reverify, data, perf]
status: pending
assignee: unassigned
created: 2026-04-11
---

# FIND-data-027: SessionEndpoints + StudentAnalyticsEndpoints still do full mt_events scans per request

## Summary

SessionEndpoints + StudentAnalyticsEndpoints still do full mt_events scans per request

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
Eliminate the global event-store scan from /api/sessions/{id} detail and
/api/sessions/{id}/replay (and the two StudentAnalytics endpoints that
share the pattern). Replace with a per-session projection or a
session-keyed event stream so each request is O(1) document load or a
bounded per-stream replay.

ROOT CAUSE
SessionEndpoints.cs:219 (GetSessionDetail) and :348 (GetSessionReplay)
both do `QueryAllRawEvents().Where(EventTypeName=="concept_attempted_v1")
.ToListAsync()` with NO Take, NO time window, NO streamKey filter, NO
tenant filter. They then filter in memory by sessionId. The IDOR check
gates the response but the underlying SQL pulls every student's events
into the API process before the filter runs. StudentAnalyticsEndpoints.cs
lines 54 and 139 share the same anti-pattern.

For an active platform with millions of concept_attempted_v1 events,
every session detail request is an O(N) sequential scan on mt_events.

EVIDENCE
  $ sed -n '219,225p' src/api/Cena.Student.Api.Host/Endpoints/SessionEndpoints.cs
    219: var events = await session.Events.QueryAllRawEvents()
    220:     .Where(e => e.EventTypeName == "concept_attempted_v1")
    221:     .ToListAsync();
    222:
    223: var sessionEvents = events
    224:     .Where(e => ExtractString(e, "sessionId") == doc.SessionId)
    225:     .ToList();

  Same shape at:
    SessionEndpoints.cs:348 (GetSessionReplay)
    StudentAnalyticsEndpoints.cs:54 (GetStudentAnalyticsSummary)
    StudentAnalyticsEndpoints.cs:139 (GetConceptTimeline)

FILES TO TOUCH
  - src/api/Cena.Student.Api.Host/Endpoints/SessionEndpoints.cs:219, 348
  - src/api/Cena.Student.Api.Host/Endpoints/StudentAnalyticsEndpoints.cs:54, 139
  - src/actors/Cena.Actors/Projections/SessionConceptHistoryProjection.cs (NEW,
    keyed by SessionId)
  - src/actors/Cena.Actors/Configuration/MartenConfiguration.cs

FILES TO READ FIRST
  - .agentdb/AGENT_CODER_INSTRUCTIONS.md
  - docs/reviews/agent-data-reverify-2026-04-11.md FIND-data-027
  - docs/reviews/agent-3-data-findings.md FIND-data-009 (sister anti-pattern)

DEFINITION OF DONE
  - SessionConceptHistoryProjection captures per-session ConceptAttempted_V1
    events and stores an ordered list of attempts keyed by SessionId.
  - All 4 QueryAllRawEvents call sites replaced with LoadAsync on this
    projection.
  - Replay endpoint orders by the projection's captured sequence, not by
    in-memory OrderBy.
  - Integration test asserts response shape is unchanged vs before the fix.
  - Static assertion (unit test) that the endpoint's SQL does not include
    a full scan on mt_events for concept_attempted_v1 (intercept Marten
    command log).
  - dotnet test green.

REPORTING REQUIREMENTS
  complete --result with branch, files, tests, and a paste of the Marten
  command log showing the new query shape.

TAGS: reverify, data, perf
RELATED PRIOR FINDING: FIND-data-009 (same anti-pattern, different files)
LINKED REPORT: docs/reviews/agent-data-reverify-2026-04-11.md


## Evidence & context

- Lens report: `docs/reviews/agent-data-reverify-2026-04-11.md`
- Merged report: `docs/reviews/cena-review-2026-04-11-reverify.md`
- Queue ID: `t_7cc75a600edf`

## Definition of Done

1. Root cause identified and fixed (not symptoms)
2. Regression test added and wired into CI (`.github/workflows/`)
3. Structured log emitted on the error path
4. `dotnet build` succeeds with 0 errors
5. All existing tests pass (`dotnet test`)
6. Code review by coordinator (`claude-code`)
