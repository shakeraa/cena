# RDY-013: Worked Examples UI Rendering

- **Priority**: High — core scaffolding technique, backend complete, UI missing
- **Complexity**: Mid frontend engineer
- **Source**: Expert panel audit — Nadia (Pedagogy), Lior (UX), Tamar (A11y)
- **Tier**: 2
- **Effort**: 1-2 weeks (revised from 3-5 days per Rami's review — faded mode validation is complex)

## Problem

Backend scaffolding service sets `ShowWorkedExample=true` at Full scaffolding level (mastery < 0.20). `QuestionCard.vue` references `props.question.workedExample` but no rendering logic exists. Faded worked examples (Renkl & Atkinson) are one of the most evidence-backed scaffolding techniques in ITS literature — but invisible to students.

**Tamar's requirement**: Each step needs `aria-label`. Fading must use opacity (not `display:none`) to stay in the accessibility tree.

## Scope

### 1. WorkedExample component

Create `WorkedExamplePanel.vue` that renders:
- Problem statement (same as question)
- Step-by-step solution (from `workedExample.steps[]`)
- Each step has: description, math expression (KaTeX), explanation
- Progressive reveal: show one step at a time with "Next step" button

### 2. Faded worked example mode

At Partial scaffolding (mastery 0.20-0.40):
- Show problem + some steps completed
- Leave later steps blank for student to fill
- Student input validated against expected step

### 3. Integration in QuestionCard

- When `props.question.workedExample` is present and scaffolding level is Full/Partial:
  - Show WorkedExamplePanel before or alongside the question
- When scaffolding level is HintsOnly/None:
  - Do not show worked example

### 4. Accessibility

- Each step: `aria-label="Step N: [description]"`
- Faded steps: `aria-hidden="false"` with reduced opacity (not removed from DOM)
- "Next step" button: keyboard-accessible, focus managed
- Math in steps: `<bdi dir="ltr">` wrapper

## Files to Create/Modify

- New: `src/student/full-version/src/components/session/WorkedExamplePanel.vue`
- `src/student/full-version/src/components/session/QuestionCard.vue` — integrate WorkedExamplePanel
- New: `src/student/full-version/tests/e2e/worked-examples.spec.ts`

## Acceptance Criteria

- [ ] Worked example renders when scaffolding=Full and workedExample data present
- [ ] Steps reveal progressively with "Next step" interaction
- [ ] Faded example renders at Partial scaffolding (blanks for student to fill)
- [ ] Each step has `aria-label` and is keyboard-navigable
- [ ] Math expressions in steps are LTR (`<bdi dir="ltr">`)
- [ ] No worked example shown at HintsOnly/None scaffolding levels
- [ ] E2E test validates rendering at each scaffolding level
- [ ] Step progression announced via `aria-live="polite"`: "Step X of Y: [description]"
- [ ] Active step marked with `aria-current="step"`
- [ ] Faded steps use `aria-disabled="true"` (not hidden from accessibility tree)
- [ ] Answer feedback for partial scaffolding announces expected vs. student answer for screen readers

> **Cross-review (Tamar)**: Original task lacked screen reader announcement for step progression. Added `aria-live`, `aria-current`, `aria-disabled` criteria.
>
> **Cross-review (Nadia)**: ScaffoldingService.cs bug — `ShowWorkedExample` is `false` at Partial level (line ~55) but should be `true` (faded worked examples are the Partial-level technique per Renkl & Atkinson). Fix in backend before wiring frontend.
