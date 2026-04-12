---
id: FIND-PEDAGOGY-011
task_id: t_fb95d37042e7
severity: P0 — Critical
lens: pedagogy
tags: [reverify, pedagogy, stub, fake-fix, regression]
status: pending
assignee: unassigned
created: 2026-04-11
type: regression
type: fake-fix
---

# FIND-pedagogy-011: MSW dev mock returns binary feedback + linear delta — defeats every pedagogy fix

## Summary

MSW dev mock returns binary feedback + linear delta — defeats every pedagogy fix

## Severity

**P0 — Critical** — REGRESSION — FAKE-FIX

## Requirements

The fix for this task MUST be production-grade:

- **No stubs, no canned data, no hardcoded objects, no `NotImplementedException`**
- **Labels must match actual data** — if a button says "Save", it must persist; if a metric says "tokens", it must count real tokens
- **Verify E2E** — query the DB, call the API, render the UI, compare field names
- **Include a CI-wired regression test** that fails on the current (buggy) commit and passes on the fix
- **Add a structured log line** on the error path so a re-regression is detectable in production

## Task body

FIND-pedagogy-011: Replace MSW dev mock binary feedback + linear delta with real shape
related_prior_finding: FIND-pedagogy-001, FIND-pedagogy-003
classification: fake-fix (dev path)

The .NET SessionEndpoints fix for FIND-pedagogy-001 (formative feedback
with explanation) and FIND-pedagogy-003 (real BKT posterior) is invisible
to anyone running the student web in dev mode because MSW (Mock Service
Worker) intercepts the /api/sessions calls and returns the OLD broken
shape:

src/student/full-version/src/plugins/fake-api/handlers/student-sessions/index.ts:
  - Line 20: const CANNED: CannedQuestion[] — 5 hardcoded questions, no
    Explanation, no DistractorRationales, no LearningObjectiveId.
  - Line 211: feedback: correct ? 'Correct! Great work.' : `Not quite — the answer was "${currentQ.correctAnswer}".`
  - Line 213: masteryDelta: correct ? 0.05 : -0.02
    (the FIND-pedagogy-003 linear delta, in the data the QA team
    actually exercises)

The Vue AnswerFeedback component (line 87) renders feedback.feedback as
raw text below the i18n heading. With the MSW mock active, the student
sees only "Correct! Great work." or the wrong-answer literal — there is
no explanation field at all. Demo screens to stakeholders use the broken
path. The user rule "no stubs, no canned" (feedback_no_stubs_production_grade
2026-04-11) is violated by a file that literally has a constant called
CANNED.

Files to read first:
  - src/student/full-version/src/plugins/fake-api/handlers/student-sessions/index.ts
  - src/api/Cena.Student.Api.Host/Endpoints/SessionEndpoints.cs (lines 1062-1118 for shape)
  - src/api/Cena.Api.Contracts/Sessions/SessionDtos.cs:175-183
  - src/student/full-version/src/components/session/AnswerFeedback.vue
  - feedback_no_stubs_production_grade memory file

Definition of done:
  - CANNED questions include real Explanation strings and per-option
    DistractorRationales maps. Replace the constant name CANNED with
    something descriptive (it is allowed dev-only data, not a stub).
  - MSW response carries Explanation and DistractorRationale fields
    matching SessionAnswerResponseDto exactly.
  - The literal feedback string "Correct! Great work." removed; ship
    the same short pill the .NET endpoint ships ("Correct" / "Not quite")
    OR remove the field entirely (see FIND-pedagogy-017).
  - Mastery delta computed from a deterministic BKT shim function that
    matches BktService.Update for the same prior + slip + guess + isCorrect
    inputs. NOT a hardcoded constant.
  - Contract Vitest passes that diffs MSW response shape against the
    TypeScript SessionAnswerResponseDto type.
  - Behavior Vitest: wrong answer ⇒ distractorRationale present.
  - Behavior Vitest: 5 consecutive correct answers do NOT produce a
    linear mastery trajectory (assert non-linear BKT curve).

Reporting requirements:
  - Branch <worker>/<task-id>-find-pedagogy-011-msw-real-shape
  - Push, then complete with summary + branch + test names


## Evidence & context

- Lens report: `docs/reviews/agent-pedagogy-reverify-2026-04-11.md`
- Merged report: `docs/reviews/cena-review-2026-04-11-reverify.md`
- Queue ID: `t_fb95d37042e7`

## Definition of Done

1. Root cause identified and fixed (not symptoms)
2. Regression test added and wired into CI (`.github/workflows/`)
3. Structured log emitted on the error path
4. `dotnet build` succeeds with 0 errors
5. All existing tests pass (`dotnet test`)
6. Code review by coordinator (`claude-code`)
