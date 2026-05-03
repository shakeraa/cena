---
id: FIND-UX-011
task_id: t_6b1c4fbe96d2
severity: P1 — High
lens: ux
tags: [frontend, student-web, silent-failure, a11y]
status: pending
assignee: unassigned
created: 2026-04-11
---

# FIND-ux-011 + FIND-ux-012: Student swallow-and-smile failures — social vote/accept silently drop errors, keyboard shortcut eats '?' in tutor textarea

## Summary

FIND-ux-011 + FIND-ux-012: Student swallow-and-smile failures — social vote/accept silently drop errors, keyboard shortcut eats '?' in tutor textarea

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

## Goal
Fix two silent-failure patterns on the student web:

1. src/student/full-version/src/pages/social/friends.vue:24-32 and pages/social/peers.vue:24-35 — handleAccept and handleVote catch errors with an empty body (// swallow / // swallow — would surface a snackbar in 12b). The UI has no way to know the action failed.
2. src/student/full-version/src/components/shell/ShellShortcuts.vue — the keyboard shortcut handler intercepts '?' and the 'g h/s/p/t/k/l/n' sequences even when a textarea / input / contenteditable is focused. Typing 'What is 2+2?' in the tutor input loses the '?' and pops the shortcuts modal mid-message.

## Files to modify
- src/student/full-version/src/pages/social/friends.vue — on caught error, set an error state and surface a toast / VAlert so the user sees the failure.
- src/student/full-version/src/pages/social/peers.vue — same.
- src/student/full-version/src/components/shell/ShellShortcuts.vue — in the global keydown listener, return early if document.activeElement.tagName is INPUT / TEXTAREA / SELECT or the element has contenteditable='true'.

## Definition of done
- Forcing a 500 on POST /api/social/friends/:id/accept produces a visible error toast and leaves the list unchanged.
- Same for /vote.
- Typing 'What is 2 + 2?' in the tutor textarea produces the literal value 'What is 2 + 2?' and does NOT open any modal.
- Typing 'g h' in the textarea stays in the textarea instead of navigating.

## Evidence
- docs/reviews/agent-5-ux-findings.md FIND-ux-011, FIND-ux-012

## Severity: p1


## Evidence & context

- Lens report: `docs/reviews/agent-ux-reverify-2026-04-11.md`
- Merged report: `docs/reviews/cena-review-2026-04-11-reverify.md`
- Queue ID: `t_6b1c4fbe96d2`

## Definition of Done

1. Root cause identified and fixed (not symptoms)
2. Regression test added and wired into CI (`.github/workflows/`)
3. Structured log emitted on the error path
4. `dotnet build` succeeds with 0 errors
5. All existing tests pass (`dotnet test`)
6. Code review by coordinator (`claude-code`)
