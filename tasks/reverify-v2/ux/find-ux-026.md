---
id: FIND-UX-026
task_id: t_c92f6a542e75
severity: P1 — High
lens: ux
tags: [reverify, ux, a11y, wcag2.2]
status: pending
assignee: unassigned
created: 2026-04-11
---

# FIND-ux-026: Admin login heading hierarchy is H1 -> H4 (skips H2 + H3)

## Summary

Admin login heading hierarchy is H1 -> H4 (skips H2 + H3)

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

## FIND-ux-026: Admin login heading hierarchy is H1 → H4 (skips H2 + H3)

**Severity**: p1
**Lens**: ux
**Category**: a11y, WCAG-2.2-AA

## Files
- src/admin/full-version/src/pages/login.vue:106-155
- Same pattern likely repeats in src/admin/full-version/src/pages/forgot-password.vue, register.vue — verify with `grep -n '<h[1-6]' src/admin/full-version/src/pages/{login,register,forgot-password}.vue`

## Evidence
- Lighthouse: `heading-order` audit score=0 on admin /login. JSON at docs/reviews/screenshots/reverify-2026-04-11/ux/lighthouse/admin-login.json.
- File: login.vue:109 has `<h1 class="auth-title">Cena Admin</h1>` and login.vue:153 has `<h4 class="text-h4">Cena Admin</h4>`. Two competing headings, skipping H2/H3.

## Definition of Done
1. Each auth page has exactly one H1 = the page's content title.
2. Sequential heading order (no skipped levels).
3. Lighthouse `heading-order` audit returns 1.0 on /login, /forgot-password, /register, /dashboards/admin.
4. Best practice: demote the logo H1 to a non-heading element. The brand mark belongs in a `<header>` landmark, not as an H1.

## Source
docs/reviews/agent-ux-reverify-2026-04-11.md#FIND-ux-026


## Evidence & context

- Lens report: `docs/reviews/agent-ux-reverify-2026-04-11.md`
- Merged report: `docs/reviews/cena-review-2026-04-11-reverify.md`
- Queue ID: `t_c92f6a542e75`

## Definition of Done

1. Root cause identified and fixed (not symptoms)
2. Regression test added and wired into CI (`.github/workflows/`)
3. Structured log emitted on the error path
4. `dotnet build` succeeds with 0 errors
5. All existing tests pass (`dotnet test`)
6. Code review by coordinator (`claude-code`)
