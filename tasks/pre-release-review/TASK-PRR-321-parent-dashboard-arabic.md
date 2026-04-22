# TASK-PRR-321: Parent dashboard — Arabic parity (RTL + i18n)

**Priority**: P0 — launch-blocker per persona #6
**Effort**: M (2 weeks incl. SME content review)
**Lens consensus**: persona #6 Arabic-Israeli parent (explicit launch-blocker)
**Source docs**: [BUSINESS-MODEL-001-pricing-10-persona-review.md](../../docs/design/BUSINESS-MODEL-001-pricing-10-persona-review.md)
**Assignee hint**: frontend-dev + Arabic-native content reviewer
**Tags**: epic=epic-prr-i, parent-ux, i18n, rtl, priority=p0, launch-blocker
**Status**: Ready (pending §5 decision #3 — Arabic on critical path Y/N)
**Source**: 10-persona pricing review 2026-04-22
**Tier**: launch (per persona #6 — do not defer)
**Epic**: [EPIC-PRR-I](EPIC-PRR-I-subscription-pricing-model.md)

---

## Goal

Full Arabic parity for parent dashboard. Without this, Premium tier at ₪249 cannot be sold to Arabic-Israeli segment (persona #6 consensus — the dashboard IS Premium's parent value).

## Scope

- All strings Arabic-translated (native reviewer, not machine).
- RTL layout preserved for heatmaps, timelines, tables.
- Math formula rendering LTR inside RTL pages via `<bdi dir="ltr">` (memory "Math always LTR").
- Date/time formatting locale-aware.
- Numeric display preserving numerals per locale preference.
- Topic labels reflect Arabic terminology used in Israeli Arabic-curriculum Bagrut (not MSA-only).

## Files

- `src/parent/src/i18n/ar/dashboard.json` (new)
- RTL style overrides on existing Vuexy components.
- Tests: RTL screenshot comparison, formula LTR preservation, Arabic copy renders without fallback.

## Definition of Done

- Arabic locale complete; no Hebrew fallback strings.
- RTL layout visually correct.
- Math LTR inside RTL pages.
- Native Arabic reviewer sign-off recorded.
- Full sln green.

## Non-negotiable references

- Memory "Math always LTR".
- Memory "Language Strategy" — Arabic secondary but full parity where shipped.
- Memory "No stubs — production grade" — strings are real, not placeholder.

## Reporting

complete via: standard queue complete.

## Related

- [PRR-320](TASK-PRR-320-parent-dashboard-mvp.md), [PRR-322](TASK-PRR-322-parent-dashboard-english.md)
