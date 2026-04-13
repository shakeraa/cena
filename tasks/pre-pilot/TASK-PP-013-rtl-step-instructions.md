# PP-013: Add RTL Direction Handling on Step Solver Instructions

- **Priority**: Medium — visual confusion for Arabic/Hebrew students
- **Complexity**: Senior engineer — Vue template bidi fixes
- **Source**: Expert panel review § Step Solver UI (Tamar)

## Problem

`StepSolverCard.vue` at `src/student/full-version/src/components/session/StepSolverCard.vue:86` wraps the question stem in `<bdi dir="ltr">` — correct for math content. However, the step instructions and hints rendered inside `StepInput.vue` may be in Arabic or Hebrew. These instruction strings are not wrapped with explicit direction handling.

When an Arabic instruction like "حلل الطرف الأيسر" (factor the left side) appears next to a mathematical expression, the browser's bidi algorithm may misorder the text, especially when the instruction contains embedded LTR content like variable names or operators.

## Scope

1. In `StepInput.vue`, wrap step instruction text in `<bdi dir="auto">` (or `dir="rtl"` if the locale is known to be Arabic/Hebrew)
2. Keep mathematical expressions within instructions in `<bdi dir="ltr">`
3. Wrap hint text in the same manner
4. Test with mixed-direction content: an Arabic instruction containing a LaTeX expression

Example rendering:
```html
<div class="step-instruction">
  <bdi :dir="locale === 'ar' || locale === 'he' ? 'rtl' : 'ltr'">
    {{ step.instruction }}
  </bdi>
  <bdi dir="ltr" v-if="step.fadedExample">
    {{ step.fadedExample }}
  </bdi>
</div>
```

## Files to Modify

- `src/student/full-version/src/components/session/StepInput.vue` — add bdi wrapping on instructions and hints
- `src/student/full-version/src/components/session/StepSolverCard.vue` — verify stem direction is correct (already is)

## Acceptance Criteria

- [ ] Step instructions in Arabic/Hebrew render with correct RTL direction
- [ ] Mathematical expressions within instructions remain LTR
- [ ] Hints follow the same direction rules
- [ ] Visual test: an Arabic instruction "حلل المعادلة x² + 2x + 1 = 0" renders with Arabic RTL and equation LTR
