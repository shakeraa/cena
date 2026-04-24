# TASK-E2E-K-04: Update prompt when new SPA build ships

**Status**: Proposed
**Priority**: P2
**Epic**: [EPIC-E2E-K](EPIC-E2E-K-offline-pwa.md)
**Tag**: `@offline @pwa @p2`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/update-prompt.spec.ts`

## Journey

User on version N → N+1 deployed → SPA detects via SW → "Update available" prompt → user accepts → reload → new version active.

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| DOM | Update prompt UI |
| Reload | Preserves route + scroll |
| Next visit | New build active |

## Regression this catches

Update never surfaces; update loses session state; update prompts every visit (loop).

## Done when

- [ ] Spec lands
