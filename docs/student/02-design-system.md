# 02 — Design System

The student web app reuses the **Vuexy Vue 3 theme** already in `src/admin/full-version/`. This document captures the rules and student-specific extensions.

## Theme

- **Base**: Vuexy Material Dark / Light (shipped with admin)
- **Primary color**: `#696CFF` (Vuexy indigo) — same as admin, keeps brand consistency
- **Secondary color**: `#8A8D93`
- **Accent (Student)**: `#FFB300` — warm gold for flow state, XP, achievements (matches mobile `flow_ambient_indicator.dart`)
- **Success**: `#71DD37`
- **Warning**: `#FFAB00`
- **Error**: `#FF3E1D`
- **Info**: `#03C3EC`

Student-specific tokens (add to `plugins/vuetify/theme.ts`):

```ts
const studentExtensions = {
  flow: {
    warming:     '#1565C0',  // cool blue, calm warm-up
    approaching: '#FF8F00',  // light amber, building momentum
    inFlow:      '#FFB300',  // warm gold, peak engagement
    disrupted:   '#1565C0',  // cool blue, gentle nudge
    fatigued:    'transparent',
  },
  mastery: {
    novice:      '#EF5350',
    learning:    '#FFA726',
    proficient:  '#66BB6A',
    mastered:    '#42A5F5',
    expert:      '#AB47BC',
  },
}
```

## Layouts

Three layouts live in `src/student/full-version/src/layouts/`:

1. **`default.vue`** — sidebar + app bar + main content. Used for 90% of pages.
2. **`blank.vue`** — no chrome. Used for live sessions, onboarding, full-screen diagrams.
3. **`auth.vue`** — centered card with hero image. Used for login/register/forgot-password.

Layout is selected per-route via `meta.layout`. See [01-navigation-and-ia.md](01-navigation-and-ia.md).

## Typography

- **Font family**: `Public Sans` (same as admin) for Latin; `Noto Sans Arabic` for Arabic; `Heebo` for Hebrew.
- **Heading scale**: Vuexy default (`h1` 40 → `h6` 18).
- **Body**: 14/20 (regular) and 15/22 (session content — slightly larger for readability).
- **Math**: rendered with KaTeX; font size matches body + 10%.

## Spacing & Layout Grid

- 8 px base unit (Vuetify default).
- Max content width on large screens: `1440 px` (admin defaults) for most pages; sessions go full-bleed.
- Page padding: `24 px` desktop, `16 px` tablet, `12 px` mobile-web.

## Iconography

- **Set**: Tabler icons (`@iconify-json/tabler`) — same as admin.
- **Size**: 20 px default, 24 px in app bars, 16 px inline in text.
- **Rule**: Never mix icon sets. All icons must be Tabler.

## Motion

- **Micro-interactions** (hover, tap ripple, button press): Vuetify default.
- **Page transitions**: fade-through, 200 ms, matches admin.
- **Celebration animations** (XP popup, badge unlock, streak fire): Rive or Lottie, 800–1500 ms, bypassable via `prefers-reduced-motion`.
- **Flow ambient**: background tint cross-fade over 600 ms (mirrors mobile `FlowAmbientIndicator`).

## Accessibility

- All interactive elements must meet WCAG 2.1 AA contrast.
- Every form field has a visible label (no placeholder-only).
- All icons that convey meaning must have an `aria-label`.
- `prefers-reduced-motion` disables all non-essential animation.
- Full keyboard navigation — no trap, no mouse-only interactions.
- Focus ring visible on all focusable elements (Vuexy default strengthened to 2 px).
- Screen-reader live regions for XP gains, badge unlocks, timer warnings, and session events.
- Color-blind safe mastery colors (tested for protanopia, deuteranopia, tritanopia).

## Internationalization (i18n)

- **Primary**: English (`en`).
- **Secondary**: Arabic (`ar`) — RTL, Hebrew (`he`) — RTL, hideable outside Israel based on user location / admin config.
- **Library**: `vue-i18n` v9 composition API.
- **Files**: `src/student/full-version/src/locales/{en,ar,he}.json`.
- **Number / date formatting**: use `Intl.NumberFormat` and `Intl.DateTimeFormat` with the active locale.
- **RTL**: Vuetify's `rtl` prop flips layouts globally when active locale is RTL.
- **Math**: KaTeX renders LTR even in RTL contexts (standard math convention); the surrounding paragraph flow remains RTL.

## Dark Mode

- Follows system preference by default.
- Manual override in `/settings/appearance` persists to `localStorage` and to the backend user preference.
- All flow / mastery tokens have dark-mode variants pre-computed.

## Responsive Breakpoints

Follow Vuetify defaults:

| Name | Range | Use-case |
|------|-------|----------|
| `xs`  | 0–599 px  | Mobile-web fallback |
| `sm`  | 600–959 px | Tablet portrait |
| `md`  | 960–1263 px | Tablet landscape / small laptop |
| `lg`  | 1264–1903 px | Laptop / desktop (primary target) |
| `xl`  | 1904+ px | Large desktop / multi-pane |

Below 600 px we show the bottom nav and hide the sidebar. At ≥ `xl` we enable multi-pane features (see [14-web-enhancements](14-web-enhancements.md)).

## Empty States

Every list / dashboard must have a designed empty state with:
- An illustrative icon or Rive loop.
- A one-sentence explanation.
- A primary CTA ("Start your first session", "Add friends", etc.).

Use the shared `<StudentEmptyState />` component.

## Error States

- **Inline form errors**: Vuetify default with slightly stronger color.
- **API errors**: toast with retry action, plus inline message on the affected card.
- **Fatal (boundary)**: full-page card with error code, copy-to-clipboard, and "Report to support" CTA.

## Loading States

- **Skeleton screens** for cards, lists, and charts (never spinners on initial render).
- **Progress bar** at top of the viewport during route transitions.
- **Inline spinners** only for contextual actions (e.g. "Submitting answer…").

## Acceptance Criteria

- [ ] `STU-DS-001` — Vuexy theme imported and matches admin colors exactly.
- [ ] `STU-DS-002` — Flow and mastery color tokens added to the theme and typed.
- [ ] `STU-DS-003` — All three layouts (`default`, `blank`, `auth`) implemented.
- [ ] `STU-DS-004` — Typography scale and locale-aware fonts loaded.
- [ ] `STU-DS-005` — i18n set up with en / ar / he locales and RTL switching.
- [ ] `STU-DS-006` — Dark mode toggle persists to localStorage and backend.
- [ ] `STU-DS-007` — `prefers-reduced-motion` disables all non-essential animation.
- [ ] `STU-DS-008` — All interactive elements pass WCAG 2.1 AA contrast audit (axe-core CI check).
- [ ] `STU-DS-009` — Shared `<StudentEmptyState />`, `<StudentSkeletonCard />`, `<StudentErrorBoundary />` components in place.
- [ ] `STU-DS-010` — Responsive breakpoints trigger bottom nav under 600 px and multi-pane at ≥ 1904 px.
