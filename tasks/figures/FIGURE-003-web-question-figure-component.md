# FIGURE-003: Web — `<QuestionFigure>` Vue component

## Goal
Build a single Vue component that renders any `FigureSpec` (from FIGURE-002) with publication-ready quality, dark-mode parity, RTL safety, and accessibility.

## Depends on
- FIGURE-001 (ADR locked)
- FIGURE-002 (schema merged — component consumes real DTOs)

## Context
`src/student/full-version/src/components/session/QuestionCard.vue` currently renders text only. `katex@^0.16.45` is installed but unused. No function-plot, no JSXGraph, no D3.

## Work to do
1. Install whatever libraries the ADR locks. Typical expected set: `function-plot`, `jsxgraph`, `d3`. Confirm bundle-size impact and lock versions.
2. Create `src/student/full-version/src/components/session/QuestionFigure.vue`:
   - Props: `spec: FigureSpec`, `theme: 'light' | 'dark'`, `locale: 'en' | 'ar' | 'he'`
   - Renders the right sub-component based on `spec.type`
   - Accessible: wraps output in `<figure role="img" :aria-label="spec.ariaLabel">` with an inside `<figcaption v-if="spec.caption">`
3. Sub-component `FunctionPlotFigure.vue`:
   - Uses `function-plot` library
   - Colors from Vuexy theme (`#7367F0` for curve in light, lighter purple in dark)
   - Renders markers (roots, vertex, y-intercept) from `spec.markers`
   - Axes + grid style matches the theme, 1px crisp, retina-safe
   - Curve draws in over 300ms on mount (respect `prefers-reduced-motion`)
   - Hover/tap a marker → shows coordinate callout (mobile: tap; desktop: hover)
4. Sub-component `GeometryFigure.vue`:
   - Uses JSXGraph from `spec.jsxGraphJson`
   - Same theme integration
5. Sub-component `PhysicsFigure.vue`:
   - **Option A**: consume server-generated SVG URL from FIGURE-005 pipeline (simpler, matches mobile pattern)
   - **Option B**: client-side D3 renderer (heavier, allows interactivity)
   - Pick one in the ADR, implement here
6. Sub-component `RasterFigure.vue`:
   - `<img :src="spec.cdnUrl" :width :height :alt="spec.ariaLabel">`
   - Lazy-loaded, skeleton while loading, srcset for retina
7. Dark mode: reactive to `useTheme()` or equivalent; all four sub-components re-render / re-color on theme change
8. RTL: question stem flows RTL for AR/HE but the figure stays LTR. Use `dir="ltr"` on the figure wrapper. Numeric axis labels LTR, any localized caption uses `dir="auto"`.
9. Tests:
   - `src/student/full-version/tests/unit/QuestionFigure.spec.ts` — one test per figure type
   - Snapshot test per variant
   - Accessibility test: `aria-label` present and non-empty; fails build if missing

## Non-negotiables
- No variable-ratio animation (Point 7 of the proposal — ship-gate)
- No confetti, no sparkles, no sound on render
- `prefers-reduced-motion` strictly honored
- Bundle-size budget: total impact < 120KB gzipped. If JSXGraph alone is >100KB, lazy-load it on geometry questions only.

## Do NOT
- Wire into `QuestionCard.vue` here — that's FIGURE-004
- Render any spec without an `aria-label` (should error loudly)
- Use chart.js or apexcharts — wrong tool

## DoD
- Component + 4 sub-components exist
- 4+ unit tests pass
- Bundle-size report included in PR description
- Storybook or demo page renders all 4 types in light + dark + RTL

## Reporting
Complete with branch name, bundle-size delta, and list of files touched.
