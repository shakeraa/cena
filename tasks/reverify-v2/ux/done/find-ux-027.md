---
id: FIND-UX-027
task_id: t_4b64574b8e08
severity: P1 — High
lens: ux
tags: [reverify, ux, a11y, wcag2.2, target-size]
status: pending
assignee: unassigned
created: 2026-04-11
---

# FIND-ux-027: Admin password eye icon is a 16x16 hit target — wrap in IconBtn (subsumes FIND-ux-018)

## Summary

Admin password eye icon is a 16x16 hit target — wrap in IconBtn (subsumes FIND-ux-018)

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

## FIND-ux-027: Admin password eye icon is a 16x16 hit target — wrap in IconBtn (subsumes FIND-ux-018)

**Severity**: p1
**Lens**: ux
**Category**: a11y, WCAG-2.2-AA
**Related prior finding**: FIND-ux-018 (admin "Append" accessible name leak — same element)

## Files
- src/admin/full-version/src/pages/login.vue:193-203 (password field)
- src/admin/full-version/src/pages/register.vue (same field, copy/paste)
- src/admin/full-version/src/pages/forgot-password.vue (likely the same)

## Evidence
- Lighthouse: `target-size` audit score=0 on admin /login. Element is `<i class="tabler-eye">` at 16x16 px. WCAG 2.5.5 requires ≥24x24.
- Snapshot also shows the button is labeled "Append" (the Vuetify slot default name) — that's FIND-ux-018, same element.

## Definition of Done
1. Replace `:append-inner-icon` + `@click:append-inner` with a `<template #append-inner>` slot containing `<VBtn icon variant="text" size="x-small" :aria-label="...">`.
2. The button hit area is ≥24×24 CSS px (Vuetify x-small renders ~32×32).
3. Lighthouse `target-size` audit on /login = 1.0. axe `target-size` count = 0.
4. The button's `aria-label` is i18n'd and reflects the toggle state ("Show password" / "Hide password").
5. Same fix applied to register.vue and forgot-password.vue if they have the same field.
6. **This subsumes FIND-ux-018** (the "Append" leak) — when this lands, mark FIND-ux-018 done.

## Source
docs/reviews/agent-ux-reverify-2026-04-11.md#FIND-ux-027


## Evidence & context

- Lens report: `docs/reviews/agent-ux-reverify-2026-04-11.md`
- Merged report: `docs/reviews/cena-review-2026-04-11-reverify.md`
- Queue ID: `t_4b64574b8e08`

## Definition of Done

1. Root cause identified and fixed (not symptoms)
2. Regression test added and wired into CI (`.github/workflows/`)
3. Structured log emitted on the error path
4. `dotnet build` succeeds with 0 errors
5. All existing tests pass (`dotnet test`)
6. Code review by coordinator (`claude-code`)
