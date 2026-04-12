---
id: FIND-UX-030
task_id: t_ac2f39927d61
severity: P1 — High
lens: ux
tags: [reverify, ux, a11y, wcag2.2]
status: pending
assignee: unassigned
created: 2026-04-11
---

# FIND-ux-030: Session-setup subject chips have no role/aria-pressed — multi-select unusable for SR users

## Summary

Session-setup subject chips have no role/aria-pressed — multi-select unusable for SR users

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

## FIND-ux-030: Session-setup subject chips have no role/aria-pressed — multi-select unusable for SR users

**Severity**: p1
**Lens**: ux
**Category**: a11y, WCAG-2.2-AA

## Files
- src/student/full-version/src/components/session/SessionSetupForm.vue:59-69 (subject chips)
- locale files (add `session.setup.subjectChipAria` key with EN/AR/HE)

## Evidence
- Snapshot a11y tree at /session shows:
  ```
  - generic [cursor=pointer]: מתמטיקה  ← Math chip (just a span!)
  - generic [cursor=pointer]: פיזיקה
  ...
  ```
- DOM probe: chips are `<span>` with `tabindex=0` (Vuetify VChip default) but `role: null, ariaPressed: null, ariaSelected: null`.
- The Start Session button stays disabled until at least one subject is chosen, so SR users have NO indication of how to enable it.

## Definition of Done
1. Each subject toggle is a real interactive control with `role="button"` (or is wrapped in a `<button>`/`<input type="checkbox">`).
2. `aria-pressed` (or `aria-checked`) reflects the current selection state.
3. axe-core scan of /session reports zero `aria-required-attr` or `button-name` violations on subject chips.
4. Add `tests/e2e/a11y-session-setup.spec.ts` that uses keyboard-only flow (Tab + Space) to select Math + Physics, then presses Tab+Enter on the Start Session button, and asserts a session POST is fired.
5. Same fix applied to the duration toggle and mode toggle if they share the pattern (verify with axe).

## Source
docs/reviews/agent-ux-reverify-2026-04-11.md#FIND-ux-030


## Evidence & context

- Lens report: `docs/reviews/agent-ux-reverify-2026-04-11.md`
- Merged report: `docs/reviews/cena-review-2026-04-11-reverify.md`
- Queue ID: `t_ac2f39927d61`

## Definition of Done

1. Root cause identified and fixed (not symptoms)
2. Regression test added and wired into CI (`.github/workflows/`)
3. Structured log emitted on the error path
4. `dotnet build` succeeds with 0 errors
5. All existing tests pass (`dotnet test`)
6. Code review by coordinator (`claude-code`)
