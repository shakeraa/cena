# RDY-043: Conformance Runner — Route Through `ICasRouterService`

- **Priority**: High — ADR-0032 correctness
- **Complexity**: Mid engineer
- **Effort**: 2-3 hours
- **Dependencies**: RDY-037 merged

## Problem

`CasConformanceSuiteRunner` calls `IMathNetVerifier` + `ISymPySidecarClient` directly. ADR-0032 §Enforcement claims it runs via `ICasRouterService`. The current runner measures cross-engine agreement but bypasses router-specific behavior (fallback ordering, circuit-breaker interaction, cost-breaker).

## Scope

Add `RunThroughRouterAsync` to `CasConformanceSuiteRunner` that calls `ICasRouterService.VerifyAsync` for each pair. Nightly runs both modes; baseline publishes both numbers. CI gate picks one; ADR updated to match.

## Acceptance

- [ ] `ICasConformanceSuiteRunner.RunThroughRouterAsync` exists
- [ ] Router-mode pass rate published in baseline doc
- [ ] ADR-0032 §Enforcement statement matches the code
