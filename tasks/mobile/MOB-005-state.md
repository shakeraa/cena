# MOB-005: Riverpod — 5 Notifiers, Study Energy, Zoom/Pan Local State

**Priority:** P0 — blocks all mobile UI
**Blocked by:** MOB-001 (Flutter scaffold)
**Estimated effort:** 3 days
**Contract:** `contracts/mobile/lib/core/state/app_state.dart` (SessionNotifier, KnowledgeGraphNotifier, UserNotifier, OfflineNotifier, OutreachNotifier)

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule. `throw UnimplementedError`, `// TODO: implement`, empty bodies, and mock returns are FORBIDDEN in source code. If you cannot implement it fully, file a blocking dependency instead.

## Context

Five Riverpod StateNotifiers manage the app's reactive state: SessionNotifier (active learning session), KnowledgeGraphNotifier (visualization with zoom/pan), UserNotifier (auth, XP, streaks, study energy), OfflineNotifier (sync queue), OutreachNotifier (notifications, streak warnings). Study energy = 50 LLM interactions/day, tracked locally and enforced server-side.

## Subtasks

### MOB-005.1: SessionNotifier + UserNotifier

**Files to create/modify:**
- `src/mobile/lib/core/state/session_notifier.dart`
- `src/mobile/lib/core/state/user_notifier.dart`

**Acceptance:**
- [ ] `SessionNotifier`: start session, submit answer, request hint, skip question, end session, break suggestion
- [ ] WebSocket event subscription: QuestionPresented, AnswerEvaluated, MethodologySwitched, CognitiveLoadWarning, SessionSummary
- [ ] `UserNotifier`: setStudent on login, logout clears state, XP/streak updates via WebSocket
- [ ] Study energy: `remainingStudyEnergy = 50 - llmInteractionsToday`, decrement on each LLM interaction
- [ ] Providers: `sessionProvider` (auto-dispose), `userProvider` (kept alive)

**Test:**
```dart
test('SessionNotifier tracks accuracy', () {
  final notifier = SessionNotifier(webSocketService: mock, syncManager: mock);
  notifier.state = notifier.state.copyWith(questionsAttempted: 10, questionsCorrect: 7);
  expect(notifier.state.accuracy, closeTo(0.7, 0.01));
});

test('UserNotifier tracks study energy', () {
  final notifier = UserNotifier(webSocketService: mock);
  notifier.recordLlmInteraction();
  expect(notifier.state.remainingStudyEnergy, equals(49));
});
```

---

### MOB-005.2: KnowledgeGraphNotifier (Zoom/Pan)

**Files to create/modify:**
- `src/mobile/lib/core/state/knowledge_graph_notifier.dart`

**Acceptance:**
- [ ] Zoom: 0.3x to 3.0x, clamped
- [ ] Pan: offset X/Y updated via gesture detector
- [ ] Node selection: tap to select, tap again to deselect
- [ ] Subject filter: show only math or physics nodes
- [ ] Search: filter by concept name (Hebrew)
- [ ] Highlight path: BFS from roots to selected concept

**Test:**
```dart
test('Zoom clamps to valid range', () {
  final notifier = KnowledgeGraphNotifier(webSocketService: mock);
  notifier.setZoom(5.0);
  expect(notifier.state.zoomLevel, equals(3.0));
  notifier.setZoom(0.1);
  expect(notifier.state.zoomLevel, equals(0.3));
});
```

---

### MOB-005.3: OfflineNotifier + SyncManager Integration

**Files to create/modify:**
- `src/mobile/lib/core/state/offline_notifier.dart`

**Acceptance:**
- [ ] Monitors: queued event count, sync status, last sync time, connectivity
- [ ] `syncNow()` triggers immediate sync attempt
- [ ] Conflict count tracked from sync results
- [ ] Connectivity monitor integration for online/offline detection

**Test:**
```dart
test('OfflineNotifier reflects pending events', () {
  final notifier = OfflineNotifier(syncManager: mock, connectivityMonitor: mock);
  // Simulate 5 pending events
  expect(notifier.state.hasPendingEvents, isTrue);
});
```

---

### MOB-005.4: OutreachNotifier + Derived Providers

**Files to create/modify:**
- `src/mobile/lib/core/state/outreach_notifier.dart`
- `src/mobile/lib/core/state/derived_providers.dart`

**Acceptance:**
- [ ] `OutreachNotifier`: add/dismiss notifications, streak warning management
- [ ] Derived providers: `isSessionActiveProvider`, `connectionStateProvider`, `hasPendingEventsProvider`, `remainingStudyEnergyProvider`, `filteredGraphNodesProvider`, `selectedConceptProvider`
- [ ] All derived providers auto-dispose when listeners removed

**Test:**
```dart
test('OutreachNotifier tracks unread count', () {
  final notifier = OutreachNotifier();
  notifier.addNotification(AppNotification(id: '1', title: 'Review due', body: '...', createdAt: DateTime.now()));
  expect(notifier.state.unreadCount, equals(1));
  notifier.markRead('1');
  expect(notifier.state.unreadCount, equals(0));
});
```

---

## Rollback Criteria
- Fall back to StatefulWidget with setState() (loses reactivity, increases bugs)

## Definition of Done
- [ ] All 5 notifiers implemented with tests
- [ ] Derived providers working
- [ ] Study energy tracking functional
- [ ] PR reviewed by architect
