# 05 — Frontend Web Tasks (React PWA)

**Technology:** React 19, TypeScript, Zustand, REST API, SignalR
**Contract files:** `contracts/frontend/*.ts`, `contracts/backend/kg-access-control.md`
**Stage:** Parallel with mobile (Weeks 8-14)

---

## WEB-001: React PWA Scaffold
**Priority:** P1 | **Blocked by:** None
- [ ] Vite + React 19 + TypeScript
- [ ] PWA manifest, service worker, offline shell caching
- [ ] RTL support (Hebrew + Arabic) with `dir="rtl"` attribute
- [ ] **Test:** `npm run build && lighthouse --preset=pwa` scores > 90

## WEB-002: SignalR Client Types
**Priority:** P0 | **Blocked by:** WEB-001
- [ ] All types from `signalr-messages.ts` compile
- [ ] `MessageEnvelope<T,P>` discriminated union with exhaustive switch
- [ ] 9 commands, 12 events typed
- [ ] Auto-reconnection with exponential backoff

**Test:**
```typescript
// Type safety: discriminated union exhaustiveness
function handleMessage(msg: ServerEvent) {
  switch (msg.type) {
    case 'QuestionPresented': /* ... */ break;
    case 'AnswerEvaluated': /* ... */ break;
    // tsc --noEmit errors if any case is missing
  }
}

test('WebSocket reconnects with backoff', async () => {
  const ws = createMockWebSocket({ failFirstN: 3 });
  const client = new CenaHubProxy(ws);
  await client.connect();
  expect(ws.connectAttempts).toBe(4); // 3 failures + 1 success
  expect(ws.backoffDelays).toEqual([1000, 2000, 4000]); // exponential
});
```

## WEB-003: REST API Client
**Priority:** P1 | **Blocked by:** WEB-001
- [ ] Typed fetch wrapper with `get<T>(url)` and JWT auth injection
- [ ] Token refresh on 401 with single retry
- [ ] Teacher hooks: `useClassOverview`, `useKnowledgeGaps`, `useAssignmentCompletion`
- [ ] Parent hooks: `useChildProgress`, `useWeeklyReport`, `useRiskAlerts`
- [ ] Student hooks: `useStudentGraph`, `useStudentSessions`
- [ ] Request deduplication for concurrent identical GETs
- [ ] **Test:** Auth injection, 401 retry, typed responses verified

## WEB-004: Zustand State Management
**Priority:** P1 | **Blocked by:** WEB-002
- [ ] 5 state slices from `state-contracts.ts`: Session, KnowledgeGraph, User, Offline, Connection
- [ ] Selectors with memoization (shallow equality)
- [ ] `filteredGraphNodes` cached (not recomputed on pan/zoom)
- [ ] **Test:** Zustand store unit tests — state transitions match contract

## WEB-005: Offline Sync Client
**Priority:** P1 | **Blocked by:** WEB-004
- [ ] All types from `offline-sync-client.ts` implemented
- [ ] `IOfflineEventQueue` backed by IndexedDB (not SQLite on web)
- [ ] 6-step sync handshake with progress callback
- [ ] Clock skew detection (NTP-style)
- [ ] **Test:** Simulate offline → 10 events → reconnect → verify sync completes

## WEB-006: Teacher Dashboard
**Priority:** P2 | **Blocked by:** WEB-003
- [ ] Class overview: traffic light indicators per student (green/yellow/red)
- [ ] Knowledge gap analysis: concept heatmap across class
- [ ] Assignment completion tracking
- [ ] Real-time via SignalR `onClassMasteryUpdate` events
- [ ] **Test:** Mock 30-student class → verify heatmap renders correctly

## WEB-007: Parent Progress View
**Priority:** P2 | **Blocked by:** WEB-003
- [ ] Weekly report card (mastery gained, sessions completed, streaks)
- [ ] Risk alerts (7 days inactive, mastery declining)
- [ ] Child selection (multi-child support)
- [ ] **Test:** Mock parent with 2 children → verify both views render

## WEB-008: Knowledge Graph (Web)
**Priority:** P2 | **Blocked by:** WEB-003, WEB-004
- [ ] Canvas-based renderer (similar to mobile CustomPainter but HTML Canvas)
- [ ] Data from `GET /api/student/me/graph/:subjectId`, live updates via SignalR
- [ ] Viewport culling for 2000+ nodes
- [ ] Same mastery color palette as mobile
- [ ] Accessible: `aria-label` on interactive elements
- [ ] **Test:** Render 1000 nodes → verify < 16ms frame time
