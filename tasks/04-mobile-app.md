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
**Research:** Docs 3 (CLT), 4 (Flow State), 6 (Microinteractions), 8 (SRS), 9 (Mobile UX)

**Description:**
Implement core learning loop per `session_screen.dart` with three-phase session arc
(warm-up → core challenge → cool-down) and flow state immersive mode.

**Acceptance Criteria:**
- [ ] `QuestionCard`: renders MCQ, free-text, numeric, proof (with LaTeX via flutter_math_fork)
- [ ] `AnswerInput`: polymorphic per question type (radio, text field, math keyboard)
- [ ] `FeedbackOverlay`: correct/wrong animation, NO auto-dismiss on wrong answers (user taps "Continue")
- [ ] `SessionProgressBar`: question count + colored fatigue dot (green/yellow/red)
- [ ] `ChangeApproachButton`: 4 student-friendly labels in Hebrew (+ Arabic)
- [ ] `CognitiveLoadBreakScreen`: timer + "Ready for more?" button
- [ ] RTL layout: Hebrew text wraps correctly, LaTeX stays LTR (`MathText` widget)
- [ ] **Immersive mode:** hide bottom nav bar during core challenge phase (Doc 4, 9)
- [ ] **Contiguity:** question stem + ALL options fit one viewport, NEVER split across scroll (Doc 3)
- [ ] **Wrong answer = soft orange glow:** "Let's figure this out" → constructive explanation (Doc 6)
- [ ] **Correct answer = green pulse:** bar advances, XP queued (not popup during flow) (Doc 4, 6)
- [ ] **Three-phase arc:** warm-up (P=0.80-0.90, review mastered), core (ZPD), cool-down (end on a win, P=0.85) (Doc 4)
- [ ] **Max 6-8 visible elements:** question + options + submit + progress bar + pause. No extras. (Doc 3)
- [ ] **End-on-a-win:** last question before session end has P(correct) ≈ 0.85 (Doc 4)

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
**Research:** Docs 1 (Habit Loops), 2 (Gamification), 6 (Microinteractions), 10 (Ethical)

**Description:**
Implement streaks, XP, badges per `streak_widget.dart`. Gamification must follow
Octalysis ethical allocation (45% White Hat, 25% Neutral, 20% Black Hat + escape valve).

**Acceptance Criteria:**
- [ ] `StreakCounter`: animated flame, current + longest
- [ ] `XpBar`: level progress with difficulty-weighted XP display
- [ ] `BadgeGrid` + `BadgeTile`: unlock flip animation
- [ ] `DailyGoalWidget`: circular progress ring
- [ ] `StreakFreezeButton`: freeze activation with remaining count (1 free/week, earn more)
- [ ] `VacationModeConfig`: Shabbat auto-freeze (opt-in), Hebrew holiday detection
- [ ] `StreakWarning`: max 1 notification/day (NOT live countdown)
- [ ] **Quality-gated streaks:** streak counts ONLY if ≥3 questions, avg response >5s, session >30s (Doc 1, 10)
- [ ] **Age-stratified intensity:** 12-14 = XP 2x + frequent badges; 15-17 = 1x + milestones; 18+ = mastery-focused (Doc 2)
- [ ] **`GamificationIntensitySelector`:** one-tap switch between minimal/standard/full (Doc 2)
- [ ] **No gamification chrome during session:** XP queued for post-session summary (Doc 4)

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
**Priority:** P0 (upgraded from P1)
**Blocked by:** MOB-007
**Stage:** Week 10
**Research:** Docs 7 (Onboarding), 3 (CLT), 4 (Flow State)

**Description:**
Build onboarding flow with diagnostic quiz. Time-to-value < 30 seconds.
Insert "try question" BEFORE signup — this is the #1 retention lever.

**Acceptance Criteria:**
- [ ] **"Try a Question" page BEFORE signup:** one real math MCQ → instant XP animation → proves value (Doc 7)
- [ ] Subject selection screen (Math first, Physics next)
- [ ] **Goal setting page:** Pass Bagrut / Improve / Get ahead / Uni prep + daily time commitment (Doc 7)
- [ ] **Signup AFTER value delivery:** email/Google/Apple, minimal fields (Doc 7)
- [ ] Diagnostic quiz: 10-15 KST questions (adaptive, branching)
- [ ] **Framed as exploration:** "Finding your starting point" — NEVER "testing your level" (Doc 7)
- [ ] Progress persisted to SQLite — survives app kill at question 5
- [ ] Resume from where student left off on relaunch
- [ ] **Knowledge Graph Reveal (AHA moment):** full-screen animated graph fade-in, mastered=green, weak=yellow, 5s animation (Doc 7)
- [ ] Partial data usable (3/15 questions still informs BKT priors)
- [ ] **Re-onboarding for returning users:** <7d = resume, 7-30d = recap + refresher, >30d = mini diagnostic (Doc 7)

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
**Research:** Docs 1 (Habit Loops), 10 (Ethical Persuasion)

**Description:**
Set up Firebase Cloud Messaging for streak/review notifications.
Strict ethical guardrails on timing and language.

**Acceptance Criteria:**
- [ ] FCM token registered with backend on login
- [ ] Push → deep link → resume session flow
- [ ] Notification channels: streak warning, review due, break suggestion
- [ ] **Quiet hours 21:00-07:00 (9 PM-7 AM):** ZERO notifications, client-side enforcement (Doc 10)
- [ ] Deferred notifications queue for 07:15 delivery (Doc 10)
- [ ] Notification tap opens correct screen (session, knowledge graph, etc.)
- [ ] **No shame language:** never "You're falling behind" or "Don't lose your streak!" (Doc 10)
- [ ] **Trigger migration:** weeks 1-4 = 2-3/day, weeks 4-8 = 1-2/day, week 8+ = reduce (Doc 1)
- [ ] **Max 1 streak warning/day:** no live countdowns, no guilt (Doc 1, 10)

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

---

# ═══════════════════════════════════════════════════════════════
# WAVE 1 — Foundation (Highest ROI)
# Research-derived tasks from 10 UX psychology deep-research docs
# ═══════════════════════════════════════════════════════════════

---

## MOB-017: 5th Navigation Tab — Knowledge Graph (Map)
**Priority:** P1
**Blocked by:** MOB-006
**Stage:** Wave 1
**Research:** Doc 9 (Mobile UX Patterns), Doc 3 (CLT)

**Description:**
Promote Knowledge Graph from buried feature to first-class 5th tab.
Blueprint navigation: Home | Learn | Map | Progress | Profile.
Current 4-tab layout buries the hero differentiator.

**Acceptance Criteria:**
- [ ] Bottom nav expands from 4 to 5 tabs: Home, Learn, Map, Progress, Profile
- [ ] Map tab shows Knowledge Graph (MOB-006) directly — no intermediate screen
- [ ] "New" badge on Map tab for first 15 sessions (progressive discovery, Doc 7)
- [ ] Tab icons + labels in He/Ar/En
- [ ] Tab order respects thumb zone (Map in center = easiest reach)
- [ ] Max 5 tabs — never 6+ (Cowan's 4±1, Doc 3)

**Test:**
```dart
testWidgets('bottom nav has 5 tabs with Map in center', (tester) async {
  await tester.pumpWidget(HomeScreen());
  final nav = find.byType(BottomNavigationBar);
  expect(nav, findsOneWidget);
  // Verify 5 items
  final items = tester.widget<BottomNavigationBar>(nav).items;
  expect(items.length, equals(5));
  expect(items[2].label, contains('Map')); // Center position
});
```

---

## MOB-018: Contiguity Audit — No Scroll-Split on Questions
**Priority:** P0
**Blocked by:** MOB-007
**Stage:** Wave 1
**Research:** Doc 3 (Cognitive Load Theory)

**Description:**
Audit and enforce that question stem + ALL answer options fit in one viewport
without scrolling. Contiguity violation causes 20-30% accuracy loss (split-attention effect).

**Acceptance Criteria:**
- [ ] Audit every question type (MCQ 4/5 options, free-text, numeric) across device sizes
- [ ] Responsive text sizing: min 14pt options, 16pt question stem
- [ ] If question + options exceed viewport: abbreviate question, NEVER split options across scroll
- [ ] Long-stem questions: collapsible "show full problem" above fold, options always visible
- [ ] Add runtime assertion in debug mode: warn if question card height > viewport height
- [ ] Test on smallest supported device (iPhone SE / 320dp width)

**Test:**
```dart
testWidgets('question + options fit viewport without scroll', (tester) async {
  // Use smallest device size
  tester.binding.window.physicalSizeTestValue = const Size(320, 568);
  await tester.pumpWidget(QuestionCard(question: longestMcqQuestion));
  // Verify no scrollable ancestor
  expect(find.byType(SingleChildScrollView), findsNothing);
  // Verify all options visible
  expect(find.text('A)'), findsOneWidget);
  expect(find.text('D)'), findsOneWidget);
});
```

---

## MOB-019: Session Immersive Mode
**Priority:** P0
**Blocked by:** MOB-007
**Stage:** Wave 1
**Research:** Doc 4 (Flow State), Doc 9 (Mobile UX)

**Description:**
During core challenge phase, hide everything except the question.
Flow state is sacred — any extraneous chrome breaks immersion.

**Acceptance Criteria:**
- [ ] Hide bottom navigation bar during active session
- [ ] Hide XP counter, badges, streak display
- [ ] Hide all notifications (suppress FCM locally)
- [ ] Show ONLY: question + options + submit + 4px mastery progress bar + pause icon
- [ ] No visible timer (kills flow) — use ambient progress bar (X of N)
- [ ] Re-show bottom nav on session end, pause, or exit
- [ ] When `FocusLevel.Flow` (score ≥0.8): also skip microbreak suggestions, defer XP to post-session
- [ ] Post-session: "You were in flow for [N] minutes! That's when deep learning happens."

**Test:**
```dart
testWidgets('bottom nav hidden during session', (tester) async {
  await tester.pumpWidget(SessionScreen(isActive: true));
  expect(find.byType(BottomNavigationBar), findsNothing);
  // End session
  await tester.tap(find.byIcon(Icons.close));
  await tester.pumpAndSettle();
  expect(find.byType(BottomNavigationBar), findsOneWidget);
});
```

---

# ═══════════════════════════════════════════════════════════════
# WAVE 2 — Core Psychology
# ═══════════════════════════════════════════════════════════════

---

## MOB-020: Flow Monitor Integration
**Priority:** P1
**Blocked by:** MOB-007, Backend FlowMonitorActor
**Stage:** Wave 2
**Research:** Doc 4 (Flow State Design)

**Description:**
Integrate FlowMonitorActor's per-question FocusLevel analysis into the session UI.
Dynamically adjust ZPD P(correct) targeting by focus level.

**Acceptance Criteria:**
- [ ] Receive `FocusLevel` updates from backend (Flow / Engaged / Drifting / Fatigued)
- [ ] **Flow (≥0.8):** target P(correct) 0.55-0.65, suppress all chrome
- [ ] **Engaged:** target P(correct) 0.65-0.75, standard UI
- [ ] **Drifting:** target P(correct) 0.75-0.85, ease pressure
- [ ] **Fatigued:** target P(correct) 0.85-0.90, suggest break after 2 more questions
- [ ] Pass FocusLevel to backend with each `SubmitAnswer` command
- [ ] Track flow state duration for post-session analytics
- [ ] Ambient visual cue: thin progress bar color shifts (blue=flow, green=engaged, yellow=drifting, orange=fatigued) — SUBTLE, not distracting

---

## MOB-021: 5-Tier Celebration System
**Priority:** P1
**Blocked by:** MOB-008
**Stage:** Wave 2
**Research:** Doc 6 (Microinteractions & Emotional Design)

**Description:**
Replace uniform correct-answer animations with proportional celebrations.
Over-celebrating trivial events trains students to ignore celebrations by question #20.

**Acceptance Criteria:**
- [ ] **Tier 1 (correct answer):** XP float + bounce, 900ms, soft chime, light haptic
- [ ] **Tier 2 (3-in-a-row):** XP + sparkle particles, 1.2s, ascending chime, medium haptic
- [ ] **Tier 3 (concept mastered / badge / session complete):** icon + confetti 50 particles, 2s, achievement fanfare, heavy haptic
- [ ] **Tier 4 (level up / streak 7/14/30):** confetti 100 particles + glow, 3s, extended fanfare, heavy sequence
- [ ] **Tier 5 (course mastered / 100-day streak):** full animated scene + fireworks, 5s, celebration soundtrack, custom haptic
- [ ] CelebrationOrchestratorActor determines tier based on event significance
- [ ] During FocusLevel.Flow: defer Tier 1-2 to post-session, suppress audio
- [ ] Respect `prefers-reduced-motion`: skip animation, show text-only acknowledgment
- [ ] Sounds OFF by default, toggle in settings (Doc 6)

---

## MOB-022: Interleaving Scheduler UI
**Priority:** P1
**Blocked by:** MOB-007, Backend InterleaveSchedulerActor
**Stage:** Wave 2
**Research:** Doc 8 (Learning Science & SRS)

**Description:**
Display and support interleaved topic switching within sessions.
Never blocked practice. Switch topic every 3-4 questions.

**Acceptance Criteria:**
- [ ] Question queue from backend contains interleaved topics (not same topic blocks)
- [ ] Subtle topic badge on each question card (e.g., small colored chip: "Quadratics", "Trigonometry")
- [ ] No "chapter" grouping in session — treat all topics as one flow
- [ ] If student requests "stay on topic": allow for max 5 questions, then resume interleaving
- [ ] Track topic-switch effect on accuracy for analytics

---

## MOB-024: Progressive Feature Discovery (Training Wheels)
**Priority:** P1
**Blocked by:** MOB-005, MOB-013
**Stage:** Wave 2
**Research:** Docs 3 (CLT), 7 (Onboarding)

**Description:**
Implement gradual feature unlocking so new users aren't overwhelmed.
Features unlock invisibly — students don't know features are hidden until they appear.

**Acceptance Criteria:**
- [ ] Session 1-2: questions, XP, basic feedback only
- [ ] Session 3: hint button appears (pulses after 10s inactivity) with tooltip
- [ ] Session 5: streak mechanic introduced via celebration
- [ ] Session 7: methodology switch available (bottom sheet explanation)
- [ ] Session 10: study groups unlocked (if social feature enabled) with notification
- [ ] Session 15: knowledge graph full access with "New" badge
- [ ] `ScaffoldingLevel` enum: L0 (training wheels), L1 (intermediate), L2 (full)
- [ ] ScaffoldingActor on backend tracks `sessionsCompleted` and controls visibility
- [ ] Feature gates never feel like restrictions — features "appear" naturally
- [ ] No tutorial modals or forced walkthroughs

---

## MOB-025: Momentum Meter (Streak Anxiety Alternative)
**Priority:** P2
**Blocked by:** MOB-008
**Stage:** Wave 2
**Research:** Docs 1 (Habit Loops), 10 (Ethical Persuasion)

**Description:**
Alternative to daily streaks for anxiety-prone students.
7-day rolling percentage that never reaches zero — eliminates streak-break shame.

**Acceptance Criteria:**
- [ ] `MomentumMeter` widget: circular gauge showing 7-day rolling study %
- [ ] Calculated as: (days studied in last 7) / 7 × 100
- [ ] Minimum never shows 0% — "You studied 1 of 7 days" is still 14%
- [ ] One-tap switch between Streak ↔ Momentum Meter in profile settings
- [ ] Auto-detect streak anxiety: if <30s sessions, 2AM study, declining accuracy → suggest switch
- [ ] `StreakAnxietyDetectorActor` flags student for intervention
- [ ] Growth mindset messaging: "Every day you study moves you forward"

---

## MOB-026: Haptic & Sound System
**Priority:** P2
**Blocked by:** MOB-021
**Stage:** Wave 2
**Research:** Doc 6 (Microinteractions & Emotional Design)

**Description:**
Implement consistent haptic feedback and sound design across the app.
Sounds OFF by default. Haptics follow interaction significance.

**Acceptance Criteria:**
- [ ] **Haptic mapping:**
  - MCQ option tap → `selectionClick()`
  - Submit button → `mediumImpact()`
  - Correct answer → `heavyImpact()`
  - Level-up → `heavyImpact()` × 2 (200ms gap)
  - Wrong answer → `lightImpact()` (gentle, not punitive)
- [ ] **Sound design:**
  - Correct: ascending major chord (warm, not startling)
  - Wrong: soft descending tone (neutral, not harsh)
  - Achievement: instrument-based fanfare (not chiptune — sophistication for teens)
- [ ] Ambient study music option: lo-fi instrumental, no lyrics (lyrics compete with math cognition)
- [ ] **Defaults:** sounds OFF, haptics ON
- [ ] Volume auto-reduces during questions, increases during breaks
- [ ] Settings: independent toggles for haptics and sound

---

# ═══════════════════════════════════════════════════════════════
# WAVE 3 — Social & Personalization
# ═══════════════════════════════════════════════════════════════

---

## MOB-027: Contextual Social Layer (Aggregate Class Activity)
**Priority:** P2
**Blocked by:** MOB-005, Backend PeerProgressNarrativeActor
**Stage:** Wave 3
**Research:** Doc 5 (Social Learning), Doc 10 (Ethical Persuasion)

**Description:**
Surface social proof at the right moment on existing screens.
NOT a feed or timeline. Aggregate-only, no named individuals, all opt-in.

**Acceptance Criteria:**
- [ ] **Home screen:** "Your class mastered 47 concepts this week" (aggregate, real data, Doc 5)
- [ ] **Post-answer:** "87% of students found this concept challenging at first" (normalizes struggle)
- [ ] **Progress tab:** class-level mastery heatmap (anonymized)
- [ ] **Never:** named rankings, downward comparisons, fabricated numbers (Doc 10)
- [ ] All social signals opt-in with one-tap disable
- [ ] Opt-out persists across sessions
- [ ] Social data fetched lazily (not blocking main content load)

---

## MOB-028: Peer Solution Replays
**Priority:** P2
**Blocked by:** MOB-007, Backend PeerSolutionRecommenderActor
**Stage:** Wave 3
**Research:** Doc 5 (Social Learning)

**Description:**
After student attempts a question, show how a similar-mastery peer solved it.
Lateral modeling (±0.1 P(known)) — never show expert solution as "peer."

**Acceptance Criteria:**
- [ ] Only shown AFTER student submits their own answer (never before)
- [ ] Peer solution is anonymized ("A student like you solved it this way:")
- [ ] Matched by similar mastery level (lateral peer, ±0.1 P(known))
- [ ] "Was this helpful?" thumbs up/down rating
- [ ] Target: >70% rated ≥4/5 helpfulness
- [ ] Bottom sheet on the feedback screen, below the student's own feedback
- [ ] If no peer solution available: don't show anything (never show empty state)

---

## MOB-029: "Students Like You" Success Stories
**Priority:** P2
**Blocked by:** MOB-005, Backend PeerProgressNarrativeActor
**Stage:** Wave 3
**Research:** Doc 5 (Social Learning)

**Description:**
Weekly anonymized success stories filtered by mastery similarity.
Lateral peer modeling drives motivation better than expert modeling.

**Acceptance Criteria:**
- [ ] Weekly card on home screen: anonymized peer trajectory ("A student who started at your level mastered Quadratics in 3 weeks")
- [ ] Matched by similar starting mastery, similar subjects
- [ ] No identifying information (no name, no school, no photo)
- [ ] Max 1 story/week (not spammy)
- [ ] Dismissible, opt-out available
- [ ] Expected: +10% 7-day retention (Doc 5)

---

## MOB-030: Routine Profile & Smart Notification Timing
**Priority:** P2
**Blocked by:** MOB-014, Backend RoutineProfileActor
**Stage:** Wave 3
**Research:** Doc 1 (Habit Loops & Hook Model)

**Description:**
Learn daily patterns from session timestamps: wake, commute, study, sleep.
Use detected routine to schedule notifications at the student's natural study time.

**Acceptance Criteria:**
- [ ] `RoutineProfileActor` learns study patterns from session timestamps
- [ ] Detect: morning, commute, evening, before-bed study windows
- [ ] Personalize notification timing: send at detected study-time, not arbitrary hour
- [ ] **Habit stacking suggestions:**
  - Morning: "3-min review" @ wake-time + 15 min
  - Evening: "10-20 min session" @ routine time
  - Before bed: "2-min review" by 9 PM (sleep consolidation)
- [ ] Show detected routine in Profile screen (editable overrides)
- [ ] Needs 7+ days of data before activating

---

## MOB-031: Worked-Solution Fading
**Priority:** P2
**Blocked by:** MOB-007
**Stage:** Wave 3
**Research:** Doc 8 (Learning Science & SRS)

**Description:**
Progressively hide solution steps as mastery increases.
Full scaffolding for beginners, full independence for masters.

**Acceptance Criteria:**
- [ ] **P(Known) < 0.30:** full worked solution, all steps visible
- [ ] **P(Known) 0.30-0.60:** steps 3-5 hidden, "Can you complete?" prompt
- [ ] **P(Known) 0.60-0.85:** only answer shown, student re-derives
- [ ] **P(Known) > 0.85:** no solution shown — full independent practice (Feynman mode)
- [ ] Fading level determined by backend mastery state, not client-side
- [ ] "Show full solution" override button always available (but tracked for calibration)

---

## MOB-032: Digital Wellbeing Dashboard
**Priority:** P2
**Blocked by:** MOB-005, Backend DigitalWellbeingEnforcer
**Stage:** Wave 3
**Research:** Doc 10 (Ethical Persuasion & Digital Wellbeing)

**Description:**
Study time tracking with enforced limits. Visible to student and optionally to parents.
Hard limits prevent overuse. Growth mindset messaging on breaks.

**Acceptance Criteria:**
- [ ] Daily study time tracker visible in Profile screen
- [ ] **90 min (soft):** "Research shows diminishing returns. Take a break?"
- [ ] **120 min (firm):** stronger suggestion + auto-insert cool-down session
- [ ] **180 min (hard):** session ends. "Time's up for today. Your streak is safe."
- [ ] Weekly study time chart (daily bars)
- [ ] Parental access option: read-only dashboard link (opt-in by student)
- [ ] **Break messaging = growth mindset:** "A break will help consolidation" (Doc 10)
- [ ] Evening study nudge: "It's getting late — one more review before bed?"
- [ ] All limits server-enforced via DigitalWellbeingEnforcer actor

---

## MOB-033: Gesture Language Consistency
**Priority:** P2
**Blocked by:** MOB-007, MOB-006
**Stage:** Wave 3
**Research:** Doc 6 (Microinteractions), Doc 9 (Mobile UX Patterns)

**Description:**
Implement consistent gesture vocabulary across all screens.

**Acceptance Criteria:**
- [ ] Swipe right = confident/skip (flashcard review)
- [ ] Swipe left = needs review (flashcard review)
- [ ] Swipe up = skip (question skip, with confirmation)
- [ ] Long press = bookmark any content
- [ ] Double tap = favorite
- [ ] Pinch = zoom (diagrams, knowledge graph)
- [ ] Draw gesture = handwriting math input mode
- [ ] All gestures reversible in RTL mode where appropriate (swipe directions mirror)
- [ ] Gesture hints on first encounter: translucent overlay showing swipe direction

---

# ═══════════════════════════════════════════════════════════════
# WAVE 4 — Advanced & Compliance
# ═══════════════════════════════════════════════════════════════

---

## MOB-034: FSRS Shadow Mode
**Priority:** P2
**Blocked by:** Backend SRSActor
**Stage:** Wave 4
**Research:** Doc 8 (Learning Science & SRS)

**Description:**
Run FSRS (Free Spaced Repetition Scheduler) in parallel with existing HLR.
Shadow mode: FSRS predicts review intervals but doesn't affect student experience.
Prepare for A/B test comparing HLR vs FSRS.

**Acceptance Criteria:**
- [ ] Client sends same answer events to both HLR and FSRS backends
- [ ] FSRS predictions logged but NOT used for scheduling (shadow only)
- [ ] Analytics dashboard compares HLR vs FSRS prediction accuracy
- [ ] Behavioral grading (not self-assessment): correct+fast=Easy, correct+normal=Good, correct+hints=Hard, wrong=Again
- [ ] After 8 weeks of shadow data: enable A/B test for 10% cohort
- [ ] Expected outcome: FSRS yields +10-15% retention improvement over HLR

---

## MOB-035: Peer Tutoring Matching
**Priority:** P2
**Blocked by:** MOB-028, Backend TutoringMatchmakerActor
**Stage:** Wave 4
**Research:** Doc 5 (Social Learning)

**Description:**
Match expert students (P≥0.90) with struggling peers (5+ consecutive wrong).
Async voice/text explanations. Both parties earn XP.

**Acceptance Criteria:**
- [ ] Hourly batch matching: expert ↔ struggling on same concept
- [ ] Expert records async voice or text explanation (max 2 min)
- [ ] Struggling student receives explanation with "helpful?" rating
- [ ] Both parties earn XP: expert for teaching, learner for completing review
- [ ] All content AI-moderated (Haiku pre-filter) before delivery
- [ ] Opt-in for both roles
- [ ] Expected: >60% tutee concept improvement post-tutoring

---

## MOB-036: Study Groups
**Priority:** P3
**Blocked by:** MOB-027, Backend StudyGroupCoordinatorActor
**Stage:** Wave 4
**Research:** Doc 5 (Social Learning)

**Description:**
Small groups (3-5) with shared weekly challenges and collective progress.
Teacher-created for <13, student-created for 13+.

**Acceptance Criteria:**
- [ ] Group creation: 3-5 members, subject-tagged
- [ ] Shared weekly challenges (group must collectively answer N questions)
- [ ] Collective progress bar visible to all members
- [ ] **Age-gating:** teacher-created only for <13 (COPPA, Doc 10)
- [ ] Group chat: text-only, AI-moderated, teacher can view for <13
- [ ] Leave group: one-tap, no guilt messaging
- [ ] Inactive group auto-archive after 14 days no activity

---

## MOB-037: COPPA Parental Consent Flow
**Priority:** P3
**Blocked by:** MOB-013
**Stage:** Wave 4
**Research:** Doc 10 (Ethical Persuasion & Digital Wellbeing)

**Description:**
Age gate + verifiable parental consent for students under 13.
Required for US market expansion and Israeli Amendment 13 compliance.

**Acceptance Criteria:**
- [ ] Age check during onboarding (date of birth or grade → inferred age)
- [ ] If <13: redirect to parental consent flow
- [ ] Parent email collection → verification email with consent link
- [ ] Until consent received: limited mode (no social features, no peer content, no analytics beyond essential)
- [ ] Parent dashboard link: study time, progress (read-only)
- [ ] Data minimization: collect only essential PII for <13
- [ ] Account deletion: 2-tap process for parent
- [ ] Consent records stored with timestamp for audit trail

---

## MOB-038: Metacognition & Confidence Calibration
**Priority:** P3
**Blocked by:** MOB-007, Backend MetacognitionActor
**Stage:** Wave 4
**Research:** Doc 8 (Learning Science & SRS)

**Description:**
Student predicts confidence before answering — compare to actual correctness.
Builds metacognitive awareness (knowing what you know).

**Acceptance Criteria:**
- [ ] Optional confidence slider before answer: "How confident are you?" (1-5)
- [ ] Post-answer: show predicted vs actual (calibration feedback)
- [ ] Calibration graph in Progress tab: ideal = 45° line, over-confident = above, under-confident = below
- [ ] Weekly insight card: "You tend to overestimate on Trigonometry — focus your practice there"
- [ ] Feature unlocks at Session 10+ (progressive discovery, Doc 3)
- [ ] Slider is opt-in, never forced (adds cognitive load if student doesn't want it)

---

## MOB-039: RTL Comprehensive Audit
**Priority:** P3
**Blocked by:** MOB-010
**Stage:** Wave 4
**Research:** Doc 9 (Mobile UX Patterns)

**Description:**
Full audit of every screen in both Hebrew and Arabic RTL modes.
Catch edge cases: FAB position, swipe direction, card layouts, math rendering.

**Acceptance Criteria:**
- [ ] Every screen tested in he_IL and ar locales
- [ ] FAB mirrors to bottom-left in RTL
- [ ] Card swipe gestures reverse in RTL
- [ ] Answer option alignment correct in RTL (radio buttons on right)
- [ ] LaTeX math stays LTR inside RTL text containers (MathText widget)
- [ ] Knowledge graph node labels render correctly in Arabic (Noto Sans Arabic)
- [ ] Bottom nav icon order does NOT reverse (standard Material behavior)
- [ ] Automated widget test suite for RTL: screenshot comparison tests

---

## MOB-040: Accessibility Accommodations (Beyond WCAG)
**Priority:** P3
**Blocked by:** MOB-011
**Stage:** Wave 4
**Research:** Doc 3 (Cognitive Load), Doc 11 cross-reference

**Description:**
Specific accommodations for dyslexia, ADHD, color-blindness, and motor disabilities.
Goes beyond basic WCAG AA compliance.

**Acceptance Criteria:**
- [ ] **Dyslexia:** OpenDyslexic font option, increased line spacing (1.5x), cream background option
- [ ] **ADHD:** timer opt-in (not default), reduced animation toggle, focus mode (even less chrome), chunked content
- [ ] **Color-blind:** blue/orange palette option (not red/green), pattern + icon + color (never color alone)
- [ ] **Motor:** enlarged touch targets (56dp option), swipe alternatives for all taps
- [ ] Accessibility settings screen in Profile
- [ ] Settings persist via user preferences (server-synced)
- [ ] Test with iOS VoiceOver and Android TalkBack

---

## MOB-041: Extrinsic → Intrinsic Gamification Transition
**Priority:** P3
**Blocked by:** MOB-008, MOB-024
**Stage:** Wave 4
**Research:** Doc 2 (Gamification & Motivation), Doc 8 (Learning Science)

**Description:**
Progressively reduce extrinsic motivation emphasis (XP, badges) and shift to
intrinsic indicators (mastery %, knowledge graph growth).
Prevents overjustification effect.

**Acceptance Criteria:**
- [ ] **Weeks 1-2:** heavy XP popups (scaffolding motivation, extrinsic is appropriate here)
- [ ] **Weeks 2-8:** reduce popup frequency, emphasize mastery % alongside XP
- [ ] **Week 9+:** knowledge graph growth and mastery % are primary indicators, XP secondary
- [ ] `GamificationRotationService`: progressive decay per element type
- [ ] Decay rate configurable per student via `GamificationIntensityActor`
- [ ] Never fully remove XP — just de-emphasize (smaller font, moved to profile)
- [ ] A/B test: fast decay vs slow decay vs control

---

## MOB-042: Dark Pattern Prevention & Compliance Audit
**Priority:** P3
**Blocked by:** MOB-013, MOB-014
**Stage:** Wave 4 (ongoing)
**Research:** Doc 10 (Ethical Persuasion & Digital Wellbeing)

**Description:**
Automated and manual audit of all 10 dark pattern categories.
Runs as CI gate — any dark pattern detection blocks release.

**Acceptance Criteria:**
- [ ] **Forced continuity:** cancellation equally easy as signup ✓
- [ ] **Hidden costs:** all features disclosed upfront ✓
- [ ] **Confirmshaming:** "Not now" — never "No, I don't want to learn" ✓
- [ ] **Fake urgency:** no countdown timers on pricing ✓
- [ ] **Fake social proof:** all numbers from real database queries ✓
- [ ] **Roach motel:** account deletion = 2-tap process ✓
- [ ] **Privacy zuckering:** consent opt-in per category ✓
- [ ] **Loot boxes:** no randomized paid rewards, ever ✓
- [ ] **Infinite scroll:** sessions have natural endpoints ✓
- [ ] **Manipulative notifications:** no late-night, no guilt messaging ✓
- [ ] Lint rule or test: scan all strings for shame/guilt language patterns
- [ ] Accessibility audit: no dark patterns in accessibility flows
- [ ] Quarterly manual review checklist signed off by team lead

---

## UX Psychology Tasks (MOB-030 — MOB-058)

> Tasks generated from 10 deep research documents synthesized into the [CENA Mobile UX Psychology Blueprint](../docs/mobile-research/CENA_Mobile_UX_Psychology_Blueprint.md).

### Phase 1: Foundation (Months 1-3)

| Task | Description | Effort | Impact | File |
|------|-------------|--------|--------|------|
| MOB-030 | Session Flow Arc (Warm-Up/Core/Cool-Down) | M | Critical | [MOB-030](mobile/MOB-030-session-flow-arc.md) |
| MOB-031 | FlowMonitorActor — Flow-Aware Dynamic Difficulty | L | Critical | [MOB-031](mobile/MOB-031-flow-monitor-actor.md) |
| MOB-032 | Immersive Session Mode (hide nav, DND) | S | High | [MOB-032](mobile/MOB-032-immersive-session-mode.md) |
| MOB-033 | Onboarding V2 — Try Before Signup | M | Critical | [MOB-033](mobile/MOB-033-onboarding-v2.md) |
| MOB-034 | Progressive Disclosure — 4 Levels | M | High | [MOB-034](mobile/MOB-034-progressive-disclosure.md) |
| MOB-035 | Training Wheels Mode (Sessions 1-3) | S | High | [MOB-035](mobile/MOB-035-training-wheels.md) |
| MOB-036 | SRSActor — FSRS Spaced Repetition | L | Critical | [MOB-036](mobile/MOB-036-srs-actor.md) |
| MOB-037 | Review Due Badge + Daily Review Session | M | High | [MOB-037](mobile/MOB-037-review-due-badge.md) |
| MOB-038 | Thumb Zone Audit + StatefulShellRoute | S | Medium | [MOB-038](mobile/MOB-038-thumb-zone-audit.md) |
| MOB-039 | Skeleton Screens Replacing Spinners | S | Medium | [MOB-039](mobile/MOB-039-skeleton-screens.md) |
| MOB-058 | Riverpod .select() Optimization | S | High | [MOB-058](mobile/MOB-058-riverpod-select-optimization.md) |

### Phase 2: Engagement Layer (Months 3-5)

| Task | Description | Effort | Impact | File |
|------|-------------|--------|--------|------|
| MOB-040 | Quality-Gated Streaks | M | Critical | [MOB-040](mobile/MOB-040-quality-gated-streaks.md) |
| MOB-041 | Habit Stacking — Context-Aware Sessions | L | High | [MOB-041](mobile/MOB-041-habit-stacking.md) |
| MOB-042* | Quest System — Daily/Weekly/Monthly | L | High | [MOB-042q](mobile/MOB-042-quest-system.md) |
| MOB-043 | Badge Expansion (10 → 30+) | M | Medium | [MOB-043](mobile/MOB-043-badge-expansion.md) |
| MOB-057 | Smart Notification Suppression | M | High | [MOB-057](mobile/MOB-057-notification-intelligence.md) |

*\*MOB-042 quest system is separate from the existing MOB-042 dark pattern audit above.*

### Phase 3: Social Layer (Months 5-8)

| Task | Description | Effort | Impact | File |
|------|-------------|--------|--------|------|
| MOB-044 | Class Achievement Feed + ClassActor | L | High | [MOB-044](mobile/MOB-044-class-social-feed.md) |
| MOB-045 | Age-Tiered Social Safety + ComplianceActor | L | Critical | [MOB-045](mobile/MOB-045-age-tiered-safety.md) |
| MOB-046 | Moderation Pipeline (AI + Community + Teacher) | L | Critical | [MOB-046](mobile/MOB-046-moderation-pipeline.md) |
| MOB-052 | WellbeingActor — Digital Wellbeing | M | High | [MOB-052](mobile/MOB-052-wellbeing-actor.md) |
| MOB-053 | Peer Solution Replays | L | High | [MOB-053](mobile/MOB-053-peer-solutions.md) |

### Phase 4: Advanced Intelligence (Months 8-12)

| Task | Description | Effort | Impact | File |
|------|-------------|--------|--------|------|
| MOB-047 | Confidence Calibration + MetacognitionActor | M | High | [MOB-047](mobile/MOB-047-confidence-calibration.md) |
| MOB-048 | Teach-Back Mode (Student Explanations) | L | High | [MOB-048](mobile/MOB-048-teach-back-mode.md) |
| MOB-049 | Adaptive Interleaving Probability | M | High | [MOB-049](mobile/MOB-049-adaptive-interleaving.md) |
| MOB-054 | Deep Study Mode (45-90 min blocks) | M | Medium | [MOB-054](mobile/MOB-054-deep-study-mode.md) |
| MOB-055 | Boss Battles — Narrative Assessments | L | Medium | [MOB-055](mobile/MOB-055-boss-battles.md) |

### Phase 5: Polish & Delight (Months 12-15)

| Task | Description | Effort | Impact | File |
|------|-------------|--------|--------|------|
| MOB-050 | Five-Tier Celebration System | L | Medium | [MOB-050](mobile/MOB-050-celebration-system.md) |
| MOB-051 | Sound Design System (12 effects + ambient) | M | Medium | [MOB-051](mobile/MOB-051-sound-design.md) |
| MOB-056 | Haptic Feedback Audit | S | Medium | [MOB-056](mobile/MOB-056-haptic-audit.md) |
