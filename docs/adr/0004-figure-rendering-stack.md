# ADR-0004 — Figure rendering stack for Cena questions

- **Status**: Proposed
- **Date proposed**: 2026-04-13
- **Deciders**: Shaker (project owner), claude-code (coordinator)
- **Supersedes**: none
- **Related**: [ADR-0002](0002-sympy-correctness-oracle.md) (CAS verification), [Mobile diagram models](../../src/mobile/lib/features/diagrams/models/diagram_models.dart)
- **Task**: FIGURE-001 (`t_df2c171d1345`)

---

## Context

`QuestionCard.vue` renders text-only today — zero figure or graph support. The student web has `katex`, `apexcharts`, and `chart.js` installed, but none are appropriate for mathematical function plots or physics diagrams. The mobile app has a 575-line diagram model spec (`diagram_models.dart`) covering 9 diagram types across 4 formats. The web needs a matching capability.

Physics diagrams must be publication-ready (inclined planes, force vectors, free-body diagrams with LaTeX labels). **Text-to-image models are rejected** — they hallucinate forces and labels. All mathematical content in figures must be CAS-verifiable per ADR-0002.

---

## Decisions

### 1. Function plots — `function-plot` (d3-based)

| Library | Size | License | Pros | Cons |
|---------|------|---------|------|------|
| **function-plot** | ~17KB | MIT | Purpose-built for y=f(x), parametric, polar; d3-based; interactive panning/zoom | Limited to 2D functions |
| JSXGraph | ~300KB | LGPL-3.0+ | Full-featured, geometry too | Heavy; LGPL complicates bundling |
| Desmos API | 0 (hosted) | Commercial | Best UX | Non-free; sandbox-only per Decision 8 |
| D3 from scratch | varies | BSD | Full control | Engineering cost too high for function plots |

**Chosen**: `function-plot` — 17KB, MIT, covers all Bagrut/AP/SAT function plot needs.
**Bundle impact**: +17KB gzipped to student-web.

### 2. Geometry & constructions — JSXGraph

| Library | Size | License | Pros | Cons |
|---------|------|---------|------|------|
| **JSXGraph** | ~300KB | LGPL-3.0+ | Euclidean constructions, transformations, loci; 20+ year history | LGPL requires dynamic linking (OK for web, needs care for bundling) |
| GeoGebra embed | ~2MB | GPL/Commercial | Most capable | GPL; massive bundle; commercial license needed |
| Custom SVG | 0 | - | Lightweight | Engineering cost prohibitive for interactive geometry |

**Chosen**: JSXGraph — loaded dynamically (not in main bundle) for geometry questions only. LGPL satisfied via dynamic `<script>` tag loading.
**Bundle impact**: 0 in main bundle; ~300KB lazy-loaded when geometry questions appear.

### 3. Physics diagrams — programmatic SVG (D3 + KaTeX-to-SVG)

| Approach | Render time | Quality | Correctness | Infra |
|----------|-------------|---------|-------------|-------|
| **Programmatic SVG** (D3 + KaTeX) | <50ms client | High | CAS-verifiable specs | None — client-side |
| TikZ server-side | 2–5s | Publication | CAS-verifiable | LaTeX install in container |
| Matplotlib + tikzplotlib | 1–3s | Good | CAS-verifiable | Python service |
| Text-to-image AI | 1–5s | Variable | **NOT verifiable** | API calls |
| Hand-drawn SVG templates | <10ms | Rigid | Manual only | None |

**Chosen**: Programmatic SVG from a declarative `PhysicsDiagramSpec` JSON, rendered client-side using D3 for layout and KaTeX-to-SVG for math labels. The spec is authored in admin, validated by CAS (e.g. equilibrium check: sum of forces = 0), and rendered deterministically.

**Rejected**: Text-to-image models — they hallucinate force magnitudes, mislabel vectors, and cannot be CAS-verified. This is a hard rejection per ADR-0002.
**Bundle impact**: D3 already a transitive dependency via function-plot. KaTeX already installed. Net +0KB.

### 4. Ingested raster figures — PNG via CDN

Bagrut PDF OCR pipeline produces bounding-boxed PNGs. Store as-is, serve via CDN with `<img>` tag. Alt text required (aria-label from OCR or manual entry). No client-side rendering library needed.

**Bundle impact**: 0KB.

### 5. Math typesetting — KaTeX only

KaTeX is the sole math typesetter. MathJax is rejected (470KB vs KaTeX's 90KB; 3-5x slower render). KaTeX is already installed and used for question text rendering.

**Bundle impact**: 0KB (already installed).

### 6. Dark mode + RTL

- All SVG figures use CSS custom properties for colors (`--cena-figure-fg`, `--cena-figure-bg`, `--cena-figure-accent`)
- KaTeX math inherits parent color
- `function-plot` uses D3 SVG which respects CSS
- JSXGraph respects container styles
- Physics diagram text elements carry a `script` property (`ltr`/`rtl`) for bidi rendering (FIG-RTL-001)
- All math content rendered LTR regardless of page direction (per existing "Math always LTR" rule)

### 7. Print / PDF export — SVG-first

All figure types render as SVG (except raster fallback), which scales cleanly to print. The "Bagrut practice booklet" export path uses server-side SVG → PDF (via Puppeteer or wkhtmltopdf). No rasterization step for vector figures.

### 8. Sandbox mode — Desmos API (sandbox only)

Desmos Graphing Calculator API is permitted **only** for the PhET-style sandbox mode (game-design Point 4). It is NOT used for static question figures — those use `function-plot`. Desmos license requires attribution and prohibits commercial resale of the calculator UI itself; Cena's use (embedded exploration tool) is within terms.

**Bundle impact**: Desmos API loaded from CDN only on sandbox pages. 0KB in main bundle.

---

## Bundle impact summary

| Library | Pages | Load strategy | Size |
|---------|-------|---------------|------|
| function-plot | Question cards with function plots | Lazy import | +17KB |
| JSXGraph | Geometry questions only | Dynamic `<script>` | +300KB (lazy) |
| D3 (subset) | Physics diagrams | Already included | +0KB |
| KaTeX | All math rendering | Already included | +0KB |
| Desmos API | Sandbox mode only | CDN script | +0KB (external) |
| **Total main bundle increase** | | | **+0KB** |
| **Total lazy-loadable** | | | **~317KB** |

---

## Downstream tasks unblocked

- **FIGURE-002**: `figure_spec` JSON schema on QuestionDocument
- **FIGURE-003**: `<QuestionFigure>` Vue component wiring function-plot + JSXGraph + SVG
- **FIGURE-004**: Wire into QuestionCard.vue + seed demo questions
- **FIGURE-005**: Backend `PhysicsDiagramService` generating SVG specs
- **FIGURE-006**: Admin figure editor with live preview
- **FIGURE-007**: Quality gate rules (CAS equilibrium check, aria-label)
- **FIGURE-008**: AI generation proposing figure specs

---

## Licensing summary

| Library | License | Commercial use | Bundling notes |
|---------|---------|----------------|----------------|
| function-plot | MIT | Yes | No restrictions |
| JSXGraph | LGPL-3.0+ | Yes | Must link dynamically (satisfied via script tag) |
| D3 | ISC | Yes | No restrictions |
| KaTeX | MIT | Yes | No restrictions |
| Desmos API | Commercial (free for educational) | Attribution required | CDN-only, no bundling |
