# WEB-002: SignalR Client — Types, Envelope, Commands, Events, Reconnection

**Priority:** P0 — real-time communication layer for all interactive features
**Blocked by:** WEB-001 (scaffold)
**Estimated effort:** 3 days
**Contract:** `contracts/frontend/signalr-messages.ts`

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule. `throw UnimplementedError`, `// TODO: implement`, empty bodies, and mock returns are FORBIDDEN in source code. If you cannot implement it fully, file a blocking dependency instead.

## Context
SignalR over WebSocket is the real-time channel between the React PWA and the ASP.NET Core backend. Every client command (StartSession, SubmitAnswer, etc.) and server event (QuestionPresented, AnswerEvaluated, etc.) flows through a typed `MessageEnvelope` with a `type` discriminator, `correlationId` for request-response tracking, and `direction` tag. The client must handle reconnection with exponential backoff and jitter for mobile network conditions.

## Subtasks

### WEB-002.1: Type Definitions & Envelope
**Files:**
- `src/web/src/types/signalr-messages.ts` — copy of contract types (or re-export from shared)
- `src/web/src/types/signalr-enums.ts` — MethodologyType, ErrorType, QuestionFormat, MasteryStatus
- `src/web/src/services/signalr/envelope.ts` — envelope creation helpers

**Acceptance:**
- [ ] `MessageEnvelope<T, P>` generic type with `type`, `direction`, `correlationId`, `timestamp`, `payload`
- [ ] 8 MethodologyType values: `socratic`, `spaced-repetition`, `project-based`, `blooms-progression`, `feynman`, `worked-example`, `analogy`, `retrieval-practice`
- [ ] 6 ErrorType values: `conceptual-misunderstanding`, `computational-error`, `notation-error`, `incomplete-reasoning`, `off-topic`, `partial-understanding`
- [ ] 5 QuestionFormat values: `free-text`, `multiple-choice`, `numeric`, `proof`, `graph-sketch`
- [ ] 4 MasteryStatus values: `not-started`, `in-progress`, `mastered`, `decaying`
- [ ] 9 ClientCommand types: `StartSession`, `SubmitAnswer`, `EndSession`, `RequestHint`, `SkipQuestion`, `AddAnnotation`, `SwitchApproach`, `RequestNextConcept`, `UpdatePreferences`
- [ ] 12 ServerEvent types: `SessionStarted`, `QuestionPresented`, `AnswerEvaluated`, `MasteryUpdated`, `MethodologySwitched`, `SessionSummary`, `XpAwarded`, `StreakUpdated`, `KnowledgeGraphUpdated`, `CognitiveLoadWarning`, `HintDelivered`, `StagnationDetected`, `Error`
- [ ] `createEnvelope(type, payload)` helper generates UUIDv7 `correlationId` and ISO 8601 `timestamp`
- [ ] `SignalRErrorCode` union: `SESSION_NOT_FOUND`, `SESSION_ALREADY_ACTIVE`, `QUESTION_EXPIRED`, `RATE_LIMITED`, `UNAUTHORIZED`, `INTERNAL_ERROR`, `METHODOLOGY_UNAVAILABLE`, `CONCEPT_NOT_ACCESSIBLE`, `SYNC_IN_PROGRESS`

**Test:**
```typescript
import { createEnvelope } from '@/services/signalr/envelope';
import type { StartSessionPayload } from '@/types/signalr-messages';

test('createEnvelope generates valid envelope', () => {
  const envelope = createEnvelope<'StartSession', StartSessionPayload>('StartSession', {
    subjectId: 'math',
    conceptId: null,
    device: { platform: 'web', screenWidth: 1920, screenHeight: 1080, locale: 'he-IL' },
  });

  expect(envelope.type).toBe('StartSession');
  expect(envelope.direction).toBe('command');
  expect(envelope.correlationId).toMatch(/^[0-9a-f-]{36}$/);
  expect(new Date(envelope.timestamp).getTime()).not.toBeNaN();
  expect(envelope.payload.subjectId).toBe('math');
});

test('envelope types are exhaustive', () => {
  // TypeScript compiler verifies exhaustive switch
  const handleEvent = (event: ServerEvent) => {
    switch (event.type) {
      case 'SessionStarted': return;
      case 'QuestionPresented': return;
      case 'AnswerEvaluated': return;
      case 'MasteryUpdated': return;
      case 'MethodologySwitched': return;
      case 'SessionSummary': return;
      case 'XpAwarded': return;
      case 'StreakUpdated': return;
      case 'KnowledgeGraphUpdated': return;
      case 'CognitiveLoadWarning': return;
      case 'HintDelivered': return;
      case 'StagnationDetected': return;
      case 'Error': return;
      default: {
        const _exhaustive: never = event;
        return _exhaustive;
      }
    }
  };
  expect(handleEvent).toBeDefined();
});
```

---

### WEB-002.2: Hub Proxy Implementation
**Files:**
- `src/web/src/services/signalr/hub-proxy.ts` — `CenaHubProxy` implementation
- `src/web/src/services/signalr/index.ts` — public API

**Acceptance:**
- [ ] Implements `CenaHubProxy` interface from contract
- [ ] Uses `@microsoft/signalr` HubConnectionBuilder
- [ ] `start(accessToken)` connects with JWT bearer auth via `accessTokenFactory`
- [ ] `invoke(type, payload)` sends command envelope to server
- [ ] `on(type, handler)` registers typed event handler, returns unsubscribe function
- [ ] `onConnectionChange(handler)` emits `ConnectionEvent` on state transitions
- [ ] Connection URL from environment variable `VITE_SIGNALR_URL`
- [ ] JSON protocol (MessagePack negotiable but not required for v1)
- [ ] Hub method name matches the `type` field of the envelope
- [ ] Automatic correlation: `invoke` returns a promise that resolves when a matching `correlationId` event arrives (or timeout after 30s)

**Test:**
```typescript
import { createHubProxy } from '@/services/signalr/hub-proxy';

test('hub proxy starts with access token', async () => {
  const mockConnection = createMockConnection();
  const proxy = createHubProxy(mockConnection);

  await proxy.start('test-jwt-token');
  expect(mockConnection.start).toHaveBeenCalled();
});

test('hub proxy dispatches events to handlers', () => {
  const mockConnection = createMockConnection();
  const proxy = createHubProxy(mockConnection);
  const handler = vi.fn();

  proxy.on('QuestionPresented', handler);
  mockConnection.simulateEvent('QuestionPresented', {
    sessionId: 'sess-1', questionId: 'q-1', conceptId: 'c-1',
    conceptName: 'Addition', questionText: 'What is 2+2?',
    diagram: null, format: 'numeric', options: null,
    difficulty: 3, methodology: 'socratic', questionIndex: 0, isReview: false,
  });

  expect(handler).toHaveBeenCalledWith(expect.objectContaining({
    questionId: 'q-1',
  }));
});

test('hub proxy on() returns unsubscribe function', () => {
  const mockConnection = createMockConnection();
  const proxy = createHubProxy(mockConnection);
  const handler = vi.fn();

  const unsub = proxy.on('XpAwarded', handler);
  unsub();
  mockConnection.simulateEvent('XpAwarded', { amount: 10, source: 'correct-answer', totalXP: 100, level: 5, levelProgress: 0.5 });

  expect(handler).not.toHaveBeenCalled();
});
```

---

### WEB-002.3: Reconnection Strategy
**Files:**
- `src/web/src/services/signalr/reconnection.ts` — exponential backoff with jitter
- `src/web/src/services/signalr/hub-proxy.ts` — reconnection integration

**Acceptance:**
- [ ] Default strategy: `maxRetries: 8`, `baseDelayMs: 1000`, `maxDelayMs: 30000`, `backoffMultiplier: 2.0`, `jitterFactor: 0.3`, `syncOnReconnect: true`
- [ ] Connection state machine: `Disconnected -> Connecting -> Connected -> Reconnecting -> Connected` (or back to Disconnected after max retries)
- [ ] Delay calculation: `delay = min(maxDelayMs, baseDelayMs * backoffMultiplier^attempt) * (1 +/- jitter)`
- [ ] Jitter: random factor in range `[1 - jitterFactor, 1 + jitterFactor]`
- [ ] On successful reconnect: trigger offline sync if `syncOnReconnect: true`
- [ ] On max retries exceeded: emit `ConnectionEvent` with state `Disconnected`, error message
- [ ] `ConnectionEvent` includes `retryAttempt`, `nextRetryMs`, `error`
- [ ] Visible to user: offline banner shows reconnection status and retry countdown

**Test:**
```typescript
import { computeDelay, DEFAULT_RECONNECTION_STRATEGY } from '@/services/signalr/reconnection';

test('delay increases exponentially', () => {
  const delays = [0, 1, 2, 3, 4].map(attempt =>
    computeDelay(attempt, { ...DEFAULT_RECONNECTION_STRATEGY, jitterFactor: 0 })
  );
  // 1000, 2000, 4000, 8000, 16000
  expect(delays[0]).toBe(1000);
  expect(delays[1]).toBe(2000);
  expect(delays[2]).toBe(4000);
});

test('delay capped at maxDelayMs', () => {
  const delay = computeDelay(10, { ...DEFAULT_RECONNECTION_STRATEGY, jitterFactor: 0 });
  expect(delay).toBeLessThanOrEqual(30000);
});

test('jitter adds randomness', () => {
  const delays = new Set(
    Array.from({ length: 20 }, () => computeDelay(2, DEFAULT_RECONNECTION_STRATEGY))
  );
  // With jitter, delays should not all be identical
  expect(delays.size).toBeGreaterThan(1);
});

test('connection state transitions are valid', () => {
  const proxy = createTestHubProxy();
  const events: ConnectionEvent[] = [];
  proxy.onConnectionChange(e => events.push(e));

  proxy.simulateDisconnect();
  proxy.simulateReconnectAttempt(1);
  proxy.simulateReconnectSuccess();

  expect(events.map(e => e.state)).toEqual([
    'reconnecting', 'reconnecting', 'connected'
  ]);
});
```

**Edge cases:**
- Server returns 401 during reconnection -> do NOT retry; redirect to login
- Network restored but server is down -> retry continues with backoff
- User navigates away during reconnection -> cancel reconnection, clean up timers
- Multiple tabs open -> each has its own connection (no shared worker in v1)

---

## Integration Test

```typescript
test('signalr full flow: connect, send command, receive event', async () => {
  const proxy = createHubProxy(createMockConnection());
  await proxy.start('test-token');

  const eventPromise = new Promise<AnswerEvaluatedPayload>((resolve) => {
    proxy.on('AnswerEvaluated', resolve);
  });

  await proxy.invoke('SubmitAnswer', {
    sessionId: 'sess-1',
    questionId: 'q-1',
    answer: '4',
    responseTimeMs: 2500,
    confidence: null,
    behavioralSignals: { backspaceCount: 0, answerChangeCount: 0 },
  });

  // Mock server responds
  proxy.simulateServerEvent('AnswerEvaluated', {
    sessionId: 'sess-1', questionId: 'q-1', correct: true, score: 1.0,
    explanation: 'Correct!', errorType: null, nextAction: 'next-question',
    updatedMastery: 0.72, xpEarned: 25,
  });

  const result = await eventPromise;
  expect(result.correct).toBe(true);
  expect(result.xpEarned).toBe(25);
});
```

## Rollback Criteria
- If `@microsoft/signalr` has issues on web: fall back to raw WebSocket with manual JSON framing
- If reconnection causes thundering herd: increase jitter factor to 0.5
- If MessagePack needed for bandwidth: add `@microsoft/signalr-protocol-msgpack`

## Definition of Done
- [ ] All 3 subtasks pass their tests
- [ ] `npm test -- --filter signalr` -> 0 failures
- [ ] All 9 command types and 13 server event types typed and handled
- [ ] Reconnection with exponential backoff verified by test
- [ ] Connection state machine transitions are correct
- [ ] No unhandled event types (exhaustive switch verified by TypeScript compiler)
- [ ] PR reviewed by frontend lead
