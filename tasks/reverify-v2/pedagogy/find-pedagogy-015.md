---
id: FIND-PEDAGOGY-015
task_id: t_1bec032a8290
severity: P1 — High
lens: pedagogy
tags: [reverify, pedagogy, i18n]
status: pending
assignee: unassigned
created: 2026-04-11
---

# FIND-pedagogy-015: Date and number formatters hardcoded en-US — do not switch with UI language

## Summary

Date and number formatters hardcoded en-US — do not switch with UI language

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

FIND-pedagogy-015: Locale-aware date and number formatters
classification: new

src/student/full-version/src/@core/utils/formatters.ts hardcodes
Intl.DateTimeFormat('en-US') in formatDate (line 29) and
formatDateToMonthShort (line 45). kFormatter (line 12-16) uses a
manual ',' thousands separator with no locale awareness.

Every formatted date in the student web shows en-US format regardless
of whether the user has selected English, Arabic, or Hebrew. Arabic and
Hebrew learners see en-US dates and numbers, breaking the otherwise
localized surface. TimeBreakdownChart.vue uses
toLocaleDateString(undefined, ...) which uses the BROWSER locale, not
the UI locale, so an English UI on a French OS gets French dates.

Per Unicode TR35 (https://unicode.org/reports/tr35/tr35-numbers.html),
number formatting is one of the most heavily-locale-sensitive UI surfaces.
Per Sweller 1988 (DOI: 10.1207/s15516709cog1202_4), mismatched locale
formatting forces a context switch that imposes extraneous cognitive
load competing with germane learning load.

Files to read first:
  - src/student/full-version/src/@core/utils/formatters.ts
  - src/student/full-version/src/components/progress/TimeBreakdownChart.vue (lines 35, 41)
  - src/student/full-version/src/plugins/i18n/index.ts

Definition of done:
  - formatDate / formatDateToMonthShort take or read the active i18n
    locale and pass it to Intl.DateTimeFormat. Expose getActiveLocale()
    from plugins/i18n/index.ts for non-Vue callers.
  - kFormatter uses Intl.NumberFormat with locale-aware notation
    (notation: 'compact' for the >9999 case, default grouping otherwise).
  - TimeBreakdownChart uses the active i18n locale, not undefined.
  - Vitest: formatDate('2026-04-11') under i18n.locale='ar' returns
    Arabic date format, not en-US.
  - Vitest: kFormatter(1234567) under ar returns Arabic-thousands format
    (different from en-US '1,234,567').
  - Vitest: TimeBreakdownChart label rendering switches between en/ar.

Reporting:
  - Branch <worker>/<task-id>-find-pedagogy-015-locale-formatters
  - Push, then complete with summary + branch + test names


## Evidence & context

- Lens report: `docs/reviews/agent-pedagogy-reverify-2026-04-11.md`
- Merged report: `docs/reviews/cena-review-2026-04-11-reverify.md`
- Queue ID: `t_1bec032a8290`

## Definition of Done

1. Root cause identified and fixed (not symptoms)
2. Regression test added and wired into CI (`.github/workflows/`)
3. Structured log emitted on the error path
4. `dotnet build` succeeds with 0 errors
5. All existing tests pass (`dotnet test`)
6. Code review by coordinator (`claude-code`)
