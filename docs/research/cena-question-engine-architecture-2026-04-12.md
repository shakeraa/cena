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
20. [References](#20-references)

---

## 1. Executive Summary

Cena's question engine serves math and physics questions to Israeli Bagrut (4/5 unit), AP Calculus, and SAT students in English, Arabic, and Hebrew. The engine must:

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

---

## 20. References

### Academic

- VanLehn, K. (2011). The relative effectiveness of human tutoring, intelligent tutoring systems, and other tutoring systems. *Educational Psychologist*, 46(4), 197–221. [d ≈ 0.76 for step-based ITS]
- Renkl, A., & Atkinson, R. K. (2003). Structuring the transition from example study to problem solving. *Educational Psychologist*, 38(1), 15–22. [d ≈ 0.4–0.6 for faded examples]
- Kalyuga, S., et al. (2003). The expertise reversal effect. *Educational Psychologist*, 38(1), 23–31. [scaffolds hurt experts]
- Sweller, J., et al. (1998). Cognitive architecture and instructional design. *Educational Psychology Review*, 10(3), 251–296.
- Finkelstein, N. D., et al. (2005). When learning about the real world is better done virtually. *Physical Review Special Topics*, 1(1). [PhET beats real lab on conceptual items]
- Aleven, V., & Koedinger, K. R. (2000). The need for tutorial dialog to support self-explanation. *AAAI/IAAI*, 2000.
- Sailer, M., & Homner, L. (2020). The gamification of learning: a meta-analysis. *Educational Psychology Review*, 32(1), 77–112. [g ≈ 0.49]

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
