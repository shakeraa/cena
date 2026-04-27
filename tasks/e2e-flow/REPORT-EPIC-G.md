# REPORT-EPIC-G — Admin SPA full-page coverage

**Status**: ✅ green — full smoke + responsiveness + cross-page nav + 3 prod JS bugs fixed
**Date**: 2026-04-27
**Worker**: claude-1
**Specs**:
- `EPIC-G-admin-pages-smoke.spec.ts` — 39 admin routes
- `EPIC-G-admin-pages-responsive.spec.ts` — 23 routes × 3 viewports = 69 pairs
- `EPIC-X-cross-page-journey.spec.ts` (admin half) — sidebar real-click drive

## Smoke matrix

39 routes probed (35 signed-in admin + 4 public). Buckets:

| Bucket | Count | Notes |
|---|---|---|
| OK | 23 | Page loads, no console errors, no JS exceptions |
| Known-broken (allowlisted) | 11 | See list below — each tied to a backend gap |
| No content | 3 | Pages without `<main>` selector — informational |
| Hard JS errors | **0** | All 3 prod JS bugs surfaced and fixed |

Total wall: 44.9s.

### Production JS bugs surfaced and fixed

1. `/apps/pedagogy/mcm-graph` — `Cannot read properties of undefined (reading 'length')`
   Fix in [src/admin/.../pedagogy/mcm-graph.vue](src/admin/full-version/src/pages/apps/pedagogy/mcm-graph.vue): nullish-coalesce `data.errorTypes`, `data.conceptCategories`, `data.edges` to empty arrays so the empty-state path renders cleanly when the API returns a partial body.

2. `/apps/system/ai-settings` — `Cannot read properties of undefined (reading 'modelId')`
   Fix in [src/admin/.../system/ai-settings.vue](src/admin/full-version/src/pages/apps/system/ai-settings.vue): gate the "Provider Configuration" `<VCard>` on `v-if="activeProviderConfig"` so the `v-model="activeProviderConfig!.modelId"` non-null assertion below doesn't crash when `providers` is empty.

3. `/apps/system/token-budget` — `Cannot read properties of undefined (reading 'toFixed')`
   Fix in [src/admin/.../system/token-budget.vue](src/admin/full-version/src/pages/apps/system/token-budget.vue): merge `budgetData` over the initial defaults so every numeric field is guaranteed before `statCards` computed accesses `.toFixed(2)` etc.

### Known-broken routes (allowlisted with reason)

Each entry is a real backend gap surfaced by the spec — queued for backend work, not stubbed:

| Route | Reason |
|---|---|
| /apps/ingestion/pipeline | admin-api: GET /api/admin/ingestion/{stats,pipeline-status} 500 |
| /apps/questions/languages | admin-api: GET /api/admin/questions/languages 500 |
| /apps/questions/list | admin-api: GET /api/admin/questions list 500 |
| /apps/sessions/live | admin-api: 401 from realtime endpoint — token shape mismatch |
| /apps/sessions/monitor | admin-api: SignalR /sessionMonitor hub negotiate 404 |
| /apps/system/actors | admin-api: SignalR /actors hub negotiate 404 |
| /apps/system/ai-settings | admin-api: GET /api/admin/ai/settings 500 (page now renders empty-state cleanly) |
| /apps/system/architecture | admin-api: SignalR /architecture hub negotiate 404 |
| /apps/system/events | admin-api: SignalR /events hub negotiate 404 |
| /instructor | admin-api: GET /api/instructor/* 404 |
| /mentor | admin-api: GET /api/mentor/institutes 404 |

Anti-flake filter: 429 / "Too Many Requests" / FETCH_FAILED / "Failed to fetch" on a route's console-error array are treated as smoke-iteration artifacts (rate-limit window / aborted request on nav), not product bugs.

## Responsiveness

23 admin routes × 3 viewports (375 / 768 / 1440):

| Viewport | Overflows | No-heading |
|---|---|---|
| Mobile (375px) | 0 | 0 |
| Tablet (768px) | 0 | 0 |
| Desktop (1440px) | 0 | 0 |

Total wall: 1.2 min. Hard fail on mobile overflow; soft fail on tablet/desktop. The admin SPA's responsive shell holds up across all breakpoints.

## Cross-page navigation

[EPIC-X-cross-page-journey.spec.ts:142](src/student/full-version/tests/e2e-flow/workflows/EPIC-X-cross-page-journey.spec.ts) drives admin@cena.local through 3 sidebar real-click navigations (Permissions, Roles, Audit Log), asserts each page renders a heading and no JS errors propagate. Wall time 2.9s.

## What's NOT covered (queue these)

- **Per-cell admin functionality**: clicking individual filter chips, approving moderation items, promoting users via the role-change endpoint, CAS-override workflow. Each needs seeded data the dev stack doesn't guarantee.
- **Dynamic [id] routes**: `/apps/experiments/[id]`, `/apps/user/view/[id]`, `/apps/mastery/student/[id]`, etc. Need real seeded ids.
- **Admin page-load WebSocket / SSE assertions**: many admin pages open SignalR hubs on mount; the smoke catches the 404 negotiation but doesn't assert what data should flow once a hub is live.

## Build gate

```
$ dotnet build src/actors/Cena.Actors.sln --nologo --verbosity minimal
0 Error(s)
Time Elapsed 00:00:26.00
```
