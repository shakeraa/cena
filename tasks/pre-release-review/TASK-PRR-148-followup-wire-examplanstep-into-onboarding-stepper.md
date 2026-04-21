---
id: prr-148-followup
re-opens: prr-148
priority: P1
tier: mvp
tags: [epic-prr-a, pedagogy, onboarding, false-done]
---

# prr-148-followup — Wire `ExamPlanStep` into onboarding stepper

## Why this exists

prr-148 ("Student-input UI for AdaptiveScheduler — deadline + weekly time
budget") was marked done and moved to `done/` in an earlier batch. The
underlying Vue component `ExamPlanStep.vue` was authored, but the
onboarding stepper in `src/student/full-version/src/pages/onboarding.vue`
never mounted it as a step. Students cannot currently supply the inputs
the AdaptiveScheduler (prr-149) consumes — so prr-148's claimed outcome
("student can set exam-date + weekly minutes") is **false**.

This task corrects the false-done and ships the actual wiring.

## Goal

When a student reaches the onboarding flow, after the self-assessment step
and before `confirm`, they land on an `exam-plan` step rendered by
`ExamPlanStep.vue`. The inputs persist into `onboardingStore` and flow
into the existing `POST /api/v1/onboarding/complete` contract so the
scheduler receives them on session start.

## Files

- `src/student/full-version/src/pages/onboarding.vue` — add `exam-plan`
  to the step rotation between `self-assessment` and `confirm`; mount
  `<ExamPlanStep>` when `onboarding.step === 'exam-plan'` and route
  `@complete` → `onboarding.next()`.
- `src/student/full-version/src/stores/onboardingStore.ts` — add
  `'exam-plan'` to the `step` union; add `examDate`, `weeklyStudyMinutes`
  fields; extend `next()` / `back()` rotation; include the new fields in
  the submit payload.
- `src/student/full-version/src/components/onboarding/ExamPlanStep.vue` —
  verify the component emits `@complete` (or add it) consistent with
  the existing self-contained step pattern (SelfAssessmentStep,
  DiagnosticQuiz). Inner Skip+Continue row, no outer Next (handled via
  `stepOwnsAdvance` guard added in the sidebar fix dated 2026-04-21).
- `src/api/Cena.Student.Api.Host/Endpoints/OnboardingEndpoints.cs` —
  confirm the DTO accepts `examDate` (ISO-8601 date) and
  `weeklyStudyMinutes` (integer ≥0, ≤7*24*60); reject out-of-range.
- `src/student/full-version/tests/unit/onboardingStore.spec.ts` — add
  tests covering the new step rotation + payload fields.

## Definition of Done

- Student reaching onboarding sees `exam-plan` as a step, lands on it
  after self-assessment, before confirm.
- Submitting the onboarding flow sends `examDate` + `weeklyStudyMinutes`
  to the backend; server-side validation bounds the values.
- `AdaptiveScheduler` (prr-149) reads these fields on the first session
  and uses them as the deadline + weekly-budget inputs it was designed
  for.
- Playwright happy-path spec covers: (a) student fills exam-plan, (b)
  student skips exam-plan (values default to a sane TBD sentinel).
- Outer Back/Next bar does NOT double up with the step's internal
  Skip/Continue (already handled via `stepOwnsAdvance` — add
  `'exam-plan'` to that computed list).

## Senior-architect protocol

Ask *why* the step was skipped in the original prr-148: was the
component authored-but-not-wired, or was the store contract incomplete?
The fix must not silently widen scope — if `ExamPlanStep.vue` turns out
to be a stub without real state, this task ends and a new task is filed
for the component. Stop, don't ship.

## Non-negotiables

- Banned-mechanics scanner green on any new copy (no "hurry — exam in N
  days!" framing; use prr-006 / ADR-0048 neutral readiness language).
- Math in `<bdi dir="ltr">` if the exam-date UI renders any math.
- ADR-0003 preserved — no session-scoped misconception data touched.
- No files >500 LOC.

## Reporting

```
git add -A
git commit -m "feat(prr-148-followup): wire ExamPlanStep into onboarding stepper — fixes false-done"
git push
```

Skip queue-complete (no SQLite row for this task; coordinator tracks by
file move to `done/` on landing).
