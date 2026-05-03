# TASK-STU-W-01: Design System Bootstrap

**Priority**: HIGH — blocks all visual feature work
**Effort**: 2-3 days
**Phase**: 1
**Depends on**: [STU-W-00](TASK-STU-W-00-project-scaffold.md)
**Backend tasks**: none
**Status**: Not Started

---

## Goal

Ship the design system chassis: Vuexy theme with student-specific color tokens, three layouts, i18n + RTL wiring, a11y primitives, and the set of shared components every feature task will reuse.

## Spec

Full specification lives in [docs/student/02-design-system.md](../../docs/student/02-design-system.md). All `STU-DS-*` acceptance criteria in that file form this task's checklist.

## Scope

In scope:

- Vuetify theme configuration matching the Vuexy indigo palette from admin
- Student-specific token extensions: `flow.*` colors and `mastery.*` colors from the design system doc
- Dark mode toggle that persists to `localStorage`
- Three layouts in `src/layouts/`: `default.vue`, `blank.vue`, `auth.vue`
- Typography scale with locale-aware font loading (Public Sans / Noto Sans Arabic / Heebo)
- `vue-i18n` instance with `en.json` / `ar.json` / `he.json` populated with the shared UI strings (buttons, statuses, common errors)
- RTL mode wired — when locale is `ar` or `he`, Vuetify `rtl` is true and HTML `dir="rtl"`
- `prefers-reduced-motion` global composable `useReducedMotion()` returning a `Ref<boolean>`
- Shared components under `src/components/common/`:
  - `<StudentEmptyState>` — icon, heading, subcopy, CTA slot
  - `<StudentSkeletonCard>` — skeleton shell for cards
  - `<StudentErrorBoundary>` — error boundary with report-to-support CTA
  - `<KpiCard>` — label, value, trend arrow
  - `<FlowAmbientBackground>` — the mobile-parity ambient tint (pure visual, no data)
  - `<LanguageSwitcher>` — dropdown for en / ar / he
- Axe-core wired into Playwright as a global check; CI fails on any violation
- Storybook **not** included (explicit non-goal for v1)

Out of scope:

- Route definitions or auth guards (STU-W-02)
- API client (STU-W-03)
- Any feature-specific components (feature tasks)
- Actual pages beyond a visual showcase page at `/_dev/design-system`

## Definition of Done

- [ ] All `STU-DS-001` through `STU-DS-010` acceptance criteria from [02-design-system.md](../../docs/student/02-design-system.md) pass
- [ ] Dark mode toggle on the showcase page switches theme and persists across reloads
- [ ] Language switcher swaps `en` / `ar` / `he` and RTL flips correctly
- [ ] `prefers-reduced-motion` test verifies animations are disabled under reduced motion
- [ ] Axe-core runs clean on the showcase page in all three locales and both themes
- [ ] Flow ambient background renders all 5 flow states with the documented colors
- [ ] `<StudentEmptyState>`, `<StudentSkeletonCard>`, `<StudentErrorBoundary>`, `<KpiCard>` all have unit tests and at least one visual Playwright snapshot
- [ ] All shared strings are in locale files, no hardcoded text
- [ ] Cross-cutting concerns from the bundle README apply

## Risks

- **Font loading flash** — locale-specific fonts must be preloaded to avoid FOUT. Use `<link rel="preload" as="font">` for the primary font of the current locale.
- **RTL bugs in third-party components** — test Vuetify carefully in RTL mode, especially ApexCharts (which is LTR-only for some axes).
- **Dark mode token drift** — every custom token must have both light and dark variants computed at theme definition time.
