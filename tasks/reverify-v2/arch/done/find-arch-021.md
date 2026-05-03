---
id: FIND-ARCH-021
task_id: t_3c0bbeea2124
severity: P1 — High
lens: arch
tags: [reverify, arch, contract]
status: pending
assignee: unassigned
created: 2026-04-11
---

# FIND-arch-021: 5 orphan NATS publishers (ingest, student.escalation, admin.methodology)

## Summary

5 orphan NATS publishers (ingest, student.escalation, admin.methodology)

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

**Goal**: Reconcile every NATS subject in the codebase to a known
publisher AND a known subscriber, OR delete it. Add a CI guard.

**Five orphan publishers identified**:
  1. `cena.ingest.file.received` (IngestionOrchestrator:117)
  2. `cena.ingest.item.classified` (IngestionOrchestrator:320)
  3. `cena.student.escalation` (StudentActor.Queries:92, .Methodology:239)
  4. `cena.admin.methodology.confidence-reached` (StudentActor.Methodology:104)
  5. `cena.admin.methodology.switch-deferred` (StudentActor.Methodology:188)

None are matched by `cena.events.>` so the catch-all NatsEventSubscriber
does NOT pick them up. Same anti-pattern as FIND-arch-011 / FIND-arch-012.

**Files to read first**:
  - src/actors/Cena.Actors/Bus/NatsSubjects.cs
  - src/actors/Cena.Actors/Ingest/IngestionOrchestrator.cs
  - src/actors/Cena.Actors/Students/StudentActor.Queries.cs
  - src/actors/Cena.Actors/Students/StudentActor.Methodology.cs
  - docs/reviews/agent-1-arch-findings.md (FIND-arch-011 + 012 for the canonical fix pattern)

**Files to touch**:
  - src/actors/Cena.Actors/Notifications/MethodologyAdminAlertHandler.cs
    (new BackgroundService subscribing to cena.student.escalation + cena.admin.methodology.*)
  - src/api/Cena.Admin.Api/Registration/CenaAdminServiceRegistration.cs (register the handler)
  - OR delete the orphan publish calls if no consumer makes business sense
  - src/actors/Cena.Actors.Tests/Bus/OrphanSubjectGuardTests.cs (new)

**Definition of Done**:
  - [ ] All five orphan publishers either have a real subscriber or are deleted
  - [ ] OrphanSubjectGuardTests passes on the resulting tree
  - [ ] CI runs the guard on every PR

**Reporting requirements**:
  - For each of the five subjects, state the decision (wired vs deleted) and the reason.
  - Paste the orphan-subject test output.

**Reference**: FIND-arch-021 in docs/reviews/agent-arch-reverify-2026-04-11.md


## Evidence & context

- Lens report: `docs/reviews/agent-arch-reverify-2026-04-11.md`
- Merged report: `docs/reviews/cena-review-2026-04-11-reverify.md`
- Queue ID: `t_3c0bbeea2124`

## Definition of Done

1. Root cause identified and fixed (not symptoms)
2. Regression test added and wired into CI (`.github/workflows/`)
3. Structured log emitted on the error path
4. `dotnet build` succeeds with 0 errors
5. All existing tests pass (`dotnet test`)
6. Code review by coordinator (`claude-code`)
