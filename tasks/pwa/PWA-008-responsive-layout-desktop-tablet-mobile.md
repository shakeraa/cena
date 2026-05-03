# PWA-008: Responsive Layout — Desktop + Tablet + Mobile

## Goal
Implement a production-grade responsive layout system for the Vue 3 student app that serves desktop (1200px+), tablet (768-1199px), and mobile (320-767px) from a single codebase. The student experience must feel purpose-built for each form factor, not "desktop shrunk down."

## Context
- User confirmed: desktop, tablet, and mobile are all target platforms
- Student app: `src/student/full-version/` — Vue 3 + Vite
- Vuexy design system is the base — check what responsive utilities it already provides
- Key pages: Session (question + step solver), Mastery Map, Topic Selector, Settings, Photo Capture
- RTL layout (Arabic primary, Hebrew secondary) must work across all breakpoints
- The step solver is the most layout-critical page — math input + question + figure + feedback must all be visible

## Scope of Work

### 1. Breakpoint System
Define consistent breakpoints (aligned with Vuexy if it has them):

```scss
$breakpoints: (
  mobile-sm: 320px,    // iPhone SE, small Androids
  mobile: 375px,       // iPhone 12/13/14
  mobile-lg: 428px,    // iPhone Pro Max
  tablet: 768px,       // iPad portrait
  tablet-lg: 1024px,   // iPad landscape
  desktop: 1200px,     // Standard desktop
  desktop-lg: 1440px   // Large desktop
);
```

Create utility composable `src/student/full-version/src/composables/useBreakpoint.ts`:
```typescript
const { isMobile, isTablet, isDesktop, breakpoint } = useBreakpoint();
```

### 2. Page-Level Layout Patterns

**Session Page (Question + Step Solver):**
- **Desktop**: Two-column — question+figure left (60%), step solver right (40%)
- **Tablet portrait**: Two-column — question+figure left (55%), step solver right (45%)
- **Tablet landscape**: Same as desktop
- **Mobile**: Single column — question+figure on top, step solver below, sticky math input at bottom (above keyboard)

**Mastery Map:**
- **Desktop**: Full skill tree with connections, zoom controls
- **Tablet**: Same as desktop, slightly smaller nodes
- **Mobile**: Vertical skill list grouped by topic, collapsible sections (tree layout doesn't fit)

**Topic Selector:**
- **Desktop**: Grid of topic cards (3-4 columns)
- **Tablet**: Grid (2-3 columns)
- **Mobile**: List view (1 column), larger touch targets

**Photo Capture (PWA-006):**
- **Desktop**: Hidden or minimal (desktop users don't photograph textbooks)
- **Tablet/Mobile**: Prominent camera button, full-screen capture mode

### 3. Navigation Patterns

**Desktop**: Side navigation (Vuexy default) — topic list, mastery map, settings, session history
**Tablet**: Collapsible side navigation (hamburger toggle)
**Mobile**: Bottom tab bar — 4 tabs maximum:
  1. Session (current/new session)
  2. Mastery (mastery map)
  3. Review (offline review from PWA-005)
  4. Profile (settings, session history)

Create `src/student/full-version/src/components/MobileBottomNav.vue`:
- Fixed to bottom, above safe area inset
- Active tab highlighted with #7367F0
- Icons + labels (Arabic/Hebrew i18n)
- 44×44px minimum touch targets
- Hidden when keyboard is open (use `useVirtualKeyboard` from PWA-003)

### 4. Step Solver — Mobile Layout (Critical)
The step solver on mobile is the make-or-break UX:

```
┌──────────────────────────┐
│ Question text (scrollable)│
│ Figure (if present)       │
├──────────────────────────┤
│ Current step instruction  │
│ Hint button              │
├──────────────────────────┤
│ Step input (sticky)       │  ← Always visible above keyboard
│ [Submit]                  │
└──────────────────────────┘
│ Virtual keyboard          │
└──────────────────────────┘
```

- Question text and figure scroll independently of step input
- Step input is `position: sticky; bottom: var(--keyboard-height, 0)` (from PWA-003)
- Previous completed steps collapse into a summary ("Step 1: ✓, Step 2: ✓")
- Expand previous steps on tap to review

### 5. Typography Scaling
- **Desktop**: Body 16px, headings per Vuexy scale
- **Tablet**: Same as desktop
- **Mobile**: Body 16px (no smaller — iOS zoom prevention), headings scaled down proportionally
- **Math expressions (KaTeX)**: Never smaller than 14px on mobile
- **Arabic text**: May need 1-2px larger than Hebrew at same apparent size (Arabic glyphs are denser)
- Use `clamp()` for fluid typography where appropriate:
  ```css
  font-size: clamp(14px, 4vw, 18px);
  ```

### 6. CSS Logical Properties (RTL)
Audit ALL components and replace physical properties with logical ones:

| Physical | Logical |
|----------|---------|
| `margin-left` | `margin-inline-start` |
| `margin-right` | `margin-inline-end` |
| `padding-left` | `padding-inline-start` |
| `text-align: left` | `text-align: start` |
| `float: left` | `float: inline-start` |
| `border-left` | `border-inline-start` |
| `left: 0` | `inset-inline-start: 0` |

This is not optional — physical properties break RTL layouts. Create a linting rule if possible (stylelint-use-logical-spec).

## Files to Create/Modify
- `src/student/full-version/src/composables/useBreakpoint.ts`
- `src/student/full-version/src/components/MobileBottomNav.vue`
- `src/student/full-version/src/layouts/StudentLayout.vue` (modify — responsive behavior)
- `src/student/full-version/src/views/SessionView.vue` (modify — responsive layout)
- `src/student/full-version/src/views/MasteryView.vue` (modify — responsive layout)
- `src/student/full-version/src/assets/styles/responsive.css` (or equivalent)
- Multiple component files — CSS logical property migration

## Non-Negotiables
- **Step input must be visible above keyboard on mobile** — this is the #1 UX requirement
- **CSS logical properties for all directional styles** — physical properties are RTL bugs waiting to happen
- **Bottom nav on mobile, side nav on desktop** — don't force desktop navigation patterns on phone users
- **No horizontal scroll on any page at any breakpoint** — test at 320px minimum
- **Typography never below 14px on mobile** — readability for students in varying lighting conditions

## Acceptance Criteria
- [ ] Session page: two-column on desktop/tablet, single-column on mobile
- [ ] Step input sticky above keyboard on mobile (test with real keyboard)
- [ ] Mastery map: tree on desktop, list on mobile
- [ ] Bottom nav: 4 tabs, visible on mobile, hidden on desktop
- [ ] Side nav: visible on desktop, hamburger on tablet, hidden on mobile
- [ ] No horizontal scroll at 320px viewport width
- [ ] All directional CSS uses logical properties (audit results attached)
- [ ] Arabic and Hebrew layouts correct at all breakpoints
- [ ] Typography: 14px minimum on mobile, KaTeX 14px minimum
- [ ] Photo capture button prominent on mobile, subtle on desktop

## Testing Requirements
- **Unit**: `useBreakpoint.ts` — test all breakpoint transitions
- **Integration**: Playwright — test at 375px, 768px, 1024px, 1440px viewports (both LTR and RTL)
- **Visual regression**: Screenshot comparison at each breakpoint (Percy, Playwright visual comparison, or manual)
- **Manual (REQUIRED)**: Real device testing at minimum: iPhone SE (375px), iPad (810px), desktop (1440px)
- **Accessibility**: axe-core at each breakpoint

## DoD
- PR merged to `main`
- Screenshot grid: 4 breakpoints × 3 pages × 2 languages (Arabic + Hebrew) = 24 screenshots
- No horizontal scroll at any breakpoint (verification attached)
- CSS logical property audit result (0 physical directional properties remaining)

## Reporting
Complete with: `branch=<worker>/<task-id>-pwa-responsive-layout,breakpoints=<n>,pages_adapted=<n>,logical_props_migrated=<n>`
