# TASK-E2E-K-01: Install the PWA

**Status**: Proposed
**Priority**: P2
**Epic**: [EPIC-E2E-K](EPIC-E2E-K-offline-pwa.md)
**Tag**: `@offline @pwa @p2`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/pwa-install.spec.ts`
**Prereqs**: none beyond shared fixtures (`tenant`, `authUser`, `stripeScope` — wired in `fixtures/tenant.ts`)

## Journey

Student visits SPA in Chrome → install prompt appears (manifest valid) → install → launches standalone → routes work offline after first load.

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| Manifest | Valid + discoverable |
| Service worker | Registered |
| Standalone | Detected post-install |

## Regression this catches

Manifest invalid; service worker not registering; standalone routes break.

## Done when

- [ ] Spec lands
