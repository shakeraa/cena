# TASK-E2E-COV-01: Per-page performance — sweep every route, measure FCP/LCP/INP/TBT

**Status**: Proposed
**Priority**: P1
**Epic**: Coverage matrix (cross-cutting; covers EPIC-E2E-A through L surface)
**Tag**: `@coverage @perf @p1`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/EPIC-X-performance-budgets.spec.ts` (extend) + new `EPIC-X-performance-prod-build.spec.ts`
**Prereqs**: TASK-E2E-INFRA-04 (production-build perf harness — see sibling task) for the LCP-real-numbers half

## Why this exists

The current perf spec measures **5 student + 4 admin canonical pages** (9 of ~76 user-facing routes). LCP entries are `-1` because the dev-mode bundle is too small to trip Largest-Contentful-Paint heuristics. We catch FCP/DCL/LOAD/TBT regressions on those 9 pages, but the rest of the surface is unmeasured. A page can quietly regress from 200 ms → 4 s without any test failing today.

## Journey

Driven from inside an authenticated session (one for student, one for admin):

1. Iterate every route in `STUDENT_ROUTES` + `ADMIN_STATIC_ROUTES` (matrices already in the smoke specs).
2. For each route: `page.goto(url, { waitUntil: 'load' })` then `await page.waitForTimeout(1500)` so LCP candidates settle.
3. Read `performance.getEntriesByType('paint' | 'largest-contentful-paint' | 'navigation' | 'longtask')`.
4. Record FCP, LCP, DCL, LOAD, TBT, total transferred bytes (`navigation.transferSize`).
5. Append to per-route results array; attach as JSON artifact.

A second spec (`EPIC-X-performance-prod-build.spec.ts`) runs the same sweep but against the **production-build** SPA (see TASK-E2E-INFRA-04) so LCP isn't `-1` and budgets reflect reality.

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| Browser perf | FCP ≤ 1.5 s (prod) / ≤ 4 s (dev) per route; LCP ≤ 2.5 s (prod); TBT ≤ 200 ms |
| Network | `navigation.transferSize` ≤ 2 MB per route; route-bundle JS ≤ 800 KB gzipped |
| Regression delta | When a route's previous-baseline LCP grows > 20 %, fail with a clear before/after diff |

## Regression this catches

- A new dependency added to a low-traffic page bloats the chunk graph (Vite's automatic code-splitting can regress when an import path is moved across barrel files).
- A SignalR / SSE handler eagerly retries on mount, blocking the main thread (TBT regression).
- An API call on mount blocks first paint because the page isn't using `<Suspense>` properly.

## Done when

- [ ] `EPIC-X-performance-budgets.spec.ts` iterates all signed-in admin + student static routes (47 total)
- [ ] `EPIC-X-performance-prod-build.spec.ts` reuses the same matrix against the production build (depends on INFRA-04)
- [ ] Per-route baselines stored under `tests/e2e-flow/baselines/perf/{route-slug}.json` so regression deltas are detectable
- [ ] CI annotation surfaces top-3 worst-LCP pages on every run
- [ ] Tagged `@perf @p1`
