# FIGURE-004: Web — wire `<QuestionFigure>` into `QuestionCard.vue` + seed demo

## Goal
Make a student opening a question with a figure see that figure, end-to-end, in light mode, dark mode, EN, AR, HE. Plus seed 6 demo questions covering all 4 figure types so the pipeline is verifiable by eye.

## Depends on
- FIGURE-002 (schema merged, API serving `figure_spec`)
- FIGURE-003 (`QuestionFigure.vue` built + tested)

## Work to do
1. Edit `src/student/full-version/src/components/session/QuestionCard.vue`:
   - Import `QuestionFigure`
   - Render `<QuestionFigure v-if="question.figureSpec" :spec="question.figureSpec" :theme="currentTheme" :locale="locale" />` above the question stem
   - Spacing + responsive layout: figure takes full card width on mobile, 60% with stem beside on desktop ≥ 1024px, stacks again on very narrow
   - KaTeX rendering of the stem itself — since `katex` is installed but unused, this is the moment to actually wire it for all questions via a small `useKatex()` composable or directly in the template. Scope: minimal, just render inline and block math in the stem.
2. Edit `src/api/Cena.Admin.Api/QuestionBankSeedData.cs` — add 6 seed questions:
   a. **Algebra function plot**: "Find the roots of f(x) = x² − 4x + 3" with `FunctionPlotSpec` showing the parabola and root markers at x=1, x=3
   b. **Calculus function plot**: "Sketch f(x) = sin(x) + 0.5x on [-2π, 2π]" with parametric curve
   c. **Geometry construction**: "Given triangle ABC with..." rendered via JSXGraph
   d. **Physics inclined plane**: "A 5 kg block slides down a 30° incline with μ_k = 0.2. Find the acceleration." → `PhysicsDiagramSpec` (inclined plane, 5 forces, non-inertial OFF)
   e. **Physics free-body**: standalone free-body diagram for a two-body pulley
   f. **Raster fallback**: a legacy Bagrut figure served from a test CDN path
3. Dark-mode verification:
   - Run the student app, open each seed question in both light and dark
   - Screenshots go into `docs/reviews/screenshots/figure-004/` (6 questions × 2 themes = 12 images)
4. RTL verification:
   - Open at least (a) and (d) in AR and HE — figure stays LTR, stem flows RTL, no layout break
   - Screenshots added to same folder
5. Accessibility verification:
   - Run axe on one session page containing figure (a); zero new violations
   - Screen-reader spot check on the `aria-label` of (d) — the description must be meaningful ("inclined plane at 30 degrees with friction force up the slope")

## Non-negotiables
- If `figureSpec.ariaLabel` is empty or missing, DO NOT render — log an error and fail the question load. (Policy: no question with a figure ships without alt text.)
- No layout shift (CLS) when the figure loads — reserve space from the spec dimensions.

## DoD
- Build + unit + integration tests green
- 12 light/dark screenshots + 2 RTL screenshots committed
- Axe run zero new violations
- Seed questions visible in admin Question Bank

## Reporting
Complete with branch name, screenshot folder path, and axe report summary.
