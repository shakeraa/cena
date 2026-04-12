---
id: FIND-ARCH-023
task_id: t_766c5582f2a2
severity: P1 — High
lens: arch
tags: [reverify, arch, perf]
status: pending
assignee: unassigned
created: 2026-04-11
---

# FIND-arch-023: SessionEndpoints GetSessionDetail/Replay full-scan event store

## Summary

SessionEndpoints GetSessionDetail/Replay full-scan event store

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

**Goal**: Stop full-scanning the event store on every per-session
page load. Replace with a projection-backed read.

**Files to read first**:
  - src/api/Cena.Student.Api.Host/Endpoints/SessionEndpoints.cs
    (lines 200-410 for GetSessionDetail and GetSessionReplay)
  - src/actors/Cena.Actors/Projections/StudentLifetimeStatsProjection.cs
    (template — same fix pattern as FIND-data-009)
  - src/actors/Cena.Actors/Configuration/MartenConfiguration.cs
  - src/actors/Cena.Actors/Events/LearnerEvents.cs

**Files to touch**:
  - src/actors/Cena.Actors/Projections/SessionAttemptHistoryProjection.cs (new)
  - src/actors/Cena.Actors/Configuration/MartenConfiguration.cs (register)
  - src/api/Cena.Student.Api.Host/Endpoints/SessionEndpoints.cs (replace QueryAllRawEvents calls at lines 219, 348)
  - src/actors/Cena.Actors.Tests/Projections/SessionAttemptHistoryProjectionTests.cs (new)

**Definition of Done**:
  - [ ] `grep -n "QueryAllRawEvents" src/api/Cena.Student.Api.Host/Endpoints/SessionEndpoints.cs` returns zero matches
  - [ ] Both endpoints load a single document by sessionId
  - [ ] Projection rebuild on a 10k-event seed completes in < 30s
  - [ ] Perf test asserts the new path reads ≤ 30 events per call

**Reporting requirements**:
  - Paste before/after p95 latency from the perf test.
  - Paste the projection rebuild timing on the seed dataset.

**Reference**: FIND-arch-023 in docs/reviews/agent-arch-reverify-2026-04-11.md


## Evidence & context

- Lens report: `docs/reviews/agent-arch-reverify-2026-04-11.md`
- Merged report: `docs/reviews/cena-review-2026-04-11-reverify.md`
- Queue ID: `t_766c5582f2a2`

## Definition of Done

1. Root cause identified and fixed (not symptoms)
2. Regression test added and wired into CI (`.github/workflows/`)
3. Structured log emitted on the error path
4. `dotnet build` succeeds with 0 errors
5. All existing tests pass (`dotnet test`)
6. Code review by coordinator (`claude-code`)
