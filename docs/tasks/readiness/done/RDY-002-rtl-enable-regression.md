# RDY-002: Enable RTL + Visual Regression

- **Priority**: SHIP-BLOCKER — primary users are Arabic/Hebrew speakers
- **Complexity**: Mid engineer — uncomment binding + visual regression testing
- **Source**: Expert panel audit — Tamar (A11y), Amjad (Curriculum), Lior (UX)
- **Tier**: 0 (blocks deployment for target population)
- **Effort**: 2-3 days (5-7 days if Vuetify layout bugs surface)

> **Rami's challenge**: The task doesn't account for WHY RTL was disabled. If it was a Vuetify bug workaround, re-enabling may expose a cascade of layout breaks with no budget for fixes. Add conditional scope: "If Vuetify layout breaks, add +3 days per component fixed."

## Problem

`isAppRTL = ref(false)` is hardcoded at `src/student/full-version/src/@layouts/stores/config.ts` line 94. The correct binding `ref(layoutConfig.app.isRTL)` is commented out. The LanguageSwitcher correctly sets `document.documentElement.dir`, but the Vuetify layout store overrides it. Arabic and Hebrew students see LTR layout.

This was intentionally disabled (comment trail suggests a Vuetify layout conflict). The fix may be one line or may expose a cascade of layout breaks that need individual component fixes.

## Scope

### 1. Re-enable RTL binding

- Uncomment `const isAppRTL = ref(layoutConfig.app.isRTL)` at config.ts:94
- Verify LanguageSwitcher and Vuetify VLocaleProvider agree on direction

### 2. Visual regression on Arabic locale

- Switch to Arabic locale in student app
- Screenshot every major page: Home, Session Setup, Session (question), Progress, Profile, Onboarding
- Identify and fix layout breaks (padding/margin flips, icon direction, nav alignment)
- Fix individual components rather than re-disabling RTL

### 3. Verify math remains LTR

- All KaTeX/MathLive content must stay `dir="ltr"` inside RTL pages
- Verify `<bdi dir="ltr">` wrappers on all math expressions in QuestionCard, StepInput, MathInput
- Test mixed-direction strings (Arabic text + embedded equation)

### 4. Admin app RTL

- Check if admin app has same hardcoded `false` — apply same fix if so

## Files to Modify

- `src/student/full-version/src/@layouts/stores/config.ts` — uncomment RTL binding (line 94)
- Any Vue components with broken RTL layout (discovered during regression)
- `src/admin/full-version/src/@layouts/stores/config.ts` — if same issue exists

## Acceptance Criteria

- [ ] `isAppRTL` is driven by `layoutConfig.app.isRTL`, not hardcoded
- [ ] Arabic locale: full RTL layout renders correctly on all major pages
- [ ] Hebrew locale: RTL layout renders correctly (if Hebrew is enabled via build flag)
- [ ] Math expressions remain LTR inside RTL pages
- [ ] Mixed-direction strings (Arabic + math) render without visual corruption
- [ ] No layout regressions in English (LTR) locale
- [ ] Visual regression screenshots captured and reviewed
- [ ] VNavigationDrawer items render correctly in RTL (icon-to-text order, click targets)
- [ ] All directional icons auto-flip in RTL context (arrows, chevrons) via CSS logical properties
- [ ] Form inputs (VTextField, VSelect, VAutocomplete) preserve RTL text entry direction
- [ ] Breadcrumbs and pagination reverse order in RTL
- [ ] No non-logical CSS properties (`margin-left`/`margin-right`) — use `margin-inline-start`/`-end`

> **Cross-review (Tamar)**: Document WHY RTL was originally disabled (Vuetify layout conflict) as part of the fix. Confirm the root cause is resolved, not just the symptom.
