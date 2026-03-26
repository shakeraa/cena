# MOB-004: Offline Sync Service (Durable Queue + Conflict Resolution)

**Priority:** P1 — data safety layer; student work must never be lost
**Blocked by:** MOB-002 (domain models), MOB-003 (WebSocket service)
**Estimated effort:** 5 days
**Contract:** `contracts/mobile/lib/core/services/offline_sync_service.dart`

---

## Context
Cena is offline-first: every student answer, session event, and annotation is persisted to SQLite (via drift) BEFORE being sent to the server. If the app crashes mid-submit, the durable command queue recovers on next launch. Events are classified into three tiers (unconditional, conditional, server-authoritative) that determine how the server handles them during sync. Clock skew detection uses NTP-style estimation to ensure event ordering across client and server. This is the most critical data safety layer — zero student work may be lost.

## Subtasks

### MOB-004.1: Durable Command Queue (Crash-Safe Write Path)
**Files:**
- `lib/core/services/durable_command_queue_impl.dart`
- `lib/core/database/commands_table.dart` (drift table definition)

**Acceptance:**
- [ ] `DurableCommandQueueImpl implements DurableCommandQueue` backed by a drift SQLite table
- [ ] `OutgoingCommand` class: `id` (UUIDv7), `type` (e.g., "SubmitAnswer"), `payload` (Map), `enqueuedAt`, `retryCount` (default 0), `lastError`
- [ ] `DurableRetryResult` class: `succeeded`, `failed`, `remaining` counts
- [ ] `enqueueCommand(cmd)`: persists command to SQLite, then attempts to send via WebSocket — returns command ID
- [ ] `markAcknowledged(commandId)`: removes the command from the retry queue
- [ ] `getPendingCommands()`: returns all unacknowledged commands ordered by `enqueuedAt` — called on app startup
- [ ] `retryPending()`: sends all pending commands in order, returns `DurableRetryResult` with counts
- [ ] `pendingCount()`: returns total number of unacknowledged commands
- [ ] SQLite write happens BEFORE WebSocket send (write-ahead pattern)
- [ ] If WebSocket send fails, command remains in queue with incremented `retryCount` and error message in `lastError`
- [ ] Maximum 5 retries per command before marking as permanently failed (logged, not deleted)

**Test:**
```dart
test('command survives simulated crash', () async {
  final db = await createTestDatabase();
  final queue = DurableCommandQueueImpl(db: db, webSocket: mockWebSocket);

  // Enqueue a command
  final id = await queue.enqueueCommand(OutgoingCommand(
    id: 'cmd-001',
    type: 'SubmitAnswer',
    payload: {'exerciseId': 'ex-001', 'answer': 'x=2'},
    enqueuedAt: DateTime.now(),
  ));

  // Simulate crash: close and reopen database
  await db.close();
  final db2 = await createTestDatabase();
  final queue2 = DurableCommandQueueImpl(db: db2, webSocket: mockWebSocket);

  final pending = await queue2.getPendingCommands();
  expect(pending, hasLength(1));
  expect(pending.first.id, equals('cmd-001'));
  expect(pending.first.type, equals('SubmitAnswer'));
});

test('markAcknowledged removes command from queue', () async {
  final queue = DurableCommandQueueImpl(db: testDb, webSocket: mockWebSocket);
  await queue.enqueueCommand(OutgoingCommand(
    id: 'cmd-001',
    type: 'StartSession',
    payload: {'studentId': 'stu-001'},
    enqueuedAt: DateTime.now(),
  ));

  await queue.markAcknowledged('cmd-001');
  expect(await queue.pendingCount(), equals(0));
});

test('retryPending retries in order and reports results', () async {
  final queue = DurableCommandQueueImpl(db: testDb, webSocket: mockWebSocket);

  // Enqueue 3 commands
  for (int i = 1; i <= 3; i++) {
    await queue.enqueueCommand(OutgoingCommand(
      id: 'cmd-$i',
      type: 'SubmitAnswer',
      payload: {'answer': '$i'},
      enqueuedAt: DateTime.now(),
    ));
  }

  // Second command fails
  when(() => mockWebSocket.send(target: any(named: 'target'), payload: any(named: 'payload')))
    .thenAnswer((inv) async {
      final payload = inv.namedArguments[#payload] as Map;
      if (payload['answer'] == '2') throw Exception('Network error');
    });

  final result = await queue.retryPending();
  expect(result.succeeded, equals(2));
  expect(result.failed, equals(1));
  expect(result.remaining, equals(1));
});

test('write-ahead: SQLite write happens before WebSocket send', () async {
  int writeOrder = 0;
  int sendOrder = 0;
  int counter = 0;

  // Mock to track call order
  final queue = DurableCommandQueueImpl(
    db: testDb,
    webSocket: mockWebSocket,
    onDbWrite: () => writeOrder = ++counter,
    onWsSend: () => sendOrder = ++counter,
  );

  await queue.enqueueCommand(OutgoingCommand(
    id: 'cmd-001',
    type: 'Test',
    payload: {},
    enqueuedAt: DateTime.now(),
  ));

  expect(writeOrder, lessThan(sendOrder)); // DB write first
});
```

**Edge Cases:**
- App killed by OS between SQLite write and WebSocket send — command survives in queue, retried on next launch
- WebSocket acknowledges but `markAcknowledged` fails — command will be re-sent on retry; server uses idempotency key to deduplicate
- SQLite database corrupted — detect on open, recreate with empty queue, log error (lost commands are logged to analytics)

---

### MOB-004.2: Offline Event Queue & Event Classifier
**Files:**
- `lib/core/services/offline_event_queue_impl.dart`
- `lib/core/services/event_classifier_impl.dart`
- `lib/core/database/events_table.dart` (drift table definition)

**Acceptance:**
- [ ] `OfflineEventQueueImpl implements OfflineEventQueue` backed by drift SQLite table
- [ ] `enqueue()` assigns next idempotency key (UUID:sequence), classifies event, stores with monotonic sequence number
- [ ] `peek(count)` returns next N events without removing them
- [ ] `getAll()` returns all pending events ordered by sequence number
- [ ] `removeAcknowledged(acknowledgedUpTo)` deletes events where `sequenceNumber <= acknowledgedUpTo`, returns count removed
- [ ] `markFailed(key, error)` sets the `lastError` field on the event
- [ ] `incrementRetry(key)` increments `retryCount`
- [ ] `pendingCount` and `pendingCountStream` provide real-time queue size
- [ ] `clear()` removes all events (used on logout)
- [ ] `isEmpty` returns true when no events are pending
- [ ] `EventClassifierImpl implements EventClassifier` with the contract classification map:
  - Unconditional: `AddAnnotation`, `SkipQuestion`, `SwitchApproach`
  - Conditional: `AttemptConcept`, `RequestHint`, `StartSession`, `EndSession`
  - ServerAuthoritative: `MasteryUpdate`, `XpCalculation`, `StreakCalculation`
- [ ] `weightFor()` returns 1.0 for unconditional, 0.75 for conditional, 0.0 for server-authoritative

**Test:**
```dart
test('events are assigned monotonically increasing sequence numbers', () async {
  final queue = OfflineEventQueueImpl(db: testDb, classifier: classifier, keyGen: keyGen);

  final e1 = await queue.enqueue(eventType: 'AttemptConcept', payload: {'a': 1});
  final e2 = await queue.enqueue(eventType: 'AddAnnotation', payload: {'b': 2});
  final e3 = await queue.enqueue(eventType: 'RequestHint', payload: {'c': 3});

  expect(e1.sequenceNumber, lessThan(e2.sequenceNumber));
  expect(e2.sequenceNumber, lessThan(e3.sequenceNumber));
});

test('removeAcknowledged removes correct events', () async {
  final queue = OfflineEventQueueImpl(db: testDb, classifier: classifier, keyGen: keyGen);

  await queue.enqueue(eventType: 'AttemptConcept', payload: {});
  await queue.enqueue(eventType: 'AttemptConcept', payload: {});
  await queue.enqueue(eventType: 'AttemptConcept', payload: {});

  final all = await queue.getAll();
  final removed = await queue.removeAcknowledged(all[1].sequenceNumber);
  expect(removed, equals(2)); // first two removed
  expect(await queue.pendingCount, equals(1));
});

test('EventClassifier classifies per contract map', () {
  final classifier = EventClassifierImpl();

  expect(classifier.classify('AddAnnotation'), equals(EventClassification.unconditional));
  expect(classifier.classify('SkipQuestion'), equals(EventClassification.unconditional));
  expect(classifier.classify('AttemptConcept'), equals(EventClassification.conditional));
  expect(classifier.classify('MasteryUpdate'), equals(EventClassification.serverAuthoritative));

  expect(classifier.weightFor(EventClassification.unconditional), equals(1.0));
  expect(classifier.weightFor(EventClassification.conditional), equals(0.75));
  expect(classifier.weightFor(EventClassification.serverAuthoritative), equals(0.0));
});

test('pendingCountStream emits on changes', () async {
  final queue = OfflineEventQueueImpl(db: testDb, classifier: classifier, keyGen: keyGen);
  final counts = <int>[];
  queue.pendingCountStream.listen(counts.add);

  await queue.enqueue(eventType: 'Test', payload: {});
  await queue.enqueue(eventType: 'Test', payload: {});
  await Future.delayed(Duration(milliseconds: 50));

  expect(counts, contains(1));
  expect(counts, contains(2));
});

test('unknown event type classified as conditional by default', () {
  final classifier = EventClassifierImpl();
  expect(classifier.classify('UnknownEvent'), equals(EventClassification.conditional));
});
```

**Edge Cases:**
- Sequence number overflow — use 64-bit integer (Dart `int` is 64-bit), wrap at `maxSafeInteger`
- Event with very large payload (>1MB) — warn but allow; drift handles large TEXT columns
- `clear()` called while sync is in progress — guard with a mutex to prevent race condition

---

### MOB-004.3: Idempotency Key Generator & Clock Skew Detector
**Files:**
- `lib/core/services/idempotency_key_generator_impl.dart`
- `lib/core/services/clock_skew_detector_impl.dart`

**Acceptance:**
- [ ] `IdempotencyKeyGeneratorImpl implements IdempotencyKeyGenerator`
- [ ] `next()` returns `{UUID v4}:{sequence}` where sequence is monotonically increasing
- [ ] `currentSequence` is persisted across app restarts (stored in Hive or SharedPreferences)
- [ ] `resetTo(sequence)` sets the counter to a specific value (after server ack)
- [ ] `ClockSkewDetectorImpl implements ClockSkewDetector`
- [ ] `updateEstimate()` computes offset using NTP formula: `offset = ((serverTimestamp - clientSendTime) + (serverTimestamp - clientReceiveTime)) / 2`
- [ ] `estimatedOffsetMs` returns the running average offset in milliseconds
- [ ] `adjustToServerTime(clientTime)` adds the estimated offset to the client time
- [ ] `sampleCount` tracks how many calibration samples have been collected
- [ ] `confidence` increases with more samples, decreases with high variance — starts at 0.0, reaches 1.0 after 10 consistent samples

**Test:**
```dart
test('idempotency key format is UUID:sequence', () {
  final gen = IdempotencyKeyGeneratorImpl();
  final key1 = gen.next();
  final key2 = gen.next();

  expect(key1, matches(RegExp(r'^[a-f0-9-]+:\d+$')));
  expect(key2, matches(RegExp(r'^[a-f0-9-]+:\d+$')));

  final seq1 = int.parse(key1.split(':').last);
  final seq2 = int.parse(key2.split(':').last);
  expect(seq2, equals(seq1 + 1));
});

test('idempotency sequence persists across instances', () async {
  final gen1 = IdempotencyKeyGeneratorImpl(storage: testStorage);
  gen1.next(); // seq 1
  gen1.next(); // seq 2
  final seq = gen1.currentSequence;

  // Simulate restart
  final gen2 = IdempotencyKeyGeneratorImpl(storage: testStorage);
  await gen2.loadState();
  expect(gen2.currentSequence, equals(seq));
});

test('clock skew detector estimates offset from NTP-style samples', () {
  final detector = ClockSkewDetectorImpl();

  // Client is 100ms behind server
  detector.updateEstimate(
    clientSendTime: DateTime(2026, 1, 1, 10, 0, 0, 0),
    serverTimestamp: DateTime(2026, 1, 1, 10, 0, 0, 150),
    clientReceiveTime: DateTime(2026, 1, 1, 10, 0, 0, 200),
  );

  // Offset should be approximately 100ms
  expect(detector.estimatedOffsetMs, closeTo(100, 50));
  expect(detector.sampleCount, equals(1));
});

test('clock skew confidence increases with consistent samples', () {
  final detector = ClockSkewDetectorImpl();

  for (int i = 0; i < 10; i++) {
    detector.updateEstimate(
      clientSendTime: DateTime(2026, 1, 1, 10, 0, i, 0),
      serverTimestamp: DateTime(2026, 1, 1, 10, 0, i, 100),
      clientReceiveTime: DateTime(2026, 1, 1, 10, 0, i, 200),
    );
  }

  expect(detector.confidence, greaterThan(0.8));
  expect(detector.sampleCount, equals(10));
});

test('adjustToServerTime applies offset', () {
  final detector = ClockSkewDetectorImpl();
  detector.updateEstimate(
    clientSendTime: DateTime(2026, 1, 1, 10, 0, 0, 0),
    serverTimestamp: DateTime(2026, 1, 1, 10, 0, 0, 500),
    clientReceiveTime: DateTime(2026, 1, 1, 10, 0, 1, 0),
  );

  final clientTime = DateTime(2026, 1, 1, 12, 0, 0, 0);
  final adjusted = detector.adjustToServerTime(clientTime);
  expect(adjusted.isAfter(clientTime), isTrue);
});
```

**Edge Cases:**
- Device clock manually changed by user — clock skew detector adapts over next 3-5 samples
- Server latency spike creates outlier sample — use median filter (discard samples with RTT > 5 seconds)
- First launch with no samples — confidence is 0.0, offset defaults to 0ms

---

### MOB-004.4: Conflict Resolver
**Files:**
- `lib/core/services/conflict_resolver_impl.dart`

**Acceptance:**
- [ ] `ConflictResolverImpl implements ConflictResolver`
- [ ] `resolve(correction)` returns the merged value based on the correction's `weight`:
  - `weight == 1.0`: client value accepted (unconditional)
  - `weight == 0.75`: weighted merge — for numeric values, `client * 0.75 + server * 0.25`; for strings, server value wins
  - `weight == 0.0`: server value replaces client value entirely (server-authoritative)
- [ ] `applyCorrections(List<SyncCorrection>)` applies corrections in order, updating local state via callbacks
- [ ] Weight constants: `weightFull = 1.0`, `weightReduced = 0.75`, `weightHistorical = 0.0`
- [ ] Each applied correction is logged for audit trail
- [ ] Unresolvable conflicts (weight between 0 and 1 on non-numeric fields) are flagged for user attention

**Test:**
```dart
test('server-authoritative correction replaces client value', () {
  final resolver = ConflictResolverImpl();
  final result = resolver.resolve(SyncCorrection(
    idempotencyKey: 'key-1',
    field: 'pKnown',
    clientValue: '0.85',
    serverValue: '0.82',
    weight: 0.0,
  ));
  expect(result, equals('0.82'));
});

test('unconditional correction preserves client value', () {
  final resolver = ConflictResolverImpl();
  final result = resolver.resolve(SyncCorrection(
    idempotencyKey: 'key-2',
    field: 'text',
    clientValue: 'my annotation',
    serverValue: 'different',
    weight: 1.0,
  ));
  expect(result, equals('my annotation'));
});

test('conditional correction applies weighted merge for numerics', () {
  final resolver = ConflictResolverImpl();
  final result = resolver.resolve(SyncCorrection(
    idempotencyKey: 'key-3',
    field: 'pKnown',
    clientValue: '0.80',
    serverValue: '0.60',
    weight: 0.75,
  ));
  // 0.80 * 0.75 + 0.60 * 0.25 = 0.60 + 0.15 = 0.75
  final merged = double.parse(result.toString());
  expect(merged, closeTo(0.75, 0.01));
});

test('applyCorrections processes batch in order', () async {
  final resolver = ConflictResolverImpl();
  final applied = <String>[];

  await resolver.applyCorrections(
    [
      SyncCorrection(idempotencyKey: 'k1', field: 'f1', clientValue: 'a', serverValue: 'b', weight: 0.0),
      SyncCorrection(idempotencyKey: 'k2', field: 'f2', clientValue: 'c', serverValue: 'd', weight: 1.0),
    ],
    onApply: (key, value) => applied.add(key),
  );

  expect(applied, equals(['k1', 'k2']));
});
```

**Edge Cases:**
- Correction for a field that no longer exists locally (event was already purged) — skip and log
- Weight value outside [0.0, 1.0] range — clamp to bounds
- Non-numeric field with weight 0.75 — treat as server-wins (cannot weighted-merge a string)

---

### MOB-004.5: Sync Manager (Orchestrator)
**Files:**
- `lib/core/services/sync_manager_impl.dart`

**Acceptance:**
- [ ] `SyncManagerImpl implements SyncManager` orchestrates the full sync lifecycle:
  1. Detect connectivity via `ConnectivityMonitor`
  2. Perform clock skew calibration handshake
  3. Collect all pending events from `OfflineEventQueue`
  4. Build `SyncRequest` with events, `clockOffsetMs`, and `lastAcknowledgedSequence`
  5. Submit to server via REST endpoint (not WebSocket — sync is batch)
  6. Process `SyncResponse`: accepted, corrected, rejected
  7. Apply corrections via `ConflictResolver`
  8. Remove acknowledged events from queue
  9. Emit updated `SyncStatus`
- [ ] `status` and `statusStream` track: `idle`, `syncing`, `error`, `conflict`
- [ ] `lastSyncTime` and `lastSyncTimeStream` update after successful sync
- [ ] `syncNow()` triggers immediate sync, returns true if successful
- [ ] `startAutoSync(interval)` listens for connectivity changes and syncs periodically (default 5 minutes)
- [ ] `stopAutoSync()` cancels auto-sync monitoring
- [ ] `calibrateClock()` performs the initial clock handshake with server
- [ ] `pendingEventCount` and `pendingEventCountStream` delegate to `OfflineEventQueue`
- [ ] `hasConflicts` and `getUnresolvedConflicts()` expose unresolved corrections
- [ ] `acceptCorrection(key)` marks a specific conflict as resolved
- [ ] `dispose()` releases all resources

**Test:**
```dart
test('syncNow performs full sync lifecycle', () async {
  final syncManager = SyncManagerImpl(
    eventQueue: mockEventQueue,
    clockSkew: mockClockSkew,
    conflictResolver: mockConflictResolver,
    restClient: mockRestClient,
    connectivityMonitor: mockConnectivity,
  );

  when(() => mockEventQueue.getAll()).thenAnswer((_) async => [
    OfflineEvent(
      idempotencyKey: 'key-1:1',
      clientTimestamp: DateTime.now(),
      eventType: 'AttemptConcept',
      payload: '{"answer":"x=2"}',
      classification: EventClassification.conditional,
      sequenceNumber: 1,
    ),
  ]);

  when(() => mockRestClient.postSync(any())).thenAnswer((_) async => SyncResponse(
    acknowledgedUpTo: 1,
    acceptedKeys: ['key-1:1'],
    serverTimestamp: DateTime.now(),
  ));

  final success = await syncManager.syncNow();
  expect(success, isTrue);
  verify(() => mockEventQueue.removeAcknowledged(1)).called(1);
});

test('sync with corrections applies conflict resolution', () async {
  final syncManager = SyncManagerImpl(
    eventQueue: mockEventQueue,
    clockSkew: mockClockSkew,
    conflictResolver: mockConflictResolver,
    restClient: mockRestClient,
    connectivityMonitor: mockConnectivity,
  );

  when(() => mockRestClient.postSync(any())).thenAnswer((_) async => SyncResponse(
    acknowledgedUpTo: 2,
    acceptedKeys: ['key-1:1'],
    corrections: [
      SyncCorrection(
        idempotencyKey: 'key-2:2',
        field: 'pKnown',
        clientValue: '0.85',
        serverValue: '0.80',
        weight: 0.0,
      ),
    ],
    serverTimestamp: DateTime.now(),
  ));

  await syncManager.syncNow();
  verify(() => mockConflictResolver.applyCorrections(any())).called(1);
});

test('statusStream emits syncing then idle on success', () async {
  final syncManager = SyncManagerImpl(
    eventQueue: mockEventQueue,
    clockSkew: mockClockSkew,
    conflictResolver: mockConflictResolver,
    restClient: mockRestClient,
    connectivityMonitor: mockConnectivity,
  );

  final statuses = <SyncStatus>[];
  syncManager.statusStream.listen(statuses.add);

  when(() => mockEventQueue.getAll()).thenAnswer((_) async => []);
  when(() => mockRestClient.postSync(any())).thenAnswer((_) async => SyncResponse(
    acknowledgedUpTo: 0,
    serverTimestamp: DateTime.now(),
  ));

  await syncManager.syncNow();
  expect(statuses, containsAllInOrder([SyncStatus.syncing, SyncStatus.idle]));
});

test('auto-sync triggers on connectivity restoration', () async {
  fakeAsync((async) async {
    final connectivityController = StreamController<bool>();
    when(() => mockConnectivity.onConnectivityChanged)
      .thenAnswer((_) => connectivityController.stream);

    final syncManager = SyncManagerImpl(
      eventQueue: mockEventQueue,
      clockSkew: mockClockSkew,
      conflictResolver: mockConflictResolver,
      restClient: mockRestClient,
      connectivityMonitor: mockConnectivity,
    );

    await syncManager.startAutoSync(interval: Duration(minutes: 5));

    // Go offline then online
    connectivityController.add(false);
    connectivityController.add(true);
    async.elapse(Duration(seconds: 1));

    verify(() => mockRestClient.postSync(any())).called(1);
  });
});
```

**Edge Cases:**
- Sync in progress when another sync is requested — debounce, return the existing future
- Server returns HTTP 429 (rate limited) — respect `Retry-After` header, back off
- Server returns HTTP 409 (conflict) with `rejectedKeys` — move rejected events to a dead-letter queue
- Network drops mid-sync — sync status transitions to `error`, retries on next auto-sync
- Empty queue — `syncNow()` returns `true` immediately without hitting the server

---

## Integration Test

```dart
void main() {
  group('MOB-004 Integration: Full offline sync round-trip', () {
    test('enqueue while offline -> go online -> sync -> verify', () async {
      final db = await createTestDatabase();
      final eventQueue = OfflineEventQueueImpl(db: db, classifier: EventClassifierImpl(), keyGen: IdempotencyKeyGeneratorImpl());
      final commandQueue = DurableCommandQueueImpl(db: db, webSocket: mockWebSocket);

      // Enqueue events while offline
      await eventQueue.enqueue(eventType: 'AttemptConcept', payload: {'answer': 'x=2'});
      await eventQueue.enqueue(eventType: 'AddAnnotation', payload: {'text': 'note'});
      await commandQueue.enqueueCommand(OutgoingCommand(
        id: 'cmd-001',
        type: 'SubmitAnswer',
        payload: {'answer': 'x=2'},
        enqueuedAt: DateTime.now(),
      ));

      expect(await eventQueue.pendingCount, equals(2));
      expect(await commandQueue.pendingCount(), equals(1));

      // Sync
      final syncManager = SyncManagerImpl(
        eventQueue: eventQueue,
        clockSkew: ClockSkewDetectorImpl(),
        conflictResolver: ConflictResolverImpl(),
        restClient: mockRestClient,
        connectivityMonitor: mockConnectivity,
      );

      when(() => mockRestClient.postSync(any())).thenAnswer((_) async => SyncResponse(
        acknowledgedUpTo: 2,
        acceptedKeys: ['key-1:1', 'key-2:2'],
        serverTimestamp: DateTime.now(),
      ));

      final success = await syncManager.syncNow();
      expect(success, isTrue);
      expect(await eventQueue.pendingCount, equals(0));
    });

    test('idempotency key prevents duplicate processing', () async {
      final eventQueue = OfflineEventQueueImpl(db: testDb, classifier: EventClassifierImpl(), keyGen: IdempotencyKeyGeneratorImpl());

      final e1 = await eventQueue.enqueue(eventType: 'AttemptConcept', payload: {});
      final e2 = await eventQueue.enqueue(eventType: 'AttemptConcept', payload: {});

      // Keys are unique
      expect(e1.idempotencyKey, isNot(equals(e2.idempotencyKey)));

      // Keys contain sequence numbers
      final seq1 = int.parse(e1.idempotencyKey.split(':').last);
      final seq2 = int.parse(e2.idempotencyKey.split(':').last);
      expect(seq2, greaterThan(seq1));
    });
  });
}
```

## Rollback Criteria
- If drift SQLite has performance issues with large queues (>10K events): batch `removeAcknowledged` into chunks of 1000
- If NTP-style clock skew is too complex: use simple `serverTimestamp - clientTimestamp` from the first server response
- If conflict resolution weighted-merge causes confusion: simplify to binary server-wins / client-wins based on classification
- If sync API endpoint changes: abstract behind a `SyncApiClient` interface to swap implementations

## Definition of Done
- [ ] All 5 subtasks pass their individual tests
- [ ] Durable command queue survives simulated app crashes (SQLite persistence verified)
- [ ] Events maintain correct ordering through enqueue -> sync -> acknowledge cycle
- [ ] Clock skew detector converges within 5 samples
- [ ] Conflict resolver handles all three weight tiers correctly
- [ ] Sync manager completes full lifecycle: detect connectivity -> calibrate -> sync -> apply corrections -> purge
- [ ] No data loss scenario: every student answer reaches the server eventually
- [ ] PR reviewed by mobile lead
