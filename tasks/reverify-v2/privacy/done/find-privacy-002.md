---
id: FIND-PRIVACY-002
task_id: t_44b059e06deb
severity: P0 — Critical
lens: privacy
tags: [reverify, privacy, COPPA, GDPR, ICO-Children, Israel-PPL, policy]
status: pending
assignee: unassigned
created: 2026-04-11
---

# FIND-privacy-002: No Privacy Policy / Terms of Service / Children's Notice anywhere

## Summary

No Privacy Policy / Terms of Service / Children's Notice anywhere

## Severity

**P0 — Critical**

## Requirements

The fix for this task MUST be production-grade:

- **No stubs, no canned data, no hardcoded objects, no `NotImplementedException`**
- **Labels must match actual data** — if a button says "Save", it must persist; if a metric says "tokens", it must count real tokens
- **Verify E2E** — query the DB, call the API, render the UI, compare field names
- **Include a CI-wired regression test** that fails on the current (buggy) commit and passes on the fix
- **Add a structured log line** on the error path so a re-regression is detectable in production

## Task body

framework: COPPA (16 CFR §312.4(d)), GDPR (Art 13/14), ICO-Children (Std 4, 6), Israel-PPL §11
severity: P0 (critical)
lens: privacy
related_prior_finding: none

## Goal

Author and publish a Children's Privacy Notice, an adult Privacy Policy, and
a Terms of Service. Render at /privacy, /privacy/children, /terms in all three
locales (en, ar, he), and link from every auth page footer.

## Background

There is no Privacy Policy page, no Terms of Service page, no Children's
Privacy Notice, and no Cookie Notice anywhere in either the student or admin
web app. The only "privacy" route is `src/student/full-version/src/pages/settings/privacy.vue`
which is three toggles persisted only to localStorage. Confirmed by:

```
$ find src/student/full-version/src/pages -iname '*privacy*' -o -iname '*terms*' -o -iname '*policy*'
src/student/full-version/src/pages/settings/privacy.vue
$ find src/admin/full-version/src/pages -iname '*privacy*' -o -iname '*terms*' -o -iname '*policy*'
(zero results)
```

Live screenshot evidence: `privacy-student-login.png`,
`privacy-register-student.png`, `privacy-admin-login.png` at the
review-privacy worktree root.

## Files

- `src/student/full-version/src/pages/privacy.vue` (NEW — full adult)
- `src/student/full-version/src/pages/privacy/children.vue` (NEW — child-friendly)
- `src/student/full-version/src/pages/terms.vue` (NEW)
- `src/admin/full-version/src/pages/privacy.vue` (NEW)
- `src/admin/full-version/src/pages/terms.vue` (NEW)
- `docs/legal/privacy-policy.md` (canonical source)
- `docs/legal/privacy-policy-children.md` (Flesch-Kincaid grade 5 max)
- `docs/legal/terms-of-service.md`
- `src/student/full-version/src/components/common/StudentAuthCard.vue` (footer link)
- `src/student/full-version/src/plugins/i18n/locales/{en,ar,he}.json` (legal strings)

## Definition of Done

1. /privacy, /privacy/children, /terms render in all three locales
2. Each page contains the minimum-required sections:
   - data collected (link to FIND-privacy-011 catalog)
   - lawful basis per processing purpose
   - retention windows (link to DataRetentionPolicy + FIND-privacy-004)
   - rights + how to exercise them (link to FIND-privacy-003 endpoints)
   - DPO contact (FIND-privacy-014 — placeholder until DPO appointed)
   - processor list (Anthropic — FIND-privacy-008)
   - cross-border transfer mechanism
   - parent rights (COPPA §312.6, GDPR Art 8)
   - DSAR contact + 30-day SLA
3. Footer link from login, register, forgot-password, settings, every admin
   page footer
4. Children's Privacy Notice has been Flesch-Kincaid scored ≤ grade 5
5. Hebrew + Arabic translations are RTL-correct (defer to `pedagogy` lens
   for translation accuracy review)
6. E2E test asserts the four pages exist and contain the required headings

## Reporting requirements

Branch: `<worker>/<task-id>-privacy-002-policy-pages`. Result must include:

- the canonical markdown source of each policy
- screenshots of each page in each locale (12 screenshots total: 3 pages × 3 locales + 3 admin)
- the legal-review status (placeholder: "draft awaiting Cena legal counsel review")

## Out of scope

- Translation of Hebrew/Arabic strings into final legal copy (assume
  professional translation will replace the placeholder)
- DPO contact (handled in FIND-privacy-014)


## Evidence & context

- Lens report: `docs/reviews/agent-privacy-reverify-2026-04-11.md`
- Merged report: `docs/reviews/cena-review-2026-04-11-reverify.md`
- Queue ID: `t_44b059e06deb`

## Definition of Done

1. Root cause identified and fixed (not symptoms)
2. Regression test added and wired into CI (`.github/workflows/`)
3. Structured log emitted on the error path
4. `dotnet build` succeeds with 0 errors
5. All existing tests pass (`dotnet test`)
6. Code review by coordinator (`claude-code`)
