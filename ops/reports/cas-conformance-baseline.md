# CAS Conformance Baseline

Target: ≥99% pass rate across the cases below via `ICasRouterService.VerifyAsync`. Used by `CasConformanceSuiteRunner` (nightly CI).

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

- Total: 27 cases
- Required: ≥27 (100% for v1 baseline; drift to 99% acceptable once >100 cases land)
- Enforced by: `CasConformanceSuiteRunner` (see `src/actors/Cena.Actors.Tests/Cas/`)
