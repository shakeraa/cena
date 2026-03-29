# ADM-018: Explanation Cache Monitoring

**Priority:** P0 — observability for SAI-001/002/003 cache layers
**Blocked by:** None (SAI-001, SAI-002, SAI-003 are complete)
**Estimated effort:** 2 days
**Stack:** Vue 3 + Vuetify 3 + TypeScript, Admin API (.NET 9)

---

> **NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic.

## Context

The student AI interaction layer has 3 explanation cache tiers: L1 (persisted to question, SAI-001), L2 (Redis by error type, SAI-003), L3 (personalized via LLM context, SAI-004). Admins have zero visibility into cache health, hit rates, or invalidation. This task adds a monitoring page under System.

## Backend: New Admin API Endpoints

### ADM-018.1: ExplanationCacheAdminService + Endpoints

**Files to create:**

- `src/api/Cena.Admin.Api/ExplanationCacheAdminDtos.cs`
- `src/api/Cena.Admin.Api/ExplanationCacheAdminService.cs`

**Files to modify:**

- `src/api/Cena.Admin.Api/AdminApiEndpoints.cs` — add `MapExplanationCacheEndpoints`

**Endpoints:**

```
GET  /api/admin/explanations/cache-stats
     Returns: L1 count, L2 Redis key count + memory, L3 generation count, overall hit/miss rates

GET  /api/admin/explanations/by-question/{questionId}
     Returns: All cached explanations for a question (L1 persisted + L2 entries + L3 history)

POST /api/admin/explanations/invalidate
     Body: { questionId, level: "L1"|"L2"|"L3"|"all" }
     Returns: { invalidatedCount }

GET  /api/admin/explanations/quality-scores
     Query: ?minScore=0&maxScore=1&page=1&pageSize=20
     Returns: Paged list of explanations with factual/linguistic/pedagogical scores
```

**Data source:** L1 from Marten `question_bank` projection (explanation field). L2 from Redis `explanation:*` keys via `IConnectionMultiplexer`. L3 counts from Marten event stream (ExplanationGenerated events).

**Acceptance:**

- [ ] Cache stats queries real Redis for L2 metrics (key count, memory usage via `INFO memory`)
- [ ] L1 count derived from questions with non-null explanation field
- [ ] Hit/miss rates calculated from event stream (ExplanationCacheHit / ExplanationCacheMiss events)
- [ ] Quality scores query reads from quality gate results stored alongside explanations
- [ ] Invalidation actually deletes Redis keys for L2 and clears Marten field for L1
- [ ] All endpoints require `SuperAdminOnly` auth policy

### ADM-018.2: Explanation Cache Dashboard Page

**Files to create:**

- `src/admin/full-version/src/pages/apps/system/explanation-cache.vue`
- `src/admin/full-version/src/views/apps/system/explanation-cache/CacheStatsCards.vue`
- `src/admin/full-version/src/views/apps/system/explanation-cache/CacheHitRateChart.vue`
- `src/admin/full-version/src/views/apps/system/explanation-cache/QualityScoreTable.vue`

**Acceptance:**

- [ ] 4 stat cards at top: L1 Count, L2 Key Count, L3 Generation Count, Overall Hit Rate
- [ ] Hit rate line chart: L1 vs L2 vs L3 hit rates over last 7 days (ApexCharts)
- [ ] Quality score data table: question stem (truncated), factual score, linguistic score, pedagogical score, composite
- [ ] Filter by score range, sort by any score column
- [ ] Invalidation button per question (with confirmation dialog)
- [ ] Bulk invalidation: "Clear all L2 cache" button (SuperAdmin only, with double confirmation)

### ADM-018.3: Navigation & Routing

**Files to modify:**

- `src/admin/full-version/src/navigation/vertical/apps-and-pages.ts` — add under System heading
- Router auto-discovers from pages directory (file-based routing)

**Acceptance:**

- [ ] Nav item: "Explanation Cache" under System section
- [ ] Icon: `tabler-database-cog`
- [ ] CASL subject: `System`, action: `read`
