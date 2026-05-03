# CAS Conformance Baseline

**Target**: ≥99% pass rate across the concrete pairs below.
**Enforced by**: `CasConformanceBaselineRunnerTests.Router_Runs_Every_Baseline_Case_And_Writes_Artifact` (nightly CI, `.github/workflows/cas-nightly.yml`).
**Runner artifact**: `ops/reports/cas-conformance-last-run.json` (uploaded by CI).
**Strict 99% gate** activates automatically when the test environment has a reachable SymPy sidecar (`sympy_reachable=true` in the artifact). Otherwise the MathNet-only floor (≥ 35%) applies so the runner still exercises the parse + execution path in dev + PR builds.

---

## Scope of the corpus (RDY-044 reconciliation)

The conformance suite file `src/actors/Cena.Actors/Cas/CasConformanceSuite.cs` declares a **500-slot** skeleton. Of those slots:

- **27 concrete pairs** are the hand-enumerated corpus below, each with a well-defined expected outcome (Ok / Failed / Unverifiable).
- **~23 additional concrete pairs** live inside the suite file across the algebra/calculus/trig/unverifiable categories but are not yet listed here — they are part of the 50-pair "real" set the runner advertises.
- **450 placeholders** are auto-generated slots filtered out by `CasConformanceSuiteRunner.RunCoreAsync(p => p.Category != "placeholder")`. They exist to reserve budget for Bagrut corpus ingestion.

Effective runnable size today: **~50 pairs**. The 27 below are the durable written contract the runner's CI gate measures against; the remaining ~23 are implementation-file-resident and not yet promoted into this doc.

## Runner modes (RDY-043)

1. **Engine-agreement mode** — `RunAsync` / `RunCategoryAsync` hits MathNet and SymPy directly, measures **cross-engine agreement**. This is a platform-invariant check (two independent CASes must agree on simplifiable math).
2. **Router mode** — `RunThroughRouterAsync` hits `ICasRouterService.VerifyAsync`, measures **correctness against the expected label**. This exercises the fallback ordering + circuit-breaker integration that the direct-engine mode bypasses.

ADR-0032 §Enforcement names **router-mode** as the CI gate. Engine-agreement runs alongside as a leading indicator — a persistent engine-agreement drift flags an ADR-0032 §7 review before router-mode flips red.

---

Format: `id | operation | expressionA | expressionB | variable | expected_status`

## Algebra — equivalence

- alg-eq-001 | Equivalence | `2*x + 3*x` | `5*x` | `x` | Ok
- alg-eq-002 | Equivalence | `(x+1)^2` | `x^2 + 2*x + 1` | `x` | Ok
- alg-eq-003 | Equivalence | `x^2 - 1` | `(x-1)*(x+1)` | `x` | Ok
- alg-eq-004 | Equivalence | `a*(b+c)` | `a*b + a*c` | `a` | Ok
- alg-eq-005 | Equivalence | `1/(x+1) + 1/(x-1)` | `2*x/(x^2-1)` | `x` | Ok
- alg-eq-006 | Equivalence | `sqrt(4)` | `2` | — | Ok
- alg-eq-neg-001 | Equivalence | `x^2 + 1` | `(x+1)^2` | `x` | Failed

## Algebra — normal form

- alg-nf-001 | NormalForm | `x + x + x` | — | `x` | Ok
- alg-nf-002 | NormalForm | `2*x - x + 3` | — | `x` | Ok
- alg-nf-003 | NormalForm | `(x+1)*(x-1)` | — | `x` | Ok

## Linear systems

- lin-001 | SolveLinear | `2*x + 3 = 7` | — | `x` | Ok
- lin-002 | SolveLinear | `3*(x-1) = 2*x + 4` | — | `x` | Ok
- lin-003 | SolveLinear | `x/2 + 1 = 5` | — | `x` | Ok

## Calculus — derivatives

- calc-d-001 | Derivative | `x^2` | `2*x` | `x` | Ok
- calc-d-002 | Derivative | `sin(x)` | `cos(x)` | `x` | Ok
- calc-d-003 | Derivative | `ln(x)` | `1/x` | `x` | Ok
- calc-d-004 | Derivative | `x^3 + 2*x` | `3*x^2 + 2` | `x` | Ok
- calc-d-neg-001 | Derivative | `x^2` | `x` | `x` | Failed

## Calculus — integrals

- calc-i-001 | Integral | `2*x` | `x^2` | `x` | Ok
- calc-i-002 | Integral | `cos(x)` | `sin(x)` | `x` | Ok
- calc-i-003 | Integral | `1/x` | `ln(x)` | `x` | Ok

## Trigonometry

- trig-001 | Equivalence | `sin(x)^2 + cos(x)^2` | `1` | `x` | Ok
- trig-002 | Equivalence | `2*sin(x)*cos(x)` | `sin(2*x)` | `x` | Ok
- trig-003 | Equivalence | `tan(x)` | `sin(x)/cos(x)` | `x` | Ok

## Unverifiable edge cases (should return Unverifiable, not Failed)

- unv-001 | Equivalence | `f(x)` | `g(x)` | `x` | Unverifiable
- unv-002 | Equivalence | `|x|` | `sqrt(x^2)` | `x` | Unverifiable   # domain-dependent

## Pass-rate target

- **Total written cases**: 27
- **Runnable pairs (incl. file-resident)**: ~50
- **Required**: 100% on the 27 written cases for v1; ≥99% on the full runnable set before Enforce flips in prod
- **CI gate (ADR-0032)**: router-mode pass rate ≥99%, enforced by `.github/workflows/cas-nightly.yml`
- **Measured baseline**: awaits first green nightly run — update this field with `<measured rate> @ <run id> <timestamp>` on first pass
