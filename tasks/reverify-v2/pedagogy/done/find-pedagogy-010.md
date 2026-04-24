---
id: FIND-PEDAGOGY-010
task_id: t_f0cfa809cd67
severity: P0 — Critical
lens: pedagogy
tags: [reverify, pedagogy, i18n, fake-fix, regression]
status: pending
assignee: unassigned
created: 2026-04-11
type: regression
type: fake-fix
---

# FIND-pedagogy-010: Hide-Hebrew gate bypassable via cookie + onboarding picker hardcodes HE

## Summary

Hide-Hebrew gate bypassable via cookie + onboarding picker hardcodes HE

## Severity

**P0 — Critical** — REGRESSION — FAKE-FIX

## Requirements

The fix for this task MUST be production-grade:

- **No stubs, no canned data, no hardcoded objects, no `NotImplementedException`**
- **Labels must match actual data** — if a button says "Save", it must persist; if a metric says "tokens", it must count real tokens
- **Verify E2E** — query the DB, call the API, render the UI, compare field names
- **Include a CI-wired regression test** that fails on the current (buggy) commit and passes on the fix
- **Add a structured log line** on the error path so a re-regression is detectable in production

## Task body

FIND-pedagogy-010: Close hide-Hebrew gate at runtime, not just menu
related_prior_finding: FIND-ux-014 (preflight verified-fixed → reverify FAKE-FIX)
classification: fake-fix

The build-time VITE_ENABLE_HEBREW flag is a fake-fix of FIND-ux-014.
It only filters the LanguageSwitcher.vue menu items. The runtime i18n
loader (plugins/i18n/index.ts:18) reads the cookie 'cena-student-language'
directly with no awareness of the gate, and the OnboardingLanguagePicker
(components/onboarding/LanguagePicker.vue:24-46) hardcodes a 3-locale
LOCALES array. The themeConfig.app.i18n.langConfig also hardcodes all
three locales.

Live reproduction (verified 2026-04-11 against origin/main @ cc3f702):
  1. Build with VITE_ENABLE_HEBREW=false (the default)
  2. document.cookie = 'cena-student-language=he; path=/'
  3. Reload http://localhost:5175/session
  4. Entire UI renders in Hebrew with htmlDir='rtl', title 'התחל שיעור'

A learner outside Israel can be served Hebrew via cookie, via the
hardcoded onboarding picker, or via a stale localStorage value. The user
rule "English primary, Arabic/Hebrew secondary, Hebrew hideable outside
Israel" (feedback_language_strategy 2026-03-27) is verifiably broken at
runtime. Per August & Shanahan 2006, ISBN 978-0805860788, comprehension
feedback in a language the learner does not read provides zero formative
value and adds extraneous cognitive load.

Files to read first:
  - src/student/full-version/src/plugins/i18n/index.ts (line 18)
  - src/student/full-version/src/composables/useAvailableLocales.ts
  - src/student/full-version/src/components/common/LanguageSwitcher.vue
  - src/student/full-version/src/components/onboarding/LanguagePicker.vue
  - src/student/full-version/themeConfig.ts (lines 24-44)
  - feedback_language_strategy memory file

Definition of done:
  - Cookie 'cena-student-language=he' on a VITE_ENABLE_HEBREW=false
    build resolves to active i18n locale 'en' (cookie rewritten by the
    i18n plugin on load if Hebrew is gated off).
  - Onboarding LanguagePicker uses useAvailableLocales — Hebrew option
    absent when gate is off.
  - themeConfig langConfig is filtered through the same gate at module
    load time.
  - Vitest unit: cookieRef('language')='he' + isHebrewEnabled=false ⇒
    effective i18n locale = 'en'.
  - Playwright e2e: build with VITE_ENABLE_HEBREW=false, navigate
    /onboarding, assert [data-testid="locale-he"] is absent.
  - Playwright e2e: build with VITE_ENABLE_HEBREW=false, set cookie
    cena-student-language=he, reload /home, assert htmlDir='ltr' and
    htmlLang='en'.
  - Cross-link the original FIND-ux-014 task as related.

Reporting requirements:
  - Branch name: <worker>/<task-id>-find-pedagogy-010-hide-hebrew-runtime-gate
  - Push branch and complete with summary + branch + test names
  - Mention this re-opens FIND-ux-014's verdict in the result string


## Evidence & context

- Lens report: `docs/reviews/agent-pedagogy-reverify-2026-04-11.md`
- Merged report: `docs/reviews/cena-review-2026-04-11-reverify.md`
- Queue ID: `t_f0cfa809cd67`

## Definition of Done

1. Root cause identified and fixed (not symptoms)
2. Regression test added and wired into CI (`.github/workflows/`)
3. Structured log emitted on the error path
4. `dotnet build` succeeds with 0 errors
5. All existing tests pass (`dotnet test`)
6. Code review by coordinator (`claude-code`)
