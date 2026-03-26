# 04 — Mobile App Tasks (Flutter)

**Technology:** Flutter 3.x, Dart, Riverpod, Drift (SQLite), web_socket_channel, freezed
**Contract files:** `contracts/mobile/lib/**/*.dart`, `contracts/mobile/pubspec.yaml`
**Stage:** Parallel with backend (Weeks 5-16)

---

## MOB-001: Flutter Project Scaffold
**Priority:** P0
**Blocked by:** None
**Stage:** Week 5

**Description:**
Initialize Flutter project with all dependencies from `pubspec.yaml`.

**Acceptance Criteria:**
- [ ] `flutter create cena_app` with `--org com.cena`
- [ ] All dependencies from `pubspec.yaml` resolve (`flutter pub get`)
- [ ] `build_runner` generates freezed/json_serializable code
- [ ] 3 font families loaded: Heebo (Hebrew), Noto Sans Arabic, Inter (Latin)
- [ ] App launches on iOS simulator and Android emulator
- [ ] RTL default direction set (`Directionality` widget wraps app)

**Test:**
```bash
flutter pub get && dart run build_runner build --delete-conflicting-outputs
flutter test  # All generated code compiles
flutter run   # App launches
```

---

## MOB-002: Domain Models (Freezed)
**Priority:** P0
**Blocked by:** MOB-001
**Stage:** Week 5

**Description:**
Implement all models from `domain_models.dart` with freezed + json_serializable.

**Acceptance Criteria:**
- [ ] 15 freezed models compile and generate: Student, Concept, MasteryState, Session, Exercise, AnswerResult, etc.
- [ ] 9 enums with `@JsonValue` annotations
- [ ] All models round-trip JSON serialization
- [ ] `Student.locale` defaults to `'he'`, supports `'ar'` and `'en'`
- [ ] `SyncRequest` and `SyncResponse` models match offline-sync-protocol spec

**Test:**
```dart
test('Student model round-trips JSON', () {
  final student = Student(id: '1', name: 'Test', locale: 'he', ...);
  final json = student.toJson();
  final restored = Student.fromJson(json);
  expect(restored, equals(student));
});

test('All enums serialize to JsonValue strings', () {
  expect(BloomLevel.application.toJson(), equals('application'));
  expect(Methodology.socratic.toJson(), equals('socratic'));
});
```

---

## MOB-003: WebSocket Service
**Priority:** P0
**Blocked by:** MOB-002
**Stage:** Week 6

**Description:**
Implement WebSocket connection to backend per `websocket_service.dart`.

**Acceptance Criteria:**
- [ ] `MessageEnvelope` discriminated union pattern matching backend SignalR format
- [ ] 7 client→server commands typed and serializable
- [ ] 9 server→client events parsed and dispatched
- [ ] Auto-reconnection with exponential backoff + jitter (1s → 30s cap)
- [ ] Heartbeat ping/pong every 30 seconds
- [ ] `ConnectionState` stream for UI binding

**Test:**
```dart
test('reconnects with exponential backoff', () async {
  final ws = WebSocketServiceImpl(url: 'wss://invalid');
  // Attempt connect → fails
  // Wait and verify retry delays: ~1s, ~2s, ~4s
  expect(ws.connectionState, emits(ConnectionState.reconnecting));
});
```

---

## MOB-004: Offline Sync Service + Durable Command Queue
**Priority:** P0
**Blocked by:** MOB-002
**Stage:** Week 6

**Description:**
Implement `OfflineEventQueue`, `SyncManager`, `DurableCommandQueue` per contracts.

**Acceptance Criteria:**
- [ ] `OfflineEventQueue`: drift/SQLite-backed, survives app crash
- [ ] `DurableCommandQueue`: every outgoing command persisted to SQLite BEFORE sending
- [ ] `SyncManager`: 6-step handshake (request → classify → submit → corrections)
- [ ] Three-tier classification: Unconditional / Conditional / ServerAuthoritative
- [ ] `ClockSkewDetector`: NTP-style offset estimation
- [ ] `IdempotencyKey`: generated per event, used by server for dedup

**Test:**
```dart
test('command queue survives simulated crash', () async {
  final queue = DurableCommandQueue(db: testDb);
  await queue.enqueueCommand(OutgoingCommand(type: 'SubmitAnswer', ...));
  // Simulate crash: close and reopen DB
  final restored = DurableCommandQueue(db: testDb);
  final pending = await restored.getPendingCommands();
  expect(pending.length, equals(1));
});

test('sync deduplicates on retry', () async {
  final events = generateOfflineEvents(5);
  await syncManager.syncNow(events);
  await syncManager.syncNow(events); // retry
  // Server should see 5 events, not 10
});
```

---

## MOB-005: State Management (Riverpod)
**Priority:** P0
**Blocked by:** MOB-003, MOB-004
**Stage:** Week 7

**Description:**
Implement all 5 Riverpod notifiers per `app_state.dart`.

**Acceptance Criteria:**
- [ ] `SessionNotifier`: active session, current question, methodology, fatigue score
- [ ] `KnowledgeGraphNotifier`: nodes, mastery overlay, zoom/pan (local TransformationController)
- [ ] `UserNotifier`: profile, XP, streak, study energy (50/day LLM cap)
- [ ] `OfflineNotifier`: queue count, sync status, last sync
- [ ] `OutreachNotifier`: pending notifications, streak warnings
- [ ] No unnecessary rebuilds: zoom/pan is LOCAL state, not in Riverpod

**Test:**
```dart
test('SessionNotifier handles answer flow', () {
  final notifier = SessionNotifier(...);
  notifier.submitAnswer('question-1', 'cos(x)');
  expect(notifier.state.questionsAttempted, equals(1));
});

test('UserNotifier tracks study energy', () {
  final notifier = UserNotifier(...);
  notifier.consumeEnergy(1);
  expect(notifier.state.studyEnergyRemaining, equals(49));
});
```

---

## MOB-006: Knowledge Graph Widget
**Priority:** P1 — hero feature
**Blocked by:** MOB-005
**Stage:** Week 8-10

**Description:**
Implement interactive knowledge graph per `knowledge_graph_widget.dart`.

**Acceptance Criteria:**
- [ ] Single `CustomPainter` for all nodes (NOT 2000 individual widgets)
- [ ] Viewport culling via quadtree: only paint visible nodes (target: 60fps with 2000 nodes)
- [ ] Mastery color coding: green=mastered, yellow=in-progress, gray=unknown
- [ ] Tap node → concept detail bottom sheet
- [ ] Animated mastery transition (node pulses when mastered)
- [ ] Subject color palette per `SubjectDiagramPalette`
- [ ] `KnowledgeGraphSemantics` accessibility overlay for screen readers
- [ ] `MasteryAccessibilityLabel` in Hebrew, Arabic, English

**Test:**
```dart
testWidgets('knowledge graph renders 500 nodes at 60fps', (tester) async {
  await tester.pumpWidget(InteractiveKnowledgeGraph(
    graph: generateTestGraph(500),
  ));
  // Verify CustomPainter paints (not 500 widgets)
  expect(find.byType(CustomPaint), findsOneWidget);
  // Performance: measure frame time during pan gesture
});

test('accessibility labels support Arabic', () {
  expect(
    MasteryAccessibilityLabel.forMastery(0.90, locale: 'ar'),
    equals('مُتقَن'),
  );
});
```

---

## MOB-007: Session Screen
**Priority:** P0
**Blocked by:** MOB-005, MOB-003
**Stage:** Week 7-8

**Description:**
Implement core learning loop per `session_screen.dart`.

**Acceptance Criteria:**
- [ ] `QuestionCard`: renders MCQ, free-text, numeric, proof (with LaTeX via flutter_math_fork)
- [ ] `AnswerInput`: polymorphic per question type (radio, text field, math keyboard)
- [ ] `FeedbackOverlay`: correct/wrong animation, NO auto-dismiss on wrong answers (user taps "Continue")
- [ ] `SessionProgressBar`: question count + colored fatigue dot (green/yellow/red)
- [ ] `ChangeApproachButton`: 4 student-friendly labels in Hebrew (+ Arabic)
- [ ] `CognitiveLoadBreakScreen`: timer + "Ready for more?" button
- [ ] RTL layout: Hebrew text wraps correctly, LaTeX stays LTR (`MathText` widget)

**Test:**
```dart
testWidgets('wrong answer waits for user tap', (tester) async {
  await tester.pumpWidget(FeedbackOverlay(isCorrect: false, ...));
  await tester.pump(Duration(seconds: 10)); // Wait longer than any auto-dismiss
  expect(find.byType(FeedbackOverlay), findsOneWidget); // Still visible
  await tester.tap(find.text('המשך')); // Hebrew "Continue"
  await tester.pumpAndSettle();
  expect(find.byType(FeedbackOverlay), findsNothing); // Now dismissed
});
```

---

## MOB-008: Gamification Widgets
**Priority:** P2
**Blocked by:** MOB-005
**Stage:** Week 11-12

**Description:**
Implement streaks, XP, badges per `streak_widget.dart`.

**Acceptance Criteria:**
- [ ] `StreakCounter`: animated flame, current + longest
- [ ] `XpBar`: level progress with difficulty-weighted XP display
- [ ] `BadgeGrid` + `BadgeTile`: unlock flip animation
- [ ] `DailyGoalWidget`: circular progress ring
- [ ] `StreakFreezeButton`: freeze activation with remaining count
- [ ] `VacationModeConfig`: Shabbat auto-freeze (opt-in), Hebrew holiday detection
- [ ] `StreakWarning`: max 1 notification/day (NOT live countdown)

**Test:**
```dart
test('streak freeze activates correctly', () {
  final config = VacationModeConfig(freezesRemaining: 2);
  // Activate freeze
  expect(config.freezesRemaining, equals(2));
  // After activation: 1 remaining
});

test('Shabbat detection', () {
  // Friday 18:00 Israel time → Shabbat started
  final config = VacationModeConfig(shabbatAutoFreeze: true);
  expect(config.isAutoFreezeDay(fridayEvening), isTrue);
  expect(config.isAutoFreezeDay(sundayMorning), isFalse);
});
```

---

## MOB-009: Diagram Viewer + Cache
**Priority:** P2
**Blocked by:** MOB-006
**Stage:** Week 11-12

**Description:**
Implement diagram display and caching per `diagram_models.dart`.

**Acceptance Criteria:**
- [ ] `ConceptDiagram` renders SVG (via flutter_svg) with interactive hotspots
- [ ] Tap hotspot → reveal explanation (localized: He/Ar/En)
- [ ] `ChallengeCard`: game-style question with diagram
- [ ] `DiagramCacheService`: pre-fetches frontier concepts, evicts stale
- [ ] Cache budget: max 50MB per subject
- [ ] Offline: diagrams served from cache without network

**Test:**
```dart
test('diagram cache serves offline', () async {
  await cacheService.prefetchForFrontier(conceptIds: ['c1', 'c2']);
  // Go offline
  final diagram = await cacheService.getCachedDiagram(conceptId: 'c1');
  expect(diagram, isNotNull);
  expect(diagram!.inlineSvg, isNotNull);
});
```

---

## MOB-010: i18n (Hebrew + Arabic + English)
**Priority:** P1
**Blocked by:** MOB-001
**Stage:** Week 9

**Description:**
Set up full internationalization with 3 locales.

**Acceptance Criteria:**
- [ ] `AppLocales.supported` = [he_IL, ar, en_US]
- [ ] `AppLocales.isRtl(locale)` returns true for he and ar
- [ ] ARB files for all 3 locales with flutter_localizations
- [ ] Font switching: `fontFamilyForLocale()` returns Heebo/NotoSansArabic/Inter
- [ ] All UI strings externalized (no hardcoded Hebrew in widgets)
- [ ] RTL layout correct for both Hebrew and Arabic

**Test:**
```dart
test('Arabic is RTL', () {
  expect(AppLocales.isRtl(AppLocales.arabic), isTrue);
});

test('font family resolves correctly', () {
  expect(TypographyTokens.fontFamilyForLocale('ar'), equals('Noto Sans Arabic'));
  expect(TypographyTokens.fontFamilyForLocale('he'), equals('Heebo'));
});
```

---

## MOB-011: Accessibility (WCAG 2.1 AA)
**Priority:** P1
**Blocked by:** MOB-006, MOB-007
**Stage:** Week 13-14

**Description:**
Add accessibility per Israeli law requirements.

**Acceptance Criteria:**
- [ ] `KnowledgeGraphSemantics`: list-based overlay for screen readers
- [ ] All interactive widgets have `semanticLabel`
- [ ] Mastery status conveyed by shape + text (not just color)
- [ ] Touch targets >= 48dp
- [ ] Dynamic text scaling: no fixed font sizes (use `Theme.of(context).textTheme`)
- [ ] `announceForAccessibility` on mastery, methodology switch, break suggestion

**Test:**
```dart
testWidgets('screen reader can traverse knowledge graph', (tester) async {
  await tester.pumpWidget(KnowledgeGraphSemantics(...));
  final semantics = tester.getSemantics(find.byType(KnowledgeGraphSemantics));
  // Verify each concept has a semantic label
  expect(semantics.label, contains('נשלט')); // "Mastered" in Hebrew
});
```

---

## MOB-012: Analytics Service
**Priority:** P3
**Blocked by:** MOB-005
**Stage:** Week 13

**Description:**
Implement privacy-safe analytics per `analytics_service.dart`.

**Acceptance Criteria:**
- [ ] 10 event types tracked (SessionStart, QuestionAttempt, Mastery, etc.)
- [ ] All PII hashed with SHA-256 + per-install salt
- [ ] Batch upload on connectivity restore
- [ ] Local purge after successful upload
- [ ] Performance metrics: app launch time, graph render FPS, sync duration

**Test:**
```dart
test('PII is hashed before logging', () {
  final event = QuestionAttemptEvent(studentId: 'real-id');
  final logged = analyticsService.prepare(event);
  expect(logged.studentId, isNot(equals('real-id')));
  expect(logged.studentId.length, equals(64)); // SHA-256
});
```

---

## MOB-013: Onboarding + Diagnostic Quiz
**Priority:** P1
**Blocked by:** MOB-007
**Stage:** Week 10

**Description:**
Build onboarding flow with diagnostic quiz.

**Acceptance Criteria:**
- [ ] Subject selection screen (Math first, Physics next)
- [ ] Diagnostic quiz: 10-15 KST questions (adaptive, branching)
- [ ] Progress persisted to SQLite — survives app kill at question 5
- [ ] Resume from where student left off on relaunch
- [ ] Results screen: knowledge graph reveal with initial mastery overlay
- [ ] Partial data usable (3/15 questions still informs BKT priors)

**Test:**
```dart
test('diagnostic quiz survives app kill', () async {
  // Start quiz, answer 5 questions
  await quizService.submitAnswer(question5, answer);
  // Kill and restore
  final restored = await quizService.getProgress();
  expect(restored.questionsAnswered, equals(5));
  expect(restored.partialResults.length, equals(5));
});
```

---

## MOB-014: Push Notifications
**Priority:** P2
**Blocked by:** MOB-001
**Stage:** Week 11

**Description:**
Set up Firebase Cloud Messaging for streak/review notifications.

**Acceptance Criteria:**
- [ ] FCM token registered with backend on login
- [ ] Push → deep link → resume session flow
- [ ] Notification channels: streak warning, review due, break suggestion
- [ ] Respects quiet hours (22:00-07:00) — handled server-side, but client suppresses too
- [ ] Notification tap opens correct screen (session, knowledge graph, etc.)

**Test:**
```dart
test('notification deep link navigates to session', () async {
  final payload = {'type': 'ReviewDue', 'conceptId': 'c1'};
  await notificationHandler.onTap(payload);
  expect(navigator.currentRoute, equals('/session?concept=c1'));
});
```

---

## MOB-015: MathText Widget (RTL + LTR Bidi)
**Priority:** P1
**Blocked by:** MOB-001
**Stage:** Week 8

**Description:**
Build a widget that handles mixed Hebrew/Arabic RTL text with LTR LaTeX islands.

**Acceptance Criteria:**
- [ ] Parses `$...$` delimiters to split into text segments and math segments
- [ ] Text segments: `Directionality(textDirection: TextDirection.rtl)`
- [ ] Math segments: `Directionality(textDirection: TextDirection.ltr)` with flutter_math_fork
- [ ] Works in QuestionCard, FeedbackOverlay, and MCQ options
- [ ] Arabic text also handled as RTL

**Test:**
```dart
testWidgets('MathText renders mixed Hebrew and LaTeX', (tester) async {
  await tester.pumpWidget(MathText('מצא את $x$ כאשר $x^2 + 3x - 4 = 0$'));
  // Hebrew "Find x when" is RTL
  // Math expressions are LTR
  expect(find.byType(Math), findsNWidgets(2)); // Two LaTeX segments
});
```

---

## MOB-016: App Size Optimization
**Priority:** P3
**Blocked by:** MOB-001
**Stage:** Week 15

**Description:**
Optimize APK/IPA size to stay under 50MB download.

**Acceptance Criteria:**
- [ ] `--split-per-abi` enabled for Play Store (separate arm64/armeabi)
- [ ] Font subsetting: JetBrains Mono subset to digits + math operators
- [ ] Evaluate: can `rive` be replaced with `flutter_animate` for node effects? (saves ~2-3MB)
- [ ] Evaluate: can `graphql_flutter` be replaced with `dio` + manual queries? (saves ~1.5MB)
- [ ] Target: < 40MB download size per ABI
- [ ] Tree shaking: `--no-tree-shake-icons` removed, unused icons pruned

**Test:**
```bash
flutter build apk --release --split-per-abi
# Check arm64 APK size
ls -la build/app/outputs/flutter-apk/app-arm64-v8a-release.apk
# Assert < 40MB
```
