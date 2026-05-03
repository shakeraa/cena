---
id: FIND-PEDAGOGY-016
task_id: t_36ad75f9a484
severity: P1 — High
lens: pedagogy
tags: [reverify, pedagogy, contract]
status: pending
assignee: unassigned
created: 2026-04-11
---

# FIND-pedagogy-016: REST session not adaptive — queue never seeded; returns 'Session completed' on first GET

## Summary

REST session not adaptive — queue never seeded; returns 'Session completed' on first GET

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

FIND-pedagogy-016: Wire AdaptiveQuestionPool into REST session start + refill
related_prior_finding: FIND-pedagogy-006 (root cause)
classification: partial-fix (structural)

The REST session flow on Cena.Student.Api.Host is structurally broken:
POST /api/sessions/start creates a session record (ActiveSessionSnapshot)
but does NOT seed the LearningSessionQueueProjection question queue.
The first GET /api/sessions/{id}/current-question hits an empty queue
(queue.PeekNext() returns null) and returns the
"Session completed! No more questions." stub (SessionEndpoints.cs:486-498)
before the student answers anything.

The SessionEndpoints.cs:116 comment literally says
  // Phase 1: null, wired in STB-01b
STB-01b never landed. AdaptiveQuestionPool exists in
src/actors/Cena.Actors/Serving/AdaptiveQuestionPool.cs with a working
InitializeSessionAsync at line 81 — but it is not registered or invoked
in Cena.Student.Api.Host (rg returns zero matches).

In dev mode, MSW masks this by intercepting the calls (see
FIND-pedagogy-011) and returning canned questions. In production
deployment hitting the real backend, the entire adaptive REST flow is
dead. This is the root cause of FIND-pedagogy-006 / FIND-pedagogy-012
being partial-fixes: there is no scaffolding to wire because there is
no question to scaffold.

Per Wilson, Shenhav, Straccia & Cohen (2019), Nature Communications 10
4646, DOI: 10.1038/s41467-019-12552-4 — the 85% rule requires actual
item selection. Without queue seeding, no item selection happens.

Files to read first:
  - src/api/Cena.Student.Api.Host/Endpoints/SessionEndpoints.cs:49-118
  - src/api/Cena.Student.Api.Host/Endpoints/SessionEndpoints.cs:452-518
  - src/actors/Cena.Actors/Serving/AdaptiveQuestionPool.cs
  - src/actors/Cena.Actors/Projections/LearningSessionQueueProjection.cs
  - src/api/Cena.Api.Contracts/Sessions/SessionDtos.cs (SessionStartResponse, FirstQuestionId)

Definition of done:
  - IAdaptiveQuestionPool registered in Cena.Student.Api.Host Program.cs.
  - POST /start, after appending LearningSessionStarted_V1 and creating
    ActiveSessionSnapshot, calls AdaptiveQuestionPool.InitializeSessionAsync
    to seed the queue with N questions per the requested
    subjects/duration/mode.
  - SessionStartResponse.FirstQuestionId returns the first seeded
    question's id (replace the // Phase 1: null comment).
  - Refill mechanism: when queue.NeedsRefill (already exists at
    LearningSessionQueueProjection.cs:57) returns true after RecordAnswer,
    call AdaptiveQuestionPool.RefillQueueAsync.
  - HTTP integration test against a real Postgres+Marten test fixture:
    POST /start ⇒ GET /current-question returns an actual questionId
    (not "completed"), an actual prompt (not the stub message).
  - Integration test: after answering 5 questions, queue still has more
    via the refill mechanism.
  - Cross-link FIND-pedagogy-006 and FIND-pedagogy-012 in the result
    (this fix unblocks both).

Reporting:
  - Branch <worker>/<task-id>-find-pedagogy-016-rest-adaptive-pool
  - Push, then complete with summary + branch + test names


## Evidence & context

- Lens report: `docs/reviews/agent-pedagogy-reverify-2026-04-11.md`
- Merged report: `docs/reviews/cena-review-2026-04-11-reverify.md`
- Queue ID: `t_36ad75f9a484`

## Definition of Done

1. Root cause identified and fixed (not symptoms)
2. Regression test added and wired into CI (`.github/workflows/`)
3. Structured log emitted on the error path
4. `dotnet build` succeeds with 0 errors
5. All existing tests pass (`dotnet test`)
6. Code review by coordinator (`claude-code`)
