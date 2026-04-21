---
id: prr-a11y-toolbar-enrich
depends-on: [prr-031, prr-032]
priority: P1
tier: mvp
tags: [a11y, i18n, il-equal-rights-law-5758-1998, enrichment]
---

# A11yToolbar enrichment — language switcher + expanded controls

## Why this exists

Commits `cdfc0a24` + `418aec7a` shipped the first-pass A11yToolbar (text
size, high contrast, reduced motion, dyslexia font) on default + auth +
blank layouts. User observed on a follow-up review:

1. **No Hebrew language visible** anywhere on the pre-login surfaces —
   students reaching `/` in their OS language can see English fallback
   and sometimes Arabic auto-detection, but not Hebrew.
2. **No language switcher in the A11yToolbar.** Students who land on the
   wrong locale (wrong OS default, shared device, public kiosk) cannot
   switch before signing in — they must complete login → reach onboarding
   → use the LanguagePicker step. That's an access barrier for
   first-language Hebrew/Arabic readers trying to authenticate, and
   arguably a violation of IL 5758-1998 "independent control" principle.

## Goal

Expand A11yToolbar so every student can, from any surface (pre- or
post-login), adjust language alongside the existing a11y controls. Also
close the gaps in content/controls that the first pass left thin.

## Scope

### 1. First-visit language chooser + persistent-until-changed model

**UX model** (refined 2026-04-21 per user):

1. **First visit ever** (no `cena-student-locale` key in localStorage):
   a full-screen `FirstRunLanguageChooser.vue` renders BEFORE any route
   content — three large tiles (English / עברית / العربية), each in its
   native script. Student picks one. Store writes the choice + a
   `localeLocked: true` flag.
2. **All subsequent visits**: the app boots into the stored locale with
   zero prompts. No auto-detect from `navigator.language` once locked —
   auto-detect is a fallback ONLY on first run before the user picks.
3. **Change later via settings** (two surfaces):
   - A11yToolbar: language section with a radio group (EN/עברית/العربية).
   - `/settings/appearance`: a formal locale selector. Both write to the
     same store + same localStorage key so they stay in lockstep.

**Implementation:**

- New store: `useLocaleStore` (Pinia) owning `current`, `locked`,
  `setLocale(code)`, `resetLock()`. Replaces the currently-scattered
  side-effect writers (`onboarding.setLocale`,
  `applyLocaleSideEffects` in `onboarding.vue:68-75`, direct
  `i18n.global.locale` mutations). Extract the DOM side-effect helper
  into a `useLocaleSideEffects()` composable that every caller funnels
  through — one seam, no drift.
- New component: `FirstRunLanguageChooser.vue` under
  `src/components/common/`. Mounts from the root `App.vue` with
  `v-if="!localeStore.locked"` and overlays a full-screen
  `VOverlay`+`VDialog`-style container. Renders three large
  accessibility-grade tiles (min 44×44 + native-script label + locale
  code badge). Focus lands on the first tile; arrow keys cycle; Enter
  commits; Esc is DISABLED (there is no "cancel" from first-run
  selection — commit is required).
- Persist to `localStorage['cena-student-locale']` = `{ code, locked,
  version: 1 }`. A11yToolbar's language radio group reads from the same
  store. Settings page reads from the same store.
- Option labels render in the target language's native script:
  Hebrew entry shows **עברית**, Arabic shows **العربية**. Never
  transliterated. Verify correct rendering with the per-locale fonts
  loaded in cdfc0a24 (Noto Sans Hebrew / Noto Kufi Arabic).
- `document.documentElement.dir` flips to `rtl` on he/ar and `ltr` on
  en via `useLocaleSideEffects()` — single code path, no duplicate logic.
- Pre-existing `LanguagePicker.vue` step in the onboarding stepper can
  remain as a "confirm your language" step (reads from the store, can
  edit) or be removed entirely — decide during implementation. Default
  recommendation: remove, since the first-run chooser covered it. File
  a retirement note in `retired.md` if removed.

### 1b. Migration for existing users

- Users with a locale already stored under the old key layout (if any —
  audit `cena-student-locale`, `i18nLang`, any other historical keys)
  get auto-migrated to the new shape (`{ code, locked: true, version: 1 }`)
  on first load with a store-init upcast. No first-run chooser for
  them — they already made a choice.

### 2. Expanded a11y controls (the first pass left thin)

- **Color-blind modes**: protanopia / deuteranopia / tritanopia filter
  toggles. Applied via CSS filter on `<main>` so color-semantic cells
  (e.g., prr-209 heatmap) remain distinguishable. Document in the
  toolbar description that the filters simulate what a color-blind
  student sees so teachers reviewing copy can self-audit.
- **Reading guide**: a thin horizontal ruler that follows the cursor
  (toggle on/off). Helps students with ADHD-type tracking difficulty.
- **Underline all links**: global override for the "underlines-only-on-hover"
  default. WCAG 2.4.7 focus visibility.
- **Cursor magnifier**: boost cursor size (system-level; CSS
  `cursor: url(...)` with a 24px / 32px / 48px stepped choice).
- **Line-height slider**: currently Arabic gets a fixed 1.6 line-height
  bump; expose a 1.2–2.0 slider for all locales.

### 3. A11y toolbar semantics tightening

- **Shortcuts**: add keyboard shortcut `Alt+A` (documented in the toolbar
  itself) to open the toolbar. Register it alongside ShellShortcuts so
  it's discoverable via the existing cheatsheet.
- **Per-preference announce**: when a preference changes, emit a live-region
  announcement via `aria-live="polite"` ("Text size: Large"). Students
  using screen readers confirm their change landed without re-opening.
- **Skip-to-toolbar** link: the existing skip-link at page top should
  include "Skip to accessibility toolbar" as a tertiary target.

### 4. Legal / content

- Add a link in the toolbar footer: "Accessibility statement →
  `/accessibility-statement`" (required by IL Reg 5773-2013 §35).
  Content is a separate task — stub the route, create the Markdown
  skeleton, ship a follow-up.
- Make the legalNote reference the regulation AND include a contact
  method ("accessibility@cena.app") per §35(b)(4).

### 5. Verification gate

- Playwright smoke test covering:
  - Toolbar opens from every layout (default / auth / blank).
  - Language switcher flips between en/he/ar; body direction flips
    RTL/LTR accordingly.
  - Text-size slider survives a full route navigation.
  - `data-a11y-*` attributes on `<html>` reflect the in-drawer state
    after any change.
- Architecture test: no layout wrapper in `src/layouts/*.vue` may
  render its primary content without `<A11yToolbar />` somewhere in the
  template (the ratchet ships as `A11yToolbarMountedOnEveryLayoutTest`).

## Non-negotiables

- Files ≤500 LOC (A11yToolbar.vue is currently ~180; language switcher
  + expanded controls may push it past — split into partial files or
  child components as needed).
- Ship-gate scanner green on all new copy in all three locales.
- Primary color `#7367F0` locked — contrast boost must NOT alter hue.
- No files under `src/` root (all new work under
  `src/student/full-version/src/components/common/` or `stores/`).

## Senior-architect protocol

Ask *why* the language switcher didn't ship in the first pass. Answer
(from commit history): the toolbar scope was deliberately narrow
(WCAG-oriented) and i18n was considered "somewhere else in the app".
The follow-up collapses that separation — IL 5758-1998 defines language
access as an a11y concern for Arabic + Hebrew readers. Document the
reversal in an inline `// WHY:` block on the new switcher.

## Deferred to separate follow-up

- `/accessibility-statement` route content (legal copy)
- Actual color-blind simulation SVG filters (vs library)
- OpenDyslexic font self-hosted bundle (license review required)

## Reporting

```
git add -A
git commit -m "feat(a11y-toolbar-enrich): language switcher + color-blind + reading-guide + 5773-2013 statement link"
git push
```
