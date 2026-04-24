# ADR-0031: Error Pattern Matching Approach (CAS-Grounded Symbolic Matching)

- **Status**: Accepted
- **Date**: 2026-04-15
- **Task**: RDY-033
- **Supersedes**: Inline string matchers in `MisconceptionDetectionService` (RDY-014)
- **Related**: ADR-0002 (SymPy CAS oracle), ADR-0003 (misconception session scope)

## Context

RDY-014 shipped `MisconceptionDetectionService` with eight inline matchers that compare the student's answer to the correct answer using string heuristics (substring checks, length ratios, exact-string matches on decimal forms). These matchers break on trivially equivalent expressions: `x^2 + 6x + 9` and `6x + x^2 + 9` are mathematically identical but fail the substring comparison; `(x+3)^2` vs `x²+6x+9` fails on Unicode. They also silently mislabel: a correct answer rewritten in a different form is flagged as a misconception.

The task RDY-033 (adversarial review, Rami) calls out this gap. The ASSISTments tradition (Koedinger et al.) treats error pattern matching as a symbolic problem: parse both the correct and student answers into a CAS AST, apply a known buggy transformation to the correct answer, and check if the student's answer is symbolically equivalent to the buggy transform's output. String matching is a proxy that works only when expressions are in canonical form.

Cena already has a 3-tier CAS router (MathNet in-process, SymPy sidecar via NATS, MathNet fallback) locked by ADR-0002. It is the only approved math oracle in the system. Any production-grade matcher must go through it.

## Decision

**Adopt CAS-grounded symbolic matching via `ICasRouterService`** for the five highest-frequency misconceptions specified by the task. Each rule is a first-class `IErrorPatternMatcher` that:

1. **Parses context from the question stem** (regex) to extract the algebraic shape the buggy rule applies to.
2. **Constructs the buggy expected output** by applying the buggy transformation symbolically.
3. **Calls `ICasRouterService.VerifyAsync(Equivalence, studentAnswer, buggyOutput)`** to check if the student's answer is symbolically equivalent to what the buggy rule would produce.
4. **Scores confidence** based on the CAS result:
   - `1.0` — CAS reports `Verified = true` (exact symbolic equivalence)
   - `0.7` — numerical-tolerance match (`NumericalTolerance` op, ε = 1e-9)
   - `0.0` — no match, CAS error, or unsupported expression shape

Matchers live in `Cena.Actors.Services.ErrorPatternMatching.BuggyRuleMatchers` and are registered in DI as `IEnumerable<IErrorPatternMatcher>`. A single `ErrorPatternMatcherEngine` iterates the matchers, applies a **100 ms wall-clock budget**, and returns the highest-confidence match (or "unrecognized" if none).

### Matchers in scope for RDY-033

| Rule ID | Pattern | Buggy Transform |
|---------|---------|-----------------|
| `DIST-EXP-SUM` | `(a+b)^n` | `a^n + b^n` |
| `CANCEL-COMMON` | `(a+b)/a` | `b` (or `1+b/a` → `b`) |
| `SIGN-NEGATIVE` | `-(a+b)` | `-a + b` |
| `ORDER-OPS` | `a + b*c` (arithmetic) | `(a+b)*c` (left-to-right) |
| `FRACTION-ADD` | `a/b + c/d` | `(a+c)/(b+d)` |

The three new catalog entries (`CANCEL-COMMON`, `SIGN-NEGATIVE`, `ORDER-OPS`) are added to `MisconceptionCatalog` alongside the existing 15 rules.

### Why not rule-based regex only

Considered and rejected. Regex matching on string forms:

- Fails the moment a student reorders terms (`9 + x^2` vs `x^2 + 9`).
- Fails on equivalent notations (`**` vs `^`, `3x` vs `3*x`, degrees vs radians in Unicode).
- Cannot handle partial credit or near-misses that numerical tolerance catches.
- Produces false positives when the student's answer happens to contain a buggy-rule substring but is actually correct in a different form.

CAS-grounded matching is strictly more expressive at a bounded cost (the router's Tier 1 MathNet path is <1 ms for polynomial equivalence, Tier 2 SymPy is ~50 ms).

### Why not LLM-only classification

`IErrorClassificationService` already performs LLM-based classification into five buckets (`ConceptualMisunderstanding`, `ProceduralError`, etc.). It cannot identify *which* of the 15 buggy rules applies — it only tells us *that* the error looks conceptual. LLM classification is complementary, not a substitute: ADR-0002 forbids LLM output from reaching students without CAS verification, and misconception detection is a safety-adjacent concern (a false-positive misconception label burns trust).

### Confidence scoring, not binary

The matcher engine reports a real-valued confidence so downstream code (remediation routing, teacher dashboards, session-scoped tally) can decide how to act. Threshold for "detected" is 0.5. Below 0.5 the result is "unrecognized error", logged but not attributed to a buggy rule.

### Performance budget (100 ms)

Each call to the engine is bounded by a `CancellationTokenSource.CancelAfter(100ms)` linked to the caller's token. Matchers that fail the budget are skipped; the best match so far is returned. The CAS router's 5 s timeout is strictly larger than the budget, so we rely on the linked-cancel path rather than the router's timeout for latency enforcement.

### Unmatched errors are logged, never dropped

When no matcher returns ≥0.5 confidence, the engine emits a structured log event `[ERROR_UNMATCHED]` with `{subject, correctAnswer, studentAnswer, matchers_tried, elapsed_ms}`. This is the corpus from which future buggy rules are proposed (the "gap" in the 15-rule catalog is filled from observed unmatched data, not guesswork).

## Consequences

### Positive

- Matchers become semantic, not lexical. Reordered and renotated student answers match correctly.
- Every match has a CAS audit trail (the `ICasRouterService` records `cena.cas.*` metrics).
- The matcher registry is extensible: adding a 6th rule = adding one class + DI registration.
- `MisconceptionDetected_V1` events (already defined, never emitted) get a real production source.
- Unmatched-error logs become a data source for future buggy-rule authoring.

### Negative / Trade-offs

- Adds a hard dependency on the CAS router being registered in DI (previously the CAS stack was implemented but unwired). This ADR forces that wiring in the Actor Host — surfaced under "DI Impact" below.
- SymPy sidecar unavailability degrades matcher coverage. Mitigated by MathNet fallback; fully-degraded outcomes flow through as "unrecognized" rather than blocking the session.
- 100 ms budget is tight. If SymPy latency grows, fewer matchers run per call. Mitigated by metric `cena.cas.latency.ms` + alerts on p95 > 50 ms.

### Neutral

- The legacy inline matchers inside `MisconceptionDetectionService` are kept as a fallback for buggy rules outside the five RDY-033 targets. They run only when the engine returns "unrecognized". As the engine grows to cover all 15 rules, the legacy matchers can be retired.

## DI Impact

The Actor Host (`Cena.Actors.Host/Program.cs`) must register:

- `IMathNetVerifier` → `MathNetVerifier`
- `ISymPySidecarClient` → `SymPySidecarClient`
- `ICasRouterService` → `CasRouterService`
- Every `IErrorPatternMatcher` concrete type (5 of them)
- `IErrorPatternMatcherEngine` → `ErrorPatternMatcherEngine`
- `IMisconceptionDetectionService` → `MisconceptionDetectionService`

`INatsConnection` and `ICostCircuitBreaker` are already registered (lines 125 and 182 respectively).

## Session Scope (ADR-0003 enforcement)

Matchers are **pure query services** — they read `(questionStem, correctAnswer, studentAnswer)` and return a `ErrorPatternMatchResult`. They do not write to any store, do not append events, and do not touch student profiles. Event emission (`MisconceptionDetected_V1`) happens at the call site (`MisconceptionDetectionService` → `SessionEndpoints` handoff), which is where `[MlExcluded]` and 30-day retention (ADR-0003) are enforced.

## Test Strategy

Each matcher has:
- **One positive case** — a hand-authored pair that should match with confidence ≥ 0.5
- **One negative case (correct answer)** — student answer equals correct answer, must not match
- **One negative case (unrelated error)** — wrong answer that does not match the buggy rule, must not match
- **One CAS-failure case** — input that causes the CAS to return an error, must return 0.0 confidence gracefully

Tests mock `ICasRouterService` with NSubstitute (pattern established in `CasRouterServiceTests`) to avoid dependence on the live SymPy sidecar.

## Out of Scope for RDY-033

- Wiring `IMisconceptionDetectionService` into `SessionEndpoints.POST /answer` (belongs to RDY-014 follow-up).
- Emitting `MisconceptionDetected_V1` events from the answer pipeline (same).
- Matchers for the remaining 10 catalog rules (`TRIG-*`, `DERIVATIVE-EXP-POWER`, physics rules).
- Recalibrating confidence thresholds from production data (post-pilot).

## References

- `src/actors/Cena.Actors/Cas/CasContracts.cs`
- `src/actors/Cena.Actors/Cas/CasRouterService.cs`
- `src/actors/Cena.Actors/Services/MisconceptionCatalog.cs`
- `src/actors/Cena.Actors/Services/MisconceptionDetectionService.cs`
- `src/actors/Cena.Actors/Events/MisconceptionEvents.cs`
- Koedinger, K. R., & Anderson, J. R. (1990). *Abstract planning and perceptual chunks*
- Heffernan, N. T., & Heffernan, C. L. (2014). *The ASSISTments Ecosystem*
- Matz, M. (1982). *Towards a process model for high school algebra errors*
