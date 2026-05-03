# TASK-PRR-208: Student — wire `FreeBodyDiagramConstruct` for physics question items

**Priority**: P1
**Effort**: M — 4-5 days
**Lens consensus**: persona-cogsci, persona-educator, persona-a11y, persona-ministry
**Source docs**: `src/student/full-version/src/components/session/FreeBodyDiagramConstruct.vue`, `docs/research/cena-question-engine-architecture-2026-04-12.md:§6` + `§20.3` (FBD Construct mode)
**Assignee hint**: claude-subagent-runner-ui (student Vue)
**Tags**: source=pre-release-review-2026-04-20, epic=epic-prr-e, lens=cogsci+educator+a11y+ministry
**Status**: Not Started
**Source**: Epic PRR-E, 2026-04-20
**Tier**: mvp

---

## Goal

Route physics questions with `FigureSpec.Type = PhysicsDiagramSpec` and `DiagramMode = Construct` to `FreeBodyDiagramConstruct.vue`. Student drags force arrows onto the scene; CAS verifies the force-vector decomposition. Without this task, physics construct items are unreachable in the runner. Market-differentiator per engine doc §20.3 — nobody in Hebrew/Arabic market has interactive FBD assessment.

## Files

- `src/student/full-version/src/pages/session/[sessionId]/index.vue` — dispatch on `question.figureSpec.diagramMode === 'Construct'` to render `<FreeBodyDiagramConstruct>`
- `src/student/full-version/src/components/session/FreeBodyDiagramConstruct.vue` — wire `submit`, `request-hint-rung` events
- `src/student/full-version/src/api/sessions.ts` — `postFbdSubmission(sessionId, questionId, forces)`
- `src/actors/Cena.Actors/Physics/FbdVerifier.cs` — CAS-backed verification of submitted force vectors (direction + magnitude + components)
- `src/student/full-version/tests/unit/FreeBodyDiagramConstruct.integration.spec.ts`

## Non-negotiable references

- ADR-0002 — force-vector equivalence verified via CAS (decomposition is algebra, fully in scope).
- §6.3 of engine doc — correctness rules: Σ F = 0 (equilibrium) or Σ F = ma (acceleration); forces consistent with body type.
- a11y: keyboard-draggable alternative to mouse drag per §20.3 (engine doc hedges on drag vs numeric input — this task lands keyboard input as non-negotiable fallback).

## Definition of Done

- Runner renders FBD construct for physics questions tagged `DiagramMode = Construct`; display-mode physics questions unchanged.
- Force palette: student can add `gravity | normal | friction | applied | tension`; each force has editable magnitude and direction.
- Keyboard alternative: force type selectable via arrow keys + Enter; magnitude via Up/Down; direction via shift+arrow; submit via Ctrl+Enter.
- CAS verification: `FbdVerifier` returns per-force verdict (correct direction, correct magnitude, correct components) + overall equilibrium/acceleration check.
- Feedback renders via `AnswerFeedback` with per-force breakdown ("friction direction wrong — it opposes motion").
- Methodology-aware labeling (Halabi vs Rabinovitch physics terminology) per ADR-0040.
- Hint ladder integrates: L1 = "re-check the direction of [friction]" template; L2 = method suggestion; L3 = annotated worked example via Sonnet.
- Integration test: load seeded construct item → student adds wrong friction direction → submit → feedback correct → student corrects → submit → verified.
- Axe CI passes; keyboard-only flow verified.
- Student-web build clean.

## Reporting

complete via: `node .agentdb/kimi-queue.js complete <id> --worker claude-subagent-runner-ui --result "<branch>"`

---

## Multi-persona lens review (embedded)

- **persona-cogsci**: per-force diagnostic feedback maps to physics misconception catalog entries (session-scoped).
- **persona-educator**: methodology terminology verified on PR review.
- **persona-a11y**: keyboard-only flow mandatory, not optional. Owned here.
- **persona-ministry**: Bagrut physics track alignment verified; construct items are AI-authored CAS-gated recreations per ADR-0043.

## Related

- Parent epic: [EPIC-PRR-E](./EPIC-PRR-E-question-engine-ux-integration.md)
- Depends on: existing `PhysicsDiagramService`, prr-203 (hint ladder)

## Implementation Protocol — Senior Architect

See [epic file](./EPIC-PRR-E-question-engine-ux-integration.md#implementation-protocol--senior-architect).
