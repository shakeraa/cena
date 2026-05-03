---
id: FIND-PEDAGOGY-012
task_id: t_db9eaa46f096
severity: P0 — Critical
lens: pedagogy
tags: [reverify, pedagogy, contract]
status: pending
assignee: unassigned
created: 2026-04-11
---

# FIND-pedagogy-012: REST current-question never populates ScaffoldingLevel; /hint route not registered

## Summary

REST current-question never populates ScaffoldingLevel; /hint route not registered

## Severity

**P0 — Critical**

## Requirements

The fix for this task MUST be production-grade:

- **No stubs, no canned data, no hardcoded objects, no `NotImplementedException`**
- **Labels must match actual data** — if a button says "Save", it must persist; if a metric says "tokens", it must count real tokens
- **Verify E2E** — query the DB, call the API, render the UI, compare field names
- **Include a CI-wired regression test** that fails on the current (buggy) commit and passes on the fix
- **Add a structured log line** on the error path so a re-regression is detectable in production

## Task body

FIND-pedagogy-012: Wire ScaffoldingService + Hint route into Cena.Student.Api.Host
related_prior_finding: FIND-pedagogy-006
classification: partial-fix

The fix for FIND-pedagogy-006 added a BuildHintOptionStates helper, a
SessionQuestionDto with scaffolding fields, and a SessionHintEndpointTests
unit suite. None of those are wired into a live HTTP path:

  1. GET /api/sessions/{id}/current-question never populates
     ScaffoldingLevel, WorkedExample, HintsAvailable, or HintsRemaining
     (SessionEndpoints.cs:508-516) — they always return null/0.
  2. POST /api/sessions/{id}/question/{qid}/hint is NOT registered on
     Cena.Student.Api.Host (rg returns zero matches for any hint route).
  3. ScaffoldingService and HintGenerator are not injected anywhere in
     Cena.Student.Api.Host (zero references).

The Vue runner page POSTs to /api/sessions/{id}/question/{qid}/hint at
line 106 of pages/session/[sessionId]/index.vue and in production will
get a 404. The pedagogical fix is dead code. Every learner using the web
app — novice or expert — gets the same bare multiple-choice question with
zero scaffolding, zero hints, and zero worked examples. The Wilson 2019
85% rule cannot apply without scaffolding fade.

Citations:
  Sweller, van Merriënboer & Paas (1998), Educational Psychology Review
  10(3) 251-296, DOI: 10.1023/A:1022193728205 — Worked Example Effect.
  Renkl & Atkinson (2003), Educational Psychologist 38(1) 15-22,
  DOI: 10.1207/S15326985EP3801_3 — faded examples sequence.
  Kalyuga, Ayres, Chandler & Sweller (2003), Educational Psychologist
  38(1) 23-31, DOI: 10.1207/S15326985EP3801_4 — Expertise Reversal Effect.

Files to read first:
  - src/api/Cena.Student.Api.Host/Endpoints/SessionEndpoints.cs:452-518
  - src/api/Cena.Api.Contracts/Sessions/SessionDtos.cs:117-150
  - src/actors/Cena.Actors/Mastery/ScaffoldingService.cs
  - src/actors/Cena.Actors/Hint/HintGenerator.cs
  - src/actors/Cena.Actors.Tests/Session/SessionHintEndpointTests.cs

Definition of done:
  - Cena.Student.Api.Host registers IScaffoldingService and IHintGenerator
    in Program.cs DI graph.
  - GET /current-question populates ScaffoldingLevel, WorkedExample,
    HintsAvailable, HintsRemaining from real BKT mastery + per-question
    hint usage from queue.HintsUsedByQuestion.
  - POST /api/sessions/{id}/question/{qid}/hint registered, returns
    SessionHintResponseDto computed from HintGenerator with the
    question's BuildHintOptionStates.
  - LearningSessionQueueProjection.HintsUsedByQuestion incremented and
    persisted on each hint request.
  - BKT credit attenuation via BktParameters.AdjustForHints uses the
    real hint count.
  - HTTP-level integration test against a real Postgres+Marten test
    fixture: POST /hint returns 200 with non-empty HintText.
  - Integration test: GET /current-question returns non-null
    ScaffoldingLevel for a student with mastery 0.2 on the concept,
    and 'None' for mastery 0.9.

Reporting requirements:
  - Branch <worker>/<task-id>-find-pedagogy-012-rest-scaffolding
  - Push, then complete with summary + branch + test names


## Evidence & context

- Lens report: `docs/reviews/agent-pedagogy-reverify-2026-04-11.md`
- Merged report: `docs/reviews/cena-review-2026-04-11-reverify.md`
- Queue ID: `t_db9eaa46f096`

## Definition of Done

1. Root cause identified and fixed (not symptoms)
2. Regression test added and wired into CI (`.github/workflows/`)
3. Structured log emitted on the error path
4. `dotnet build` succeeds with 0 errors
5. All existing tests pass (`dotnet test`)
6. Code review by coordinator (`claude-code`)
