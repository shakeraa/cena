# WEB-004: Zustand State Management — 5 Slices, Selectors, Memoization

**Priority:** P0 — all UI components depend on the store
**Blocked by:** WEB-001 (scaffold), WEB-002 (SignalR types)
**Estimated effort:** 3 days
**Contract:** `contracts/frontend/state-contracts.ts`

---

## Context
The Cena web client uses Zustand with the slice pattern for state management. Five slices map to five domain concerns: Session, KnowledgeGraph, User, Offline, and ConnectionMeta. Each slice has its own state shape, actions, and selectors defined in the contract. The store uses Immer middleware for immutable updates and persist middleware (localStorage on web) for non-sensitive state.

## Subtasks

### WEB-004.1: Store Setup & Slice Infrastructure
**Files:**
- `src/web/src/store/index.ts` — combined store creation
- `src/web/src/store/createSessionSlice.ts` — session slice
- `src/web/src/store/createUserSlice.ts` — user slice
- `src/web/src/store/createKnowledgeGraphSlice.ts` — knowledge graph slice
- `src/web/src/store/createOfflineSlice.ts` — offline slice
- `src/web/src/store/createConnectionMetaSlice.ts` — connection meta slice

**Acceptance:**
- [ ] Combined store type: `CenaStore = SessionState & SessionActions & KnowledgeGraphState & KnowledgeGraphActions & UserState & UserActions & OfflineState & OfflineActions & ConnectionMetaState & ConnectionMetaActions`
- [ ] Created via `create<CenaStore>()(devtools(persist(immer(...), { name: 'cena-store' })))`
- [ ] Initial state factories match contract: `createInitialSessionState()`, `createInitialKnowledgeGraphState()`, `createInitialUserState()`, `createInitialOfflineState()`, `createInitialConnectionMetaState()`
- [ ] Session initial: `status: 'idle'`, `activeSessionId: null`, `fatigueScore: 0`, all counters at 0
- [ ] User initial: `isAuthenticated: false`, `locale: 'he-IL'`, `totalXP: 0`, `level: 1`
- [ ] Offline initial: `connectionState: 'disconnected'`, `isNetworkAvailable: true`, `syncStatus: 'idle'`
- [ ] Persist config: exclude `accessToken`, `refreshToken` (security-sensitive)
- [ ] DevTools: enabled in development only

**Test:**
```typescript
import { useStore } from '@/store';

test('store initializes with correct defaults', () => {
  const state = useStore.getState();

  // Session
  expect(state.status).toBe('idle');
  expect(state.activeSessionId).toBeNull();
  expect(state.fatigueScore).toBe(0);

  // User
  expect(state.isAuthenticated).toBe(false);
  expect(state.locale).toBe('he-IL');
  expect(state.totalXP).toBe(0);
  expect(state.level).toBe(1);

  // Offline
  expect(state.connectionState).toBe('disconnected');
  expect(state.syncStatus).toBe('idle');
  expect(state.queuedEventCount).toBe(0);
});

test('store type is CenaStore', () => {
  const state = useStore.getState();
  // TypeScript compiler ensures all slices are present
  const _session: string | null = state.activeSessionId;
  const _user: boolean = state.isAuthenticated;
  const _offline: number = state.queuedEventCount;
  const _kg: Record<string, KnowledgeGraphSnapshot> = state.graphs;
  const _conn: number | null = state.latencyMs;
  expect(true).toBe(true); // Type check is the test
});
```

---

### WEB-004.2: Session & User Actions
**Files:**
- `src/web/src/store/createSessionSlice.ts` — session actions
- `src/web/src/store/createUserSlice.ts` — user actions

**Acceptance:**
- [ ] Session actions from contract:
  - `startSession(subjectId, conceptId?)` -> sets `status: 'starting'`, invokes SignalR `StartSession`
  - `endSession(reason)` -> sets `status: 'ending'`, invokes SignalR `EndSession`
  - `submitAnswer(answer, confidence?)` -> sets `isEvaluating: true`, invokes SignalR `SubmitAnswer`
  - `requestHint(level)` -> invokes SignalR `RequestHint`
  - `skipQuestion(reason)` -> increments `questionsSkippedThisSession`, invokes SignalR `SkipQuestion`
  - `switchApproach(methodology?, reason?)` -> invokes SignalR `SwitchApproach`
  - `requestNextConcept(targetConceptId?)` -> invokes SignalR `RequestNextConcept`
  - `onQuestionPresented(payload)` -> sets `currentQuestion` with `presentedAt: Date.now()`, increments `questionIndex`
  - `onAnswerEvaluated(payload)` -> sets `isEvaluating: false`, `lastEvaluation`, updates `questionsAttempted`, `correctAnswers`, `xpEarnedThisSession`
  - `onSessionSummary(payload)` -> sets `status: 'ended'`, `summary`
  - `tick()` -> increments `elapsedSeconds`
  - `recalculateFatigue()` -> computes client-side fatigue from duration, error rate, response time
  - `reset()` -> returns to `createInitialSessionState()`
- [ ] User actions from contract:
  - `onXpAwarded(payload)` -> updates `totalXP`, `level`, `levelProgress`
  - `onStreakUpdated(payload)` -> updates `currentStreak`, `longestStreak`, `freezesRemaining`, `streakExpiresAt`
  - `logout()` -> clears all state, removes persisted storage

**Test:**
```typescript
test('startSession sets status to starting', async () => {
  const { startSession } = useStore.getState();
  await startSession('math');

  expect(useStore.getState().status).toBe('starting');
});

test('onQuestionPresented sets current question', () => {
  const { onQuestionPresented } = useStore.getState();

  onQuestionPresented({
    sessionId: 'sess-1', questionId: 'q-1', conceptId: 'c-1',
    conceptName: 'Addition', questionText: 'What is 2+2?',
    diagram: null, format: 'numeric', options: null,
    difficulty: 3, methodology: 'socratic', questionIndex: 0, isReview: false,
  });

  const state = useStore.getState();
  expect(state.currentQuestion?.questionId).toBe('q-1');
  expect(state.currentQuestion?.presentedAt).toBeGreaterThan(0);
});

test('onAnswerEvaluated updates session stats', () => {
  const store = useStore.getState();
  store.onQuestionPresented({ sessionId:'s',questionId:'q1',conceptId:'c1',conceptName:'Add',questionText:'2+2',diagram:null,format:'numeric',options:null,difficulty:3,methodology:'socratic',questionIndex:0,isReview:false });
  store.onAnswerEvaluated({
    sessionId: 's', questionId: 'q1', correct: true, score: 1.0,
    explanation: 'Correct', errorType: null, nextAction: 'next-question',
    updatedMastery: 0.72, xpEarned: 25,
  });

  const state = useStore.getState();
  expect(state.isEvaluating).toBe(false);
  expect(state.questionsAttempted).toBe(1);
  expect(state.correctAnswers).toBe(1);
  expect(state.xpEarnedThisSession).toBe(25);
});

test('onXpAwarded updates user gamification', () => {
  useStore.getState().onXpAwarded({
    amount: 50, source: 'correct-answer', totalXP: 500, level: 3, levelProgress: 0.4,
  });

  const state = useStore.getState();
  expect(state.totalXP).toBe(500);
  expect(state.level).toBe(3);
  expect(state.levelProgress).toBe(0.4);
});

test('logout clears all state', () => {
  useStore.getState().onXpAwarded({ amount: 100, source: 'correct-answer', totalXP: 100, level: 2, levelProgress: 0.1 });
  useStore.getState().logout();

  expect(useStore.getState().isAuthenticated).toBe(false);
  expect(useStore.getState().totalXP).toBe(0);
});
```

---

### WEB-004.3: Selectors with Memoization
**Files:**
- `src/web/src/store/selectors/sessionSelectors.ts`
- `src/web/src/store/selectors/knowledgeGraphSelectors.ts`
- `src/web/src/store/selectors/userSelectors.ts`
- `src/web/src/store/selectors/offlineSelectors.ts`

**Acceptance:**
- [ ] Selectors from contract:
  - `selectIsSessionActive`: `state.status === 'active'`
  - `selectCurrentQuestion`: `state.currentQuestion`
  - `selectSessionStats`: `{ questionsAttempted, correctAnswers, accuracy, xpEarned, elapsedSeconds }` — use `shallow` comparator
  - `selectFatigueScore`: `state.fatigueScore`
  - `selectActiveGraph`: `state.graphs[state.selectedSubjectId]` — memoize on `selectedSubjectId` change
  - `selectConceptNode(conceptId)`: parameterized selector factory
  - `selectReadyToLearn`: derived from `readyToLearn` + node lookup
  - `selectReviewDue`: derived from `reviewDue` + node lookup
  - `selectGamification`: `{ totalXP, level, levelProgress, currentStreak, longestStreak }` — use `shallow`
  - `selectFeatureFlag(flag)`: `state.featureFlags[flag]`
  - `selectOfflineBanner`: `{ isOffline, queuedEventCount, syncStatus, syncProgress }` — use `shallow`
  - `selectSyncNeeded`: `queuedEventCount > 0 && connectionState === 'connected'`
  - `selectConnectionIndicator`: `{ state, latencyMs, retryAttempt }`
- [ ] All multi-field selectors use Zustand `shallow` equality to prevent unnecessary re-renders
- [ ] Parameterized selectors use `useCallback` or selector factory pattern

**Test:**
```typescript
import { selectSessionStats, selectGamification, selectOfflineBanner } from '@/store/selectors';

test('selectSessionStats computes accuracy', () => {
  useStore.setState({ questionsAttempted: 10, correctAnswers: 7, xpEarnedThisSession: 150, elapsedSeconds: 300 });

  const stats = selectSessionStats(useStore.getState());
  expect(stats.accuracy).toBeCloseTo(0.7);
  expect(stats.xpEarned).toBe(150);
});

test('selectGamification returns 5 fields', () => {
  useStore.setState({ totalXP: 500, level: 3, levelProgress: 0.4, currentStreak: 5, longestStreak: 12 });

  const gam = selectGamification(useStore.getState());
  expect(gam).toEqual({ totalXP: 500, level: 3, levelProgress: 0.4, currentStreak: 5, longestStreak: 12 });
});

test('selectOfflineBanner reflects offline state', () => {
  useStore.setState({ isOfflineMode: true, queuedEventCount: 5, syncStatus: 'idle', syncProgress: null });

  const banner = selectOfflineBanner(useStore.getState());
  expect(banner.isOffline).toBe(true);
  expect(banner.queuedEventCount).toBe(5);
});

test('selectSyncNeeded is true when queued and connected', () => {
  useStore.setState({ queuedEventCount: 3, connectionState: 'connected' });
  expect(selectSyncNeeded(useStore.getState())).toBe(true);

  useStore.setState({ queuedEventCount: 0 });
  expect(selectSyncNeeded(useStore.getState())).toBe(false);
});
```

**Edge cases:**
- Store hydration from localStorage on app open -> selectors should handle stale state
- `shallow` comparison fails for deeply nested objects -> keep selector output flat
- Feature flag missing -> `selectFeatureFlag` returns `undefined`, not error
- Parameterized selector called with invalid conceptId -> returns `null`

---

## Integration Test

```typescript
test('store full session lifecycle', () => {
  const store = useStore;

  // 1. Start session
  store.getState().startSession('math');
  expect(store.getState().status).toBe('starting');

  // 2. Server confirms
  store.getState().onQuestionPresented({
    sessionId: 'sess-1', questionId: 'q-1', conceptId: 'c-1',
    conceptName: 'Addition', questionText: '2+2?', diagram: null,
    format: 'numeric', options: null, difficulty: 2,
    methodology: 'socratic', questionIndex: 0, isReview: false,
  });
  expect(selectIsSessionActive(store.getState())).toBe(true);

  // 3. Answer evaluated
  store.getState().onAnswerEvaluated({
    sessionId: 'sess-1', questionId: 'q-1', correct: true, score: 1.0,
    explanation: 'Yes!', errorType: null, nextAction: 'next-question',
    updatedMastery: 0.72, xpEarned: 25,
  });
  expect(selectSessionStats(store.getState()).accuracy).toBe(1.0);

  // 4. XP awarded
  store.getState().onXpAwarded({ amount: 25, source: 'correct-answer', totalXP: 225, level: 2, levelProgress: 0.3 });
  expect(selectGamification(store.getState()).totalXP).toBe(225);

  // 5. Session ends
  store.getState().onSessionSummary({
    sessionId: 'sess-1', durationSeconds: 300, questionsAttempted: 1,
    correctAnswers: 1, conceptsTouched: [], xpEarned: 25,
    streakMaintained: true, reason: 'completed',
  });
  expect(store.getState().status).toBe('ended');
});
```

## Rollback Criteria
- If Zustand has performance issues: switch to Jotai (atomic state) or Redux Toolkit
- If Immer causes memory issues: use plain Zustand without Immer
- If persist middleware conflicts with auth: exclude entire user slice from persistence

## Definition of Done
- [ ] All 3 subtasks pass their tests
- [ ] `npm test -- --filter store` -> 0 failures
- [ ] All 5 slices have correct initial state matching contract
- [ ] All actions mutate state correctly
- [ ] All selectors return correct derived state
- [ ] `shallow` equality prevents unnecessary re-renders (verified by render count test)
- [ ] Sensitive fields (tokens) excluded from persistence
- [ ] PR reviewed by frontend lead
