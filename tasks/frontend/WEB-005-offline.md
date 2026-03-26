# WEB-005: Offline Sync — IndexedDB Queue, Sync Handshake

**Priority:** P1 — enables offline learning on PWA
**Blocked by:** WEB-001 (scaffold), WEB-002 (SignalR), WEB-004 (state)
**Estimated effort:** 3 days
**Contract:** `contracts/frontend/offline-sync-client.ts`

---

## Context
When the web PWA loses connectivity, the client buffers learning events (exercise attempts, annotations) into a durable IndexedDB queue. On reconnection, a sync handshake reconciles offline work with the server. The protocol has 5 steps: (1) SyncRequest with queued events, (2) SyncAck with server divergence, (3) client-side pre-classification, (4) SyncCommit with resolved events, (5) SyncResult with authoritative state. The web client uses IndexedDB (via `idb` library) instead of SQLite.

## Subtasks

### WEB-005.1: IndexedDB Event Queue
**Files:**
- `src/web/src/services/offline/event-queue.ts` — IOfflineEventQueue implementation for web
- `src/web/src/services/offline/db.ts` — IndexedDB schema and migrations

**Acceptance:**
- [ ] IndexedDB database: `cena-offline` with object store `events`
- [ ] Object store schema matches `QueuedEvent`: `id` (autoIncrement), `idempotencyKey` (indexed, unique), `studentId`, `deviceId`, `clientSeq`, `eventType`, `eventPayload` (JSON string), `offlineTimestamp`, `clockOffsetMs`, `status` ("pending"|"syncing"|"synced"|"failed"), `syncSessionId`, `serverResult`, `createdAt`, `syncedAt`
- [ ] `enqueue(eventType, payload)` -> assigns UUIDv7 `idempotencyKey`, auto-increments `clientSeq`, writes to IndexedDB
- [ ] `getPendingEvents()` -> returns events with `status = 'pending'`, ordered by `clientSeq`
- [ ] `getPendingCount()` -> returns count for UI badge
- [ ] `getPendingSummary()` -> returns `Record<string, number>` grouped by eventType
- [ ] `markSyncing(syncSessionId, idempotencyKeys)` -> batch update status
- [ ] `markSynced(syncSessionId, results)` -> batch update with server result
- [ ] `markFailed(syncSessionId)` -> reset to pending for retry
- [ ] `recoverInterruptedSync()` -> reset `syncing` entries to `pending` on app startup
- [ ] `purgeSynced(retentionDays)` -> delete synced events older than N days (default 7)
- [ ] `computeChecksum()` -> SHA-256 of pending event queue for integrity verification
- [ ] `healthCheck()` -> verify IndexedDB is accessible
- [ ] Event classification lookup: `EVENT_CLASSIFICATION_MAP` from contract (SessionStarted=unconditional, ExerciseAttempted=conditional, ConceptMastered=server-authoritative, etc.)

**Test:**
```typescript
import { createEventQueue } from '@/services/offline/event-queue';

test('enqueue adds event with idempotency key', async () => {
  const queue = await createEventQueue('student-1', 'device-1');
  const event = await queue.enqueue('ExerciseAttempted', { conceptId: 'c1', answer: '4' });

  expect(event.idempotencyKey).toMatch(/^[0-9a-f-]{36}$/);
  expect(event.eventType).toBe('ExerciseAttempted');
  expect(event.status).toBe('pending');
});

test('getPendingEvents returns ordered by clientSeq', async () => {
  const queue = await createEventQueue('student-2', 'device-1');
  await queue.enqueue('ExerciseAttempted', { answer: '1' });
  await queue.enqueue('AnnotationAdded', { text: 'note' });
  await queue.enqueue('ExerciseAttempted', { answer: '2' });

  const pending = await queue.getPendingEvents();
  expect(pending).toHaveLength(3);
  expect(pending[0].clientSeq).toBeLessThan(pending[1].clientSeq);
  expect(pending[1].clientSeq).toBeLessThan(pending[2].clientSeq);
});

test('markSyncing and markSynced lifecycle', async () => {
  const queue = await createEventQueue('student-3', 'device-1');
  const e1 = await queue.enqueue('ExerciseAttempted', { answer: '1' });

  await queue.markSyncing('sync-1', [e1.idempotencyKey]);
  let pending = await queue.getPendingEvents();
  expect(pending).toHaveLength(0); // Moved to syncing

  await queue.markSynced('sync-1', { [e1.idempotencyKey]: '{"accepted":true}' });
  pending = await queue.getPendingEvents();
  expect(pending).toHaveLength(0); // Now synced
});

test('markFailed resets to pending', async () => {
  const queue = await createEventQueue('student-4', 'device-1');
  const e1 = await queue.enqueue('ExerciseAttempted', { answer: '1' });
  await queue.markSyncing('sync-2', [e1.idempotencyKey]);
  await queue.markFailed('sync-2');

  const pending = await queue.getPendingEvents();
  expect(pending).toHaveLength(1);
  expect(pending[0].status).toBe('pending');
});

test('recoverInterruptedSync resets syncing to pending', async () => {
  const queue = await createEventQueue('student-5', 'device-1');
  const e1 = await queue.enqueue('ExerciseAttempted', { answer: '1' });
  await queue.markSyncing('sync-3', [e1.idempotencyKey]);

  const recovered = await queue.recoverInterruptedSync();
  expect(recovered).toBe(1);

  const pending = await queue.getPendingEvents();
  expect(pending).toHaveLength(1);
});
```

---

### WEB-005.2: Sync Manager & Handshake Protocol
**Files:**
- `src/web/src/services/offline/sync-manager.ts` — ISyncManager implementation
- `src/web/src/services/offline/event-classifier.ts` — IEventClassifier implementation

**Acceptance:**
- [ ] `sync()` executes the 5-step handshake:
  1. Gather pending events from queue
  2. Build `SyncRequest` with `studentId`, `deviceId`, `lastKnownServerSeq`, `clientClockOffsetMs`, `queuedEvents`, `queueChecksum`
  3. POST to `/api/v1/sync` -> receive `SyncAck` with `divergedEvents`, `activeMethodologyMap`, `syncSessionId`
  4. Classify events: for each event, compare against `activeMethodologyMap` and `divergedEvents` to determine `ClientResolution` (full_weight, reduced_weight, historical_only, server_decides)
  5. POST `SyncCommit` with resolved events -> receive `SyncResult`
- [ ] `cancel()` resets events to pending
- [ ] `onProgress(callback)` emits 0.0-1.0 progress
- [ ] Event classifier: unconditional events = full_weight always; conditional events = full_weight if context matches, reduced_weight if methodology changed; server-authoritative = server_decides always
- [ ] Sync config from contract: `asyncThreshold: 50`, `syncTimeoutMs: 30000`, `retry.maxRetries: 3`, `retry.baseDelayMs: 2000`
- [ ] `SyncError` with typed codes: `NETWORK_ERROR`, `CHECKSUM_MISMATCH`, `CONCURRENT_SYNC`, `RATE_LIMITED`, `UNAUTHORIZED`, `SERVER_ERROR`, `TIMEOUT`, `QUEUE_CORRUPTION`
- [ ] Only one sync at a time per student (`isSyncing` guard)

**Test:**
```typescript
import { createSyncManager } from '@/services/offline/sync-manager';

test('sync executes 5-step handshake', async () => {
  const queue = await createEventQueue('student-6', 'device-1');
  await queue.enqueue('ExerciseAttempted', { conceptId: 'c1', answer: '4' });

  const mockFetch = vi.fn()
    .mockResolvedValueOnce({ ok: true, json: () => Promise.resolve({
      currentServerSeq: 5, divergedEvents: [], activeMethodologyMap: { c1: 'socratic' },
      knowledgeGraphVersion: 'v1', syncSessionId: 'sync-1',
    })})
    .mockResolvedValueOnce({ ok: true, json: () => Promise.resolve({
      acceptedEvents: [{ idempotencyKey: 'key-1', classification: 'full_weight', serverSeq: 6 }],
      rejectedEvents: [], recalculatedState: { masteryOverlay: {}, xpDelta: 25, totalXP: 225,
        streakStatus: 'maintained', currentStreak: 5, currentServerSeq: 6 },
      notifications: [], outreachCorrections: [],
    })});

  const manager = createSyncManager(queue, { fetch: mockFetch });
  const result = await manager.sync();

  expect(result.acceptedEvents).toHaveLength(1);
  expect(mockFetch).toHaveBeenCalledTimes(2); // SyncRequest + SyncCommit
});

test('sync rejects concurrent calls', async () => {
  const queue = await createEventQueue('student-7', 'device-1');
  const manager = createSyncManager(queue, { fetch: vi.fn().mockImplementation(() => new Promise(() => {})) });

  manager.sync(); // Start first sync (hangs)
  await expect(manager.sync()).rejects.toThrow('CONCURRENT_SYNC');
});

test('event classifier handles methodology change', () => {
  const classifier = createEventClassifier();
  const event = createQueuedEvent('ExerciseAttempted', { conceptId: 'c1', methodology: 'socratic' });
  const syncAck = { activeMethodologyMap: { c1: 'feynman' } }; // Methodology changed

  const resolution = classifier.classify(event, syncAck);
  expect(resolution).toBe('reduced_weight');
});

test('event classifier unconditional always full_weight', () => {
  const classifier = createEventClassifier();
  const event = createQueuedEvent('SessionStarted', {});
  const syncAck = { activeMethodologyMap: {}, divergedEvents: [] };

  expect(classifier.classify(event, syncAck)).toBe('full_weight');
});

test('event classifier server-authoritative always server_decides', () => {
  const classifier = createEventClassifier();
  const event = createQueuedEvent('ConceptMastered', {});

  expect(classifier.classify(event, {} as any)).toBe('server_decides');
});
```

---

### WEB-005.3: Conflict Resolver & State Reconciliation
**Files:**
- `src/web/src/services/offline/conflict-resolver.ts` — IConflictResolver implementation
- `src/web/src/services/offline/clock-skew.ts` — IClockSkewDetector implementation

**Acceptance:**
- [ ] `apply(result: SyncResult)` updates Zustand store atomically:
  - Replace mastery overlay in KnowledgeGraph slice with `result.recalculatedState.masteryOverlay`
  - Update `totalXP` and streak in User slice
  - Update `lastKnownServerSeq` in Offline slice
  - Mark events as synced in IndexedDB
  - Compute severity: `silent` if all full_weight, `minor` if any reduced_weight, `significant` if server_recalculated
- [ ] Returns `ConflictResolutionOutcome` with `severity`, counts, `xpDelta`, `streakMaintained`
- [ ] Clock skew detector: NTP-style offset estimation, rolling window of 10 measurements, outlier rejection at 2 sigma
- [ ] `adjustTimestamp(clientTimestamp)` corrects for estimated skew
- [ ] `isExcessive()` returns true if offset > 8 hours (sanity bound)
- [ ] Default config: `windowSize: 10`, `maxOffsetMs: 28800000`, `outlierSigma: 2.0`

**Test:**
```typescript
import { createConflictResolver } from '@/services/offline/conflict-resolver';
import { createClockSkewDetector, DEFAULT_CLOCK_SKEW_CONFIG } from '@/services/offline/clock-skew';

test('conflict resolver updates store state', async () => {
  const resolver = createConflictResolver();
  const result: SyncResult = {
    acceptedEvents: [{ idempotencyKey: 'k1', classification: 'full_weight', serverSeq: 10 }],
    rejectedEvents: [],
    recalculatedState: {
      masteryOverlay: { c1: { masteryLevel: 0.88, predictedRecall: 0.92, status: 'mastered' } },
      xpDelta: 50, totalXP: 500, streakStatus: 'maintained', currentStreak: 7, currentServerSeq: 10,
    },
    notifications: [],
    outreachCorrections: [],
  };

  const outcome = await resolver.apply(result);
  expect(outcome.severity).toBe('silent');
  expect(outcome.xpDelta).toBe(50);
  expect(useStore.getState().totalXP).toBe(500);
});

test('conflict resolver detects significant conflicts', async () => {
  const resolver = createConflictResolver();
  const result: SyncResult = {
    acceptedEvents: [
      { idempotencyKey: 'k1', classification: 'full_weight', serverSeq: 10 },
      { idempotencyKey: 'k2', classification: 'reduced_weight', serverSeq: 11 },
    ],
    rejectedEvents: [{ idempotencyKey: 'k3', reason: 'concept_removed', message: 'Concept no longer exists' }],
    recalculatedState: {
      masteryOverlay: {}, xpDelta: 25, totalXP: 275,
      streakStatus: 'broken', currentStreak: 0, currentServerSeq: 11,
    },
    notifications: [{ type: 'methodology_changed', message: 'Your approach was updated', details: {} }],
    outreachCorrections: [],
  };

  const outcome = await resolver.apply(result);
  expect(outcome.severity).toBe('significant');
  expect(outcome.streakMaintained).toBe(false);
});

test('clock skew detector computes offset', () => {
  const detector = createClockSkewDetector(DEFAULT_CLOCK_SKEW_CONFIG);

  // Server is 500ms ahead of client
  detector.recordMeasurement('2026-03-01T12:00:00.500Z', 1709294400000, 1709294400100);
  expect(Math.abs(detector.currentOffsetMs)).toBeLessThan(1000);
});

test('clock skew excessive returns true for large offset', () => {
  const detector = createClockSkewDetector({ ...DEFAULT_CLOCK_SKEW_CONFIG, maxOffsetMs: 1000 });
  // Force large offset
  detector.recordMeasurement('2026-03-01T12:00:10.000Z', 1000, 1100);
  expect(detector.isExcessive()).toBe(true);
});
```

**Edge cases:**
- IndexedDB quota exceeded (5MB default) -> purge oldest synced events, warn user
- Sync interrupted mid-commit -> `recoverInterruptedSync` resets on next app open
- Server returns 202 (async processing for >50 events) -> poll for result
- Clock skew > 8 hours -> flag timestamps, use server-receive time
- Multiple devices syncing for same student -> idempotency keys prevent duplicates

---

## Integration Test

```typescript
test('offline sync full lifecycle', async () => {
  const queue = await createEventQueue('offline-e2e', 'device-1');

  // 1. Go offline, queue events
  for (let i = 0; i < 5; i++) {
    await queue.enqueue('ExerciseAttempted', { conceptId: `c${i}`, answer: `${i}` });
  }
  expect(await queue.getPendingCount()).toBe(5);

  // 2. Sync with mock server
  const manager = createSyncManager(queue, { fetch: createMockSyncFetch() });
  const result = await manager.sync();
  expect(result.acceptedEvents).toHaveLength(5);

  // 3. Apply results
  const resolver = createConflictResolver();
  const outcome = await resolver.apply(result);
  expect(outcome.totalEvents).toBe(5);

  // 4. Queue is empty
  expect(await queue.getPendingCount()).toBe(0);
});
```

## Rollback Criteria
- If IndexedDB is unreliable on some browsers: fall back to localStorage with 5MB limit
- If sync handshake is too complex: simplify to fire-and-forget with server-side idempotency only
- If clock skew detection is inaccurate: rely on server timestamps exclusively

## Definition of Done
- [ ] All 3 subtasks pass their tests
- [ ] `npm test -- --filter offline` -> 0 failures
- [ ] IndexedDB queue: enqueue, sync, purge lifecycle verified
- [ ] Sync handshake 5-step protocol implemented
- [ ] Event classification matches contract table
- [ ] Conflict resolver updates Zustand store correctly
- [ ] Clock skew detector handles NTP-style measurements
- [ ] PR reviewed by frontend lead
