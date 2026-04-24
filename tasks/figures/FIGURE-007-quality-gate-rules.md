# FIGURE-007: Quality gate rules for figures

## Goal
Extend `src/api/Cena.Admin.Api.Tests/QualityGate/` and the runtime quality-gate service so no question with a figure lands in the bank unless the figure passes automated correctness and accessibility checks.

## Depends on
- FIGURE-002 (schema)
- FIGURE-005 (physics service, for equilibrium check)

## Rules to enforce
1. **`aria-label` present and ≥ 20 characters** for any figure type. Reject otherwise.
2. **FunctionPlot**: parse the expression with SymPy (or MathNet if no Python service). Reject if:
   - Expression doesn't parse
   - Any marker coordinate lies outside the visible `[xMin, xMax] × [yMin, yMax]` window
   - Stated roots are not actually roots (evaluate `f(marker.x) ≈ 0` with tolerance)
   - Domain contains the answer features (if the answer is "vertex", the vertex must be visible)
3. **PhysicsDiagram**: construct the force system; reject if:
   - Force directions inconsistent with body type (e.g. friction not along surface)
   - If `equilibrium = true` then `Σ F ≠ 0`
   - If `acceleration` is specified, `Σ F ≠ m·a`
   - Missing required forces for the body (incline always has N + mg at minimum)
4. **Geometry**: JSXGraph JSON parses; all referenced objects exist; no circular dependencies.
5. **Raster**: image exists at `cdnUrl`; dimensions match; non-empty `ariaLabel`; `sourceAttribution` present for any Bagrut-derived figure (copyright trail).
6. **Dark-mode parity**: figure renderable in both themes without errors (runtime render check for SVG-based specs).
7. **RTL safety**: no hard-coded `direction: rtl` or `ltr` that would break when the container flips.
8. **File size** (raster): < 500KB per image; warn > 200KB.

## Work to do
1. Extend `QualityGateTestData.cs` with figure test cases — one per rule, happy + sad path.
2. Add `FigureQualityGate` service that runs the above rules and returns a list of violations.
3. Wire into the existing `AiGenerationService.cs` flow so AI-generated figures run the gate before persistence.
4. Wire into the admin figure editor save path — display violations inline, block save on errors, allow save with warnings.
5. Tests: `src/api/Cena.Admin.Api.Tests/Figures/FigureQualityGateTests.cs`.

## Non-negotiables
- Rule 2 (marker correctness) is **not a warning, it's a rejection**. A "root marker" that isn't a root is a pedagogical bug that poisons the bank.
- Quality gate output is stored on the question event stream (auditable).

## DoD
- All rules implemented + tested (happy + sad path)
- Gate blocks known-bad specs from AI generation
- Gate blocks known-bad specs from admin save

## Reporting
Complete with branch and a list of which rules were enforced at save time vs generation time.
