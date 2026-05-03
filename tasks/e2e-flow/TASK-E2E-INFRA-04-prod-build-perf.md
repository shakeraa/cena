# TASK-E2E-INFRA-04: Production-build perf measurement harness

**Status**: Proposed
**Priority**: P1
**Epic**: Shared infra
**Tag**: `@infra @perf @p1`
**Prereqs**: `vite build` running clean for both admin + student SPAs (currently green)

## Why this exists

The current `EPIC-X-performance-budgets.spec.ts` runs against `vite dev`. Two consequences:

1. **LCP entries don't fire** (`-1` on every page) — Vite's dev mode serves tiny HTML chunks that never trip the LCP heuristic. We see FCP/DCL/LOAD but not the user-visible "main content has painted" metric.
2. **Bundle weights are wrong** — dev mode includes HMR runtime + source maps + un-minified code. A 2 MB dev page might be 200 KB in production. Budget regressions in real bytes-over-the-wire cannot be tracked from dev mode.

We need a separate harness that boots a production build and re-runs the perf sweep against it.

## What to build

1. Add `npm run preview:e2e` script to both SPA package.jsons:
   ```json
   "preview:e2e": "vite build && vite preview --port 5176 --host"
   ```
2. New Playwright config `playwright.e2e-prod.config.ts` that:
   - Sets `baseURL` to `http://localhost:5176`
   - Spawns the preview server as a `webServer`
   - Reuses the same `tests/e2e-flow/workflows/` glob
3. New spec `EPIC-X-performance-prod-build.spec.ts` (or convert the existing perf spec to be shared between dev + prod via a parameter) that runs the perf matrix against the preview port
4. Tighter budgets in the prod config:
   ```ts
   const BUDGETS_PROD = { fcp: 1500, lcp: 2500, load: 5000, transferSize: 800_000 } as const
   ```
5. Per-route bundle-size assertion using `Performance.getEntriesByType('resource')` summed
6. Recipe in `tests/e2e-flow/README.md` for running the prod-perf suite against a CI artifact build

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| LCP | ≤ 2.5 s on every signed-in route |
| FCP | ≤ 1.5 s on every signed-in route |
| Bundle | Per-route JS transferSize ≤ 800 KB gzipped |
| TBT | ≤ 200 ms during the load window |

## Regression this catches

- A new dependency that adds 300 KB to a route's chunk graph (Vite's automatic code-splitting can regress when imports cross barrel files)
- An eager mount-time API call that blocks LCP because the page can't paint until `/api/me` resolves
- A service-worker registration that delays first paint waiting for the cache prime

## Done when

- [ ] `preview:e2e` script in both SPAs
- [ ] `playwright.e2e-prod.config.ts` configured + boots preview server
- [ ] Prod perf spec runs all canonical pages with tighter budgets
- [ ] CI artifact: prod build → run → upload per-route JSON + bundle size summary
- [ ] When a route breaches the prod LCP budget, the test attaches a Chrome DevTools trace JSON for downstream investigation
