# WEB-003: REST API Client — Typed Fetch, Auth, Caching

**Priority:** P1 — teacher and parent dashboards depend on API queries
**Blocked by:** WEB-001 (scaffold)
**Estimated effort:** 2 days
**Contract:** `contracts/backend/kg-access-control.md` (role-based endpoints)

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule. `throw UnimplementedError`, `// TODO: implement`, empty bodies, and mock returns are FORBIDDEN in source code. If you cannot implement it fully, file a blocking dependency instead.

## Context
The REST API serves the read side (CQRS) backed by Marten async projections. Teacher dashboards query class overviews and knowledge gap analysis; parent views query weekly reports and risk alerts; students query their knowledge graph and session history. A typed fetch client handles auth, caching, and error handling. Real-time updates come through SignalR (WEB-002), not polling.

**Why REST instead of GraphQL:** The Marten projections already produce pre-shaped read models (`TeacherDashboardView`, `ParentProgressView`). Each role has 3-5 fixed endpoints with well-defined response shapes. GraphQL would add schema maintenance, DataLoader boilerplate, and N+1 risks with no benefit — the views are already shaped by the projections. REST endpoints are cacheable (HTTP GET), trivially secured with `[Authorize]` middleware, and require no additional dependencies.

## Subtasks

### WEB-003.1: API Client Setup with Auth
**Files:**
- `src/web/src/services/api/client.ts` — typed fetch wrapper
- `src/web/src/services/api/auth.ts` — JWT auth header injection + refresh
- `src/web/src/services/api/errors.ts` — error handling and classification
- `src/web/src/services/api/types.ts` — response DTOs (mirrors Marten projections)

**Acceptance:**
- [ ] Typed fetch wrapper around `fetch()` with generic `get<T>(url): Promise<T>`
- [ ] Base URL from `VITE_API_URL` environment variable
- [ ] Auth: injects `Authorization: Bearer <token>` from Zustand user store
- [ ] Token refresh: on 401 response, call `refreshTokens()` and retry the request once
- [ ] Error classification: `ApiError` with `status`, `code`, `message`, `isRetryable`
- [ ] Response types mirror Marten projection DTOs — no transformation layer needed
- [ ] HTTP caching: `Cache-Control` and `ETag` headers honored by browser
- [ ] Request deduplication: concurrent identical GETs share a single in-flight request

**Test:**
```typescript
import { createApiClient } from '@/services/api/client';

test('api client injects auth header', async () => {
  const mockFetch = vi.fn().mockResolvedValue({
    ok: true,
    json: () => Promise.resolve({ id: 'class-1', name: 'Math 7A' }),
  });

  const client = createApiClient({
    getAccessToken: () => 'test-jwt',
    fetch: mockFetch,
  });

  await client.get('/api/teacher/class/class-1/overview');

  expect(mockFetch).toHaveBeenCalledWith(
    expect.stringContaining('/api/teacher/class/class-1/overview'),
    expect.objectContaining({
      headers: expect.objectContaining({
        Authorization: 'Bearer test-jwt',
      }),
    }),
  );
});

test('api client retries on 401', async () => {
  let callCount = 0;
  const mockFetch = vi.fn().mockImplementation(() => {
    callCount++;
    if (callCount === 1) {
      return Promise.resolve({ status: 401, ok: false, json: () => Promise.resolve({ code: 'UNAUTHORIZED' }) });
    }
    return Promise.resolve({ ok: true, json: () => Promise.resolve({ id: 'class-1' }) });
  });
  const refreshTokens = vi.fn().mockResolvedValue(undefined);

  const client = createApiClient({
    getAccessToken: () => 'refreshed-jwt',
    fetch: mockFetch,
    refreshTokens,
  });

  await client.get('/api/teacher/class/class-1/overview');
  expect(refreshTokens).toHaveBeenCalledTimes(1);
  expect(callCount).toBe(2);
});

test('api client deduplicates concurrent requests', async () => {
  let callCount = 0;
  const mockFetch = vi.fn().mockImplementation(() => {
    callCount++;
    return Promise.resolve({ ok: true, json: () => Promise.resolve({ id: 'class-1' }) });
  });

  const client = createApiClient({ getAccessToken: () => 'jwt', fetch: mockFetch });

  // Two concurrent calls to same URL
  const [r1, r2] = await Promise.all([
    client.get('/api/teacher/class/class-1/overview'),
    client.get('/api/teacher/class/class-1/overview'),
  ]);

  expect(callCount).toBe(1); // Only one fetch
  expect(r1).toEqual(r2);
});
```

---

### WEB-003.2: Endpoint Hooks — Teacher, Parent, Student
**Files:**
- `src/web/src/hooks/api/useClassOverview.ts` — teacher: class overview
- `src/web/src/hooks/api/useKnowledgeGaps.ts` — teacher: gap heatmap data
- `src/web/src/hooks/api/useAssignmentCompletion.ts` — teacher: assignment tracking
- `src/web/src/hooks/api/useChildProgress.ts` — parent: child progress
- `src/web/src/hooks/api/useWeeklyReport.ts` — parent: weekly report
- `src/web/src/hooks/api/useRiskAlerts.ts` — parent: risk alerts
- `src/web/src/hooks/api/useStudentGraph.ts` — student: knowledge graph

**Acceptance:**
- [ ] Teacher endpoints:
  - `GET /api/teacher/classes` → `ClassRoom[]`
  - `GET /api/teacher/class/:classRoomId/overview` → `ClassOverviewDto`
  - `GET /api/teacher/class/:classRoomId/gaps?threshold=0.85&minAttempts=3` → `ConceptGap[]`
  - `GET /api/teacher/class/:classRoomId/assignments?conceptIds=c1,c2` → `AssignmentCompletion[]`
- [ ] Parent endpoints:
  - `GET /api/parent/children` → `ChildSummary[]`
  - `GET /api/parent/child/:childId/progress` → `ChildProgressDto`
  - `GET /api/parent/child/:childId/weekly?weekStart=2026-03-16` → `WeeklyReportDto`
  - `GET /api/parent/child/:childId/alerts?includeAcknowledged=false` → `RiskAlert[]`
  - `POST /api/parent/child/:childId/alerts/:alertId/acknowledge` → `RiskAlert`
- [ ] Student endpoints:
  - `GET /api/student/me/profile` → `StudentProfileDto`
  - `GET /api/student/me/graph/:subjectId` → `KnowledgeGraphDto`
  - `GET /api/student/me/sessions?cursor=...&limit=20` → `PaginatedResponse<SessionSummary>`
  - `GET /api/student/me/streak` → `StreakDto`
- [ ] All hooks return `{ data, loading, error, refetch }`
- [ ] Hooks use `useSyncExternalStore` or `useEffect` + state (not a heavy library)
- [ ] Auth: requests fail gracefully with typed `ApiError` if role does not match
- [ ] Pagination: cursor-based for session history, offset for alerts

**Test:**
```typescript
import { renderHook, waitFor } from '@testing-library/react';
import { useClassOverview } from '@/hooks/api/useClassOverview';

test('useClassOverview returns class data', async () => {
  const mockData = {
    id: 'class-1', name: 'Math 7A', studentCount: 30,
    averageMastery: 0.65, averageStreak: 4.2,
    students: [],
    topKnowledgeGaps: [],
    inactiveStudents: [],
  };
  const { result } = renderHook(() => useClassOverview('class-1'), {
    wrapper: createApiWrapper(mockData),
  });

  await waitFor(() => expect(result.current.loading).toBe(false));
  expect(result.current.data?.name).toBe('Math 7A');
  expect(result.current.data?.averageMastery).toBe(0.65);
});

test('useWeeklyReport returns report for child', async () => {
  const mockData = {
    studentId: 'child-1', studentName: 'Test Child',
    weekStart: '2026-03-16T00:00:00Z', weekEnd: '2026-03-22T23:59:59Z',
    totalTimeMinutes: 120, sessionsCount: 8, conceptsMastered: 3,
    xpEarned: 450, streakDays: 7, accuracyTrend: 0.05,
    subjectBreakdown: [], encouragementMessage: 'Great week!',
  };
  const { result } = renderHook(() => useWeeklyReport('child-1'), {
    wrapper: createApiWrapper(mockData),
  });

  await waitFor(() => expect(result.current.loading).toBe(false));
  expect(result.current.data?.conceptsMastered).toBe(3);
});

test('useRiskAlerts returns sorted alerts', async () => {
  const mockAlerts = [
    { id: 'a1', severity: 'INFO', message: 'Streak warning' },
    { id: 'a2', severity: 'CRITICAL', message: 'Inactive 7 days' },
  ];
  const { result } = renderHook(() => useRiskAlerts('child-1'), {
    wrapper: createApiWrapper(mockAlerts),
  });

  await waitFor(() => expect(result.current.loading).toBe(false));
  expect(result.current.data).toHaveLength(2);
});
```

---

## Backend Endpoints (ASP.NET Core)

The API layer is a thin ASP.NET Core controller that reads from Marten projections:

```csharp
// Cena.Api/Controllers/TeacherController.cs
[ApiController]
[Route("api/teacher")]
[Authorize(Roles = "TEACHER")]
public class TeacherController(IQuerySession session) : ControllerBase
{
    [HttpGet("class/{classRoomId}/overview")]
    public async Task<ActionResult<ClassOverviewDto>> GetOverview(string classRoomId)
    {
        var view = await session.LoadAsync<TeacherDashboardView>(classRoomId);
        if (view is null) return NotFound();
        // Verify teacher is assigned to this class
        var teacherId = User.FindFirst("sub")!.Value;
        if (!view.AssignedTeacherIds.Contains(teacherId)) return Forbid();
        return Ok(view.ToDto());
    }
}

[ApiController]
[Route("api/parent")]
[Authorize(Roles = "PARENT")]
public class ParentController(IQuerySession session) : ControllerBase
{
    [HttpGet("child/{childId}/progress")]
    public async Task<ActionResult<ChildProgressDto>> GetProgress(string childId)
    {
        var parentId = User.FindFirst("sub")!.Value;
        var parentClaims = User.FindFirst("student_ids")!.Value.Split(',');
        if (!parentClaims.Contains(childId)) return Forbid();
        var view = await session.LoadAsync<ParentProgressView>(childId);
        return view is null ? NotFound() : Ok(view.ToDto());
    }
}
```

These are 5-6 controller actions total. No schema, no resolvers, no DataLoader.

## Integration Test

```typescript
test('api client full flow: fetch + refresh + error', async () => {
  const client = createTestApiClient();

  // 1. Fetch class overview
  const overview = await client.get<ClassOverviewDto>('/api/teacher/class/class-1/overview');
  expect(overview.name).toBeTruthy();

  // 2. Acknowledge risk alert (POST)
  const alert = await client.post<RiskAlert>('/api/parent/child/c1/alerts/a1/acknowledge');
  expect(alert.acknowledged).toBe(true);

  // 3. Unauthorized request returns typed error
  await expect(
    client.get('/api/teacher/class/class-1/overview', { token: 'expired' })
  ).rejects.toMatchObject({ status: 401, code: 'UNAUTHORIZED' });
});
```

## Rollback Criteria
- If fetch wrapper is insufficient: adopt `ky` (tiny, typed) or `ofetch` (Nuxt's fetch wrapper)
- If request deduplication causes stale data: remove dedup, rely on HTTP caching only
- If REST endpoints prove too rigid: consider JSON:API or tRPC (still not GraphQL)

## Definition of Done
- [ ] All 2 subtasks pass their tests
- [ ] `npm test -- --filter api` -> 0 failures
- [ ] All endpoint hooks typed with response DTOs
- [ ] Auth header injected on all requests
- [ ] 401 triggers token refresh and retry
- [ ] Request deduplication works for concurrent GETs
- [ ] No GraphQL dependencies in package.json
- [ ] PR reviewed by frontend lead
