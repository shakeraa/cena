# MOB-005: Riverpod State Management (All Notifiers + Providers)

**Priority:** P1 — all screens depend on state
**Blocked by:** MOB-003 (WebSocket service), MOB-004 (offline sync)
**Estimated effort:** 4 days
**Contract:** `contracts/mobile/lib/core/state/app_state.dart`

---

## Context
Cena uses Riverpod with `StateNotifier` for all application state. The contract defines 5 notifiers (`SessionNotifier`, `KnowledgeGraphNotifier`, `UserNotifier`, `OfflineNotifier`, `OutreachNotifier`), 5 corresponding state classes, 3 infrastructure providers (WebSocket, SyncManager, ConnectivityMonitor), and 7 derived/selector providers. Each notifier subscribes to WebSocket events via `MessageRouter` and updates state reactively. The `UserNotifier` is the only non-autoDispose provider (user is always relevant). All others autoDispose when no listener is present.

## Subtasks

### MOB-005.1: SessionNotifier & SessionState
**Files:**
- `lib/core/state/session_notifier.dart`
- `lib/core/state/session_state.dart`

**Acceptance:**
- [ ] `SessionState` class with all contract fields: `currentSession`, `currentExercise`, `methodology`, `fatigueScore` (0.0), `questionsAttempted` (0), `questionsCorrect` (0), `isLoading` (false), `error`, `isBreakSuggested` (false), `hintsUsed` (0), `sessionHistory` (List<AnswerResult>, default [])
- [ ] Computed getters: `isActive` (session exists and not ended), `accuracy` (correct/attempted), `elapsed` (Duration since startedAt)
- [ ] `copyWith()` supports all fields
- [ ] `SessionNotifier extends StateNotifier<SessionState>` with dependencies: `WebSocketService`, `SyncManager`
- [ ] `startSession({Subject?, durationMinutes})`: sets `isLoading=true`, sends `StartSession` via WebSocket, handles error
- [ ] `submitAnswer(answer, timeSpentMs)`: sends `AttemptConcept` with current exercise and session IDs
- [ ] `requestHint()`: sends `RequestHint` with current hint level, increments `hintsUsed`
- [ ] `skipQuestion({reason})`: sends `SkipQuestion` via WebSocket
- [ ] `switchApproach(preferenceHint)`: sends `SwitchApproach` via WebSocket
- [ ] `endSession({reason})`: sends `EndSession` via WebSocket
- [ ] `dismissBreakSuggestion()`: sets `isBreakSuggested=false`
- [ ] `_subscribeToEvents()` listens to WebSocket `onMessage` stream and handles:
  - `QuestionPresented` -> updates `currentExercise`, resets `hintsUsed`
  - `AnswerEvaluated` -> appends to `sessionHistory`, increments `questionsCorrect` if correct
  - `MethodologySwitched` -> updates `methodology`
  - `CognitiveLoadWarning` -> updates `fatigueScore`, sets `isBreakSuggested=true`
  - `SessionSummaryEvent` -> marks session ended
- [ ] `dispose()` cancels all stream subscriptions

**Test:**
```dart
test('startSession sends command and sets loading', () async {
  final notifier = SessionNotifier(
    webSocketService: mockWs,
    syncManager: mockSync,
  );

  await notifier.startSession(subject: Subject.math, durationMinutes: 25);

  verify(() => mockWs.startSession(any())).called(1);
  // After error-free send, isLoading may still be true until QuestionPresented arrives
});

test('submitAnswer sends AttemptConcept with correct fields', () async {
  final notifier = SessionNotifier(webSocketService: mockWs, syncManager: mockSync);
  // Set up state with active session and exercise
  notifier.state = SessionState(
    currentSession: Session(id: 's-001', startedAt: DateTime.now(), methodology: Methodology.blocked),
    currentExercise: Exercise(id: 'ex-001', conceptId: 'c-001', questionType: QuestionType.numeric, difficulty: 5, content: 'test'),
  );

  await notifier.submitAnswer('42', 15000);

  final captured = verify(() => mockWs.attemptConcept(captureAny)).captured.single as AttemptConcept;
  expect(captured.sessionId, equals('s-001'));
  expect(captured.exerciseId, equals('ex-001'));
  expect(captured.answer, equals('42'));
  expect(captured.timeSpentMs, equals(15000));
});

test('QuestionPresented event updates currentExercise', () async {
  final notifier = SessionNotifier(webSocketService: mockWs, syncManager: mockSync);
  final messagesController = StreamController<MessageEnvelope>();
  when(() => mockWs.onMessage).thenAnswer((_) => messagesController.stream);

  notifier.initSubscriptions(); // or trigger in constructor

  messagesController.add(MessageEnvelope(
    target: 'QuestionPresented',
    invocationId: 'inv-1',
    arguments: [{
      'exercise': {
        'id': 'ex-002',
        'conceptId': 'c-002',
        'questionType': 'mcq',
        'difficulty': 3,
        'content': 'What is 2+2?',
        'options': ['3', '4', '5', '6'],
      },
      'sessionId': 's-001',
      'questionNumber': 2,
      'estimatedRemaining': 8,
    }],
    timestamp: DateTime.now(),
  ));

  await Future.delayed(Duration(milliseconds: 50));
  expect(notifier.state.currentExercise?.id, equals('ex-002'));
});

test('CognitiveLoadWarning triggers break suggestion', () async {
  final notifier = SessionNotifier(webSocketService: mockWs, syncManager: mockSync);
  final messagesController = StreamController<MessageEnvelope>();
  when(() => mockWs.onMessage).thenAnswer((_) => messagesController.stream);
  notifier.initSubscriptions();

  messagesController.add(MessageEnvelope(
    target: 'CognitiveLoadWarning',
    invocationId: 'inv-2',
    arguments: [{
      'fatigueScore': 0.78,
      'suggestedBreakMinutes': 5,
      'message': 'קח הפסקה קצרה',
    }],
    timestamp: DateTime.now(),
  ));

  await Future.delayed(Duration(milliseconds: 50));
  expect(notifier.state.isBreakSuggested, isTrue);
  expect(notifier.state.fatigueScore, equals(0.78));
});

test('accuracy computed correctly', () {
  final state = SessionState(questionsAttempted: 10, questionsCorrect: 7);
  expect(state.accuracy, closeTo(0.7, 0.01));
});

test('accuracy is 0 when no questions attempted', () {
  final state = SessionState();
  expect(state.accuracy, equals(0.0));
});
```

**Edge Cases:**
- `submitAnswer` called when `currentExercise` is null — return early, do not crash
- `endSession` called when no session active — return early
- WebSocket disconnects mid-session — session state preserved, commands queued via durable queue
- Multiple rapid `submitAnswer` calls — debounce or ignore while previous answer is being evaluated

---

### MOB-005.2: KnowledgeGraphNotifier & KnowledgeGraphState
**Files:**
- `lib/core/state/knowledge_graph_notifier.dart`
- `lib/core/state/knowledge_graph_state.dart`

**Acceptance:**
- [ ] `KnowledgeGraphState` class with fields: `graph` (KnowledgeGraph?), `selectedNodeId`, `subjectFilter`, `zoomLevel` (1.0), `panOffsetX` (0.0), `panOffsetY` (0.0), `isLoading` (false), `error`, `searchQuery`, `highlightedPath` (List<String>, default [])
- [ ] Computed getters: `selectedNode` (finds node by ID), `filteredNodes` (filters by subject)
- [ ] `KnowledgeGraphNotifier extends StateNotifier<KnowledgeGraphState>` with dependency: `WebSocketService`
- [ ] `selectNode(conceptId)`: updates `selectedNodeId`
- [ ] `filterBySubject(subject)`: updates `subjectFilter` (null = all subjects)
- [ ] `setZoom(zoom)`: clamps to [0.3, 3.0] and updates
- [ ] `setPanOffset(x, y)`: updates pan offset
- [ ] `search(query)`: updates `searchQuery`
- [ ] `highlightPathTo(conceptId)`: computes prerequisite path via BFS and sets `highlightedPath`
- [ ] `clearHighlight()`: resets `highlightedPath` to empty
- [ ] `_subscribeToEvents()` handles `KnowledgeGraphUpdated` events:
  - `isFullUpdate=true` -> replaces entire graph
  - `isFullUpdate=false` -> merges delta into existing graph

**Test:**
```dart
test('filteredNodes returns only matching subject', () {
  final state = KnowledgeGraphState(
    graph: KnowledgeGraph(
      nodes: [
        _mathNode('c-1'), _mathNode('c-2'), _physicsNode('c-3'),
      ],
      edges: [],
      masteryOverlay: {},
    ),
    subjectFilter: Subject.math,
  );
  expect(state.filteredNodes, hasLength(2));
  expect(state.filteredNodes.every((n) => n.subject == Subject.math), isTrue);
});

test('filteredNodes returns all when no filter', () {
  final state = KnowledgeGraphState(
    graph: KnowledgeGraph(
      nodes: [_mathNode('c-1'), _physicsNode('c-2')],
      edges: [],
      masteryOverlay: {},
    ),
  );
  expect(state.filteredNodes, hasLength(2));
});

test('setZoom clamps to bounds', () {
  final notifier = KnowledgeGraphNotifier(webSocketService: mockWs);
  notifier.setZoom(0.1);
  expect(notifier.state.zoomLevel, equals(0.3)); // clamped to min

  notifier.setZoom(5.0);
  expect(notifier.state.zoomLevel, equals(3.0)); // clamped to max

  notifier.setZoom(1.5);
  expect(notifier.state.zoomLevel, equals(1.5)); // within bounds
});

test('selectNode updates selectedNodeId', () {
  final notifier = KnowledgeGraphNotifier(webSocketService: mockWs);
  notifier.selectNode('c-001');
  expect(notifier.state.selectedNodeId, equals('c-001'));

  notifier.selectNode(null);
  expect(notifier.state.selectedNodeId, isNull);
});

test('KnowledgeGraphUpdated full update replaces graph', () async {
  final notifier = KnowledgeGraphNotifier(webSocketService: mockWs);
  final messagesController = StreamController<MessageEnvelope>();
  when(() => mockWs.onMessage).thenAnswer((_) => messagesController.stream);

  final newGraph = KnowledgeGraph(
    nodes: [_mathNode('new-1')],
    edges: [],
    masteryOverlay: {},
  );

  messagesController.add(MessageEnvelope(
    target: 'KnowledgeGraphUpdated',
    invocationId: 'inv-1',
    arguments: [{'isFullUpdate': true, 'graph': newGraph.toJson()}],
    timestamp: DateTime.now(),
  ));

  await Future.delayed(Duration(milliseconds: 50));
  expect(notifier.state.graph?.nodes, hasLength(1));
  expect(notifier.state.graph?.nodes.first.conceptId, equals('new-1'));
});

test('selectedNode returns correct node from graph', () {
  final state = KnowledgeGraphState(
    graph: KnowledgeGraph(
      nodes: [_mathNode('c-1'), _mathNode('c-2')],
      edges: [],
      masteryOverlay: {},
    ),
    selectedNodeId: 'c-2',
  );
  expect(state.selectedNode?.conceptId, equals('c-2'));
});
```

**Edge Cases:**
- `selectedNodeId` references a node that was removed in a graph update — `selectedNode` returns null
- Empty graph (no nodes) — `filteredNodes` returns empty list, `selectedNode` returns null
- Delta update with new nodes — merge without duplicating existing nodes (match by `conceptId`)

---

### MOB-005.3: UserNotifier & OfflineNotifier
**Files:**
- `lib/core/state/user_notifier.dart`
- `lib/core/state/user_state.dart`
- `lib/core/state/offline_notifier.dart`
- `lib/core/state/offline_state.dart`

**Acceptance:**
- [ ] `UserState` class: `student`, `badges` (default []), `dailyQuestionsAnswered` (0), `dailyGoal` (20), `llmInteractionsToday` (0), `isLoading` (false), `error`
- [ ] Computed getters: `isAuthenticated`, `remainingStudyEnergy` (50 - interactions), `hasStudyEnergy` (interactions < 50), `dailyProgress` (answered/goal clamped 0-1)
- [ ] `UserNotifier` methods: `setStudent(student)`, `logout()`, `recordQuestionAttempt()`, `recordLlmInteraction()`
- [ ] `_subscribeToEvents()` handles `XpAwarded`, `StreakUpdated` events — updates student XP, level, streak
- [ ] `OfflineState` class: `queuedEventCount` (0), `syncStatus` (SyncStatus.idle), `lastSyncTime`, `isOnline` (true), `lastError`, `conflictCount` (0)
- [ ] Computed getters: `hasPendingEvents`, `hasConflicts`, `isSyncing`
- [ ] `OfflineNotifier` subscribes to `SyncManager.statusStream`, `pendingEventCountStream`, `lastSyncTimeStream`, and `ConnectivityMonitor.onConnectivityChanged`
- [ ] `syncNow()` delegates to `SyncManager.syncNow()`

**Test:**
```dart
test('UserState.remainingStudyEnergy calculates correctly', () {
  final state = UserState(llmInteractionsToday: 35);
  expect(state.remainingStudyEnergy, equals(15));
  expect(state.hasStudyEnergy, isTrue);
});

test('UserState.hasStudyEnergy is false at cap', () {
  final state = UserState(llmInteractionsToday: 50);
  expect(state.hasStudyEnergy, isFalse);
  expect(state.remainingStudyEnergy, equals(0));
});

test('UserState.dailyProgress clamps to 1.0', () {
  final state = UserState(dailyQuestionsAnswered: 30, dailyGoal: 20);
  expect(state.dailyProgress, equals(1.0));
});

test('recordQuestionAttempt increments counter', () {
  final notifier = UserNotifier(webSocketService: mockWs);
  notifier.setStudent(Student(id: 's1', name: 'Test', experimentCohort: ExperimentCohort.control, lastActive: DateTime.now()));

  notifier.recordQuestionAttempt();
  notifier.recordQuestionAttempt();
  expect(notifier.state.dailyQuestionsAnswered, equals(2));
});

test('logout clears all user state', () {
  final notifier = UserNotifier(webSocketService: mockWs);
  notifier.setStudent(Student(id: 's1', name: 'Test', experimentCohort: ExperimentCohort.control, lastActive: DateTime.now()));
  notifier.recordQuestionAttempt();
  notifier.logout();
  expect(notifier.state.isAuthenticated, isFalse);
  expect(notifier.state.dailyQuestionsAnswered, equals(0));
});

test('XpAwarded event updates student XP and level', () async {
  final notifier = UserNotifier(webSocketService: mockWs);
  notifier.setStudent(Student(
    id: 's1', name: 'Test',
    experimentCohort: ExperimentCohort.control,
    lastActive: DateTime.now(),
    xp: 100, level: 2,
  ));

  final messagesController = StreamController<MessageEnvelope>();
  when(() => mockWs.onMessage).thenAnswer((_) => messagesController.stream);
  notifier.initSubscriptions();

  messagesController.add(MessageEnvelope(
    target: 'XpAwarded',
    invocationId: 'inv-1',
    arguments: [{
      'amount': 25,
      'reason': 'correct_answer',
      'totalXp': 125,
      'currentLevel': 2,
      'leveledUp': false,
    }],
    timestamp: DateTime.now(),
  ));

  await Future.delayed(Duration(milliseconds: 50));
  expect(notifier.state.student?.xp, equals(125));
});

test('OfflineNotifier reflects connectivity changes', () async {
  final connectivityController = StreamController<bool>();
  when(() => mockConnectivity.onConnectivityChanged).thenAnswer((_) => connectivityController.stream);
  when(() => mockSync.statusStream).thenAnswer((_) => Stream.empty());
  when(() => mockSync.pendingEventCountStream).thenAnswer((_) => Stream.empty());
  when(() => mockSync.lastSyncTimeStream).thenAnswer((_) => Stream.empty());

  final notifier = OfflineNotifier(
    syncManager: mockSync,
    connectivityMonitor: mockConnectivity,
  );

  connectivityController.add(false);
  await Future.delayed(Duration(milliseconds: 50));
  expect(notifier.state.isOnline, isFalse);

  connectivityController.add(true);
  await Future.delayed(Duration(milliseconds: 50));
  expect(notifier.state.isOnline, isTrue);
});

test('OfflineState computed getters', () {
  final state = OfflineState(
    queuedEventCount: 5,
    syncStatus: SyncStatus.syncing,
    conflictCount: 2,
  );
  expect(state.hasPendingEvents, isTrue);
  expect(state.hasConflicts, isTrue);
  expect(state.isSyncing, isTrue);
});
```

**Edge Cases:**
- `recordLlmInteraction` called when already at cap (50) — state still updates to 51 but `hasStudyEnergy` returns false
- `setStudent` called with a student with locale 'ar' — downstream UI should switch to Arabic font + RTL
- `OfflineNotifier` starts before `SyncManager` is ready — handle initial null/empty streams gracefully

---

### MOB-005.4: Outreach Notifier & Provider Declarations
**Files:**
- `lib/core/state/outreach_notifier.dart`
- `lib/core/state/outreach_state.dart`
- `lib/core/state/providers.dart`

**Acceptance:**
- [ ] `OutreachState` class: `pendingNotifications` (List<AppNotification>, default []), `streakExpiresAt`, `isStreakWarningActive` (false), `lastNotificationDismissedAt`
- [ ] Computed: `unreadCount` (count of notifications where `isRead == false`)
- [ ] `AppNotification` class: `id`, `title`, `body`, `createdAt`, `isRead` (false), `actionRoute` (optional deep link)
- [ ] `OutreachNotifier` methods: `addNotification()`, `markRead(id)`, `dismissAll()`, `warnStreakExpiring(expiresAt)`, `dismissStreakWarning()`
- [ ] Provider declarations in `providers.dart`:
  - `webSocketServiceProvider` — overridden at startup
  - `syncManagerProvider` — overridden at startup
  - `connectivityMonitorProvider` — overridden at startup
  - `sessionProvider` — autoDispose StateNotifierProvider
  - `knowledgeGraphProvider` — autoDispose StateNotifierProvider
  - `userProvider` — non-autoDispose StateNotifierProvider
  - `offlineProvider` — non-autoDispose StateNotifierProvider
  - `outreachProvider` — non-autoDispose StateNotifierProvider
- [ ] Derived providers:
  - `isSessionActiveProvider` — selector on sessionProvider
  - `connectionStateProvider` — StreamProvider from WebSocket
  - `hasPendingEventsProvider` — selector on offlineProvider
  - `remainingStudyEnergyProvider` — selector on userProvider
  - `filteredGraphNodesProvider` — selector on knowledgeGraphProvider
  - `selectedConceptProvider` — selector on knowledgeGraphProvider

**Test:**
```dart
test('OutreachNotifier manages notifications', () {
  final notifier = OutreachNotifier();

  notifier.addNotification(AppNotification(
    id: 'n-1',
    title: 'Streak Warning',
    body: 'Your streak expires in 2 hours',
    createdAt: DateTime.now(),
  ));
  expect(notifier.state.unreadCount, equals(1));

  notifier.markRead('n-1');
  expect(notifier.state.unreadCount, equals(0));
  expect(notifier.state.pendingNotifications.first.isRead, isTrue);
});

test('dismissAll clears all notifications', () {
  final notifier = OutreachNotifier();
  notifier.addNotification(AppNotification(id: 'n-1', title: 'A', body: 'B', createdAt: DateTime.now()));
  notifier.addNotification(AppNotification(id: 'n-2', title: 'C', body: 'D', createdAt: DateTime.now()));
  notifier.dismissAll();
  expect(notifier.state.pendingNotifications, isEmpty);
  expect(notifier.state.lastNotificationDismissedAt, isNotNull);
});

test('streak warning activation and dismissal', () {
  final notifier = OutreachNotifier();
  final expiresAt = DateTime.now().add(Duration(hours: 2));

  notifier.warnStreakExpiring(expiresAt);
  expect(notifier.state.isStreakWarningActive, isTrue);
  expect(notifier.state.streakExpiresAt, equals(expiresAt));

  notifier.dismissStreakWarning();
  expect(notifier.state.isStreakWarningActive, isFalse);
});

test('provider declarations exist with correct types', () {
  // This is a compile-time check — the test ensures the providers can be referenced
  expect(webSocketServiceProvider, isNotNull);
  expect(syncManagerProvider, isNotNull);
  expect(connectivityMonitorProvider, isNotNull);
  expect(sessionProvider, isNotNull);
  expect(knowledgeGraphProvider, isNotNull);
  expect(userProvider, isNotNull);
  expect(offlineProvider, isNotNull);
  expect(outreachProvider, isNotNull);
  expect(isSessionActiveProvider, isNotNull);
  expect(connectionStateProvider, isNotNull);
  expect(hasPendingEventsProvider, isNotNull);
  expect(remainingStudyEnergyProvider, isNotNull);
  expect(filteredGraphNodesProvider, isNotNull);
  expect(selectedConceptProvider, isNotNull);
});

test('sessionProvider is autoDispose', () {
  // Verify by creating a ProviderContainer and checking behavior
  final container = ProviderContainer(overrides: [
    webSocketServiceProvider.overrideWithValue(mockWs),
    syncManagerProvider.overrideWithValue(mockSync),
  ]);

  final sub = container.listen(sessionProvider, (_, __) {});
  sub.close(); // autoDispose should clean up
});

test('userProvider is NOT autoDispose (kept alive)', () {
  final container = ProviderContainer(overrides: [
    webSocketServiceProvider.overrideWithValue(mockWs),
  ]);

  final state1 = container.read(userProvider);
  // Even without listeners, userProvider should remain alive
  expect(state1.isAuthenticated, isFalse);
});
```

**Edge Cases:**
- `markRead` called with non-existent notification ID — no-op, no crash
- `addNotification` during an active session — notification is queued but not shown until session ends
- Derived provider `filteredGraphNodesProvider` when graph is null — returns empty list
- `connectionStateProvider` when WebSocket is not yet initialized — stream emits `disconnected`

---

## Integration Test

```dart
void main() {
  group('MOB-005 Integration: State management end-to-end', () {
    test('session lifecycle: start -> answer -> feedback -> end', () async {
      final container = ProviderContainer(overrides: [
        webSocketServiceProvider.overrideWithValue(mockWs),
        syncManagerProvider.overrideWithValue(mockSync),
        connectivityMonitorProvider.overrideWithValue(mockConnectivity),
      ]);

      final sessionNotifier = container.read(sessionProvider.notifier);
      final userNotifier = container.read(userProvider.notifier);

      // Set user
      userNotifier.setStudent(Student(
        id: 'stu-001', name: 'Test',
        experimentCohort: ExperimentCohort.control,
        lastActive: DateTime.now(),
      ));

      // Start session
      await sessionNotifier.startSession(subject: Subject.math);
      expect(container.read(sessionProvider).isLoading, isTrue);

      // Simulate question delivery
      simulateEvent(mockWs, 'QuestionPresented', {
        'exercise': testExercise.toJson(),
        'sessionId': 's-001',
        'questionNumber': 1,
        'estimatedRemaining': 9,
      });

      await Future.delayed(Duration(milliseconds: 50));
      expect(container.read(sessionProvider).currentExercise, isNotNull);
      expect(container.read(isSessionActiveProvider), isTrue);

      // Submit answer
      await sessionNotifier.submitAnswer('x=2', 15000);

      // Simulate evaluation
      simulateEvent(mockWs, 'AnswerEvaluated', {
        'exerciseId': 'ex-001',
        'result': {
          'isCorrect': true,
          'errorType': 'none',
          'priorMastery': 0.65,
          'posteriorMastery': 0.78,
          'feedback': 'Correct!',
          'xpEarned': 10,
        },
      });

      await Future.delayed(Duration(milliseconds: 50));
      expect(container.read(sessionProvider).questionsCorrect, equals(1));
      expect(container.read(sessionProvider).sessionHistory, hasLength(1));
    });

    test('all derived providers compute correctly from base state', () {
      final container = ProviderContainer(overrides: [
        webSocketServiceProvider.overrideWithValue(mockWs),
        syncManagerProvider.overrideWithValue(mockSync),
        connectivityMonitorProvider.overrideWithValue(mockConnectivity),
      ]);

      expect(container.read(isSessionActiveProvider), isFalse);
      expect(container.read(hasPendingEventsProvider), isFalse);
      expect(container.read(remainingStudyEnergyProvider), equals(50));
      expect(container.read(filteredGraphNodesProvider), isEmpty);
      expect(container.read(selectedConceptProvider), isNull);
    });
  });
}
```

## Rollback Criteria
- If `StateNotifier` is deprecated in favor of `Notifier` in newer Riverpod: migrate to `Notifier` + `AsyncNotifier` pattern with code generation
- If autoDispose causes premature cleanup of session state: switch sessionProvider to non-autoDispose with manual dispose on logout
- If derived providers cause too many rebuilds: use `select()` more aggressively or switch to `family` providers

## Definition of Done
- [ ] All 4 subtasks pass their individual tests
- [ ] All 5 notifiers update state correctly in response to WebSocket events
- [ ] All 7 derived providers compute correct values from base state
- [ ] `dispose()` on every notifier cancels all subscriptions (no memory leaks)
- [ ] Provider overrides work correctly in ProviderContainer for testing
- [ ] `flutter test` passes all state management tests
- [ ] PR reviewed by mobile lead
