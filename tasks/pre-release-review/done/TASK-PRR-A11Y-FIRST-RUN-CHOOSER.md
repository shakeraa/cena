---
id: prr-a11y-first-run-chooser
parent: prr-a11y-toolbar-enrich
priority: P2
tier: mvp
tags: [a11y, i18n, il-equal-rights-law-5758-1998, first-run]
---

# TASK-PRR-A11Y-FIRST-RUN-CHOOSER — Full-screen language chooser on first visit

## Context

Follow-up #2 filed from `prr-a11y-toolbar-enrich` close-out
(2026-04-21). The enrich MVP shipped an in-toolbar language switcher.
The parent task brief also called for a **first-run full-screen
chooser** that appears before any route content on the very first visit
(no `cena-student-locale` key in localStorage), with three large tiles
(English / עברית / العربية), Esc disabled, focus on first tile, arrow
keys cycle, Enter commits.

Deferred from MVP because:

- The in-toolbar switcher already closes the IL 5758-1998 "independent
  control" access-barrier for first-language AR/HE readers.
- The full-screen chooser adds UX friction that needs usability review
  (Arab-sector student cohort feedback already prefers
  auto-detect-with-override; Dr. Lior panel 2026-04-17).

## Scope

- New Pinia store `useLocaleStore` (code, locked, setLocale,
  resetLock) — becomes the single writer for locale changes, replacing
  scattered side-effect writers in `onboarding.vue:68-75` +
  LanguageSwitcher.vue + A11yToolbar.vue.
- New composable `useLocaleSideEffects()` wrapping
  `document.documentElement.{lang,dir}`, vuetify locale.current, i18n
  global locale — one seam, no drift.
- New component `src/student/full-version/src/components/common/FirstRunLanguageChooser.vue`.
  Mounts from root `App.vue` with `v-if="!localeStore.locked"`. Three
  large tiles (min 44×44, native script + locale code badge). Focus
  first tile, arrow cycles, Enter commits, Esc disabled.
- Migration: on first load, upcast any existing `cena-student-locale`
  string-only value to `{ code, locked: true, version: 1 }`. Users
  with a legacy locale skip the chooser.
- Decide during impl whether to retire `LanguagePicker.vue` in the
  onboarding stepper — if removed, add to `retired.md`.

## Definition of done

- First visit ever (fresh localStorage) → chooser blocks route content.
- Subsequent visits → no prompt, boot into stored locale.
- Single side-effect seam; A11yToolbar and settings both call through
  `useLocaleStore` + `useLocaleSideEffects`.
- Unit tests: chooser mount conditions, radio-to-keyboard parity,
  migration upcaster.

## Non-negotiables

- ≤500 LOC per file.
- Hebrew gated — if VITE_ENABLE_HEBREW=false, Hebrew tile is not
  rendered.
- Primary #7367F0 locked.
