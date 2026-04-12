# FIGURE-001: ADR — Figure rendering stack for Cena questions

## Goal
Produce an Architecture Decision Record (`docs/adr/ADR-XXX-figure-rendering-stack.md`) that locks the library choices for rendering mathematical function plots, geometry diagrams, physics free-body / inclined-plane / vector diagrams, and ingested raster figures across student web (Vue 3), student mobile (Flutter), admin authoring UI, and the print/PDF export path.

## Context
- Current state: `student/full-version/package.json` has `katex` (installed, not used in session UI), `apexcharts`, `chart.js`. None of these are appropriate for function-plot or physics free-body diagrams. `QuestionCard.vue` renders text only today — zero figure/graph support.
- Mobile already has a coherent spec in `src/mobile/lib/features/diagrams/models/diagram_models.dart` (575 lines): 9 diagram types (`functionPlot`, `geometry`, `circuit`, `molecular`, `physicsVector`, `workedExample`, `challengeCard`, etc.), 4 formats (SVG, PNG, Rive, Remotion). Web has nothing equivalent.
- User showed the GPAI "Text to Technical Diagram" reference (inclined-plane dynamics, force vectors, LaTeX labels, publication-ready). Cena needs to match that bar for physics.
- Research doc `docs/autoresearch/math-ocr-research.md` (2026-03-27) recommends SymPy as the validation layer — **figure correctness must be verifiable by symbolic math, not by a vision model**.

## Required decisions
For each target, produce: chosen library, alternatives considered, reasoning, trade-offs, cost, licensing.

1. **Function plots (y = f(x), parametric, polar)** — recommendation to evaluate: `function-plot.js` (d3-based, 17KB, purpose-built). Alternatives: JSXGraph, D3 from scratch, Desmos API.
2. **Geometry + constructions (Euclidean proofs, transformations, loci)** — recommendation to evaluate: JSXGraph. Alternatives: GeoGebra embed (heavy), custom SVG.
3. **Physics free-body / inclined-plane / vector / circuit** — recommendation to evaluate: **programmatic SVG via D3 + KaTeX-to-SVG for math labels**, generated server-side from a declarative `PhysicsDiagramSpec`. Alternatives: (a) TikZ server-side via headless LaTeX (best quality, 2–5s/render, heavy infra), (b) Matplotlib + tikzplotlib (python service), (c) hand-drawn SVG templates (fastest but not parametric). **Must reject text-to-image models** (Nano Banana / DALL-E / GPAI-style) for correctness reasons — they hallucinate forces and labels.
4. **Ingested raster figures (from Bagrut PDF OCR)** — recommendation: store as PNG with Mathpix bounding box, served via CDN, fallback only.
5. **Math typesetting** — confirm KaTeX is the one-and-only typesetter; reject MathJax (size + slower render).
6. **Dark mode + RTL** — theme handling strategy across all of the above.
7. **Print / PDF export** — SVG-first so everything scales cleanly to the "Bagrut practice booklet" printed view.
8. **Sandbox mode** (Point 4 of Cena game-design proposal, PhET-style) — Desmos API permitted here ONLY, not for static question figures. Licensing must be checked.

## Files to create
- `docs/adr/ADR-XXX-figure-rendering-stack.md` (new)

## Acceptance criteria
- ADR lists all 8 targets with a single chosen library each and concrete reasoning
- Bundle-size impact quantified for web (KB added to student-web bundle)
- Licensing called out for each library (MIT / BSD / commercial / noncommercial)
- At least one alternative considered per target with reason for rejection
- Matches Cena's non-negotiables from `docs/research/cena-sexy-game-research-2026-04-11.md`: no dark patterns, SymPy is the correctness oracle, publication-ready look for physics

## DoD
- ADR merged to `main`
- Follow-on tasks (FIGURE-002..008) unblocked and can reference the chosen stack

## Reporting
Complete with: `phrase=figure-001,stack={functions:X,geometry:Y,physics:Z},branch=claude-subagent-adr/figure-001-rendering-stack`
