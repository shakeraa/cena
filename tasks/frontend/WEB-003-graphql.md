# WEB-003: GraphQL Client — Apollo, Auth, Subscriptions

**Priority:** P1 — teacher and parent dashboards depend on GraphQL queries
**Blocked by:** WEB-001 (scaffold)
**Estimated effort:** 3 days
**Contract:** `contracts/frontend/graphql-schema.graphql`

---

## Context
The GraphQL API serves the read side (CQRS) backed by Marten projections. Teacher dashboards query class overviews and knowledge gap analysis; parent views query weekly reports and risk alerts; students query their knowledge graph and session history. Apollo Client handles caching, auth, and subscriptions (real-time class mastery updates via WebSocket).

## Subtasks

### WEB-003.1: Apollo Client Setup with Auth
**Files:**
- `src/web/src/services/graphql/client.ts` — Apollo Client configuration
- `src/web/src/services/graphql/auth-link.ts` — JWT auth header injection
- `src/web/src/services/graphql/error-link.ts` — error handling link
- `src/web/src/services/graphql/types.ts` — generated types from schema

**Acceptance:**
- [ ] Apollo Client with `InMemoryCache`
- [ ] HTTP link pointing to `VITE_GRAPHQL_URL` environment variable
- [ ] Auth link: injects `Authorization: Bearer <token>` from Zustand user store
- [ ] Token refresh: on 401 response, call `refreshTokens()` and retry the query once
- [ ] Error link: logs GraphQL errors, categorizes as user-facing vs internal
- [ ] Cache policies: `Student` by `id`, `Concept` by `id`, `ClassRoom` by `id`
- [ ] Relay-style cursor pagination: `fetchMore` with cursor merge in cache
- [ ] Type generation: `graphql-codegen` from `contracts/frontend/graphql-schema.graphql`
- [ ] Custom scalars: `DateTime` -> `string`, `UUID` -> `string`, `JSON` -> `Record<string, unknown>`

**Test:**
```typescript
import { createApolloClient } from '@/services/graphql/client';

test('apollo client injects auth header', async () => {
  const mockFetch = vi.fn().mockResolvedValue({
    ok: true,
    json: () => Promise.resolve({ data: { myProfile: { id: 's1' } } }),
  });

  const client = createApolloClient({
    getAccessToken: () => 'test-jwt',
    fetch: mockFetch,
  });

  await client.query({ query: MY_PROFILE_QUERY });

  expect(mockFetch).toHaveBeenCalledWith(
    expect.any(String),
    expect.objectContaining({
      headers: expect.objectContaining({
        Authorization: 'Bearer test-jwt',
      }),
    }),
  );
});

test('apollo client retries on 401', async () => {
  let callCount = 0;
  const mockFetch = vi.fn().mockImplementation(() => {
    callCount++;
    if (callCount === 1) {
      return Promise.resolve({ status: 401, ok: false, json: () => Promise.resolve({ errors: [{ message: 'Unauthorized' }] }) });
    }
    return Promise.resolve({ ok: true, json: () => Promise.resolve({ data: { myProfile: { id: 's1' } } }) });
  });
  const refreshTokens = vi.fn().mockResolvedValue(undefined);

  const client = createApolloClient({
    getAccessToken: () => 'refreshed-jwt',
    fetch: mockFetch,
    refreshTokens,
  });

  await client.query({ query: MY_PROFILE_QUERY });
  expect(refreshTokens).toHaveBeenCalledTimes(1);
  expect(callCount).toBe(2);
});
```

---

### WEB-003.2: Query Hooks — Teacher & Parent
**Files:**
- `src/web/src/services/graphql/queries/teacher.ts` — teacher query documents
- `src/web/src/services/graphql/queries/parent.ts` — parent query documents
- `src/web/src/services/graphql/queries/student.ts` — student query documents
- `src/web/src/hooks/useClassOverview.ts` — teacher hook
- `src/web/src/hooks/useWeeklyReport.ts` — parent hook
- `src/web/src/hooks/useKnowledgeGraph.ts` — student hook

**Acceptance:**
- [ ] Teacher queries: `classOverview(classRoomId)`, `knowledgeGapAnalysis(classRoomId, filter)`, `assignmentCompletion(classRoomId, conceptIds)`, `myClassrooms`
- [ ] Parent queries: `childProgress(childId)`, `weeklyReport(childId, dateRange)`, `riskAlerts(childId, first, after, includeAcknowledged)`, `myChildren`
- [ ] Student queries: `myProfile`, `myKnowledgeGraph(subjectId)`, `mySessionHistory(first, after, filter, sortBy)`, `myStreak`
- [ ] All list queries use Relay cursor pagination with `fetchMore`
- [ ] Hooks return `{ data, loading, error, refetch }`
- [ ] Auth: queries fail gracefully with "Unauthorized" if role does not match (teacher-only queries return error for student role)

**Test:**
```typescript
import { renderHook, waitFor } from '@testing-library/react';
import { useClassOverview } from '@/hooks/useClassOverview';

test('useClassOverview returns class data', async () => {
  const mockData = {
    classOverview: {
      id: 'class-1', name: 'Math 7A', studentCount: 30,
      averageMastery: 0.65, averageStreak: 4.2,
      students: { edges: [], pageInfo: { hasNextPage: false, endCursor: null } },
      topKnowledgeGaps: [],
      inactiveStudents: [],
    },
  };
  const { result } = renderHook(() => useClassOverview('class-1'), {
    wrapper: createApolloWrapper(mockData),
  });

  await waitFor(() => expect(result.current.loading).toBe(false));
  expect(result.current.data?.classOverview.name).toBe('Math 7A');
  expect(result.current.data?.classOverview.averageMastery).toBe(0.65);
});

test('useWeeklyReport returns report for child', async () => {
  const mockData = {
    weeklyReport: {
      studentId: 'child-1', studentName: 'Test Child',
      weekStart: '2026-03-16T00:00:00Z', weekEnd: '2026-03-22T23:59:59Z',
      totalTimeMinutes: 120, sessionsCount: 8, conceptsMastered: 3,
      xpEarned: 450, streakDays: 7, accuracyTrend: 0.05,
      subjectBreakdown: [], encouragementMessage: 'Great week!',
    },
  };
  const { result } = renderHook(() => useWeeklyReport('child-1'), {
    wrapper: createApolloWrapper(mockData),
  });

  await waitFor(() => expect(result.current.loading).toBe(false));
  expect(result.current.data?.weeklyReport.conceptsMastered).toBe(3);
});
```

---

### WEB-003.3: Subscriptions — Real-Time Class Updates
**Files:**
- `src/web/src/services/graphql/subscriptions.ts` — subscription documents
- `src/web/src/hooks/useClassMasteryUpdates.ts` — teacher live feed
- `src/web/src/hooks/useRiskAlerts.ts` — parent live alerts

**Acceptance:**
- [ ] WebSocket link for subscriptions: `graphql-ws` protocol over `VITE_GRAPHQL_WS_URL`
- [ ] Subscriptions from schema:
  - `onKnowledgeGraphUpdate(studentId, subjectId)` -> `KnowledgeGraphUpdate` payload
  - `onSessionProgress(studentId)` -> `SessionProgressEvent` payload
  - `onClassMasteryUpdate(classRoomId)` -> `ClassMasteryUpdate` payload with `studentId`, `studentName`, `conceptId`, `conceptName`, `previousMastery`, `newMastery`, `justMastered`, `timestamp`
  - `onRiskAlert(childId)` -> `RiskAlert` payload with `id`, `studentId`, `detectedAt`, `severity` (INFO|WARNING|CRITICAL), `riskType`, `message`, `acknowledged`, `suggestedAction`
- [ ] Subscription hooks auto-unsubscribe on unmount
- [ ] Subscription auth: same JWT as queries
- [ ] Network error on subscription: reconnect with backoff

**Test:**
```typescript
import { renderHook, act } from '@testing-library/react';
import { useClassMasteryUpdates } from '@/hooks/useClassMasteryUpdates';

test('useClassMasteryUpdates receives live updates', async () => {
  const { result } = renderHook(() => useClassMasteryUpdates('class-1'), {
    wrapper: createSubscriptionWrapper(),
  });

  act(() => {
    simulateSubscriptionEvent('onClassMasteryUpdate', {
      classRoomId: 'class-1', studentId: 's1', studentName: 'Alice',
      conceptId: 'c1', conceptName: 'Addition',
      previousMastery: 0.6, newMastery: 0.88, justMastered: true,
      timestamp: new Date().toISOString(),
    });
  });

  expect(result.current.updates).toHaveLength(1);
  expect(result.current.updates[0].justMastered).toBe(true);
});

test('useRiskAlerts filters by severity', async () => {
  const { result } = renderHook(() => useRiskAlerts('child-1'), {
    wrapper: createSubscriptionWrapper(),
  });

  act(() => {
    simulateSubscriptionEvent('onRiskAlert', {
      id: 'alert-1', studentId: 'child-1',
      detectedAt: new Date().toISOString(),
      severity: 'CRITICAL', riskType: 'inactivity',
      message: 'No activity for 5 days', acknowledged: false,
      suggestedAction: 'Check in with your child',
    });
  });

  expect(result.current.alerts).toHaveLength(1);
  expect(result.current.alerts[0].severity).toBe('CRITICAL');
});
```

**Edge cases:**
- Token expires during subscription -> re-authenticate and re-subscribe
- Server restarts -> subscription disconnects; client reconnects with last cursor
- Large class (200 students) -> subscription updates may be high-frequency; debounce UI updates to 500ms

---

## Integration Test

```typescript
test('graphql full flow: query + mutation + subscription', async () => {
  const client = createTestApolloClient();

  // 1. Query class overview
  const { data: classData } = await client.query({ query: CLASS_OVERVIEW, variables: { classRoomId: 'class-1' } });
  expect(classData.classOverview.name).toBeTruthy();

  // 2. Mutation: acknowledge risk alert
  const { data: mutationData } = await client.mutate({
    mutation: ACKNOWLEDGE_RISK_ALERT,
    variables: { alertId: 'alert-1' },
  });
  expect(mutationData.acknowledgeRiskAlert.acknowledged).toBe(true);

  // 3. Subscription receives update
  const updates: ClassMasteryUpdate[] = [];
  const sub = client.subscribe({ query: ON_CLASS_MASTERY_UPDATE, variables: { classRoomId: 'class-1' } })
    .subscribe({ next: (result) => updates.push(result.data.onClassMasteryUpdate) });

  // Simulate server push
  await simulateServerPush('onClassMasteryUpdate', { classRoomId: 'class-1', studentId: 's1', justMastered: true });
  expect(updates).toHaveLength(1);

  sub.unsubscribe();
});
```

## Rollback Criteria
- If Apollo Client bundle is too large: switch to `urql` (smaller) or `graphql-request` (minimal)
- If subscriptions are unreliable: fall back to polling every 5 seconds for dashboard data
- If codegen breaks on schema change: manually type critical queries, defer full codegen

## Definition of Done
- [ ] All 3 subtasks pass their tests
- [ ] `npm test -- --filter graphql` -> 0 failures
- [ ] All query hooks typed with codegen output
- [ ] Auth header injected on all requests
- [ ] 401 triggers token refresh and retry
- [ ] Subscriptions connect and receive events
- [ ] Relay-style pagination works with fetchMore
- [ ] PR reviewed by frontend lead
