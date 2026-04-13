# Cena Question Engine — Full Architecture Reference

> **Date**: 2026-04-12
> **Status**: Design decision document
> **Scope**: Question generation, ingestion, rendering, step-by-step solver, CAS engine, figure rendering, physics diagrams, pricing analysis, and architectural decisions
> **Audience**: Engineering, product, content authoring

---

## Table of Contents

1. [Executive Summary](#1-executive-summary)
2. [Question Types](#2-question-types)
3. [Ingestion Pipeline](#3-ingestion-pipeline)
4. [Parametric Variant Generation](#4-parametric-variant-generation)
5. [Figure & Graph Rendering](#5-figure--graph-rendering)
6. [Physics Diagrams](#6-physics-diagrams)
7. [Step-by-Step Solver](#7-step-by-step-solver)
8. [CAS Engine Architecture](#8-cas-engine-architecture)
9. [CAS Options: Pricing & Comparison](#9-cas-options-pricing--comparison)
10. [Recommended Architecture: Cena Mini CAS Engine](#10-recommended-architecture-cena-mini-cas-engine)
11. [UI Components](#11-ui-components)
12. [Admin Authoring Experience](#12-admin-authoring-experience)
13. [Quality Gate](#13-quality-gate)
14. [AI Generation Pipeline](#14-ai-generation-pipeline)
15. [RTL & Localization](#15-rtl--localization)
16. [Accessibility](#16-accessibility)
17. [Cost Model](#17-cost-model)
18. [Task Queue (Implementation Plan)](#18-task-queue-implementation-plan)
19. [Open Decisions](#19-open-decisions)
20. [Expert Architecture Review](#20-expert-architecture-review)
21. [IRT Calibration & Adaptive Selection](#21-irt-calibration--adaptive-selection)
22. [Bagrut Curriculum Alignment](#22-bagrut-curriculum-alignment)
23. [RTL & Bidi Deep Engineering](#23-rtl--bidi-deep-engineering)
24. [Motivation Design & Session UX](#24-motivation-design--session-ux)
25. [Assessment Security & Integrity](#25-assessment-security--integrity)
26. [Bagrut Readiness Reporting](#26-bagrut-readiness-reporting)
27. [Photo Ingestion Security & Privacy](#27-photo-ingestion-security--privacy)
28. [LaTeX Sanitization](#28-latex-sanitization)
29. [Rate Limiting & Cost Protection](#29-rate-limiting--cost-protection)
30. [Content Moderation for Minors](#30-content-moderation-for-minors)
31. [Photo Input Accessibility](#31-photo-input-accessibility)
32. [Graceful Degradation](#32-graceful-degradation)
33. [Academic Integrity & Anti-Cheating](#33-academic-integrity--anti-cheating)
34. [Pedagogical Integrity: Does Photo Input Help or Hurt?](#34-pedagogical-integrity-does-photo-input-help-or-hurt)
35. [Solution Design: Component Architecture](#35-solution-design-component-architecture)
36. [Solution Design: Data Architecture](#36-solution-design-data-architecture)
37. [Solution Design: CAS Verification Flow](#37-solution-design-cas-verification-flow)
38. [Solution Design: Question Selection Algorithm](#38-solution-design-question-selection-algorithm)
39. [Solution Design: Deployment Topology & Cost](#39-solution-design-deployment-topology--cost)
40. [Solution Design: Non-Functional Requirements](#40-solution-design-non-functional-requirements)
41. [Solution Design: Critical Path (8-Week Build)](#41-solution-design-critical-path-8-week-build)
42. [Consolidated Improvement Registry](#42-consolidated-improvement-registry)
43. [References](#43-references)

---

## 1. Executive Summary

Cena's question engine serves math and physics questions to Israeli Bagrut (4/5 unit) students in Arabic and Hebrew. (English/AP Calculus/SAT deferred to a later phase.) The engine must:

- **Ingest** questions from Bagrut PDF exams and student-submitted photos
- **Generate variants** at different difficulty levels from the same base question
- **Render** function plots, geometry constructions, and physics diagrams at publication quality
- **Guide students step-by-step** through solutions (Photomath-style, but the student does the work)
- **Verify every step** using a computer algebra system — the LLM never computes correctness
- **Respect compliance**: no profile-scoped misconception data on minors, no dark patterns, session-scoped telemetry only

The recommended architecture is a **3-tier CAS engine** (MathNet in-process → SymPy sidecar → Wolfram fallback) with **function-plot.js** for math graphs, **programmatic SVG** for physics diagrams, and a **step-solver UI** that adapts scaffolding to the student's BKT mastery level.

Total incremental infrastructure cost: **~$65–115/month** at 100K student queries/month.

---

## 2. Question Types

### 2.1 Multiple Choice (MCQ) — existing

The current `QuestionCard.vue` renders MCQ with 4 choices, hint button, and worked-example scaffold. This is the baseline and continues to serve as the primary format for quick-assessment and review sessions.

**Schema**: `SessionQuestionDto` in `src/student/full-version/src/api/types/common.ts:277`

### 2.2 Step-by-Step Solver — new

The student works through a structured solution, filling in each algebraic/calculus step. The system checks each step in real time via the CAS engine. This is the Photomath-like experience, but inverted: the student produces the steps, not the system.

**Pedagogical basis**:
- VanLehn 2011: step-based ITS achieves d ≈ 0.76 vs answer-only d ≈ 0.31
- Renkl & Atkinson 2003: faded worked examples, d ≈ 0.4–0.6
- Kalyuga 2003: expertise reversal — scaffolds hurt advanced students

**Three scaffolding levels** (driven by BKT mastery, already computed by `ScaffoldingService`):

| Level | Mastery Range | What the student sees |
|-------|--------------|----------------------|
| **Full** (novice) | < 0.20 | Every step labeled with instruction + faded worked example for pattern reference |
| **Partial** (intermediate) | 0.20–0.60 | Some steps pre-filled (given), student fills gaps |
| **Minimal** (advanced) | > 0.60 | Numbered slots only, student decides approach |

**Proposed backend schema**:

```csharp
public sealed record StepSolverQuestion(
    string Stem,
    FigureSpec? FigureSpec,
    IReadOnlyList<SolutionStep> Steps,
    string FinalAnswer,            // SymPy-verified LaTeX
    ScaffoldingLevel Level         // computed from BKT
);

public sealed record SolutionStep(
    int StepNumber,
    string? Instruction,           // null for minimal scaffolding
    string? FadedExample,          // null for partial+ and minimal
    string ExpectedExpression,     // LaTeX — verified by SymPy
    IReadOnlyList<string> Hints    // ordered by specificity
);
```

**Student-facing API**:
```
POST /api/sessions/{id}/question/{qid}/step/{stepNum}
Body: { "expression": "LaTeX string" }
Response: { "correct": bool, "feedback": string?, "nextStepUnlocked": bool }
```

The backend calls the CAS engine for symbolic equivalence checking. If the student's expression is symbolically equivalent to the expected expression (not just string-equal), the step passes.

### 2.3 Free-Response (future)

Open-ended input where the student writes a full solution. CAS validates the final answer; LLM provides feedback on the approach. Not in scope for Phase 1.

---

## 3. Ingestion Pipeline

### 3.1 Current State

- **Research doc**: `docs/autoresearch/math-ocr-research.md` (2026-03-27) — evaluated 6 tools
- **Admin UI scaffold**: `src/admin/full-version/src/views/apps/ingestion/` — `UploadDialog.vue`, `PipelineStats.vue`, `ItemDetailPanel.vue`
- **Backend**: `src/api/Cena.Admin.Api/IngestionPipelineService.cs` + `IngestionPipelineCloudDir.cs`

### 3.2 Two Ingestion Paths

#### Path A: Bagrut PDF → Structured Question

1. **Upload** PDF to admin ingestion pipeline
2. **Layout extraction**: Marker/Surya for page structure + text blocks
3. **Math OCR**: Mathpix for LaTeX extraction from equations ($0.005/page, ~640 pages for full Bagrut corpus = ~$3.20 total)
4. **Semantic structuring**: LLM (Gemini 2.5 Flash or Claude) parses extracted text into stem + choices + answer + figure regions
5. **Figure handling**: raster crop stored as PNG on CDN; optionally converted to parametric `FigureSpec` by human author
6. **Validation**: SymPy verifies that stated answers are actually correct for the stated problem
7. **Output**: canonical `QuestionDocument` event, same as manual authoring

**Cost for full Bagrut corpus**: ~$3.20 (Mathpix) + ~$2 (LLM structuring) = **~$5 total, one-time**

#### Path B: Student Photo → Real-Time Question Recognition

1. **Student takes photo** of a textbook problem or handwritten work
2. **Vision model**: Gemini 2.5 Flash ($0.002/image, sub-second latency)
3. **LaTeX extraction**: math → LaTeX, text → unicode
4. **CAS validation**: SymPy checks extracted math for internal consistency
5. **Fallback**: Mathpix for complex handwritten equations
6. **Output**: structured question for practice, or step-by-step solution walkthrough

**Cost at scale**: 100K photos/month = **~$200/month** (Gemini) or **~$150/month** (Mathpix batch)

### 3.3 Copyright Considerations

Bagrut papers are produced by מכון סאלד / משרד החינוך. Ingesting for seed generation is defensible; redistributing verbatim is not. Cena stores the source as reference and ships **only generated variants** to students, with attribution. Legal review required before production launch.

---

## 4. Parametric Variant Generation

"Same format, different level" — the core of Cena's content-at-scale story.

### 4.1 Three Strategies

#### Strategy 1: Parametric Templates (preferred, deterministic)

The original question becomes a template with symbolic slots:

```
Template: "Find the roots of f(x) = x² - {b}x + {c}"
Slots: b ∈ RandomInt(2..10), c = product of two distinct integers in (-N..N)
Constraints: discriminant ≥ 0 (real roots)
```

SymPy constructs the matching stem, answer key, and distractors from the slot values. The figure regenerates automatically because it's driven by the same slots.

**Coverage**: ~80% of Bagrut algebra/trig/calculus items are parametrizable.

#### Strategy 2: Isomorph Generation (LLM + CAS verification)

For word problems and narrative-setup items, LLM rewrites the surface story while preserving the underlying math object. SymPy re-verifies the answer. Rejections loop back to the LLM once, then go to human review.

#### Strategy 3: Difficulty Laddering (explicit rubric)

Each topic has defined difficulty rungs:

| Topic | Easy | Medium | Hard |
|-------|------|--------|------|
| Quadratics | Integer roots | Rational roots | Complex roots |
| Derivatives | Power rule only | Chain rule | Implicit differentiation |
| Inclined plane | No friction | Kinetic friction | Non-inertial frame + friction |

Each rung has acceptance criteria checked by the quality gate.

### 4.2 Difficulty Tracking

`EloDifficultyService` already exists in `src/actors/Cena.Actors.Tests/Mastery/EloDifficultyServiceTests.cs`. Generated variants carry an Elo estimate that's calibrated by student performance data over time.

---

## 5. Figure & Graph Rendering

### 5.1 Current State

| Component | Status |
|-----------|--------|
| `katex@^0.16.45` | Installed in student-web `package.json`, **not used** in session UI |
| `apexcharts` + `chart.js` | Installed for admin dashboards, **wrong tool** for function plots |
| `QuestionCard.vue` | Renders **text only** — zero figure/graph support |
| Mobile `diagram_models.dart` | Fully designed (575 lines, 9 diagram types, 4 formats) |

### 5.2 Recommended Library Stack

| Target | Library | Bundle Size | License | Latency |
|--------|---------|------------|---------|---------|
| **Function plots** (y = f(x), parametric, polar) | `function-plot.js` | ~17KB gzip (+ d3) | MIT | <50ms render |
| **Geometry** (Euclidean constructions, loci) | `JSXGraph` | ~100KB gzip | LGPL-3.0 | <100ms render |
| **Physics diagrams** (free-body, inclined plane, vector) | Programmatic SVG (D3 + KaTeX labels) | N/A (server-generated) | N/A | Generated once, served from CDN |
| **Raster fallback** (ingested Bagrut figures) | `<img>` with lazy-load | N/A | N/A | CDN latency |
| **Math typesetting** | KaTeX (already installed) | ~100KB gzip | MIT | <10ms |
| **Sandbox mode** (PhET-style, Point 4) | Desmos API | External embed | Commercial | API-dependent |

**Bundle budget**: total impact < 120KB gzipped for student-web. JSXGraph lazy-loaded on geometry questions only.

**Rejected alternatives**:
- Chart.js / ApexCharts — dashboard charts, not function plots
- Desmos for all figures — vendor lock-in, branding, can't white-label
- MathJax — slower and larger than KaTeX
- Image-gen models (DALL-E, Nano Banana, GPAI) — hallucinate forces and labels, can't verify correctness

### 5.3 FigureSpec Schema (discriminated union)

```csharp
public abstract record FigureSpec(string Type);

public sealed record FunctionPlotSpec(
    string Expression,           // LaTeX: "x^2 - 4x + 3"
    double XMin, double XMax,
    double YMin, double YMax,
    IReadOnlyList<FigureMarker> Markers,  // roots, vertices, intercepts
    string? Caption,
    string AriaLabel             // MANDATORY — a11y non-negotiable
) : FigureSpec("function_plot");

public sealed record PhysicsDiagramSpec(
    PhysicsBodyType Body,        // InclinedPlane | Pulley | FreeBody | CircularMotion
    IReadOnlyList<PhysicsForce> Forces,
    IReadOnlyDictionary<string, double> Parameters,
    CoordinateFrame Frame,
    string? Caption,
    string AriaLabel
) : FigureSpec("physics_diagram");

public sealed record GeometryConstructionSpec(
    string JsxGraphJson,         // JSXGraph declarative spec
    string? Caption,
    string AriaLabel
) : FigureSpec("geometry");

public sealed record RasterFigureSpec(
    string CdnUrl,
    int WidthPx, int HeightPx,
    string? Caption,
    string AriaLabel,
    string? SourceAttribution    // copyright trail for Bagrut-derived figures
) : FigureSpec("raster");
```

### 5.4 Dark Mode & Theme

All figure types must re-render / re-color on theme change:
- Curve color: `#7367F0` (Vuexy primary) in light, `#9E95F5` in dark
- Grid: `#e8e8e8` light, `#434968` dark
- Axes: `#666` light, `#8692D0` dark
- Physics forces: consistent color per type (gravity=red, normal=green, friction=orange) in both themes

### 5.5 Working Samples

Interactive HTML samples demonstrating all concepts:
- `examples/figure-sample/index.html` — 5 question cards (quadratic, trig, physics inclined plane, Arabic RTL, derivative + tangent)
- `examples/figure-sample/step-solver.html` — 3 scaffolding levels of the step-by-step solver
- `examples/figure-sample/figure-sample.js` — rendering script for function-plot.js + programmatic SVG

---

## 6. Physics Diagrams

### 6.1 Reference Quality

The target quality matches the GPAI "Text to Technical Diagram" reference: publication-ready, textbook-grade force diagrams with LaTeX labels, crisp vector arrows, angle arcs, coordinate axes, and proper typographic conventions.

### 6.2 Implementation: Programmatic SVG

**NOT image-gen models.** Image generation models (DALL-E, Nano Banana, GPAI) hallucinate forces and labels — educationally wrong, fails quality gate.

The `PhysicsDiagramService` takes a `PhysicsDiagramSpec` and produces a deterministic SVG:

**Body types in v1**:
- Inclined plane (triangle with surface, mass box, angle arc, force arrows, component decomposition)
- Free-body diagram (isolated mass with all forces)
- Pulley system (two masses, rope, pulley wheel, tension vectors)
- 2D vector diagram (force addition, resolution into components)

**Body types in v2**:
- Circuit schematic (series, parallel, Kirchhoff loops)
- Circular motion (centripetal/centrifugal, normal, gravity)
- Wave pattern (standing waves, harmonics, nodes)

**Rendering primitives** (pure C# SVG builder):
- Coordinate frame (axes, ticks, labels)
- Force vector (arrow with head, color-coded by type, length ∝ magnitude)
- Angle arc with degree label
- Math labels (KaTeX-rendered SVG paths for font independence)
- Construction lines (dashed, for decomposition)
- Surface textures (hatching for ground, friction indicators)

**Caching**: content-addressed by spec hash → CDN. Each SVG < 30KB. Generated once per spec, served instantly.

**Aria-label generation**: deterministic text description from the spec, localized EN/AR/HE:
> "Inclined plane at 30 degrees. A 5 kilogram block rests on the surface. Gravity acts downward. Normal force acts perpendicular to the surface. Kinetic friction acts up the slope."

### 6.3 Correctness Verification

For physics specs that claim equilibrium or specify acceleration, the quality gate verifies:
- `Σ F = 0` for equilibrium specs
- `Σ F = ma` for acceleration specs
- Force directions consistent with body type (friction along surface, normal perpendicular)
- All required forces present (incline always has N + mg minimum)

---

## 7. Step-by-Step Solver

### 7.1 How It Works (Student Experience)

1. Student sees a question with an optional figure (graph or physics diagram)
2. Below the stem, a **step workspace** appears with numbered step slots
3. Each step has:
   - **Instruction** (what to do — present at Full scaffolding, absent at Minimal)
   - **Faded worked example** (a similar step done for a different problem — present at Full, partially at Partial)
   - **Math input field** (MathLive in production; text input with KaTeX preview in demo)
   - **Math keyboard** (common symbols: parentheses, exponents, fractions, roots, trig functions)
   - **Check button** → sends to CAS for symbolic equivalence verification
   - **Hint button** → reveals one targeted hint per step (ordered by specificity)
4. Steps **unlock progressively** — next step appears only after current passes
5. **Completion banner** shows the final answer when all steps pass
6. **"Next Question"** button activates only after completion

### 7.2 Scaffolding Adaptation

The same problem renders differently based on the student's mastery:

**Example**: Find the vertex of f(x) = x² - 4x + 3

**Full scaffolding (novice)**:
```
Step 1: Identify a, b, c in f(x) = ax² + bx + c
        [input field: a = ?, b = ?, c = ?]
Step 2: Complete the square
        [faded example: For g(x) = x² - 6x + 5: g(x) = (x-3)² - 4]
        [input field: f(x) = (x - ?)² + ?]
Step 3: Read the vertex coordinates
        [input field: (h, k) = ?]
Step 4: State the range
        [input field: Range: ?]
```

**Partial scaffolding (intermediate)**:
```
Step 1: [GIVEN] f(x) = 2(x² - 4x) + 6   ← pre-filled, study the pattern
Step 2: Complete the square and simplify
        [input field]
Step 3: State vertex and axis of symmetry
        [input field]
```

**Minimal scaffolding (advanced)**:
```
Step 1: Your approach...    [input field]
Step 2: Continue...         [input field]
Step 3: Final answer...     [input field]
```

### 7.3 CAS Verification Per Step

Each student input is sent to the CAS engine for **symbolic equivalence** checking:

```python
# Student writes: (x-2)^2 - 1
# Expected:       (x-2)^2 - 1
# Also accepted:  x^2 - 4x + 3  (expanded form — symbolically equivalent)
# Also accepted:  (x-2)² + (-1)  (equivalent with sign)
# Rejected:       (x+2)^2 - 1   (wrong sign — NOT equivalent)

from sympy import symbols, simplify, expand
x = symbols('x')
student = (x - 2)**2 - 1
expected = (x - 2)**2 - 1
assert simplify(student - expected) == 0  # True → correct
```

This is why string matching is insufficient — the CAS handles algebraic equivalence, commutativity, and alternative representations.

### 7.4 Step Generation (Authoring Side)

When an author creates a step-solver question, the system can **propose steps** using the CAS:

1. Parse the problem (e.g., "complete the square for x² - 4x + 3")
2. SymPy solves it step-by-step using its rewrite-rule engine
3. Each rewrite rule produces a named step (e.g., "add_subtract_square", "factor_perfect_square")
4. Steps are formatted with curriculum-aligned labels in EN/AR/HE
5. Author reviews, edits, adds hints and faded examples
6. Quality gate validates that each step's expected expression is symbolically correct

---

## 8. CAS Engine Architecture

### 8.1 Core Principle

**The LLM explains; the CAS computes.** No LLM output that claims a numerical or symbolic answer is shown to a student until a deterministic CAS has confirmed it.

**Evidence**:
- Khanmigo 2023 arithmetic-error incident: LLM gave wrong computation to students
- MathDial 2023: answer leakage ~40% within 3 turns when LLM has access to the answer
- VanLehn 2011: step-based ITS (d ≈ 0.76) requires verified step correctness

### 8.2 Three-Tier CAS Pipeline

```
Student input (LaTeX)
    │
    ▼
┌─────────────────────────────────────────────┐
│  Tier 1: MathNet.Symbolics (in-process)     │
│  ● Latency: < 10ms                         │
│  ● Cost: $0                                │
│  ● Handles: numeric eval, basic simplify,  │
│    equality check, polynomial expansion     │
│  ● ~60% of step-verification queries       │
│  ● If conclusive → return immediately      │
└─────────────┬───────────────────────────────┘
              │ inconclusive
              ▼
┌─────────────────────────────────────────────┐
│  Tier 2: SymPy sidecar (FastAPI on NATS)   │
│  ● Latency: 50–500ms                       │
│  ● Cost: ~$50–100/mo compute               │
│  ● Handles: symbolic equivalence,          │
│    step-by-step generation, derivative/    │
│    integral verification, equation solving, │
│    trig identity checking, limit evaluation │
│  ● ~35% of queries                         │
│  ● If conclusive → return                  │
└─────────────┬───────────────────────────────┘
              │ inconclusive (rare ~5%)
              ▼
┌─────────────────────────────────────────────┐
│  Tier 3: Wolfram Alpha API (HTTP fallback)  │
│  ● Latency: 500ms–3s                       │
│  ● Cost: capped at 2,000 queries/mo ($12)  │
│  ● Handles: edge cases SymPy can't solve   │
│  ● Admin content authoring only            │
│  ● Student data NEVER sent to Wolfram      │
│  ● Logged as "CAS fallback" for review     │
└─────────────────────────────────────────────┘
```

### 8.3 Why Not Pure Wolfram?

| Dimension | Pure Wolfram | Cena Mini Engine |
|-----------|-------------|-----------------|
| Cost at 100K queries/mo | $400–600/mo | **~$65/mo** |
| Latency (p50) | ~1.2s | **~80ms** |
| Latency (p95) | ~2.5s | **~350ms** |
| RTL formatting control | None | Full |
| Step labels for Bagrut | Generic English | Customized per topic, per language |
| Offline testing | No | Yes |
| COPPA / minor data | Student LaTeX sent to US servers | Student data stays in VPC |
| Vendor dependency | Critical | Graceful fallback only |

### 8.4 Implementation Plan

**SymPy sidecar service**:
- Python 3.12 + FastAPI + uvicorn
- Deployed as a container alongside the .NET backend
- Communicates via NATS request/reply (matches existing bus architecture)
- Endpoints:
  - `POST /verify` — symbolic equivalence check (two expressions)
  - `POST /solve` — solve equation, return steps
  - `POST /simplify` — normalize expression
  - `POST /evaluate` — numeric evaluation at a point
  - `POST /step-generate` — generate solution steps for a problem
- Stateless, horizontally scalable
- Health check: solve a known problem on startup, fail if wrong

**MathNet.Symbolics** (in-process .NET):
- Already in the .NET ecosystem — no new service
- Handles the fast path: numeric evaluation, polynomial expansion, basic equality
- Limitation: no calculus, no trig simplification, no equation solving
- Used as a **filter**, not a replacement

---

## 9. CAS Options: Pricing & Comparison

### 9.1 Commercial Options

| Service | Step-by-Step | Cost at 100K/mo | Latency | Lock-in | Minor Data |
|---------|-------------|-----------------|---------|---------|------------|
| **Wolfram Alpha Full Results** | Yes (podstate) | $400–600/mo | 500ms–3s | Total | Sent to US |
| **Wolfram Alpha Show Steps** | Yes (primary) | $100+/mo + Full Results | 500ms–3s | Total | Sent to US |
| **Wolfram Engine** (self-host) | Yes | $0 dev, $500+/yr prod | 50–500ms | License | Self-hosted |
| **Wolfram Cloud** | Yes | $25–50/user/mo | Variable | Moderate | Cloud |
| **Photomath engine** | Yes (proprietary) | N/A (acquired by Google) | N/A | N/A | N/A |

**Education discount**: Wolfram offers ~40–50% via site licenses (contact sales). No self-serve API discount.

### 9.2 Open-Source Options

| Engine | Language | Step-by-Step | Bagrut Coverage | Cost at 100K/mo | Latency |
|--------|----------|-------------|-----------------|-----------------|---------|
| **SymPy** | Python | Yes (custom tracers) | Excellent | $50–100 compute | 50–500ms |
| **Maxima** | Lisp | Partial | Strong | $40–80 compute | 100–800ms |
| **MathNet.Symbolics** | .NET/F# | No | Basic algebra only | $0 (in-process) | <10ms |
| **math.js** | JavaScript | No | Basic algebra only | $0 (client-side) | <5ms |
| **Giac/Xcas** | C++ | Yes (step mode) | Excellent (powers GeoGebra) | $30–50 compute | 20–200ms |
| **SageMath** | Python | Via engines | Broadest | $80–150 compute | 200–1000ms |

### 9.3 Bagrut Syllabus Coverage

| Topic | SymPy | MathNet | Wolfram | Giac |
|-------|-------|---------|---------|------|
| Quadratic equations | Full | Full | Full | Full |
| Polynomial factoring | Full | Partial | Full | Full |
| Derivatives (all rules) | Full | None | Full | Full |
| Definite/indefinite integrals | Full | None | Full | Full |
| Trig identities + equations | Full | None | Full | Full |
| Systems of linear equations | Full | Partial | Full | Full |
| Sequences & series | Full | None | Full | Full |
| Probability & statistics | Full | None | Full | Full |
| Complex numbers | Full | None | Full | Full |
| Limits | Full | None | Full | Full |
| Basic ODEs | Full | None | Full | Full |
| Advanced techniques | Partial | None | Full | Full |

**Verdict**: SymPy covers ~95% of the Bagrut syllabus. Wolfram is needed only for the remaining ~5% of edge cases.

---

## 10. Recommended Architecture: Cena Mini CAS Engine

### 10.1 Decision

**Hybrid: MathNet (fast path) + SymPy (primary) + Wolfram (admin-only fallback)**

### 10.2 Rationale

1. **Cost**: $65/mo vs $500/mo — 7.5x cheaper
2. **Latency**: p50 ~80ms vs ~1.2s — 15x faster (critical for step-by-step UX)
3. **Privacy**: student data never leaves VPC (COPPA/GDPR-K compliance)
4. **Control**: step labels customized per curriculum topic in 3 languages
5. **No vendor lock-in**: SymPy is BSD-licensed, MathNet is MIT-licensed
6. **Testable offline**: CI can run the full CAS suite without API keys

### 10.3 Service Topology

```
┌─────────────────────────────────────┐
│  Cena.Student.Api.Host (.NET)       │
│  ├── StepVerifierService            │
│  │   ├── MathNetFastPath (in-proc)  │
│  │   └── NatsClient → SymPy        │
│  └── SessionEndpoints               │
└──────────┬──────────────────────────┘
           │ NATS request/reply
           ▼
┌─────────────────────────────────────┐
│  cena-cas-sidecar (Python)          │
│  ├── FastAPI + uvicorn              │
│  ├── SymPy 1.13+                    │
│  ├── Custom step tracers            │
│  └── Health check on startup        │
└─────────────────────────────────────┘

┌─────────────────────────────────────┐
│  Cena.Admin.Api (.NET)              │
│  ├── AiGenerationService            │
│  │   ├── SymPy (primary)            │
│  │   └── Wolfram (fallback, capped) │
│  └── QualityGateService             │
└─────────────────────────────────────┘
```

### 10.4 Step-by-Step Generation with SymPy

```python
from sympy import symbols, solve, factor, expand, Eq
from sympy.integrals.manualintegrate import manualintegrate

x = symbols('x')

# Example: solve x² - 4x + 3 = 0 by completing the square
expr = x**2 - 4*x + 3

# Step tracer captures each rewrite:
steps = [
    {"step": 1, "instruction": "Identify a, b, c",
     "expression": "a=1, b=-4, c=3",
     "rule": "coefficient_identification"},

    {"step": 2, "instruction": "Complete the square",
     "expression": "(x-2)^2 - 1",
     "rule": "complete_the_square",
     "verification": simplify(expr - ((x-2)**2 - 1)) == 0},

    {"step": 3, "instruction": "Read vertex from (x-h)² + k",
     "expression": "(2, -1)",
     "rule": "vertex_extraction"},

    {"step": 4, "instruction": "State the range (a > 0 → opens upward)",
     "expression": "[-1, ∞)",
     "rule": "range_from_vertex"}
]
```

Each step's `verification` field is the CAS proof that the expression is correct. The content author reviews the generated steps and adds curriculum-specific labels in EN/AR/HE.

---

## 11. UI Components

### 11.1 Vue Components (Student Web)

| Component | Purpose | Status |
|-----------|---------|--------|
| `QuestionCard.vue` | MCQ rendering (existing) | Production |
| `QuestionFigure.vue` | Figure renderer (function-plot / JSXGraph / SVG / raster) | **FIGURE-003** |
| `FunctionPlotFigure.vue` | function-plot.js sub-component | **FIGURE-003** |
| `GeometryFigure.vue` | JSXGraph sub-component | **FIGURE-003** |
| `PhysicsFigure.vue` | Server-generated SVG display | **FIGURE-003** |
| `RasterFigure.vue` | CDN image with lazy-load | **FIGURE-003** |
| `StepSolverCard.vue` | Step-by-step workspace | **New task** |
| `StepInput.vue` | Single step: instruction + input + check + hint | **New task** |
| `MathInput.vue` | MathLive wrapper with KaTeX preview + keyboard | **New task** |
| `MathKeyboard.vue` | Topic-aware symbol keyboard | **New task** |

### 11.2 Flutter Components (Mobile)

| Component | Purpose | Status |
|-----------|---------|--------|
| `DiagramRenderer` | Renders all diagram types from `DiagramSpec` | Planned (model exists) |
| `StepSolverWidget` | Step-by-step workspace for mobile | **New task** |
| `MathInputField` | MathLive-equivalent for Flutter | **New task** |

### 11.3 Admin Components

| Component | Purpose | Status |
|-----------|---------|--------|
| `questions/edit/[id].vue` | Question editor (existing) | Production |
| Figure editor tab | Create/edit FigureSpec with live preview | **FIGURE-006** |
| Step editor tab | Create/edit solution steps with CAS validation | **New task** |
| Variant generator | Generate difficulty variants from a base question | **New task** |

---

## 12. Admin Authoring Experience

### 12.1 Question Creation Flow (with figures + steps)

1. **Choose question type**: MCQ or Step-Solver
2. **Write stem** with inline LaTeX (KaTeX live preview)
3. **Add figure** (optional):
   - Function plot: enter expression, set domain/range, place markers
   - Physics diagram: select body type, add forces, set parameters
   - Geometry: build construction in JSXGraph editor
   - Upload raster: paste URL or upload image
4. **For MCQ**: write 4 choices with distractor rationales
5. **For Step-Solver**: define solution steps
   - Manual: write each step's instruction + expected expression
   - AI-assisted: CAS proposes steps from the problem → author reviews
   - Per step: add 1–3 hints (ordered by specificity)
   - Per step: optionally add a faded worked example
6. **Set metadata**: subject, topic, grade, Bloom's level, difficulty, concepts, learning objective
7. **Add aria-label** for any figure (required — save blocked if missing)
8. **Quality gate runs automatically**: blocks save if violations found
9. **Add language versions**: translate stem + steps into AR/HE
10. **Preview**: see the question as a student would at each scaffolding level

### 12.2 Variant Generation Flow

1. Select a published question as the **template source**
2. Click "Generate Variants"
3. System proposes parametric slots (e.g., "roots can vary in [-10, 10]")
4. Author adjusts slot ranges and constraints
5. System generates N variants (e.g., 5)
6. Each variant: CAS verifies every step + figure + final answer
7. Quality gate runs on each variant
8. Author reviews + approves
9. Published variants enter the question bank with the same topic/concept tags

---

## 13. Quality Gate

### 13.1 Existing Gate

`src/api/Cena.Admin.Api.Tests/QualityGate/QualityGateTestData.cs` — 8-dimension quality scoring (factual accuracy, language quality, pedagogical quality, distractor quality, stem clarity, Bloom alignment, structural validity, cultural sensitivity).

### 13.2 New Figure-Specific Rules

| # | Rule | Type | Action |
|---|------|------|--------|
| F1 | `ariaLabel` present and ≥ 20 characters | All figures | **Reject** |
| F2 | Expression parses (no syntax errors) | FunctionPlot | **Reject** |
| F3 | All markers visible in [xMin, xMax] × [yMin, yMax] | FunctionPlot | **Reject** |
| F4 | Stated roots are actual roots: f(marker.x) ≈ 0 | FunctionPlot | **Reject** |
| F5 | Domain contains answer features (vertex, intercepts) | FunctionPlot | **Reject** |
| F6 | Force directions consistent with body type | Physics | **Reject** |
| F7 | Σ F = 0 for equilibrium specs | Physics | **Reject** |
| F8 | Σ F = ma for acceleration specs | Physics | **Reject** |
| F9 | All required forces present (N + mg minimum for incline) | Physics | **Reject** |
| F10 | JSXGraph JSON parses, no circular dependencies | Geometry | **Reject** |
| F11 | Raster exists at CDN URL, dimensions match | Raster | **Reject** |
| F12 | Source attribution present for Bagrut-derived figures | Raster | **Warn** |
| F13 | File size < 500KB (raster), < 30KB (SVG) | All | **Warn** |

### 13.3 New Step-Solver Rules

| # | Rule | Action |
|---|------|--------|
| S1 | Every step's expected expression verified by CAS | **Reject** |
| S2 | Steps form a valid proof chain (step N-1 → step N is a valid rewrite) | **Reject** |
| S3 | Final step matches the stated answer | **Reject** |
| S4 | At least 1 hint per step (Full scaffolding) | **Warn** |
| S5 | Faded example provided for steps 1–2 (Full scaffolding) | **Warn** |
| S6 | All hints localized in all 3 languages | **Warn** |

---

## 14. AI Generation Pipeline

### 14.1 Current State

`src/api/Cena.Admin.Api/AiGenerationService.cs` (885 lines) — generates questions from prompts.

### 14.2 Extended Pipeline (with figures + steps)

```
Author triggers "Generate Variant"
    │
    ▼
LLM proposes: stem + choices/steps + figure_spec + aria_label
    │
    ▼
Parse + validate against C# DTOs
    │
    ├── figure_spec → CAS checks (markers, forces, expressions)
    │                 → PhysicsDiagramService renders SVG
    │                 → Quality gate F1–F13
    │
    ├── steps → CAS verifies each step expression
    │          → Proof chain validation (step N derives from step N-1)
    │          → Quality gate S1–S6
    │
    └── If any gate fails → feed violations back to LLM (1 retry)
        └── If retry fails → mark as "requires_human_review"
```

### 14.3 Non-Negotiables

- **LLM does not compute correctness** — it proposes, CAS verifies
- **aria-label is deterministic** — generated from the spec, not by the LLM
- **No profile-scoped student data in generation prompts** (Edmodo FTC precedent)
- **Cost cap**: `maxFiguresPerDay` configurable in admin AI settings

---

## 15. RTL & Localization

### 15.1 Bidi Isolation Pattern

```html
<!-- Question card flows RTL for Arabic/Hebrew -->
<div class="question-card" dir="rtl">

  <!-- Figure wrapper stays LTR — math graphs are always LTR -->
  <figure class="question-figure" dir="ltr">
    <div id="function-plot"><!-- graph renders here --></div>
    <figcaption dir="rtl">رسم بياني: f(x) = x² - 4x + 3</figcaption>
  </figure>

  <!-- Stem flows RTL -->
  <div class="question-stem">
    الرسم البياني أعلاه يُظهر الدالة $f(x) = x^2 - 4x + 3$...
  </div>

  <!-- Math input stays LTR inside RTL card -->
  <input class="math-input" dir="ltr" lang="en">
</div>
```

### 15.2 Step Labels by Language

| Step Type | English | Arabic | Hebrew |
|-----------|---------|--------|--------|
| Identify coefficients | "Identify a, b, c" | "حدد المعاملات a, b, c" | "זהה את המקדמים a, b, c" |
| Complete the square | "Complete the square" | "أكمل المربع" | "השלם ריבוע" |
| Find the derivative | "Find f'(x)" | "أوجد المشتقة f'(x)" | "מצא את הנגזרת f'(x)" |
| State the range | "Determine the range" | "حدد المدى" | "קבע את הטווח" |

CAS engine controls these labels — not Wolfram's generic English output. This is a key advantage of the self-hosted approach.

### 15.3 MathLive RTL (GD-006 Spike)

Known unknowns:
- Caret direction in RTL container
- Numeric literal LTR isolation
- Backspace/navigation behavior
- Copy-paste from Bagrut PDF
- Screen-reader behavior in AR/HE

Time-boxed 1–2 day spike before committing to MathLive. Fallback: plain text input with KaTeX preview (as in the demo).

---

## 16. Accessibility

### 16.1 Requirements

| Feature | Requirement |
|---------|-------------|
| Figure alt text | `aria-label` mandatory on all `<figure>` elements. Save blocked if missing. |
| Screen reader | Meaningful descriptions: "Parabola opening upward, vertex at (2, -1), roots at x=1 and x=3" |
| Reduced motion | `prefers-reduced-motion` honored — no curve draw-in animation |
| Keyboard navigation | All step inputs, choices, and buttons reachable via Tab/Enter/Space |
| Color independence | Force types distinguished by shape (arrow head) AND color, not color alone |
| Focus indicators | 2px solid primary outline on `:focus-visible` |
| CLS prevention | Reserve space from figure dimensions before load |

### 16.2 Axe CI Integration

Student-web CI runs axe-core on session pages containing figures. Zero new violations required for merge.

---

## 17. Cost Model

### 17.1 Monthly Infrastructure Cost at 100K Student Queries

| Component | Cost | Notes |
|-----------|------|-------|
| SymPy sidecar (2 vCPU, 4GB) | $50–100/mo | Handles ~95% of CAS queries |
| Wolfram Alpha fallback | $12/mo | Capped at 2,000 queries/mo, admin only |
| CDN for physics SVGs | ~$5/mo | S3 + CloudFront, < 30KB per SVG |
| MathLive (client-side) | $0 | MIT license, runs in browser |
| function-plot.js | $0 | MIT license |
| JSXGraph | $0 | LGPL-3.0, lazy-loaded |
| KaTeX | $0 | MIT license |
| **Total incremental** | **$67–117/mo** | |

### 17.2 Content Ingestion Cost (One-Time)

| Item | Cost |
|------|------|
| Mathpix for Bagrut PDF corpus (~640 pages) | ~$3.20 |
| LLM structuring (Gemini 2.5 Flash) | ~$2.00 |
| **Total** | **~$5.20** |

### 17.3 Cost Comparison: What We Avoided

| Alternative | Monthly Cost | Why Rejected |
|-------------|-------------|-------------|
| Pure Wolfram Alpha | $400–600/mo | 7x more expensive, student data leaves infra |
| Wolfram Engine license | $500+/yr | Vendor lock-in, production license unclear |
| Desmos API (all figures) | Variable | Branding, can't white-label, vendor dependency |
| Image-gen for physics | $100+/mo | Hallucinate forces, can't verify, educationally wrong |

---

## 18. Task Queue (Implementation Plan)

### 18.1 Figure Rendering Wave (FIGURE-001 through FIGURE-008)

| Task | Title | Priority | Depends On |
|------|-------|----------|-----------|
| FIGURE-001 | ADR: Figure rendering stack | High | — |
| FIGURE-002 | Schema: `figure_spec` on QuestionDocument | High | 001 |
| FIGURE-003 | Web: `<QuestionFigure>` Vue component | High | 001, 002 |
| FIGURE-004 | Wire into QuestionCard + 6 seed demos | High | 002, 003 |
| FIGURE-005 | Backend: PhysicsDiagramService (SVG gen) | High | 001, 002 |
| FIGURE-006 | Admin: figure editor with live preview | Normal | 002, 003, 005 |
| FIGURE-007 | Quality gate: figure rules | High | 002, 005 |
| FIGURE-008 | AI generation: propose figure specs | Normal | 002, 005, 007 |

**Critical path**: 001 → 002 → (003 ∥ 005) → 004 → (006 ∥ 007) → 008

### 18.2 Game Design Research Wave (GD-001 through GD-010)

| Task | Title | Priority | Type |
|------|-------|----------|------|
| GD-001 | ADR: SymPy correctness oracle | Critical | ADR |
| GD-002 | ADR: Misconception session scope | Critical | ADR |
| GD-003 | Rewrite Point 6 (Wordle → community puzzle) | High | Content |
| GD-004 | Point 7 ship-gate (CI scanner) | Critical | Engineering |
| GD-005 | Compliance artifacts (10 docs) | High | Legal |
| GD-006 | MathLive RTL spike | Normal | Research |
| GD-007 | PhET interview protocol | Normal | Design |
| GD-008 | Arabic-first market decision | High | Strategy |
| GD-009 | Competitor hands-on week (12 products) | Normal | Research |
| GD-010 | Memory update (ship-gate, SymPy, misconception) | High | Docs |

### 18.3 New Tasks (from this document)

| Task | Title | Priority |
|------|-------|----------|
| CAS-001 | Build Cena Mini CAS Engine (MathNet + SymPy sidecar) | Critical |
| CAS-002 | Step verifier API endpoint + NATS integration | Critical |
| STEP-001 | `StepSolverCard.vue` + `StepInput.vue` components | High |
| STEP-002 | `MathInput.vue` (MathLive wrapper) | High |
| STEP-003 | `StepSolverQuestion` schema + events + upcaster | High |
| STEP-004 | Step generation tooling in admin (CAS-proposed steps) | Normal |
| STEP-005 | Seed 10 step-solver questions (algebra/calculus/trig) | Normal |

---

## 19. Open Decisions

| # | Decision | Options | Blocking? | Owner |
|---|----------|---------|-----------|-------|
| 1 | SymPy vs Giac/Xcas as primary CAS | SymPy (richer ecosystem, Python) vs Giac (faster, powers GeoGebra) | Yes — CAS-001 | Engineering |
| 2 | MathLive vs plain-text+KaTeX for math input | MathLive (richer UX, unknown RTL) vs text+KaTeX (simple, proven) | No — GD-006 spike decides | UX |
| 3 | Wolfram fallback scope | Admin-only vs also student-facing for edge cases | No — can change later | Engineering |
| 4 | Physics SVG generation: pure C# vs TikZ server-side | C# (simpler infra) vs TikZ (textbook quality, slower) | No — FIGURE-005 decides | Engineering |
| 5 | Step count per question: fixed vs student-extendable | Fixed (author-defined) vs "add a step" button for advanced students | No | UX |
| 6 | Bagrut PDF copyright: seed-only vs direct use | Legal review needed | Yes — blocks production | Legal |
| 7 | Client-side vs server-side figure rendering for physics | Client (interactive) vs server (cached SVG, matches mobile) | No — ADR FIGURE-001 | Engineering |
| 8 | IRT estimation tooling: Python `girth` vs R `mirt` | Python fits CAS sidecar stack; R is more mature | No — CAS-001 decides | Engineering |
| 9 | Exam simulation: reserved pool size (3× vs 5× exam size) | 3× = cheaper to populate; 5× = better exposure control | No | Psychometrics |
| 10 | FBD Construct mode: drag-and-drop vs angle/magnitude input | Drag = more intuitive; numeric = more precise | No — user testing decides | UX |

---

## 20. Expert Architecture Review

> Sections 20–27 are the product of structured review sessions by 7 domain experts.
> These improvements are additive — none changes the fundamental design, they harden it.

### 20.1 Review Panel

| Persona | Domain | Key contributions |
|---------|--------|-------------------|
| **Dina** — Principal Architect (ex-Azure, ex-Wix) | Operational simplicity, failure modes | Routing table, circuit breaker, preload |
| **Oren** — Staff Architect (ex-Google, ex-Check Point) | Latency, formal contracts | Equivalence mode, conformance suite, CAS audit |
| **Dr. Nadia Karmi** — Learning Sciences (CMU LearnLab) | ITS pedagogy, misconception theory | AST-diff diagnosis, productive failure, remediation loops |
| **Dr. Yael Stern** — Psychometrician (ex-NITE, ex-ETS) | IRT calibration, adaptive testing | Rasch model, CAT algorithm, DIF analysis |
| **Prof. Amjad Halabi** — Bagrut Examiner (25 years Arab sector) | Curriculum, real misconceptions | Dual math tracks, misconception catalog, terminology |
| **Tamar Ben-Ari** — RTL/i18n Engineer (ex-Google, Unicode BiDi) | Bidi, accessibility, screen readers | KaTeX isolation, SVG text direction, SRE aria-labels |
| **Dr. Lior Ashkenazi** — Game/UX Psychologist (ex-King, ex-Khan) | Motivation, anti-dark-patterns | Session design, progress mechanics, cognitive load |
| **Ran Shachar** — Assessment Security (ex-NITE, ex-Pearson VUE) | Anti-cheating, data integrity | Exam mode, variant seeding, anomaly detection |

### 20.2 CAS Router Architecture (Improvements 1–6)

**Decision**: Both SymPy (always-on safety net) and Giac (fast-path for 5-unit physics) via NATS, with MathNet in-process for arithmetic. Router lives in .NET Actor Host as a ~30-line subject selector.

**Routing table** — externalized as JSON ConfigMap, hot-reloaded on file watch (no restart):

```json
{
  "rules": [
    {"level_max": 3, "operation": "*",             "engine": "mathnet"},
    {"level_max": 4, "operation": "*",             "engine": "sympy"},
    {"level_min": 5, "operation": "*",             "engine": "giac"},
    {"operation": "numeric_eval",                   "engine": "mathnet"},
    {"operation": "integrate|ode|matrix_solve",     "engine": "giac"}
  ],
  "fallback": "sympy"
}
```

**Circuit breaker** on Giac: 3 consecutive timeouts (>200ms) → trip → route to SymPy for 30s → half-open.

**Equivalence mode** on every CAS request:

```json
{
  "student_expr": "x = 1",
  "expected_expr": "x^3 - 1 = 0",
  "mode": "real_field"
}
```

Modes: `real_field`, `complex_field`, `numeric_approx`. Both engines must respect the mode.

**Conformance test suite**: 500+ expression pairs that both SymPy and Giac must agree on. Runs in CI on every CAS sidecar image build.

**CAS audit events**: every verification emits a structured event to Marten event store:

```json
{
  "request_id": "uuid",
  "question_id": "q_123",
  "step": 3,
  "student_expr": "x = -1 ± √2",
  "expected_expr": "(x+1)² = 2",
  "engine": "sympy",
  "mode": "real_field",
  "result": "equivalent",
  "latency_ms": 12,
  "canonical_form": "Eq(x, -1 + sqrt(2)) | Eq(x, -1 - sqrt(2))"
}
```

Admin panel gets "CAS trace" button on disputed questions. Non-negotiable for parent/teacher trust.

**Cold start mitigation**: `min-instances=1` for both sidecars. SymPy container runs dummy `simplify(1+1)` at startup to preload the engine. No cold-start penalty during exam spikes.

### 20.3 Pedagogical Depth (Improvements 7–11)

**AST-diff diagnosis**: when a step fails CAS verification, diff the student's expression tree against the expected one. Return the first divergent node:

```csharp
public sealed record StepDiagnosis(
    string ErrorClass,           // sign_error, operation_error, conceptual, strategy, skip
    string? StudentSubtree,      // the student's divergent sub-expression
    string? ExpectedSubtree,     // what was expected at that node
    int DivergenceDepth,         // how deep in the AST
    string? MisconceptionId      // maps to misconception catalog, e.g. "ALG-M01"
);
```

Error classes: `sign_error`, `operation_error`, `conceptual_error`, `strategy_error`, `correct_but_skipped`.

The LLM uses the diagnosis to generate targeted feedback. "LLM explains, CAS computes" principle holds.

**Productive failure** — 4th scaffolding level:

```csharp
public enum ScaffoldingLevel
{
    Full,         // Faded worked example — all steps visible
    Partial,      // Step 1 given, student fills the rest
    Minimal,      // Numbered slots only
    Exploratory   // Blank canvas — problem + free-form input, verify final answer only
}
```

In `Exploratory` mode: student sees only the problem. Free-form multi-line math input. CAS verifies final answer. If wrong → same problem re-renders in `Full` mode with divergence highlight.

Kapur 2008/2014: productive failure yields d=0.37 additional gain on transfer tasks.

**Misconception catalog**: static JSON per topic, human-curated from published research + examiner experience. CAS AST-diff maps student errors to catalog entries. Session-scoped tally drives targeted question selection.

**Remediation micro-tasks**: parametric templates per misconception, injected immediately after detection:

```json
{
  "id": "ALG-M01",
  "name": "sqrt_linearity",
  "pattern": "√(a+b) → √a + √b",
  "remediation_type": "numeric_counterexample",
  "template": "احسب √({a}+{b}) ثم √{a}+√{b}. هل هما متساويان؟",
  "parameters": {"a": [9,16,25], "b": [16,9,4]},
  "success_criteria": "student_states_not_equal"
}
```

CAS-verified, no LLM needed. This closes the tutoring loop and is the mechanism that earns VanLehn's d=0.76.

**FBD Construct mode**: `PhysicsDiagramSpec` gains `DiagramMode`:

```csharp
public enum DiagramMode { Display, Construct }
```

In `Construct` mode: render scene (plane, block, angle) but not forces. Student drags force arrows onto the body. CAS verifies force vectors (decomposition is algebra). Market differentiator — nobody in Hebrew/Arabic market has interactive FBD assessment.

**SolutionStep update** (technique verification + input type):

```csharp
public sealed record SolutionStep(
    int StepNumber,
    string? Instruction,
    string? FadedExample,
    string ExpectedExpression,
    string? ExpectedPattern,      // e.g. "(x + _)**2 = _" for technique check
    StepInputType InputType,      // MathExpression, VerbalExplanation, NumericOnly
    IReadOnlyList<string> Hints
);
```

---

## 21. IRT Calibration & Adaptive Selection

### 21.1 Item Calibration (Improvement 12)

Every question record gains IRT parameters:

```csharp
public sealed record ItemCalibration(
    double? AuthorDifficulty,     // 1-5 scale, author's guess
    double? IrtDifficulty,        // Rasch b-parameter, estimated from data
    double? IrtDiscrimination,    // 2PL a-parameter, null until 500+ responses
    double IrtStandardError,      // SE of the b estimate
    int ResponseCount,            // how many students have attempted this item
    CalibrationStatus Status      // Uncalibrated, Provisional, Calibrated
);

public enum CalibrationStatus
{
    Uncalibrated,     // < 50 responses, author label only
    Provisional,      // 50-499 responses, IRT estimate with high SE
    Calibrated        // 500+ responses, stable IRT parameters
}
```

Three-phase transition: Day 1 uses `AuthorDifficulty`. After 50 responses, `IrtDifficulty` becomes available with high SE. After 500 responses, stable calibration.

IRT estimation runs as a nightly batch job (Python `girth` or R `mirt`). Not real-time.

### 21.2 Difficulty-Preserving Variant Constraints (Improvement 13)

Parametric templates include constraints that prevent uncontrolled difficulty variance:

```json
{
  "template": "{a}x + {b} = {c}",
  "constraints": {
    "a": {"range": [2,9], "type": "integer"},
    "b": {"range": [1,9], "type": "integer"},
    "c": {"range": [10,50], "type": "integer"},
    "difficulty_constraints": [
      "(c - b) % a == 0",
      "abs((c - b) / a) <= 20",
      "(c - b) / a > 0"
    ]
  }
}
```

CAS validates constraints at generation time. Post-launch, IRT data validates that variants from the same template cluster around the same difficulty.

### 21.3 Constrained CAT Algorithm (Improvement 15)

Question selection uses a multi-objective algorithm that balances measurement efficiency with pedagogical goals:

```
Select item that:
  1. Is within 1 logit of student's current θ (CAT efficiency)
  2. Targets detected misconceptions if any (priority override)
  3. Covers a topic not yet seen in this session (breadth)
  4. Prefers calibrated items over uncalibrated (data quality)
  5. Has not been seen by this student before (exposure control)
```

Priority: misconception targeting > exposure control > difficulty match > topic breadth > calibration preference.

### 21.4 A-Stratified Exposure Control (Improvement 16)

Items divided into strata by discrimination. Within each stratum, select randomly from items within 0.5 logits of θ. Within-student: no item repeats. Exam mode: cross-student exposure balancing via pool partitions.

### 21.5 Item Bank Health Dashboard (Improvement 14)

| Metric | Target | Red flag |
|--------|--------|----------|
| Coverage | ≥ 20 calibrated items per syllabus topic | < 10 per topic |
| Difficulty distribution | Bell curve centered on target population | Bimodal or skewed |
| Discrimination | a > 0.5 for 80%+ of items | a < 0.3 = noise item |
| Distractor analysis | Every MCQ option chosen by ≥ 5% | < 2% = dead option |
| DIF (Arabic vs Hebrew) | No item systematically harder for one language after ability control | DIF > 0.5 logits = biased |

Quality gate: no topic goes live with fewer than 10 items. Nightly batch computation alongside IRT.

---

## 22. Bagrut Curriculum Alignment

### 22.1 Math Track Structure (Improvement 17)

| Track | Code | Content | Arab sector share |
|-------|------|---------|-------------------|
| 806 | שאלון 806 | Calculus + Analytic Geometry | ~85% |
| 807 | שאלון 807 | Calculus + Probability & Statistics | ~10% |
| Combined | 806+807 | Both | ~5% |

806 and 807 share the calculus core. The system must support both to avoid losing 15% of the market. Physics is one track: 036 (mechanics, electricity, magnetism, optics, modern physics).

**Content authoring**: questions authored natively in Arabic, not translated from Hebrew. Translation DIF is a known issue from Bagrut examiner experience.

### 22.2 Bagrut Structural Alignment Tags (Improvement 19)

```csharp
public sealed record BagrutAlignment(
    string ExamCode,           // "806", "807", "036"
    ExamPart Part,             // A or B
    int? TypicalPosition,      // Q1-Q5 in Part A, null for Part B
    string TopicCluster,       // "function_investigation", "integral_application"
    bool IsProofQuestion,      // Part B proof/show-that type
    int EstimatedMinutes       // 10-25 min per question
);
```

Math 806 Part A structure: Q1 = function investigation, Q2 = integral application, Q3 = analytic geometry, Q4 = sequence/series, Q5 = word problem (optimization/rate of change). Part B: harder versions + proof questions.

Students practice by exam structure: "Train Part A Q1" or "Train Part B proofs." CAT operates within structural strata.

### 22.3 Misconception Catalog (Improvement 18)

Seeded with empirically validated entries from 25 years of Bagrut examiner experience:

**Calculus**:

| ID | Misconception | Detection pattern |
|----|--------------|-------------------|
| CALC-M01 | Product rule: f'(g·h) = f'(g)·f'(h) | AST: derivative node with multiply child missing sum |
| CALC-M02 | ∫(1/x)dx = 1/ln(x) | Reciprocal of antiderivative |
| CALC-M03 | Definite integral bounds reversed: F(a)-F(b) instead of F(b)-F(a) | Subtraction order |
| CALC-M04 | Chain rule inner derivative omitted | d/dx[sin(3x)] = cos(3x) |
| CALC-M05 | f'=0 means minimum (ignoring max/inflection) | Missing second derivative test |
| CALC-M06 | Domain of ln(f(x)) — forget f(x) > 0 | No domain restriction |

**Algebra**:

| ID | Misconception | Detection pattern |
|----|--------------|-------------------|
| ALG-M01 | √(a²+b²) = a+b | Linearizing square root |
| ALG-M02 | (a+b)² = a²+b² | Missing cross term 2ab |
| ALG-M03 | (a+b)/a = b | Cancel across addition |
| ALG-M04 | Negative × negative = negative | Sign error in multi-step |
| ALG-M05 | log(a+b) = log(a)+log(b) | Linearizing logarithm |

**Physics**:

| ID | Misconception | Detection pattern |
|----|--------------|-------------------|
| PHY-M01 | Heavier objects fall faster | Uses m in free-fall acceleration |
| PHY-M02 | Force in direction of motion always present | Extra forward force on friction-only |
| PHY-M03 | Velocity = acceleration direction confusion | "Deceleration = zero acceleration" |
| PHY-M04 | Current "used up" in circuit | Less current after resistor |

Each entry includes a remediation micro-task template (see §20.3).

### 22.4 Arabic Math Terminology (Improvement 20)

| Concept | Primary (MoE) | Variant 1 | Variant 2 |
|---------|---------------|-----------|-----------|
| Derivative | مُشتقّة | اشتقاق | تفاضل |
| Integral | تكامل | مكاملة | — |
| Function | دالّة | تابع | اقتران |
| Limit | نهاية | حدّ | غاية |

One primary term per concept (MoE standard), all variants recognized in student input. Feedback uses primary + alternative in parentheses on first mention.

Normalization runs at input boundary before CAS submission.

---

## 23. RTL & Bidi Deep Engineering

### 23.1 Inline KaTeX Bidi Isolation (Improvement 21)

Every inline KaTeX render wrapped in `<bdi dir="ltr">` to prevent:
- Parenthesis mirroring: `f(x)` → `f)x(`
- Negative sign drift: `-3x` → `x3-`
- Operator reordering: `2x + 3 = 7` → `7 = 3 + x2`

```html
<!-- Correct pattern -->
<p>أوجد قيمة <bdi dir="ltr"><span class="katex">x = 5</span></bdi> عندما</p>
```

Display math (block-level) does not need isolation — it occupies its own block context.

### 23.2 SVG Text Direction (Improvement 22)

SVG `<text>` elements auto-detect script via Unicode range check:
- `[\u0590-\u05FF]` → Hebrew → `direction="rtl"`
- `[\u0600-\u06FF]` → Arabic → `direction="rtl"`
- Otherwise → LTR default

Applies to function-plot axis labels, physics diagram force labels, geometry annotations.

### 23.3 Eastern Arabic Numeral Handling (Improvement 23)

Display: Western Arabic numerals (0-9) everywhere — KaTeX, plots, UI. KaTeX doesn't support Eastern Arabic natively.

Input: accept Eastern Arabic (٠-٩), normalize at boundary:

```typescript
function normalizeDigits(input: string): string {
  return input.replace(/[٠-٩]/g, d =>
    String.fromCharCode(d.charCodeAt(0) - 0x0660 + 0x0030)
  );
}
```

Audit log stores both original and normalized for dispute resolution.

### 23.4 Step-Solver Input Direction (Improvement 24)

```csharp
public enum StepInputType { MathExpression, VerbalExplanation, NumericOnly }
```

- `MathExpression` → `dir="ltr"` (always)
- `VerbalExplanation` → `dir="rtl"` for Arabic/Hebrew
- `NumericOnly` → `dir="ltr"` (always)

### 23.5 Screen Reader Math (Improvement 25)

Use `speech-rule-engine` (SRE) to generate Arabic/Hebrew aria-labels:

```typescript
import * as SRE from 'speech-rule-engine';
SRE.setupEngine({locale: 'ar'});
const label = SRE.toSpeech('x^2 + 3 = 7');
// → "إكس تربيع زائد ثلاثة يساوي سبعة"
```

Cache per expression+locale. Wrap all KaTeX renders: `<span role="math" aria-label="...">`.

---

## 24. Motivation Design & Session UX

### 24.1 Session Start (Improvement 26)

Session start screen presents topic choices (autonomy) with a personalized suggestion from mastery/misconception data:

```
┌─────────────────────────────────────────┐
│  ماذا تريد أن تتدرب اليوم؟              │
│                                         │
│  [📐 تحقيق دوال]    [∫ تكامل]          │
│  [⚡ ميكانيكا]       [📊 هندسة تحليلية]  │
│                                         │
│  💡 اقتراح: "قاعدة السلسلة — كنت قريباً│
│     في المرة الماضية، حاول مرة أخرى؟"   │
│                        [نعم]  [لا]      │
│                                         │
│  📊 إتقانك: ████████░░ 78%              │
└─────────────────────────────────────────┘
```

Student always chooses. No mandatory paths. No loss-framed prompts.

### 24.2 Progress System (Improvement 27)

**Allowed**:
- Mastery map: visual grid of topics, BKT-colored (red → yellow → green)
- Session summary: problems done, what was mastered, what needs work
- Personal best: "Your derivatives accuracy: 60% → 85% this week"
- Misconception resolution: "You fixed the chain rule confusion — last 5 correct"

**Banned**: XP, streaks with loss penalty, public leaderboards by activity, inflated progress indicators.

All progress tied to measured learning, not activity volume. Unfakeable.

### 24.3 Progressive Disclosure in Step-Solver (Improvement 28)

One active step visible. Previous collapsed (showing ✓/✗). Future locked.

Failure gradient:
| Attempt | Response |
|---------|----------|
| 1st wrong | CAS diagnosis feedback |
| 2nd wrong | Hint button appears |
| 3rd wrong | Step auto-fills, marked "assisted" |

BKT update weighted by assistance:
| Outcome | BKT P(L) update |
|---------|-----------------|
| Correct first try | Strong positive |
| Correct after feedback | Moderate positive |
| Correct after hint | Weak positive |
| Auto-filled | No update / slight negative |

### 24.4 Natural Session Boundaries (Improvement 29)

Check-in every 5 problems or 15 minutes (whichever first):
- Summary: problems done, accuracy, mastery change
- Optional remediation micro-task if misconception detected
- Clean exit with no guilt framing

Compliant with dark-pattern ban (Point 7 ship-gate).

---

## 25. Assessment Security & Integrity

### 25.1 Per-Student Variant Seeds (Improvement 30)

```csharp
int variantSeed = HashCode.Combine(questionTemplateId, studentId, DateTime.UtcNow.Date);
```

Same student gets same variant within a day (for session resume). Different students get different parameters for the same template. Daily rotation.

Practice mode: **zero security theater**. No lockdown browser, no monitoring.

### 25.2 Exam Simulation Mode (Improvement 31)

| Control | Implementation |
|---------|---------------|
| Timer | Real countdown, auto-submit on expiry |
| Reserved pool | Exam draws from items never used in practice |
| No scaffolding | No hints, no step-solver, no CAS feedback |
| Randomized order | Per-student within exam parts |
| No back-navigation | Once submitted, can't revisit |
| Delayed scoring | Results after exam closes |

Pool sizing: ≥ 3× exam size per administration. Practice-seen items excluded from exam draws.

### 25.3 Behavioral Anomaly Detection (Improvement 32)

| Signal | Interpretation | Action |
|--------|---------------|--------|
| 2+ IP addresses within 30 min | Possible account sharing | Flag for teacher review |
| Response time < 3s consistently | Someone who knows answers (tutor?) | Flag for review |
| Mastery jumps 20% → 80% overnight | Different person or breakthrough | Flag for review |

Flags stored, surfaced on teacher dashboard as informational (not punitive). Framed as care: "unusual patterns — you may want to check in with this student."

---

## 26. Bagrut Readiness Reporting

### 26.1 Report Structure (Improvement 33)

```
Bagrut 806 Readiness — Student: أحمد
────────────────────────────────────────────
Part A:
  Q1 Function Investigation:  ██████████░░ 83% ± 5%  (12 items)
  Q2 Integral Application:    ████████░░░░ 67% ± 8%  (8 items)
  Q3 Analytic Geometry:       ████░░░░░░░░ 35% ± 12% (4 items)  ⚠️
  Q4 Sequences/Series:        ███████░░░░░ 58% ± 9%  (7 items)
  Q5 Word Problems:           █████████░░░ 75% ± 7%  (10 items)

Part B:
  Proof Questions:            ██████░░░░░░ 50% ± 15% (3 items)  ⚠️

Overall Readiness: 65% ± 4% (44 items)
Recommendation: Focus on Analytic Geometry and Proof Questions

Active Misconceptions:
  ⚠️ ALG-M01 (√ linearity) — detected 3 times, not yet resolved
  ✅ CALC-M04 (chain rule) — resolved after 2 remediation cycles
```

Built from existing data: BKT mastery per topic, IRT ability estimate θ, misconception tally, Bagrut alignment tags. Exportable PDF in Arabic/Hebrew.

Major differentiator for tutor/program sales channel — no competitor produces this.

---

## 27. Photo Ingestion Security & Privacy

> Source: `docs/autoresearch/screenshot-analyzer/` — 10-iteration defense-in-depth research series (iterations 01–10, each with technical + controversy companion). Cumulative security score: 87/100.

### 27.1 Threat Model

When students photograph math/physics questions and upload them to Cena, the image flows through:

```
Student photo → EXIF strip → face blur → crop → Gemini 2.5 Flash → LaTeX → CAS validation → session
```

Attack surface: an adversary (student, script, or third party) could craft images that cause the vision model to extract incorrect math, execute prompt injection, bypass content moderation, or leak system instructions.

**Critical mitigating factor**: the CAS engine independently verifies every extracted expression. An adversary who tricks Gemini into extracting "2+2=5" is caught by SymPy. This "LLM explains, CAS computes" principle provides a backstop that most vision-model deployments lack.

### 27.2 10-Layer Defense Architecture

```
L1:  Client-side checks (file type, size, EXIF strip, CAPTCHA on repeats)
L2:  Edge rate limiting (per-IP, per-user token bucket, geo-fence IL)
L3:  Auth & tenant isolation (Firebase JWT, tenant-scoped claims)
L4:  Privacy preprocessing (EXIF strip, face blur via MediaPipe, PII redaction)
L5:  Content moderation (PhotoDNA CSAM → Cloud Vision SafeSearch → educational classifier)
L6:  LaTeX sanitization (200-command allowlist, nesting/count limits)
L7:  Prompt injection hardening (system prompt canary, structured output, dual-LLM)
L8:  CAS verification (MathNet → SymPy → Wolfram backstop)
L9:  Anomaly detection (response time, upload cadence, session patterns)
L10: Audit logging + incident response (structured events, 90-day retention)
```

### 27.3 Ephemeral Image Processing (Improvement 34)

The image exists in volatile memory for ~1.5 seconds during Gemini API call, then is irreversibly discarded. **No disk, no object storage, no cache.**

Lifecycle:
1. Upload via TLS 1.3
2. EXIF strip (GPS, device serial, all metadata removed)
3. Face detection + blur (MediaPipe BlazeFace)
4. PII text detection + redaction (ID numbers, names)
5. Gemini processing (inline base64, not stored)
6. Result: LaTeX only persists
7. Image garbage collected
8. Audit trail: metadata only (timestamp, student_id, confidence, flags — no raw image)

**Legal compliance**:
- **COPPA 2025** (effective June 2025, compliance deadline April 2026): photos are biometric data; must not be retained beyond processing. Fines up to $51,744/child/violation.
- **GDPR-K (Article 8)**: data minimization + purpose limitation. Cross-border transfer to Google requires SCCs or EU data residency (use `europe-west1`).
- **Israeli PPL Amendment 13** (effective August 2025): ISS classification for biometric data; PPO requirement; DPIA mandatory.

### 27.4 Adversarial Image Defense (Improvement 35)

| Attack category | Success rate (researched) | Cena defense |
|----------------|--------------------------|--------------|
| White-box perturbations | N/A — requires Gemini weights | Google's responsibility |
| Black-box transfer attacks | 8.7–16.4% cross-model | Image preprocessing (resize, JPEG recompress, Gaussian blur) destroys perturbations |
| Typographic injection | Up to 64% on undefended CLIP | Structured output enforcement (JSON schema, not free text) |
| Steganographic embedding | 18.3% against Gemini Pro | EXIF strip + format normalization destroys metadata channels |
| Prompt injection via OCR | Variable | Canary tokens in system prompt + dual-LLM pattern + CAS backstop |

CAS backstop catches all mathematically incorrect extractions. The remaining risk is extraction of a *different but valid* math problem — low consequence (student gets a different practice problem).

---

## 28. LaTeX Sanitization

### 28.1 Threat: LaTeX is Turing-Complete (Improvement 36)

Full TeX allows file I/O (`\input{/etc/passwd}`), shell execution (`\write18{}`), and macro programming. KaTeX supports 600+ commands. An attacker who controls the photo controls the LaTeX output.

### 28.2 200-Command Allowlist

Only ~200 math-safe commands are permitted. Blocked: `\input`, `\include`, `\write`, `\href`, `\url`, `\lstinputlisting`, all file I/O.

**Limits**:
- Nesting depth: max 10 levels
- Command count: max 200 per expression
- Unicode: NFKC normalization (prevents confusable character attacks)
- CVE-2024-28243: KaTeX token expansion DoS — mitigated by command count limit

### 28.3 Tiered Expression (Improvement 37)

| Tier | Commands | Access | Rationale |
|------|----------|--------|-----------|
| Safe (default) | ~200 | All students | Covers all standard Bagrut/AP notation |
| Advanced | ~400 | Mastery ≥ 85% | Adds matrix, piecewise, cases environments |
| Custom | Teacher-defined | Per-course | Curated macro sets for specialized topics |

Transparent rejection: if a student's expression uses a blocked command, show what was blocked and why, with suggestion to rephrase.

---

## 29. Rate Limiting & Cost Protection

### 29.1 Cost Model

Gemini 2.5 Flash: ~$0.002/image. Unprotected bot loop: $2,880/day. Each upload cascades to CAS + tutoring LLM: ~10× amplification → $0.02/upload total pipeline cost.

### 29.2 Four-Tier Rate Limiting (Improvement 38)

| Tier | Scope | Algorithm | Limit |
|------|-------|-----------|-------|
| Per-student | Token bucket | 5 burst, 1 token/12s = 5/min | Allows natural bursty session starts |
| Per-institute | Sliding window | 500 + 2N/hour (N = enrolled) | Scales with school size |
| Per-session | Fixed window | 20 per session | Hard cap per learning session |
| Global cost | Circuit breaker | $50/day | Trips → rejects all uploads → pages ops |

### 29.3 Existing Integration Points

Cena's Student API Host already has 7 rate limiting policies (`api`, `ai`, `tutor`, `password-reset`, `gdpr-export`, `gdpr-erasure`, `tutor-global`). Photo upload gets its own dedicated multi-tier policy. `AiTokenBudgetService` (Redis-backed) provides cost tracking but needs extension for image-specific vectors.

---

## 30. Content Moderation for Minors

### 30.1 Four-Tier Moderation Architecture (Improvement 39)

| Tier | What | Cost | Latency |
|------|------|------|---------|
| 0: PhotoDNA | CSAM hash matching | Free (via NCMEC) | <100ms |
| 1: Client pre-filter | File type, size, EXIF strip | Free | <10ms |
| 2: Cloud Vision | SafeSearch + Face Detection + Label Detection | ~$150/mo at 100K images | ~200ms |
| 3: Custom classifier | Educational content vs non-educational | ~$20/mo (inference) | ~100ms |

Post-extraction validation (Tier 4) is already in the CAS pipeline.

### 30.2 Over-Filtering Mitigation

General-purpose filters block 20–30% of legitimate educational content ("Scunthorpe problem"). Mitigation:
- Domain-aware classifier trained on Bagrut content, not social media
- Teacher override per course
- Cultural profiles per school type (Hebrew, Arabic, Druze, Bedouin contexts)
- Appeal-and-learn loop with 15-minute SLA
- Per-language/subject telemetry on rejection rates

### 30.3 CSAM Reporting

Mandatory reporting to NCMEC (US), Israel Police (IL). PhotoDNA hash matching at Tier 0 before any other processing. If match found: block upload, preserve evidence (exception to ephemeral rule per legal obligation), notify platform safety team. Zero tolerance.

---

## 31. Photo Input Accessibility

### 31.1 Barriers (Improvement 40)

| Disability | Barrier | Mitigation |
|-----------|---------|------------|
| Blind / low vision | Cannot frame camera shot | Auto-focus assistance, typed LaTeX input, voice-to-LaTeX |
| Motor impairment | Difficulty holding phone steady | Tablet mount support, typed input as primary alternative |
| Cognitive | May not understand how to photograph correctly | Guided capture UI with visual cues, retry with feedback |
| Screen reader users | Cannot describe photo content | Voice-to-LaTeX via `speech-rule-engine` reverse pipeline |

### 31.2 Alternative Input Modalities

Photo input is a **convenience optimization**, not the only path. All alternatives must reach the same CAS verification pipeline:
1. **Typed LaTeX** with KaTeX live preview (always available)
2. **MathLive** structured math editor (post GD-006 spike)
3. **Voice-to-LaTeX** transcription (future — SRE reverse)
4. **Handwriting recognition** on tablet (Flutter canvas → Gemini)

### 31.3 WCAG 2.2 AA Compliance

- Upload button: focusable, activatable via Enter/Space
- Step-solver steps: `<ol>` with `role="list"`, navigable via Tab
- Focus indicators: 2px solid outline, 3:1 contrast ratio
- Target size: minimum 24×24px (WCAG 2.5.8, new in 2.2)
- ADA Title II deadline: April 2026 — Cena must be compliant

---

## 32. Graceful Degradation

### 32.1 17 Failure Modes (Improvement 41)

Classified by detection point (client/server/model), severity (blocking/degraded), and recovery.

**Client-side (pre-upload)**:
| Failure | Detection | Recovery |
|---------|-----------|----------|
| Too blurry | Laplacian variance < 100 | Retake guidance + focus tips |
| Too dark | Mean brightness < 40 | Better lighting suggestion |
| Too small | < 640×480 | Move camera closer |
| Wrong format | Not JPEG/PNG/WebP/HEIC | List accepted formats |

**Server-side (post-upload)**:
| Failure | Detection | Recovery |
|---------|-----------|----------|
| No math detected | Gemini confidence < 0.3 | "We couldn't find math — try photographing just the question" |
| Partial extraction | Confidence 0.3–0.7 | Show what was found, let student complete in editor |
| Vision model timeout | No response in 8s | Retry with backoff → fallback to Mathpix → manual input |
| Vision model down | HTTP 5xx | Immediate Mathpix fallback → manual input |
| LaTeX parse fails | CAS error | Show raw LaTeX in editable field, student corrects |

### 32.2 UX Framing

Fallback to manual input is NOT degradation — it's the original modality. Photo input is a speed optimization. Frame accordingly: "OCR wasn't confident on this image. Here's what we read [show LaTeX]. Please verify or correct it."

### 32.3 Fallback Chain

```
Gemini 2.5 Flash (primary) → Mathpix (backup) → Manual typed input (always available)
```

Each fallback preserves session state. Student never loses their place.

---

## 33. Academic Integrity & Anti-Cheating

### 33.1 Three Attack Scenarios (Improvement 42)

| Scenario | Severity | Description |
|----------|----------|-------------|
| **Live exam photography** | Critical | Student photographs Bagrut exam question during test |
| **Homework copy-paste** | Medium | Student photographs every problem, copies solutions without engaging |
| **Practice test harvesting** | Low | Student memorizes solutions to specific problems |

### 33.2 Why Cena is Not Photomath

Photomath shows the solution. Cena requires the student to **produce each step** and the CAS merely verifies. This is structurally different:
- Photomath: camera → answer (zero student effort)
- Cena: camera → structured problem → student works steps → CAS checks each step

Per VanLehn 2011, step-level ITS (d=0.76) vs answer-only (d=0.31). The architecture choice is the anti-cheating mechanism.

### 33.3 Guardrails

| Guardrail | Implementation |
|-----------|---------------|
| Exam-time detection | Flag uploads during official Bagrut exam hours (known from MoE schedule) |
| No direct answers | System verifies student steps, never shows the solution unprompted |
| Question bank matching | Detect uploads matching known exam papers (hash + structural similarity) |
| Rate limiting during exams | Institute-level rate limit drops to near-zero during scheduled exams |
| Teacher notification | Suspicious patterns surfaced on teacher dashboard |
| Step-solver only | During exam season, disable "show me the answer" — only step verification available |

### 33.4 Homework Copy-Paste Mitigation

Cena's step-solver inherently resists copy-paste: even if a student photographs the problem, they still must produce each step. If they can't produce the steps, they get scaffolding (which counts as "assisted" in BKT and doesn't inflate mastery). The mastery model is honest.

---

## 34. Pedagogical Integrity: Does Photo Input Help or Hurt?

### 34.1 The Objection (Steel-Manned)

Bjork (1994, 2011) "Desirable Difficulties": making learning easier often makes it worse. A screenshot tool that extracts, structures, and scaffolds is systematically removing desirable difficulties:
- Question extraction removes parsing difficulty
- Structured presentation removes problem-type identification
- Scaffolded steps remove solution planning

Kapur (2014) "Productive Failure": students who struggle before instruction outperform those who receive instruction first.

Slamecka & Graf (1978) "Generation Effect": producing an answer creates stronger memory than recognizing one.

### 34.2 Why Cena Escapes the Trap (Conditionally)

1. **Student produces, CAS verifies** — the generation effect is preserved. The student must construct each step.
2. **Exploratory scaffolding** (Improvement 8) — blank canvas first, Full mode only after failure. Productive failure is in the architecture.
3. **Assistance-weighted BKT** (Improvement 28) — auto-filled steps don't count toward mastery. The model is honest about what the student can do.
4. **Faded scaffolding** — as mastery increases, scaffolding withdraws (expertise reversal protection).

### 34.3 Empirical Validation Required

The architectural claims above are theoretically grounded but empirically untested for Cena specifically. Required measurements:
- Within-student comparison: CAS-verified vs withheld feedback sessions
- Self-assessment calibration: can students predict their own accuracy?
- Transfer tests: paper-and-pencil without the system
- OCR confirmation rates over time (do students develop verification skills?)

This is the research agenda for the PhET-style interview protocol (GD-007).

### 34.4 The Design Principle

> "If your retention metric goes up but your learning metric stays flat, you've built an addiction engine, not an education product." — Lior (UX Psychologist)

Cena measures **learning** (BKT mastery on calibrated items), not **engagement** (time-in-app, streaks, XP). If the photo input doesn't improve learning outcomes on transfer tests, it gets redesigned or removed.

---

## 35. Solution Design: Component Architecture

### 35.1 System Context

```
                    ┌─────────────────────────────────────────────────┐
                    │              STUDENT DEVICES                    │
                    │  Vue 3 (Web) ←→ Flutter (iOS/Android)          │
                    │  KaTeX · function-plot.js · JSXGraph · SVG     │
                    └──────────────────┬──────────────────────────────┘
                                       │ HTTPS / WSS (TLS 1.3)
                                       │ Firebase Auth JWT
                                       ▼
                    ┌─────────────────────────────────────────────────┐
                    │              EDGE / GATEWAY                     │
                    │  NGINX (rate limit, geo-fence IL, TLS term)     │
                    └──────────────────┬──────────────────────────────┘
                                       │
                     ┌─────────────────┼─────────────────┐
                     ▼                 ▼                 ▼
              ┌─────────────┐  ┌─────────────┐  ┌─────────────────┐
              │ Student API │  │  Admin API  │  │  Actor Host     │
              │ (.NET 8)    │  │  (.NET 8)   │  │  (Proto.Actor)  │
              └──────┬──────┘  └──────┬──────┘  └────────┬────────┘
                     │                │                   │
                     └────────────────┼───────────────────┘
                                      │ NATS JetStream
                     ┌────────────────┼────────────────┐
                     ▼                ▼                ▼
              ┌───────────┐   ┌───────────┐   ┌───────────────┐
              │ PostgreSQL │   │   Redis   │   │  CAS Sidecars │
              │ (Marten)   │   │ (cache,   │   │ SymPy · Giac  │
              │ Event Store│   │  budget)  │   └───────────────┘
              └───────────┘   └───────────┘
```

Three .NET hosts, one message bus, two CAS sidecars, one database. No Kubernetes day one — Cloud Run or a single VM with Docker Compose for the pilot.

### 35.2 Student API Host

```
Student API Host (.NET 8) — Stateless, horizontally scalable
├── Controllers/
│   ├── SessionController         — start/resume/end learning session
│   ├── QuestionController        — get next question, submit answer
│   ├── StepSolverController      — submit step, get diagnosis
│   ├── PhotoUploadController     — screenshot → LaTeX pipeline
│   └── ProgressController        — mastery map, readiness report
│
├── Middleware/
│   ├── FirebaseAuthMiddleware    — JWT validation (RS256)
│   ├── TenantScopeMiddleware     — extract instituteId from claims
│   ├── RateLimitMiddleware       — 7 existing policies + photo upload
│   └── SecurityHeadersMiddleware — CSP, HSTS, X-Frame-Options
│
├── SignalR/
│   └── SessionHub               — real-time step feedback, timer sync
│
├── Services/
│   ├── QuestionSelector          — constrained CAT (§21.3)
│   │   ├── IrtAbilityEstimator   — θ estimate from response history
│   │   ├── MisconceptionMatcher  — session-scoped tally → catalog lookup
│   │   ├── ExposureController    — a-stratified, no repeats
│   │   └── BagrutAlignmentFilter — structural strata (Q1-Q5, Part A/B)
│   │
│   ├── StepVerifier              — orchestrates CAS verification
│   │   ├── CasRouter             — routes to MathNet/SymPy/Giac
│   │   ├── AstDiffDiagnoser      — error class + divergent subtree
│   │   └── MisconceptionDetector — maps diff → catalog entry
│   │
│   ├── PhotoIngestionService     — ephemeral image processing
│   │   ├── ImageQualityGate      — blur/brightness/size checks
│   │   ├── PrivacyPreprocessor   — EXIF strip, face blur, PII redact
│   │   ├── ContentModerator      — PhotoDNA → Cloud Vision → custom
│   │   ├── VisionExtractor       — Gemini 2.5 Flash → LaTeX
│   │   ├── LatexSanitizer        — 200-command allowlist
│   │   └── FallbackChain         — Gemini → Mathpix → manual input
│   │
│   ├── ScaffoldingService        — existing BKT-driven level selector
│   ├── RemediationService        — injects micro-tasks on misconception
│   └── DigitNormalizer           — Eastern Arabic → Western at boundary
│
└── Infrastructure/
    ├── NatsPublisher             — events → NATS JetStream
    ├── MartenRepository          — event sourcing read/write
    └── RedisCache                — session state, token budget
```

### 35.3 Actor Host (Proto.Actor)

Stateful brain. One actor per student session. Handles BKT updates, scaffolding decisions, session lifecycle.

```
Actor Host (Proto.Actor on .NET 8)
├── StudentSessionActor
│   ├── State: BKT mastery per skill, session misconception tally,
│   │          current θ estimate, questions seen, scaffolding level
│   ├── On(AnswerSubmitted)       → update BKT, persist event
│   ├── On(StepVerified)          → update BKT (weighted by assistance)
│   ├── On(MisconceptionDetected) → increment session tally
│   ├── On(RemediationCompleted)  → clear misconception flag
│   ├── On(SessionCheckIn)        → emit summary (5 problems / 15 min)
│   └── On(SessionEnded)          → persist final state, clear tally
│
├── CasRouterActor
│   ├── Routing table: JSON ConfigMap, hot-reloaded on file watch
│   ├── Circuit breaker per engine (3 failures → 30s fallback)
│   ├── On(VerifyRequest)  → route to NATS: cas.sympy.* or cas.giac.*
│   ├── On(VerifyResponse) → emit CasAuditEvent to Marten
│   └── Fallback: Giac timeout → SymPy; SymPy timeout → error
│
├── IrtBatchActor (triggered nightly)
│   ├── Collects response matrix from read model
│   ├── Calls Python IRT service (girth) via NATS
│   ├── Updates ItemCalibration records
│   └── Computes DIF (Arabic vs Hebrew), flags items
│
└── ExamSimulationActor
    ├── Timer: countdown, auto-submit on expiry
    ├── Reserved pool: draws from exam-only items
    ├── No hints, no scaffolding, no CAS feedback
    ├── Randomized order per student
    └── Delayed scoring: results after exam closes
```

### 35.4 Admin API Host

```
Admin API Host (.NET 8)
├── Controllers/
│   ├── QuestionBankController    — CRUD questions, variants, figures
│   ├── IngestionController       — PDF upload, batch OCR
│   ├── FigureEditorController    — FigureSpec authoring
│   ├── MisconceptionController   — catalog management
│   ├── ItemHealthController      — IRT stats, DIF, coverage
│   ├── ReportController          — readiness reports, export PDF
│   └── ExamController            — exam pool management
│
├── Services/
│   ├── AiGenerationService       — existing (885 lines)
│   ├── VariantGenerator          — parametric + isomorph + laddering
│   │   ├── ConstraintValidator   — difficulty-preserving constraints
│   │   └── CasVerifier           — SymPy verifies every variant
│   ├── FigureSpecBuilder         — construct FunctionPlot/Physics/Geometry specs
│   └── BagrutAlignmentTagger     — auto-tag exam code, part, position
│
└── Background/
    ├── IrtEstimationJob          — nightly Rasch + 2PL estimation
    ├── ItemHealthJob             — coverage, discrimination, distractors
    └── DifAnalysisJob            — Arabic vs Hebrew fairness check
```

### 35.5 CAS Sidecars

Two stateless containers. Same NATS request/reply contract. Independently deployable.

```
cas-sympy (Docker — always-on safety net)
┌──────────────────────────────────────────────────┐
│ FastAPI + uvicorn (N workers = CPU cores)         │
│ NATS subjects: cas.sympy.verify/simplify/solve/  │
│                cas.sympy.diff/match/steps         │
│ Startup: import sympy + dummy simplify(1+1)       │
│ Memory: ~150MB/worker                             │
│ Min instances: 1 (no cold start)                  │
└──────────────────────────────────────────────────┘

cas-giac (Docker — fast path for 5-unit physics)
┌──────────────────────────────────────────────────┐
│ FastAPI + giacpy (C++ bindings)                   │
│ NATS subjects: cas.giac.*                         │
│ 10× faster per operation than SymPy               │
│ Memory: ~80MB/worker, cold start ~200ms           │
│ Min instances: 1                                  │
└──────────────────────────────────────────────────┘
```

### 35.6 CAS NATS Request/Reply Contract

Both engines implement this identical contract:

```json
// Request (published to cas.sympy.verify or cas.giac.verify)
{
  "request_id": "uuid",
  "operation": "verify | simplify | solve | diff | match | steps",
  "student_expr": "x^2 + 2*x + 1",
  "expected_expr": "(x + 1)^2",
  "mode": "real_field | complex_field | numeric_approx",
  "pattern": "(x + _)**2",
  "step_chain": [
    {"step": 1, "expr": "x^2 + 2x + 1 = 0"},
    {"step": 2, "expr": "(x+1)^2 = 0"},
    {"step": 3, "expr": "x = -1"}
  ]
}

// Response
{
  "request_id": "uuid",
  "result": "equivalent | not_equivalent | error",
  "canonical_form": "Eq((x+1)**2, 0)",
  "diagnosis": {
    "error_class": "sign_error | operation_error | conceptual_error | strategy_error | correct_but_skipped",
    "student_subtree": "x + 2",
    "expected_subtree": "x - 2",
    "divergence_depth": 2,
    "misconception_id": "ALG-M02"
  },
  "latency_ms": 12,
  "engine": "sympy | giac"
}
```

Both engines must pass the 500-pair conformance test suite. CI blocks deployment if any pair disagrees.

---

## 36. Solution Design: Data Architecture

### 36.1 Event Store (Marten on PostgreSQL)

Everything is event-sourced. No update, no delete. Append-only.

**Aggregate roots**: QuestionDocument, StudentSession, StudentProfile, ExamSimulation.

**Key events**:

```
QuestionAuthored      { questionId, stem, figureSpec, steps, bagrutAlignment }
VariantGenerated      { parentId, variantId, parameters, casVerified }
ItemCalibrated        { questionId, irtB, irtA, se, responseCount, status }
SessionStarted        { studentId, topicChoice, scaffoldingLevel }
QuestionPresented     { questionId, variantSeed, catTheta, selectionReason }
StepSubmitted         { stepNum, studentExpr, timestamp }
StepVerified          { stepNum, result, diagnosis?, engine, latencyMs }
MisconceptionDetected { misconceptionId, stepNum, remediationType }
RemediationPresented  { misconceptionId, microTaskId }
RemediationCompleted  { misconceptionId, correct }
AnswerSubmitted       { questionId, correct, assistanceLevel, bktUpdate }
SessionCheckIn        { problemCount, accuracy, masteryDelta, elapsed }
SessionEnded          { problemCount, masterySnapshot, misconceptionsResolved }
CasAuditEvent         { requestId, questionId, step, studentExpr, expected, engine, result, latencyMs }
```

### 36.2 Read Models (Marten Projections)

```
QuestionBankView       — published questions + IRT parameters + alignment tags
StudentMasteryView     — current BKT per skill per student (for CAT)
ItemResponseMatrix     — sparse (student × item × result) for IRT batch
ItemHealthView         — coverage, discrimination, distractor stats, DIF flags
BagrutReadinessView    — per-student readiness per exam section
TeacherDashboardView   — class mastery, anomaly flags, misconception heatmap
CasPerformanceView     — latency percentiles per engine, error rates, breaker status
```

### 36.3 What is NOT Persisted

| Data | Lifecycle | Reason |
|------|-----------|--------|
| Student photos | Volatile memory, ~1.5s | COPPA 2025, GDPR-K, PPL Amendment 13 |
| Misconception tally | Session-scoped, cleared on end | Edmodo consent decree precedent |
| Raw CAS AST diffs | Logged as audit event only | Part of event stream |
| Eastern Arabic original input | Audit event field | Dispute resolution |

---

## 37. Solution Design: CAS Verification Flow

### 37.1 Successful Step Verification

```
Student types step 3: "(x+1)^2 = 0"
  │
  ▼
POST /api/sessions/{sid}/question/{qid}/step/3
  │
  ▼
Student API:
  1. DigitNormalizer.normalize()     → no-op for Latin input
  2. LatexSanitizer.validate()       → PASS
  3. Load question from Marten       → expectedExpr, expectedPattern, mode
  4. Publish to NATS: cas.verify
  │
  ▼
CasRouter (Actor Host):
  Route: level=5, operation=verify → routing table → cas.sympy.verify
  │
  ▼
cas-sympy sidecar:
  1. Parse student_expr and expected_expr
  2. Solve (x+1)^2 = 0 → x = -1 (real_field)
  3. Check equivalence: VALID intermediate step
  4. Pattern match "(x + _)^2 = _": MATCH
  5. Return: { result: "equivalent", latency_ms: 8 }
  │
  ▼
Student API:
  5. Emit CasAuditEvent to Marten
  6. Emit StepVerified on StudentSession aggregate
  7. Actor updates BKT: strong positive (first try, no hint)
  8. Return via SignalR: { correct: true, nextStepUnlocked: true }
```

### 37.2 Failed Step with Misconception Detection

```
Student types: "√(9+16) = √9 + √16 = 7"    ← ALG-M01
  │
  ▼
CAS returns: not_equivalent
  diagnosis: { error_class: "conceptual_error",
               misconception_id: "ALG-M01",
               student_subtree: "sqrt(9) + sqrt(16)",
               expected_subtree: "sqrt(25)" }
  │
  ▼
LLM generates targeted feedback from diagnosis:
  "تحقق — هل √(9+16) = √9 + √16؟ جرب حساب كل طرف"
  │
  ▼
Session actor: increment misconception_tally["ALG-M01"] → count=2
  │
  ▼
At next session check-in (5 problems or 15 min):
  Inject remediation micro-task from catalog:
  "احسب √(9+16) ثم √9+√16. هل هما متساويان؟"
  │
  ▼
If remediation correct → clear ALG-M01 flag, emit RemediationCompleted
If wrong → escalate to Full scaffolding for worked example on √(a+b)
```

---

## 38. Solution Design: Question Selection Algorithm

```python
def select_next_question(session, bank):
    """Constrained CAT with misconception priority."""
    
    θ = session.current_ability_estimate
    seen = session.questions_seen
    tally = session.misconception_tally
    strata = session.bagrut_filter  # e.g. "806, Part A, Q1"
    
    # 1. Structural alignment filter
    candidates = [q for q in bank if q.bagrut_alignment matches strata]
    
    # 2. Exposure control: no repeats
    candidates = [q for q in candidates if q.id not in seen]
    
    # 3. PRIORITY: misconception targeting (overrides CAT)
    active = {m: c for m, c in tally.items() if c >= 2}
    if active:
        top = max(active, key=active.get)
        targeting = [q for q in candidates if top in q.targeted_misconceptions]
        if targeting:
            return min(targeting, key=lambda q: abs(q.irt_difficulty - θ))
    
    # 4. CAT: within 1 logit of θ
    in_range = [q for q in candidates if abs(q.irt_difficulty - θ) <= 1.0]
    
    # 5. Prefer calibrated items
    in_range.sort(key=lambda q: (
        0 if q.calibration_status == 'calibrated' else
        1 if q.calibration_status == 'provisional' else 2))
    
    # 6. A-stratified random (exposure control)
    strata = group_by_discrimination(in_range)
    return random.choice(strata[0])  # top discrimination stratum
```

---

## 39. Solution Design: Deployment Topology & Cost

### 39.1 Pilot (100 students, single city)

```
Docker Compose on 1 VM (4 vCPU, 16GB RAM)
├── student-api     (1 instance,  port 5000)
├── admin-api       (1 instance,  port 5001)
├── actor-host      (1 instance,  port 5002)
├── nats            (1 instance,  port 4222)
├── postgres        (1 instance,  port 5432)
├── redis           (1 instance,  port 6379)
├── cas-sympy       (1 instance,  2 workers)
└── cas-giac        (1 instance,  1 worker)
```

### 39.2 Scale (1,000+ students)

```
Cloud Run (or K8s)
├── student-api     (2-8 instances, CPU-autoscale)
├── admin-api       (1-2 instances)
├── actor-host      (2-4 instances, consistent-hash by student)
├── nats            (3-node JetStream cluster)
├── postgres        (Cloud SQL, 2 vCPU, read replica)
├── redis           (Memorystore, 1GB)
├── cas-sympy       (2-6 instances, min=1, CPU-autoscale at 70%)
└── cas-giac        (1-3 instances, min=1, CPU-autoscale at 70%)
```

### 39.3 Full System Cost Model

| Component | Pilot (100 students) | Scale (1K students) | Notes |
|-----------|---------------------|---------------------|-------|
| VM / Cloud Run | $50 | $200 | Autoscale during exams |
| PostgreSQL | $0 (on VM) | $50 (Cloud SQL) | Marten event store |
| Redis | $0 (on VM) | $25 (Memorystore) | Cache + budget |
| NATS | $0 (on VM) | $30 (cluster) | JetStream |
| CAS sidecars | $0 (on VM) | $45 (SymPy + Giac) | Autoscale on spike |
| Gemini Vision | $20 | $200 | $0.002/image × 100K/mo |
| Cloud Vision (mod) | $15 | $150 | SafeSearch + labels |
| Wolfram API | $0 | $25 | Admin-only, 2K/mo cap |
| CDN (figures) | $5 | $20 | Raster only |
| Firebase Auth | $0 | $0 | Free tier |
| **Total** | **~$90/month** | **~$745/month** | |
| **Per student** | **~$0.90/month** | **~$0.75/month** | Price: ₪69 → 92× margin |

---

## 40. Solution Design: Non-Functional Requirements

### 40.1 Latency Budget

| Operation | Target | How |
|-----------|--------|-----|
| Question load | < 100ms | Marten projection, cached in Redis |
| Step verification (CAS) | < 200ms | SymPy 5-50ms, Giac 0.5-20ms, NATS 5ms |
| Photo → LaTeX | < 3s | Quality gate 15ms, moderation 300ms, Gemini 1-2s |
| Figure render (client) | < 500ms | function-plot.js <100ms, SVG <200ms |
| BKT update (actor) | < 10ms | In-memory actor state |
| Session start | < 500ms | Actor activation + first question |

### 40.2 Availability Targets

| Component | Target | Strategy |
|-----------|--------|----------|
| Student API | 99.5% | Stateless, multi-instance |
| Actor Host | 99.5% | Actor relocation, snapshot recovery |
| CAS (effective) | 99.9% | SymPy safety net + Giac breaker |
| Photo ingestion | 99% | Gemini → Mathpix → manual fallback |
| PostgreSQL | 99.9% | Cloud SQL + automated backups |

### 40.3 Security Checklist

| Layer | Control | Status |
|-------|---------|--------|
| Transport | TLS 1.3 everywhere | Required |
| Auth | Firebase JWT (RS256), tenant-scoped | Implemented |
| Input | LaTeX 200-command allowlist | Designed |
| Privacy | Ephemeral image processing, no-disk | Designed |
| Content | 4-tier moderation (PhotoDNA → CV) | Designed |
| Rate | 4-tier limiting + cost circuit breaker | Designed |
| CAS | Structured audit events + admin trace | Designed |
| Anomaly | Behavioral flags (multi-IP, timing, spikes) | Designed |
| Compliance | COPPA 2025, GDPR-K, Israeli PPL Amd 13 | DPIA done |
| CSAM | PhotoDNA + mandatory reporting | Designed |

---

## 41. Solution Design: Critical Path (8-Week Build)

### Week 1–2: CAS Foundation

```
CAS-001  SymPy sidecar + NATS integration
FIGURE-001  ADR on rendering stack
FIGURE-002  FigureSpec schema in Marten
```

### Week 3–4: Step Solver Core

```
STEP-001  StepSolverCard.vue + StepInput.vue
STEP-003  StepSolverQuestion schema + events
CAS-002   Step verifier endpoint wired to SymPy
```

### Week 5–6: Figures + Content

```
FIGURE-003  FunctionPlotFigure.vue component
FIGURE-004  Wire into QuestionCard.vue
STEP-005    Seed 10 step-solver questions (algebra, 806 Part A Q1)
```

### Week 7–8: Integration + Photo Input

```
Integration testing: step-solver + CAS + figures
Photo ingestion: Gemini basic (no moderation tiers yet)
BKT integration: step outcomes → mastery updates
```

### Post-Launch (rolling)

```
IRT calibration batch (needs 50+ responses/item)
Misconception catalog seeding
Giac sidecar (when physics step-solver ships)
Content moderation tiers 2-3
Exam simulation mode
Readiness reporting
```

**Design invariant**: LLM explains, CAS computes. No exceptions.

**Architecture invariant**: every improvement is additive. The system works with just SymPy and 10 seed questions. Everything else layers on without changing the foundation.

---

## 42. Adversarial Architecture Review (Dr. Rami Khalil)

Dr. Rami Khalil — 18 years distributed systems (Microsoft Azure, AWS), principal architect at two EdTech unicorns, PhD in fault-tolerant computing (ETH Zürich). Brought in specifically to challenge every assumption made by Dina and Oren.

### 42.1 QuestionCasBinding — Eliminating CAS Drift (Improvement #43)

**Problem identified**: Two CAS engines (SymPy, Giac) disagree on ~2.3% of edge cases (branch cuts, simplification depth, floating-point rounding). A question authored and verified with SymPy may produce a different canonical form when verified by Giac at runtime.

**Solution**: Lock each question to its authoring CAS engine. Store the canonical answer and step-by-step canonical forms at authoring time.

```csharp
public sealed record QuestionCasBinding(
    string QuestionId,
    string Engine,           // "sympy" | "giac" | "mathnet"
    string CanonicalAnswer,
    IReadOnlyList<string> StepCanonicals,
    string EquivalenceMode   // "symbolic" | "numeric_1e-9" | "pattern"
);
```

**CAS Router change**: The router reads `QuestionCasBinding.Engine` first. If the bound engine is down, the circuit breaker fires — but instead of silently falling back, it returns a `CasDegradedResponse` with `confidence: "unverified"` and the student sees "answer recorded, verification pending."

**Migration**: Existing questions without a binding default to SymPy. A nightly batch job re-verifies all unbound questions against both engines and flags disagreements for human review.

### 42.2 BKT+ Extensions (Improvement #44)

**Problem identified**: Standard BKT has three blind spots: (1) no forgetting — a student who mastered derivatives 3 months ago is treated identically to one who practiced yesterday, (2) no prerequisite awareness — the system may serve L'Hôpital's Rule before limits are solid, (3) all correct answers count equally — a fast correct answer and a 4-hint-assisted correct answer both update P(L) the same way.

**Solution**: BKT+ with three extensions:

```csharp
public sealed record SkillMastery(
    string SkillId,
    double PL,              // Standard BKT P(Learned)
    double PLEffective,     // PL × decay factor
    DateTime LastPracticed,
    double DecayRate,       // Ebbinghaus half-life (days)
    int AssistanceLevel     // 0=solo, 1=one hint, 2=two hints, 3=auto-filled
);
```

**Skill prerequisite DAG**: Each skill has `IReadOnlyList<string> Prerequisites`. The question selector will not serve a skill unless all prerequisites have `PLEffective ≥ 0.6`. The DAG is hand-curated per Bagrut track (806/807/036) and stored as a static JSON resource.

**Ebbinghaus forgetting curve**: `PLEffective = PL × 2^(−daysSinceLastPractice / halfLife)`. Default half-life is 14 days, adjusted per skill based on empirical retention data. Skills with `PLEffective < 0.4` and `PL ≥ 0.8` trigger a "refresh" recommendation in the mastery map.

**Assistance-weighted updates**: BKT transition probability `P(T)` is scaled by assistance level: `P(T) × (1.0 − 0.25 × assistanceLevel)`. An auto-filled step (level 3) contributes only 25% of the normal learning credit.

### 42.3 Event Store Scaling (Improvement #45)

**Problem identified**: Marten projection rebuild on 200M+ events takes hours. At 1,000 students × 200 events/day × 365 days, the event store reaches 73M events/year.

**Solution**: Three-pronged:

1. **Snapshot every 50 events**: Marten inline snapshots on `StudentSessionStream`. Projection rebuilds start from the latest snapshot, not event zero.
2. **Partition by month**: PostgreSQL table partitioning on `mt_events` by `timestamp`. Old partitions (>12 months) move to cold storage. Queries hit only recent partitions.
3. **Async non-critical projections**: `MisconceptionTrendProjection`, `IrtCalibrationProjection`, and `TeacherReportProjection` rebuild asynchronously via NATS. They can lag by minutes without affecting student experience. Only `StudentSessionProjection` and `MasteryMapProjection` are synchronous.

### 42.4 Actor Crash Recovery (Improvement #46)

**Problem identified**: If the `StudentSessionActor` crashes mid-step (or the server restarts), the student loses their typed-but-not-submitted expression and has to re-type it. On mobile (PWA/Flutter), network drops are common.

**Solution**: Two-layer recovery:

1. **Client-side**: `localStorage` draft persistence. Every 2 seconds (debounced), the current step input is saved to `localStorage` keyed by `sessionId:stepNumber`. On page reload, the draft is restored. Cleared on successful submission.
2. **Server-side**: Full session snapshot on SignalR reconnect. When the client reconnects, the `SessionHub` sends the complete session state (current question, current step, BKT snapshot, scaffolding level). The client reconciles its local draft with the server state.

**Actor rehydration**: Proto.Actor rehydrates `StudentSessionActor` from the latest Marten snapshot + subsequent events. Actor state is fully reconstructed in <50ms for typical sessions (20-30 events since last snapshot).

### 42.5 Data-Discovered Misconceptions (Improvement #47)

**Problem identified**: The static misconception catalog (15+ entries from the Bagrut examiner) cannot anticipate every student mistake. Real students will exhibit error patterns not in the catalog.

**Solution**: Data-discovered misconception candidates from clustered anonymous AST diffs.

```csharp
public sealed record MisconceptionCandidate(
    string Topic,
    string PatternHash,                 // SHA-256 of normalized diff
    string StudentSubtreePattern,       // Anonymized AST pattern
    string ExpectedSubtreePattern,      // What was expected
    int OccurrenceCount,                // How many times seen
    int DistinctStudentCount,           // Across how many students
    CandidateStatus Status,             // Pending | Confirmed | Rejected
    string? DraftRemediationTemplate    // Auto-generated, human-reviewed
);
```

**Pipeline**: Nightly batch job clusters anonymous AST diffs (no student identifiers). When a pattern appears ≥10 times across ≥5 distinct students, it becomes a `MisconceptionCandidate` surfaced in the admin dashboard for human review. The content team can confirm it (→ added to catalog with a remediation template) or reject it (→ suppressed).

**Privacy**: Only the AST diff pattern is stored, never the student identity. The clustering operates on anonymized, session-scoped data.

### 42.6 Cross-Platform Figure Rendering (Improvement #48)

**Problem identified**: If the student app runs on both web (Vue 3) and a native mobile framework (e.g., Flutter), every figure type must render identically on both platforms. Different rendering engines produce pixel-level differences that can confuse students or introduce grading inconsistencies.

**Original solution**: Playwright vs Flutter screenshot pixel-diff in CI, with a tolerance threshold.

**Updated status**: **N/A if PWA is chosen.** A PWA approach eliminates this problem entirely — there is only one rendering engine (the browser). The same KaTeX, function-plot.js, JSXGraph, and SVG code runs everywhere. See separate PWA vs Flutter comparison documents for the full trade-off analysis.

### 42.7 Three-Layer Observability (Improvement #49)

**Solution**:

1. **Metrics**: OpenTelemetry → Prometheus. Key metrics: CAS latency p50/p95/p99, step verification throughput, actor activation count, photo pipeline latency, BKT update rate.
2. **Structured logging**: Serilog → Seq (pilot) or Loki (scale). Correlation ID flows from HTTP request through NATS to CAS sidecar. Every CAS call logs: engine, duration, equivalence result, question ID.
3. **Alerting**: 6 critical alerts:
   - CAS p99 > 500ms for 5 minutes
   - Circuit breaker open (Giac down)
   - Event store append latency > 100ms
   - Photo pipeline error rate > 5%
   - Actor activation failure rate > 1%
   - Daily cost budget exceeded ($50 threshold)

### 42.8 Design Invariant Challenge (Improvement #50)

**Rami's challenge**: "What happens when the LLM *must* compute?" Three scenarios identified:

1. **Natural language hint generation**: The LLM generates hints from the question context. If the hint accidentally reveals the answer or contains a wrong intermediate step, the student is misled. **Mitigation**: Every LLM-generated hint that contains a mathematical expression is CAS-verified before display. If verification fails, the hint is discarded and a fallback template hint is shown.

2. **Arabic/Hebrew explanation generation**: The LLM produces step explanations in natural language. If it hallucinates a mathematical claim ("since sin(π) = 1"), the student learns wrong math. **Mitigation**: All mathematical claims in LLM output are extracted via regex (`\$...\$` or `\\(...\\)`) and CAS-verified. Failed claims are replaced with `[mathematical expression omitted]` and flagged for human review.

3. **Misconception explanation**: When the system detects a misconception, the LLM explains what went wrong. If it misidentifies the misconception type, the explanation is counterproductive. **Mitigation**: The LLM receives the misconception ID and its human-curated explanation template. It can rephrase but not contradict. The template is the ground truth; the LLM is the translator.

**Revised invariant**: LLM explains, CAS computes. Every mathematical expression in LLM output is CAS-verified. No exceptions, no shortcuts.

---

## 43. Expert Review Round 2: Tenancy, Figures & Game-Design

Panel: Dina Halevi (distributed systems), Oren Mizrahi (EdTech domain), Dr. Rami Khalil (adversarial), Dr. Nadia Mansour (learning science), Dr. Layla Khoury (RTL/bidi), Dr. Yoav Ben-Ari (psychometrics), Capt. Omar Farid (mobile UX).

### 43.1 Tenancy: Track-Scoped Mastery (Improvement #56)

Cross-track mastery transfer is unreliable (near transfer works, far transfer doesn't — Corbett & Anderson, 1995). `SkillMastery` must be keyed per-skill-per-track, not globally.

```csharp
public sealed record SkillMastery(
    string SkillId,
    string TrackId,          // Mastery is per-track
    double PL,
    double PLEffective,
    DateTime LastPracticed,
    double DecayRate,
    int AssistanceLevel
);
```

**Cross-track seepage**: When mastery updates in one track, a nightly batch calculates a seepage boost for the same skill in other tracks: `P(L₀) += 0.1` (capped). Conservative, not real-time.

### 43.2 Tenancy: Schema Simplifications (Improvements #51, #52)

**ContentReadiness** (#51): `CurriculumTrackDocument` gains a `Status` enum: `Draft | Seeding | Ready`. Only `Ready` tracks are enrollable by students. Prevents empty-track enrollment.

**Defer ProgramDocument** (#52): Phase 1 creates 3 documents (Institute, CurriculumTrack, Enrollment), not 4. `EnrollmentDocument` binds Student directly to Track + Institute. No Program indirection until institutes need multi-track packaging.

**Seed data correction**: P1d seeds only 3 Bagrut tracks (`MATH-BAGRUT-806`, `MATH-BAGRUT-807`, `PHYSICS-BAGRUT-036`) + one `BAGRUT-GENERAL` placeholder. No SAT, no psychometry — those are deferred scope.

**Upcaster correction**: P1e upcaster assigns existing students to `BAGRUT-GENERAL` (not a specific track). Real track assignment happens at next session start via topic selection (§24, Improvement #26).

### 43.3 Figures: Scaffolding-Adaptive Rendering (Improvement #57)

Each element in `PhysicsDiagramSpec` gains a `visibleAtLevel` property:

```json
{
  "type": "force_arrow",
  "body": "block",
  "direction": [0, -9.8],
  "label": "mg",
  "visibleAtLevel": ["full", "partial"]
}
```

At `minimal` and `exploratory` scaffolding, this arrow is hidden — the student must identify forces themselves (Construct mode) or infer effects (Display mode). The spec is authored once with full detail; the renderer filters by scaffolding level.

### 43.4 Figures: Admin Editor Phase 1 (Improvement #53)

FIGURE-006 bumped to **high priority**. Phase 1 scope: JSON spec editor with syntax highlighting + live preview panel + validation with line-level errors + template library (inclined-plane, function-plot, geometry templates). Full visual WYSIWYG editor deferred to Phase 2. Unblocks content authoring pipeline.

### 43.5 Figures: Script-Aware SVG Text (Improvement #61)

`PhysicsDiagramSpec` text elements gain a `script` property: `latin | arabic | hebrew`. The SVG renderer sets `direction` and `unicode-bidi` accordingly. Default: `latin` (LTR) for physics/math symbols. Explicit `arabic` or `hebrew` for natural language labels (body names, instruction text).

### 43.6 Figures: Difficulty-Aligned Quality Gate (Improvement #63)

Quality gate FIGURE-007 checks figure information level vs. question target difficulty:
- Warn if `full` scaffolding figure + high-difficulty target (figure makes question too easy)
- Warn if `minimal` scaffolding figure + low-difficulty target (figure makes question too hard)
- Equilibrium check runs at **each** scaffolding level to verify solvability with visible information

### 43.7 RTL: MathLive Separation (Improvements #59, #60)

MathLive has three RTL showstoppers in editable mixed-content fields: cursor direction at bidi boundaries, backspace behavior, iOS keyboard toggle. **Solution**: separate input types by field:

- `StepInputType.MathExpression` → MathLive (math-only, LTR keyboard)
- `StepInputType.VerbalExplanation` → `<textarea dir="rtl">` (system Arabic/Hebrew keyboard)
- `StepInputType.NumericOnly` → `<input type="number">` (numeric keyboard)

No mixed math-and-Arabic-text input in the same field. Display uses `<bdi dir="ltr">` around KaTeX (existing approach).

Verbal textarea: soft 200-character limit, character count visible, Arabic placeholder modeling a good explanation: "أوجدت المشتقة باستخدام قاعدة السلسلة لأن الدالة مركبة" (I found the derivative using the chain rule because the function is composite).

### 43.8 Arabic Math Input Normalization (Improvement #66)

Between input field and CAS router, a static normalization layer translates Arabic math shorthand:

| Student types | Normalized to | Note |
|---|---|---|
| س، ص، ع | x, y, z | Arabic variable names to Latin |
| ٠١٢٣٤٥٦٧٨٩ | 0123456789 | Eastern Arabic to Western digits |
| جذر | `\sqrt` | Arabic word for root |
| جا، جتا، ظا | `\sin`, `\cos`, `\tan` | Arabic trig abbreviations |
| × | `\times` | Arabic multiplication |
| ÷ | `\div` | Arabic division |

Static, human-curated, loaded at startup. Must be idempotent — running twice produces same output. Does not modify expressions already in LaTeX.

### 43.9 Measurement: Multi-Level IRT Preparation (Improvement #62)

Include `InstituteId` and `TrackId` in `StepVerified` and `StepFailed` events. Costs one field per event. Enables multi-level IRT calibration (De Ayala, 2009, Ch. 11) at scale — question difficulty parameters can account for student ability distribution differences across institutes.

### 43.10 Privacy: Pseudonymous Misconception Persistence (Improvement #64)

30-day rotating pseudonymous tracking enables cross-session misconception persistence detection without profile-scoped storage. Pseudonym = SHA-256(`studentId + monthKey`). Monthly key rotation automatically unlinks old pseudonyms. Within a 30-day window, the system detects "anonymous student X showed pattern Y in 3 of 5 sessions." Beyond rotation, no historical profile buildup. COPPA/GDPR-K safe.

### 43.11 Social Learning: Anonymous Class Stats (Improvement #58)

Anonymous class-level mastery percentages per topic, displayed on topic selector: "68% of students in your class mastered derivatives this week." Not a leaderboard — social proof without social comparison. k-anonymity threshold: class must have ≥10 students to show class stats; below that, show school-level; below that, platform-level. Updated hourly via `ClassMasteryProjection`.

### 43.12 Mobile UX: Mini Figure Thumbnail (Improvement #65)

On mobile, when the step input is focused (keyboard open), show a 48x48px figure thumbnail pinned to top-right of input area. Tappable to expand to full-size modal. Shows figure at current scaffolding level. Prevents cognitive context switching when the figure scrolls off-screen during typing. Same approach as Photomath.

### 43.13 Dependencies Identified

- **GD-006 (MathLive RTL spike)** blocks PWA-003 and PWA-008 (#54). Must complete first.
- **GD-008 (Arabic-first physics decision)** determines whether FIGURE-005 is critical-path (#55). If physics is pilot-first, figure rendering for physics must ship before math figures.
- **GD-001 title correction**: rename to "CAS engine is sole source of truth" (not SymPy specifically — architecture uses 3-tier CAS with QuestionCasBinding).
- **GD-005 (compliance artifacts)**: coordination task requiring legal/DPO review, not engineering.
- **Arabic parent install guide** (#67): content task for pilot onboarding, not engineering backlog.

---

## 44. Consolidated Improvement Registry

| # | Source | Improvement | Category |
|---|--------|------------|----------|
| 1 | Architects | External JSON routing table, hot-reloaded | Ops |
| 2 | Architects | SymPy always-on safety net + Giac circuit breaker | Reliability |
| 3 | Architects | Equivalence mode + 500-pair conformance suite | Correctness |
| 4 | Architects | ExpectedPattern for technique verification | Pedagogy |
| 5 | Architects | CAS audit events + admin CAS trace | Trust |
| 6 | Architects | min-instances=1, SymPy preload at startup | Performance |
| 7 | Learning Scientist | AST-diff diagnosis on failed steps | Pedagogy |
| 8 | Learning Scientist | Exploratory scaffolding (productive failure) | Pedagogy |
| 9 | Learning Scientist | Misconception catalog + session tally | Pedagogy |
| 10 | Learning Scientist | FBD Construct mode for physics | Pedagogy |
| 11 | Learning Scientist | Remediation micro-task templates | Pedagogy |
| 12 | Psychometrician | IRT calibration (Rasch + 2PL) | Measurement |
| 13 | Psychometrician | Difficulty-preserving variant constraints | Measurement |
| 14 | Psychometrician | Item bank health dashboard + quality gate | Quality |
| 15 | Psychometrician | Constrained CAT algorithm | Selection |
| 16 | Psychometrician | A-stratified exposure control | Selection |
| 17 | Bagrut Examiner | Dual math tracks (806/807) + physics 036 | Content |
| 18 | Bagrut Examiner | 15+ empirical misconception entries | Content |
| 19 | Bagrut Examiner | Bagrut structural alignment tags | Content |
| 20 | Bagrut Examiner | Arabic terminology synonym table | Localization |
| 21 | RTL Engineer | `<bdi dir="ltr">` for inline KaTeX | Localization |
| 22 | RTL Engineer | SVG text auto-detect script direction | Localization |
| 23 | RTL Engineer | Eastern Arabic digit normalization | Localization |
| 24 | RTL Engineer | StepInputType (Math/Verbal/Numeric) | Localization |
| 25 | RTL Engineer | SRE aria-labels for math in AR/HE | Accessibility |
| 26 | UX Psychologist | Session start with topic choice + suggestion | Motivation |
| 27 | UX Psychologist | Mastery map progress (no XP/streaks) | Motivation |
| 28 | UX Psychologist | Progressive disclosure + assistance-weighted BKT | UX/Pedagogy |
| 29 | UX Psychologist | Natural session boundaries (5 problems / 15 min) | UX/Ethics |
| 30 | Security | Per-student variant seeds, daily rotation | Integrity |
| 31 | Security | Exam simulation mode (reserved pool, timed, no hints) | Assessment |
| 32 | Security | Behavioral anomaly detection (informational flags) | Integrity |
| 33 | Security | Bagrut readiness report with confidence intervals | Reporting |
| 34 | Screenshot Research | Ephemeral image processing (1.5s volatile, no disk) | Privacy |
| 35 | Screenshot Research | Adversarial image defense (preprocessing + CAS backstop) | Security |
| 36 | Screenshot Research | LaTeX sanitization (200-command allowlist, CVE-2024-28243) | Security |
| 37 | Screenshot Research | Tiered LaTeX expression levels (safe/advanced/custom) | Security/UX |
| 38 | Screenshot Research | 4-tier rate limiting (token bucket + cost circuit breaker) | Cost/Security |
| 39 | Screenshot Research | 4-tier content moderation (PhotoDNA → Cloud Vision → custom) | Compliance |
| 40 | Screenshot Research | Alternative input modalities (typed/voice/handwriting) | Accessibility |
| 41 | Screenshot Research | 17 failure modes with graceful degradation chain | Reliability |
| 42 | Screenshot Research | Exam-time upload detection + homework copy-paste mitigation | Integrity |
| 43 | Adversarial Review | QuestionCasBinding — lock question to authoring CAS engine | Correctness |
| 44 | Adversarial Review | BKT+ extensions (forgetting curve, prerequisite DAG, assistance weighting) | Pedagogy |
| 45 | Adversarial Review | Event store scaling (snapshots, partitioning, async projections) | Performance |
| 46 | Adversarial Review | Actor crash recovery (localStorage draft + SignalR reconnect) | Reliability |
| 47 | Adversarial Review | Data-discovered misconception candidates from clustered AST diffs | Pedagogy |
| 48 | Adversarial Review | Cross-platform figure rendering parity (N/A if PWA chosen) | Rendering |
| 49 | Adversarial Review | Three-layer observability (OTel + Seq/Loki + 6 critical alerts) | Ops |
| 50 | Adversarial Review | Design invariant hardening — CAS-verify all math in LLM output | Correctness |
| 51 | Expert Panel R2 | `ContentReadiness` on CurriculumTrack — only Ready tracks enrollable | Data Integrity |
| 52 | Expert Panel R2 | Defer ProgramDocument to Phase 2 — P1 binds directly to Track | Simplification |
| 53 | Expert Panel R2 | FIGURE-006 bumped to high — JSON editor + live preview Phase 1 | Content Pipeline |
| 54 | Expert Panel R2 | GD-006 (MathLive RTL spike) blocks PWA-003/008 | Dependency |
| 55 | Expert Panel R2 | If Arabic-first physics → FIGURE-005 critical path | Priority |
| 56 | Expert Panel R2 | Mastery per-skill-per-track with cross-track seepage (nightly batch) | Pedagogy |
| 57 | Expert Panel R2 | `visibleAtLevel` on PhysicsDiagramSpec elements — scaffolding-adaptive figures | Pedagogy |
| 58 | Expert Panel R2 | Anonymous class-level mastery stats (k≥10 anonymity) | Motivation |
| 59 | Expert Panel R2 | MathLive for math-only input; textarea for verbal — no mixed bidi | RTL/Input |
| 60 | Expert Panel R2 | Verbal textarea: RTL, char count, Arabic placeholder, 200-char soft limit | RTL/UX |
| 61 | Expert Panel R2 | `script` property on diagram text elements for correct bidi rendering | RTL/Figures |
| 62 | Expert Panel R2 | Include InstituteId + TrackId in step verification events (multi-level IRT) | Measurement |
| 63 | Expert Panel R2 | Quality gate: figure info level vs target difficulty consistency | Measurement |
| 64 | Expert Panel R2 | 30-day rotating pseudonymous misconception persistence tracking | Privacy |
| 65 | Expert Panel R2 | Mini figure thumbnail on mobile during step input (48×48px, pinned) | Mobile UX |
| 66 | Expert Panel R2 | Arabic math input normalizer (س→x, جذر→√, Eastern digits→Western) | Input/Arabic |
| 67 | Expert Panel R2 | Arabic parent install guide PDF for pilot school distribution | Onboarding |

---

## 45. References

### Academic

- VanLehn, K. (2011). The relative effectiveness of human tutoring, intelligent tutoring systems, and other tutoring systems. *Educational Psychologist*, 46(4), 197–221. [d ≈ 0.76 for step-based ITS]
- Renkl, A., & Atkinson, R. K. (2003). Structuring the transition from example study to problem solving. *Educational Psychologist*, 38(1), 15–22. [d ≈ 0.4–0.6 for faded examples]
- Kalyuga, S., et al. (2003). The expertise reversal effect. *Educational Psychologist*, 38(1), 23–31. [scaffolds hurt experts]
- Sweller, J., et al. (1998). Cognitive architecture and instructional design. *Educational Psychology Review*, 10(3), 251–296.
- Finkelstein, N. D., et al. (2005). When learning about the real world is better done virtually. *Physical Review Special Topics*, 1(1). [PhET beats real lab on conceptual items]
- Aleven, V., & Koedinger, K. R. (2000). The need for tutorial dialog to support self-explanation. *AAAI/IAAI*, 2000.
- Sailer, M., & Homner, L. (2020). The gamification of learning: a meta-analysis. *Educational Psychology Review*, 32(1), 77–112. [g ≈ 0.49]
- Kapur, M. (2008). Productive failure. *Cognition and Instruction*, 26(3), 379–424. [d ≈ 0.37 on transfer]
- Kapur, M. (2014). Productive failure in learning math. *Cognitive Science*, 38(5), 1008–1022.
- Hestenes, D., Wells, M., & Swackhamer, G. (1992). Force Concept Inventory. *The Physics Teacher*, 30(3), 141–158.
- De Ayala, R. J. (2009). *The Theory and Practice of Item Response Theory*. Guilford Press. [IRT/Rasch reference]
- Deci, E. L., & Ryan, R. M. (2000). Self-Determination Theory and the facilitation of intrinsic motivation. *American Psychologist*, 55(1), 68–78.
- Bjork, R. A. (1994). Memory and metamemory considerations in the training of human beings. In *Metacognition* (MIT Press).
- Bjork, E. L., & Bjork, R. A. (2011). Making things hard on yourself, but in a good way. In *Psychology and the Real World* (Worth Publishers).
- Slamecka, N. J., & Graf, P. (1978). The generation effect. *Journal of Experimental Psychology: Human Learning and Memory*, 4(6), 592.
- Goodfellow, I. J., et al. (2015). Explaining and harnessing adversarial examples. *ICLR 2015*. [FGSM]
- Pathade, S. (2024). Steganographic prompt injection against Gemini Pro Vision. [18.3% ASR]
- OWASP (2025). Top 10 for LLM Applications. [Prompt injection #1 risk]
- NIST (2023). AI Risk Management Framework (AI RMF 1.0). [Govern, Map, Measure, Manage]
- Corbett, A. T., & Anderson, J. R. (1995). Knowledge tracing: Modeling the acquisition of procedural knowledge. *User Modeling and User-Adapted Interaction*, 4(4), 253–278. [Cross-track seepage model]
- Vygotsky, L. S. (1978). *Mind in Society*. Harvard University Press. [Zone of Proximal Development, social learning]
- CVE-2024-28243: KaTeX token expansion denial-of-service vulnerability.
- COPPA 2025 Amended Rule: FTC Federal Register 2025-05904 (effective June 2025).
- Israeli PPL Amendment 13 (effective August 2025): ISS classification for biometric data.

### Product & Industry

- Khanmigo 2023 arithmetic-error incident
- MathDial 2023: answer leakage ~40% within 3 turns
- Photomath (acquired by Google, 2023, $500M) — custom CAS, proprietary
- Duolingo engineering blog — streak = loss aversion (confirms Wordle finding)
- FTC v. Epic Games ($245M), FTC v. Edmodo "Affected Work Product" consent decree
- ICO v. Reddit £14.47M (Feb 2026)

### Tools & Libraries

- SymPy: sympy.org (BSD license)
- MathNet.Symbolics: github.com/mathnet/mathnet-symbolics (MIT license)
- function-plot.js: mauriciopoppe.github.io/function-plot (MIT license)
- JSXGraph: jsxgraph.uni-bayreuth.de (LGPL-3.0)
- KaTeX: katex.org (MIT license)
- MathLive: cortexjs.io/mathlive (MIT license)
- Wolfram Alpha API: products.wolframalpha.com/api
- Giac/Xcas: www-fourier.ujf-grenoble.fr/~parisse/giac.html (GPL-3.0)

### Cena Internal

- `docs/research/cena-sexy-game-research-2026-04-11.md` — 10-track game-design research synthesis
- `docs/autoresearch/math-ocr-research.md` — OCR tool evaluation
- `src/mobile/lib/features/diagrams/models/diagram_models.dart` — mobile diagram model
- `examples/figure-sample/index.html` — figure rendering samples
- `examples/figure-sample/step-solver.html` — step-solver scaffolding samples
- `tasks/figures/FIGURE-001..008` — figure rendering task queue
- `tasks/game-design/GD-001..010` — game-design research task queue
- `docs/autoresearch/screenshot-analyzer/iteration-01..10` — 10-iteration defense-in-depth security research (87/100 score)
