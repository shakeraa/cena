# PWA-003: Mobile Viewport, Touch, and UX Hardening

## Goal
Harden the Vue 3 student app for mobile touch devices — viewport behavior, virtual keyboard handling, touch target sizing, safe area insets, and reduced motion. This is not "make it responsive" — it's "make it feel native on a phone."

## Context
- Architecture doc: `docs/research/cena-mobile-pwa-approach.md` §5.3
- The student spends 15-30 minutes per session typing math expressions, dragging force arrows, and tapping step buttons on a phone screen
- Step input is the most critical UX surface — virtual keyboard must not break layout
- FBD Construct mode (drag force arrows) requires precise touch handling
- Students use budget Android phones (small screens, older browsers) — not just flagships

## Scope of Work

### 1. Viewport Meta (Precise Configuration)
Update `index.html`:
```html
<meta name="viewport" content="width=device-width, initial-scale=1, maximum-scale=1, user-scalable=no, viewport-fit=cover">
```

**Why `user-scalable=no`**: Math input fields trigger zoom on iOS when font-size < 16px. Instead of allowing zoom (which breaks layout), ensure all input fields use `font-size: 16px` minimum. Document this decision.

**Why `viewport-fit=cover`**: Required for safe area insets on notched devices.

### 2. Virtual Keyboard Handling
Create `src/student/full-version/src/composables/useVirtualKeyboard.ts`:

- Use `visualViewport` API to detect keyboard open/close
- When keyboard opens: scroll the active input into view, adjust layout so the input is above the keyboard
- When keyboard closes: restore layout
- **Critical for StepInput.vue**: The step solver input field must remain visible and usable when the keyboard is open. Test with MathLive input (if used) — MathLive has its own keyboard handling that may conflict
- Handle iOS Safari's rubber-band scroll behavior — prevent the page from scrolling behind the keyboard
- Test with both standard keyboard and third-party keyboards (SwiftKey, Gboard)

```typescript
// Core logic
const { visualViewport } = window;
if (visualViewport) {
  visualViewport.addEventListener('resize', () => {
    const keyboardHeight = window.innerHeight - visualViewport.height;
    document.documentElement.style.setProperty(
      '--keyboard-height', `${keyboardHeight}px`
    );
  });
}
```

### 3. Touch Target Sizing (WCAG 2.5.5 Level AAA)
Audit and fix ALL interactive elements:

| Element | Minimum Size | Current (check) | Action |
|---------|-------------|-----------------|--------|
| Step submit button | 44×44px | ? | Verify or fix |
| Hint button | 44×44px | ? | Verify or fix |
| Answer choice (MCQ) | 44×44px tap area | ? | Verify or fix |
| Navigation tabs | 44×44px | ? | Verify or fix |
| Topic selector | 44×44px | ? | Verify or fix |
| Mastery map nodes | 44×44px | ? | Verify or fix |
| Figure zoom controls | 44×44px | ? | Verify or fix |
| Close/dismiss buttons | 44×44px | ? | Verify or fix |

Create a CSS utility class:
```css
.touch-target {
  min-width: 44px;
  min-height: 44px;
  /* Expand tap area without changing visual size */
  position: relative;
}
.touch-target::after {
  content: '';
  position: absolute;
  inset: -8px; /* Expand by 8px in each direction */
}
```

### 4. Safe Area Insets (Notched Devices)
Add to global CSS:
```css
:root {
  --safe-area-top: env(safe-area-inset-top, 0px);
  --safe-area-bottom: env(safe-area-inset-bottom, 0px);
  --safe-area-left: env(safe-area-inset-left, 0px);
  --safe-area-right: env(safe-area-inset-right, 0px);
}
```

Apply to:
- App header: `padding-top: var(--safe-area-top)`
- Bottom navigation/actions: `padding-bottom: var(--safe-area-bottom)`
- Full-width containers: `padding-inline: max(16px, var(--safe-area-left))`

### 5. Reduced Motion
Respect `prefers-reduced-motion`:
```css
@media (prefers-reduced-motion: reduce) {
  *, *::before, *::after {
    animation-duration: 0.01ms !important;
    animation-iteration-count: 1 !important;
    transition-duration: 0.01ms !important;
  }
}
```

Audit: identify all animations in the student app (page transitions, step feedback, mastery map, figure interactions) and ensure they degrade gracefully.

### 6. Pull-to-Refresh Prevention
In standalone PWA mode, Chrome adds pull-to-refresh by default. This conflicts with scrollable content (question lists, mastery map):
```css
body {
  overscroll-behavior-y: contain;
}
```

### 7. Text Selection Prevention (Where Appropriate)
Prevent accidental text selection on buttons and interactive elements, but ALLOW selection on question text and explanations (students may want to copy):
```css
.no-select {
  -webkit-user-select: none;
  user-select: none;
}
/* Question content, explanations, hints — always selectable */
.question-content, .explanation, .hint-text {
  -webkit-user-select: text;
  user-select: text;
}
```

## Files to Create/Modify
- `src/student/full-version/index.html` — viewport meta
- `src/student/full-version/src/composables/useVirtualKeyboard.ts`
- `src/student/full-version/src/assets/styles/mobile.css` (or equivalent in existing style structure)
- Multiple component files — touch target fixes (audit determines which)

## Non-Negotiables
- **44×44px minimum touch targets** — no exceptions; this is accessibility law in some jurisdictions
- **Virtual keyboard must not hide the step input** — this is the #1 mobile UX failure mode for math apps
- **Safe area insets on all edges** — test on iPhone with notch AND Dynamic Island
- **`overscroll-behavior-y: contain`** — pull-to-refresh in a tutoring session is a session-killer

## Acceptance Criteria
- [ ] No input zoom on iOS Safari (all inputs ≥ 16px font-size)
- [ ] Virtual keyboard opens → step input scrolls into view and remains interactive
- [ ] Virtual keyboard closes → layout restores correctly (no stuck offset)
- [ ] All interactive elements ≥ 44×44px tap area (audit with Chrome DevTools device mode)
- [ ] Safe area insets applied — no content hidden behind notch or home indicator
- [ ] `prefers-reduced-motion` disables all animations
- [ ] Pull-to-refresh disabled in standalone mode
- [ ] Text selectable on question content, non-selectable on buttons

## Testing Requirements
- **Unit**: `useVirtualKeyboard.ts` — mock `visualViewport`, test keyboard height calculation
- **Integration**: Playwright mobile emulation (iPhone SE, iPhone 14 Pro, Pixel 7) — verify viewport, touch targets
- **Manual (REQUIRED)**: Real device testing on at minimum:
  - iPhone SE 3rd gen (smallest modern iOS screen)
  - iPhone 14 Pro (Dynamic Island)
  - Samsung Galaxy A14 (budget Android, common in Israeli market)
  - iPad 10th gen (tablet)
- **Accessibility**: axe-core touch target audit

## DoD
- PR merged to `main`
- Real device test results (4 devices minimum) documented in PR
- Touch target audit spreadsheet attached to PR
- Virtual keyboard behavior video (iOS + Android) attached to PR

## Reporting
Complete with: `branch=<worker>/<task-id>-pwa-mobile-ux,devices_tested=<n>,touch_targets_fixed=<n>`
