# TASK-PRR-206: Student — route step-solver items to `StepSolverCard` + `MathInput`

**Priority**: P0 — ship-blocker
**Effort**: L — 5-7 days
**Lens consensus**: persona-cogsci, persona-educator, persona-a11y, persona-ministry, persona-sre
**Source docs**: `src/student/full-version/src/components/session/StepSolverCard.vue`, `src/student/full-version/src/components/session/StepInput.vue`, `src/student/full-version/src/components/session/MathInput.vue`, `docs/research/cena-question-engine-architecture-2026-04-12.md:§7`
**Assignee hint**: claude-subagent-runner-ui (student Vue) — coordinate with prr-205 owner
**Tags**: source=pre-release-review-2026-04-20, epic=epic-prr-e, lens=cogsci+educator+a11y
**Status**: Not Started
**Source**: Epic PRR-E, 2026-04-20
**Tier**: mvp

---

## Goal

Extend the session runner to render step-solver questions via the already-built `StepSolverCard` (+ `StepInput`, `MathInput`) components — today the runner is MCQ-only and step-solver components have zero references from `pages/session`. Each submitted step calls the existing CAS step-verify API; feedback loops through `AnswerFeedback.vue`; per-step hint ladder integrates with prr-205. Physics FBD construct mode is prr-208.

## Files

- `src/student/full-version/src/pages/session/[sessionId]/index.vue` — dispatch on `question.type ∈ {MCQ, step-solver}` to render `<QuestionCard>` vs `<StepSolverCard>`
- `src/student/full-version/src/api/sessions.ts` — `postStep(sessionId, questionId, stepIndex, expression)`
- `src/student/full-version/src/composables/useStepSolver.ts` (new) — step state machine (pending → typing → submitted → verified|rejected → next)
- `src/student/full-version/src/components/session/StepSolverCard.vue` — wire events `step-submit`, `request-hint-rung`
- `src/student/full-version/src/components/session/MathInput.vue` — confirm RTL-safe LaTeX entry per user memory `feedback_math_always_ltr`
- `src/student/full-version/src/api/types/common.ts` — add `StepSolverQuestion` discriminator per engine doc §2.2
- `src/actors/Cena.Actors/Sessions/SessionQuestionDispatcher.cs` — emit the correct DTO shape based on `QuestionDocument.Type`
- `src/student/full-version/tests/unit/StepSolverCard.integration.spec.ts` (new)

## Non-negotiable references

- ADR-0002 (SymPy oracle) — every step verified by CAS via existing `ICasRouterService` with `mode: real_field|complex_field|numeric_approx` per step spec.
- ADR-0045 (hint tier) — per-step hint rungs route through prr-203 endpoint the same way question-level hints do.
- Math-always-LTR — `MathInput` internal rendering wraps math in `<bdi dir="ltr">`; input direction matches.
- `HintAdjustedBktService` — scaffolded step attempts (with hints consumed) update mastery with the rung-aware penalty.

## Definition of Done

- Runner dispatches correctly based on `question.type`; MCQ path unchanged for regression safety.
- `StepSolverCard` renders scaffolding level (`Full` / `Partial` / `Minimal` / `Exploratory`) per the server-provided scaffolding level.
- Step submission calls the CAS step-verify endpoint; on `correct: true`, the next step unlocks; on `false`, AST-diff feedback (per engine doc §20.3) renders via `AnswerFeedback` with misconception tag.
- Hint rung requests on a step route through the same prr-203 endpoint with a `stepIndex` parameter.
- `MathInput` RTL: Hebrew/Arabic UI locale with math in the input renders left-to-right within an RTL page; caret and input direction verified manually + in test.
- Accessibility: each step input has an aria-label describing the step ("step 2: isolate the variable"); the AST-diff feedback is announced via aria-live; MathInput has a voice-input fallback hook (actual voice implementation is a separate task — this task lands the hook).
- No MCQ questions regress (smoke test exercises 3 MCQ questions + 3 step-solver questions end-to-end).
- `ScaffoldingLevel.Exploratory` respected: blank-canvas rendering, CAS verifies final answer only; wrong answer re-renders the same problem in `Full` mode with divergence highlight (engine doc §20.3 productive-failure pattern).
- Integration test: a seeded step-solver question with 3 steps → student submits correct → next unlocks → submits wrong → AST-diff feedback appears → requests L1 hint → re-submits correct → completes.
- Student-web build clean; Axe CI passes.

## Reporting

complete via: `node .agentdb/kimi-queue.js complete <id> --worker claude-subagent-runner-ui --result "<branch>"`

---

## Multi-persona lens review (embedded)

- **persona-cogsci**: AST-diff feedback + productive-failure mode surfaced; scaffolded BKT update owned here.
- **persona-educator**: step rendering respects methodology — a Halabi student sees Halabi step labels. Owned here (translation keys per methodology).
- **persona-a11y**: MathInput aria + voice fallback hook owned here; step-level aria-live owned here.
- **persona-ministry**: step content for Bagrut-track-tagged questions uses Ministry terminology — verified via translation review on the PR.
- **persona-sre**: CAS step-verify call has a circuit breaker — on CAS sidecar down, the step input shows "checking later" and queues; on recovery, verifies. No silent accept/reject.

## Related

- Parent epic: [EPIC-PRR-E](./EPIC-PRR-E-question-engine-ux-integration.md)
- Depends on: prr-203 (hint ladder), existing CAS step-verify endpoint
- Adjacent: prr-205 (runner ladder), prr-208 (FBD construct)

## Implementation Protocol — Senior Architect

See [epic file](./EPIC-PRR-E-question-engine-ux-integration.md#implementation-protocol--senior-architect).
