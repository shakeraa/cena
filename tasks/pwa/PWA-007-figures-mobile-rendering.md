# PWA-007: Figure Rendering — Mobile Optimization & Touch Interaction

## Goal
Optimize all figure rendering (function plots, geometry, physics diagrams) for mobile viewports and touch interaction. This is NOT about reimplementing figures (they're browser-native and already work) — it's about making them excellent on small screens with touch input, including FBD Construct mode where students drag force arrows.

## Context
- Architecture doc: `docs/research/cena-question-engine-architecture-2026-04-12.md` §5 (Figures), §6 (Physics), §42.6 (#48 — N/A for PWA, parity is free)
- PWA approach doc: `docs/research/cena-mobile-pwa-approach.md` §5.1
- All figure libraries (function-plot.js, JSXGraph, D3/SVG, KaTeX) are browser-native — they already render on mobile
- The problem is UX: small screens need responsive sizing, pinch-to-zoom on figures, and touch-friendly interaction points
- FBD Construct mode (physics) is the hardest: students drag force arrow endpoints on a 375px-wide screen

## Scope of Work

### 1. Responsive Figure Container
Create `src/student/full-version/src/components/figures/FigureContainer.vue`:

- Wrapper for all figure types
- Responsive: figure fills available width, maintains aspect ratio
- On mobile (< 768px): figure takes full viewport width minus padding
- On tablet (768-1024px): figure takes 80% of content width
- On desktop (> 1024px): figure takes content width (existing behavior)
- Aspect ratio varies by figure type:
  - Function plots: 4:3
  - Geometry: 1:1
  - Physics FBD: 3:2
  - Physics circuit: 16:9
- **Pinch-to-zoom**: Enable `touch-action: pinch-zoom` on figure container. Use CSS `transform: scale()` for smooth zoom. Constrain zoom: 1×-3×. Double-tap to reset zoom.
- **Pan when zoomed**: Allow touch-pan when zoomed in. Use `touch-action: pan-x pan-y` when scale > 1.

### 2. Function Plot Mobile Optimization
Modify function-plot.js integration:

- Reduce tick label count on mobile (every 2nd tick instead of every tick)
- Increase axis label font size to 14px minimum (readability on small screens)
- Touch interaction: tap on curve to show (x, y) coordinates at that point
- If function-plot.js doesn't support touch natively, add a transparent overlay `<canvas>` that captures touch and maps to data coordinates

### 3. JSXGraph Mobile Optimization
Modify JSXGraph integration:

- Set `board.setBoundingBox()` based on viewport width
- Increase point radius for touch: 8px minimum (vs 4px on desktop)
- Increase label font size: 14px minimum
- Touch: drag points, pinch-to-zoom the board
- JSXGraph already has touch support — verify it works on iOS Safari and Android Chrome, fix any issues

### 4. FBD Construct Mode — Mobile Touch
This is the hardest interaction pattern. Students drag force arrow endpoints to build free-body diagrams:

- **Arrow handle size**: 20×20px minimum (larger than the visual arrow tip)
- **Touch tolerance**: Accept touches within 16px of an arrow endpoint (fat-finger tolerance)
- **Drag feedback**: Show magnitude/angle numerically while dragging (can't read the arrow precisely on small screens)
- **Snap-to-grid**: Optional grid snap (15° angle increments, 0.5N magnitude increments) with toggle
- **Undo**: Single-tap undo button (last arrow placed). Long-press to clear all.
- **Conflict resolution**: If two arrows overlap and the student taps in the overlap zone, show a disambiguation menu: "Which force? [Gravity] [Normal]"
- **Orientation lock**: Lock to portrait during FBD Construct (landscape on phones makes the diagram too narrow)

### 5. Physics Diagram Display Mode — Mobile
For display-only physics diagrams (not construct mode):

- SVG viewBox must scale to container width
- Text elements in SVG: minimum 12px rendered size
- Force arrows: minimum 2px stroke width (thin arrows disappear on mobile)
- Labels: position to avoid overlap at mobile scale (may need to reflow vs desktop positions)

### 6. KaTeX in Figures — Mobile
Math labels inside figures (axis labels, force magnitudes, angle markers):

- KaTeX renders as HTML overlays or SVG `<foreignObject>`
- On mobile: ensure KaTeX text doesn't overflow figure bounds
- Font size: 14px minimum for inline math, 12px minimum for subscripts
- RTL: math labels are LTR even in Arabic/Hebrew context (existing behavior, verify on mobile)

### 7. Figure Screenshot / Share
Create `src/student/full-version/src/composables/useFigureExport.ts`:

- "Share" button on each figure (long-press or dedicated icon)
- Capture figure as PNG using `html2canvas` or `canvas.toBlob()`
- Use Web Share API (`navigator.share()`) to share to WhatsApp, Telegram, etc.
- Fallback: download as PNG if Web Share API not available
- **Privacy**: Only the figure is captured, not student data or session info

## Files to Create/Modify
- `src/student/full-version/src/components/figures/FigureContainer.vue`
- `src/student/full-version/src/components/figures/FunctionPlotMobile.vue` (or modify existing)
- `src/student/full-version/src/components/figures/JSXGraphMobile.vue` (or modify existing)
- `src/student/full-version/src/components/figures/FBDConstructMobile.vue` (or modify existing)
- `src/student/full-version/src/components/figures/PhysicsDiagramMobile.vue` (or modify existing)
- `src/student/full-version/src/composables/usePinchZoom.ts`
- `src/student/full-version/src/composables/useFigureExport.ts`

## Non-Negotiables
- **44×44px minimum touch targets** on ALL interactive figure elements (points, handles, buttons)
- **Pinch-to-zoom must not conflict with page zoom** — `touch-action` must be set correctly on figure container
- **FBD Construct mode must be usable on a 375px screen** — if it's not, the feature is broken for mobile
- **KaTeX labels must be readable** — 12px minimum at any zoom level
- **No figure cropping** — figures must be fully visible without scrolling within their container

## Acceptance Criteria
- [ ] Function plots render correctly and fill mobile viewport width
- [ ] JSXGraph geometry renders with touch-draggable points (8px+ radius)
- [ ] Pinch-to-zoom works on all figure types (1×-3×, double-tap reset)
- [ ] FBD Construct mode: drag force arrows with fat-finger tolerance on mobile
- [ ] FBD: magnitude/angle shown numerically during drag
- [ ] FBD: undo and clear work correctly
- [ ] Physics display diagrams scale SVG correctly to mobile viewport
- [ ] KaTeX labels in figures: minimum 12-14px rendered size
- [ ] Figure share/export produces clean PNG via Web Share API
- [ ] All figure types tested on iPhone SE (375px) and iPad (810px)
- [ ] RTL: figure labels correct in Arabic and Hebrew

## Testing Requirements
- **Unit**: `usePinchZoom.ts` — test zoom calculation, bounds clamping, double-tap reset
- **Unit**: `useFigureExport.ts` — test PNG capture, Web Share API fallback
- **Integration**: Playwright mobile emulation — render each figure type, verify sizing, verify touch interactions
- **Manual (REQUIRED)**: Real device testing for touch precision in FBD Construct mode — emulators do not accurately simulate finger imprecision

## DoD
- PR merged to `main`
- Screenshot comparison: each figure type on desktop vs mobile vs tablet
- FBD Construct mode video on real phone
- Touch precision test results (can a user place arrows accurately?)

## Reporting
Complete with: `branch=<worker>/<task-id>-pwa-figures-mobile,figure_types_optimized=<n>,fbd_construct_usable=<yes|no>`
