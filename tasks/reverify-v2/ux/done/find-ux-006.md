---
id: FIND-UX-006
task_id: t_f7bb146a546b
severity: P2 — Normal
lens: ux
tags: [review-followup]
status: pending
assignee: unassigned
created: 2026-04-11
---

# FIND-ux-006c: Student forgot-password.vue rewrite to consume real POST /api/auth/password-reset

## Summary

FIND-ux-006c: Student forgot-password.vue rewrite to consume real POST /api/auth/password-reset

## Severity

**P2 — Normal**

## Requirements

The fix for this task MUST be production-grade:

- **No stubs, no canned data, no hardcoded objects, no `NotImplementedException`**
- **Labels must match actual data** — if a button says "Save", it must persist; if a metric says "tokens", it must count real tokens
- **Verify E2E** — query the DB, call the API, render the UI, compare field names
- **Include a CI-wired regression test** that fails on the current (buggy) commit and passes on the fix
- **Add a structured log line** on the error path so a re-regression is detectable in production

## Task body

**Source**: Follow-up from FIND-ux-006b (t_fb998eeb8af1), the backend endpoint shipped in the coordinator session 2026-04-11. The sub-agent's spawn prompt restricted that run to the backend, so the frontend-side pieces are still open.

**Problem**: The Student web forgot-password page is currently an honest 'unavailable' card (from FIND-ux-006 / ux-bundle commit 85b17e2). Now that POST /api/auth/password-reset exists on Cena.Student.Api.Host, the Vue page should become a real form.

**Files to touch**:
  - src/student/full-version/src/pages/forgot-password.vue — rewrite form back in, wire to the new endpoint
  - src/student/full-version/src/api/auth.ts (or wherever Axios calls live) — add the POST helper
  - src/student/full-version/src/plugins/i18n/locales/{en,ar,he}.json — reuse existing password-reset strings or add new ones (don't break the i18n parity established in FIND-ux-007/014)
  - src/student/full-version/tests/e2e/stuw04a.spec.ts — flip the previous 'no outbound request' assertion to 'one POST /api/auth/password-reset, 204'

**Definition of Done**:
  - [ ] The Vue page submits a real POST to /api/auth/password-reset
  - [ ] On 204 (both 'link generated' and 'unknown email' — indistinguishable), show a generic 'If an account with that email exists, a reset link has been sent' message — NEVER confirm existence
  - [ ] On 429 (rate limited), show a 'Too many requests, try again in a few minutes' message
  - [ ] On 503 (Firebase unavailable), show a 'Service temporarily unavailable' message
  - [ ] i18n keys exist in en/ar/he with real translations
  - [ ] E2E test asserts the form submits, receives 204, and shows the generic success message
  - [ ] check:fake-api guard still passes
  - [ ] Branch: worker/task-id-ux-006c-forgot-password-frontend

**Security rationale**: same as FIND-ux-006b — never distinguish 'email exists' from 'email unknown' in user-facing messaging. OWASP Authentication Cheat Sheet ('Forgot Password' section).

**Priority**: normal — auth recovery works end-to-end once this ships; right now only the backend is live.

## Evidence & context

- Lens report: `docs/reviews/agent-ux-reverify-2026-04-11.md`
- Merged report: `docs/reviews/cena-review-2026-04-11-reverify.md`
- Queue ID: `t_f7bb146a546b`

## Definition of Done

1. Root cause identified and fixed (not symptoms)
2. Regression test added and wired into CI (`.github/workflows/`)
3. Structured log emitted on the error path
4. `dotnet build` succeeds with 0 errors
5. All existing tests pass (`dotnet test`)
6. Code review by coordinator (`claude-code`)
