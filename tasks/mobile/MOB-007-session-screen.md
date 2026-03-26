# MOB-007: Session Screen (Core UX Loop)

**Priority:** P0 — this IS the learning experience
**Blocked by:** MOB-005 (Riverpod state), MOB-002 (domain models)
**Estimated effort:** 7 days
**Contract:** `contracts/mobile/lib/features/session/session_screen.dart`

---

## Context
The session screen is Cena's core UX: question -> answer -> feedback -> next question. It renders adaptive questions from the server, accepts answers in 5 different input formats (MCQ, free text, numeric, proof, diagram), shows animated feedback overlays, tracks cognitive load, and manages break suggestions. All text must render in Hebrew RTL (primary), Arabic RTL, or English LTR based on student locale. LaTeX math expressions are rendered inline via `flutter_math_fork`. The screen listens to `SessionNotifier` for state changes and displays `FeedbackOverlay` after each answer evaluation.

## Subtasks

### MOB-007.1: SessionScreen Container & Progress Bar
**Files:**
- `lib/features/session/session_screen.dart`
- `lib/features/session/widgets/session_progress_bar.dart`
- `lib/features/session/widgets/methodology_indicator.dart`

**Acceptance:**
- [ ] `SessionScreen extends ConsumerStatefulWidget` with parameters: `subject` (optional), `durationMinutes` (default 25)
- [ ] `initState` starts a session via `sessionNotifier.startSession()`
- [ ] Layout follows contract diagram: ProgressBar -> MethodologyIndicator -> QuestionCard -> AnswerInput -> action buttons
- [ ] `_questionStopwatch` tracks time spent on current question (reset on each new question)
- [ ] `SessionProgressBar extends ConsumerWidget`:
  - Linear progress of `questionsAttempted / maxQuestionsPerSession`
  - Fatigue overlay: red tint that increases proportionally with `fatigueScore`
  - Timer text showing elapsed time
  - Accuracy sparkline of last 10 answers from `sessionHistory`
- [ ] `MethodologyIndicator extends StatelessWidget`:
  - Shows student-friendly label (NOT internal methodology name): "Review Mode" / "מצב חזרה", "Mix It Up" / "מצב מעורב", "Deep Focus" / "מצב התמקדות", "Challenge Mode" / "מצב אתגר", "Guided Discovery" / "מצב גילוי מודרך"
  - Icon per methodology: `Icons.replay`, `Icons.shuffle`, `Icons.center_focus_strong`, `Icons.trending_up`, `Icons.lightbulb_outline`
  - Subtle chip/badge design, non-distracting
- [ ] RTL layout: all widgets use `Directionality.of(context)` for Hebrew/Arabic

**Test:**
```dart
testWidgets('SessionScreen starts session on init', (tester) async {
  await tester.pumpWidget(
    ProviderScope(
      overrides: testOverrides,
      child: MaterialApp(
        home: SessionScreen(subject: Subject.math),
      ),
    ),
  );

  verify(() => mockWs.startSession(any())).called(1);
});

testWidgets('SessionProgressBar shows fatigue overlay', (tester) async {
  final container = ProviderContainer(overrides: testOverrides);
  container.read(sessionProvider.notifier).state = SessionState(
    fatigueScore: 0.6,
    questionsAttempted: 15,
  );

  await tester.pumpWidget(
    UncontrolledProviderScope(
      container: container,
      child: MaterialApp(home: Scaffold(body: SessionProgressBar())),
    ),
  );

  // Verify fatigue overlay exists with opacity proportional to fatigueScore
  final opacity = tester.widget<Opacity>(find.byType(Opacity));
  expect(opacity.opacity, closeTo(0.6, 0.1));
});

testWidgets('MethodologyIndicator shows Hebrew label', (tester) async {
  await tester.pumpWidget(
    MaterialApp(
      locale: const Locale('he', 'IL'),
      home: Scaffold(body: MethodologyIndicator(methodology: Methodology.spacedRepetition)),
    ),
  );
  expect(find.text('מצב חזרה'), findsOneWidget);
});

testWidgets('MethodologyIndicator shows correct icon per methodology', (tester) async {
  for (final entry in {
    Methodology.spacedRepetition: Icons.replay,
    Methodology.interleaved: Icons.shuffle,
    Methodology.blocked: Icons.center_focus_strong,
    Methodology.adaptiveDifficulty: Icons.trending_up,
    Methodology.socratic: Icons.lightbulb_outline,
  }.entries) {
    await tester.pumpWidget(
      MaterialApp(home: Scaffold(body: MethodologyIndicator(methodology: entry.key))),
    );
    expect(find.byIcon(entry.value), findsOneWidget);
  }
});
```

**Edge Cases:**
- Session fails to start (network error) — show error state with "Retry" button
- Student navigates away mid-session — prompt "End session?" confirmation dialog
- Very long session (>30 min) — progress bar handles values > 1.0 gracefully (clamp)

---

### MOB-007.2: QuestionCard with LaTeX & Bidi Text
**Files:**
- `lib/features/session/widgets/question_card.dart`
- `lib/features/session/widgets/math_text.dart`

**Acceptance:**
- [ ] `QuestionCard extends StatelessWidget` with parameters: `exercise`, `currentHintLevel`, `hintsAvailable`
- [ ] Renders question content with inline LaTeX: text segments between `$...$` rendered via `flutter_math_fork`, non-math text rendered as `Text` widget
- [ ] `MathText` widget: parses mixed Hebrew/Arabic text + LaTeX, handles bidi:
  - Hebrew/Arabic text segments: `TextDirection.rtl`
  - LaTeX math segments: `TextDirection.ltr` (math is always LTR)
  - Mixed line: `Wrap` widget with appropriate directionality per segment
- [ ] Question number badge (top-left in LTR, top-right in RTL)
- [ ] Difficulty indicator: subtle dots (1-10) in top corner
- [ ] Optional diagram: displayed via `CachedNetworkImage` or `SvgPicture` based on URL extension
- [ ] Hints displayed in expandable tiles (revealed progressively based on `currentHintLevel`)
- [ ] Card respects `TextScaler` for dynamic font scaling (accessibility)

**Test:**
```dart
testWidgets('QuestionCard renders LaTeX math', (tester) async {
  await tester.pumpWidget(MaterialApp(
    home: Scaffold(
      body: QuestionCard(
        exercise: Exercise(
          id: 'ex-001',
          conceptId: 'c-001',
          questionType: QuestionType.freeText,
          difficulty: 5,
          content: r'פתור: $2x + 3 = 7$',
        ),
      ),
    ),
  ));

  // Math widget rendered
  expect(find.byType(Math), findsOneWidget);
  // Hebrew text rendered
  expect(find.textContaining('פתור'), findsOneWidget);
});

testWidgets('QuestionCard shows hints progressively', (tester) async {
  await tester.pumpWidget(MaterialApp(
    home: Scaffold(
      body: QuestionCard(
        exercise: Exercise(
          id: 'ex-001',
          conceptId: 'c-001',
          questionType: QuestionType.freeText,
          difficulty: 5,
          content: 'Test question',
          hints: ['Hint 1', 'Hint 2', 'Hint 3'],
        ),
        currentHintLevel: 2,
        hintsAvailable: ['Hint 1', 'Hint 2', 'Hint 3'],
      ),
    ),
  ));

  // First 2 hints visible
  expect(find.text('Hint 1'), findsOneWidget);
  expect(find.text('Hint 2'), findsOneWidget);
  // Third hint NOT visible (currentHintLevel = 2, 0-indexed means hints 0 and 1)
});

testWidgets('MathText handles bidi mixed content', (tester) async {
  await tester.pumpWidget(MaterialApp(
    home: Directionality(
      textDirection: TextDirection.rtl,
      child: Scaffold(
        body: MathText(text: r'נגזרת של $f(x) = x^2$ היא $f\'(x) = 2x$'),
      ),
    ),
  ));

  // Multiple Math widgets for each LaTeX segment
  expect(find.byType(Math), findsNWidgets(2));
});

testWidgets('QuestionCard shows diagram when present', (tester) async {
  await tester.pumpWidget(MaterialApp(
    home: Scaffold(
      body: QuestionCard(
        exercise: Exercise(
          id: 'ex-001',
          conceptId: 'c-001',
          questionType: QuestionType.diagram,
          difficulty: 5,
          content: 'Identify the extrema',
          diagram: 'https://cdn.cena.education/diagrams/function-plot.svg',
        ),
      ),
    ),
  ));

  expect(find.byType(SvgPicture), findsOneWidget);
});
```

**Edge Cases:**
- Malformed LaTeX (unclosed `$`) — render as plain text, do not crash
- Very long question text (>500 chars) — scroll within the card
- Hebrew text with embedded English technical terms — bidi algorithm handles correctly
- Question with no hints — hint section hidden entirely
- RTL question with LTR math: `$2x + 3 = 7$` renders left-to-right inside right-to-left text

---

### MOB-007.3: AnswerInput (All Question Types)
**Files:**
- `lib/features/session/widgets/answer_input.dart`
- `lib/features/session/widgets/mcq_answer_input.dart`
- `lib/features/session/widgets/free_text_answer_input.dart`
- `lib/features/session/widgets/numeric_answer_input.dart`
- `lib/features/session/widgets/diagram_answer_input.dart`

**Acceptance:**
- [ ] `AnswerInput extends StatefulWidget` — polymorphic dispatcher based on `questionType`:
  - `QuestionType.multipleChoice` -> `McqAnswerInput`
  - `QuestionType.freeText` -> `FreeTextAnswerInput`
  - `QuestionType.numeric` -> `NumericAnswerInput`
  - `QuestionType.proof` -> `FreeTextAnswerInput` (with structured mode, later)
  - `QuestionType.diagram` -> `DiagramAnswerInput`
- [ ] `McqAnswerInput`: `RadioListTile` group with Hebrew option text, submit button enabled only when option selected, `_selectedIndex` state
- [ ] `FreeTextAnswerInput`: multiline `TextField` with RTL support, configurable placeholder, submit button enabled when non-empty
- [ ] `NumericAnswerInput`: numeric keyboard, optional fraction support (`allowFractions`), optional unit label display
- [ ] `DiagramAnswerInput`: `CustomPaint` canvas with touch drawing, undo/redo, color picker, eraser, background image support, submit serializes as base64 PNG
- [ ] All inputs call `onSubmit(String answer)` on submission
- [ ] All inputs are disabled when `isSubmitting = true`
- [ ] Submit button shows `CircularProgressIndicator` when `isSubmitting`

**Test:**
```dart
testWidgets('McqAnswerInput enables submit after selection', (tester) async {
  String? submitted;
  await tester.pumpWidget(MaterialApp(
    home: Scaffold(
      body: McqAnswerInput(
        options: ['x = 1', 'x = 2', 'x = 3'],
        onSubmit: (answer) => submitted = answer,
      ),
    ),
  ));

  // Submit button initially disabled
  final submitButton = find.widgetWithText(ElevatedButton, 'Submit');
  expect(tester.widget<ElevatedButton>(submitButton).enabled, isFalse);

  // Select second option
  await tester.tap(find.text('x = 2'));
  await tester.pump();

  // Submit button now enabled
  expect(tester.widget<ElevatedButton>(submitButton).enabled, isTrue);

  // Tap submit
  await tester.tap(submitButton);
  expect(submitted, equals('1')); // index of selected option
});

testWidgets('FreeTextAnswerInput handles RTL Hebrew text', (tester) async {
  String? submitted;
  await tester.pumpWidget(MaterialApp(
    locale: const Locale('he', 'IL'),
    home: Directionality(
      textDirection: TextDirection.rtl,
      child: Scaffold(
        body: FreeTextAnswerInput(
          onSubmit: (answer) => submitted = answer,
          placeholder: 'כתוב את תשובתך',
        ),
      ),
    ),
  ));

  await tester.enterText(find.byType(TextField), 'התשובה שלי');
  await tester.tap(find.widgetWithText(ElevatedButton, 'שלח'));
  expect(submitted, equals('התשובה שלי'));
});

testWidgets('NumericAnswerInput shows unit label', (tester) async {
  await tester.pumpWidget(MaterialApp(
    home: Scaffold(
      body: NumericAnswerInput(
        onSubmit: (_) {},
        unit: 'm/s',
      ),
    ),
  ));

  expect(find.text('m/s'), findsOneWidget);
});

testWidgets('AnswerInput dispatches to correct widget type', (tester) async {
  await tester.pumpWidget(MaterialApp(
    home: Scaffold(
      body: AnswerInput(
        questionType: QuestionType.multipleChoice,
        options: ['A', 'B', 'C'],
        onSubmit: (_) {},
      ),
    ),
  ));
  expect(find.byType(McqAnswerInput), findsOneWidget);

  await tester.pumpWidget(MaterialApp(
    home: Scaffold(
      body: AnswerInput(
        questionType: QuestionType.numeric,
        options: null,
        onSubmit: (_) {},
      ),
    ),
  ));
  expect(find.byType(NumericAnswerInput), findsOneWidget);
});

testWidgets('all inputs disabled when isSubmitting', (tester) async {
  await tester.pumpWidget(MaterialApp(
    home: Scaffold(
      body: FreeTextAnswerInput(
        onSubmit: (_) {},
        isSubmitting: true,
      ),
    ),
  ));

  final textField = tester.widget<TextField>(find.byType(TextField));
  expect(textField.enabled, isFalse);
  expect(find.byType(CircularProgressIndicator), findsOneWidget);
});
```

**Edge Cases:**
- MCQ with 0 options (server error) — show error message instead of empty list
- Numeric input with commas (European locale) — normalize decimal separator
- DiagramAnswerInput on devices without stylus — support finger drawing with sufficient hit area
- Very long free text answer — enforce max length (configurable per question)
- Question type changes mid-session (new question) — AnswerInput rebuilds with correct subtype

---

### MOB-007.4: FeedbackOverlay
**Files:**
- `lib/features/session/widgets/feedback_overlay.dart`

**Acceptance:**
- [ ] `FeedbackOverlay extends StatefulWidget` with `SingleTickerProviderStateMixin`
- [ ] Parameters: `result` (AnswerResult), `onDismiss` (VoidCallback), `displayDuration` (default 4 seconds)
- [ ] Animated slide-up from bottom (500ms animation)
- [ ] Correct answer: green checkmark with scale animation, XP earned badge, brief positive feedback text
- [ ] Wrong answer: red X with scale animation, error type label, feedback text, optional "Show Solution" button (if `workedSolution` is not null)
- [ ] Mastery delta visualization: horizontal bar showing `priorMastery` -> `posteriorMastery` change
- [ ] Auto-dismisses after `displayDuration` via `Timer`
- [ ] Tap anywhere to dismiss immediately
- [ ] `_autoDismissTimer` is canceled in `dispose()` to prevent use-after-dispose

**Test:**
```dart
testWidgets('FeedbackOverlay shows green for correct answer', (tester) async {
  await tester.pumpWidget(MaterialApp(
    home: Scaffold(
      body: FeedbackOverlay(
        result: AnswerResult(
          isCorrect: true,
          errorType: ErrorType.none,
          priorMastery: 0.65,
          posteriorMastery: 0.78,
          feedback: '!נכון',
          xpEarned: 10,
        ),
        onDismiss: () {},
      ),
    ),
  ));

  await tester.pump(Duration(milliseconds: 250)); // mid-animation
  expect(find.byIcon(Icons.check_circle), findsOneWidget);
  expect(find.text('!נכון'), findsOneWidget);
  expect(find.text('+10 XP'), findsOneWidget);
});

testWidgets('FeedbackOverlay shows error type for wrong answer', (tester) async {
  await tester.pumpWidget(MaterialApp(
    home: Scaffold(
      body: FeedbackOverlay(
        result: AnswerResult(
          isCorrect: false,
          errorType: ErrorType.conceptual,
          priorMastery: 0.65,
          posteriorMastery: 0.58,
          feedback: 'יש לך שגיאה מושגית',
          workedSolution: r'$2x + 3 = 7 \Rightarrow x = 2$',
        ),
        onDismiss: () {},
      ),
    ),
  ));

  await tester.pumpAndSettle();
  expect(find.byIcon(Icons.cancel), findsOneWidget);
  expect(find.textContaining('שגיאה מושגית'), findsOneWidget);
  expect(find.text('Show Solution'), findsOneWidget);
});

testWidgets('FeedbackOverlay auto-dismisses after duration', (tester) async {
  bool dismissed = false;
  await tester.pumpWidget(MaterialApp(
    home: Scaffold(
      body: FeedbackOverlay(
        result: _correctResult(),
        onDismiss: () => dismissed = true,
        displayDuration: Duration(seconds: 2),
      ),
    ),
  ));

  await tester.pump(Duration(seconds: 1));
  expect(dismissed, isFalse);

  await tester.pump(Duration(seconds: 2));
  expect(dismissed, isTrue);
});

testWidgets('FeedbackOverlay dismisses on tap', (tester) async {
  bool dismissed = false;
  await tester.pumpWidget(MaterialApp(
    home: Scaffold(
      body: FeedbackOverlay(
        result: _correctResult(),
        onDismiss: () => dismissed = true,
      ),
    ),
  ));

  await tester.pumpAndSettle();
  await tester.tap(find.byType(FeedbackOverlay));
  expect(dismissed, isTrue);
});

testWidgets('mastery delta bar shows progression', (tester) async {
  await tester.pumpWidget(MaterialApp(
    home: Scaffold(
      body: FeedbackOverlay(
        result: AnswerResult(
          isCorrect: true,
          errorType: ErrorType.none,
          priorMastery: 0.65,
          posteriorMastery: 0.78,
          feedback: 'Good',
        ),
        onDismiss: () {},
      ),
    ),
  ));

  await tester.pumpAndSettle();
  // Find the mastery bar widget
  expect(find.textContaining('65%'), findsOneWidget);
  expect(find.textContaining('78%'), findsOneWidget);
});
```

**Edge Cases:**
- `onDismiss` called after widget is disposed (timer fires after navigation) — guard with `mounted` check
- `workedSolution` contains LaTeX — render via `MathText` widget from MOB-007.2
- Very long feedback text — scroll within the overlay
- `xpEarned == 0` — hide XP badge

---

### MOB-007.5: CognitiveLoadBreak Screen
**Files:**
- `lib/features/session/widgets/cognitive_load_break_screen.dart`

**Acceptance:**
- [ ] `CognitiveLoadBreakScreen extends ConsumerWidget` with: `suggestedBreakMinutes`, `onReady` callback, optional `message`
- [ ] Calming color palette (soft blues/greens, distinct from session screen)
- [ ] Countdown timer circle showing remaining break time
- [ ] Friendly Hebrew message encouraging a break (default or from `message` parameter)
- [ ] Session stats: questions answered, accuracy from `SessionState`
- [ ] "Ready for more?" `ElevatedButton` — calls `onReady`, available immediately (timer is a suggestion, not a lock)
- [ ] Optional breathing exercise animation (expanding/contracting circle, 4-7-8 rhythm)
- [ ] Displayed when `SessionState.isBreakSuggested == true`
- [ ] `sessionNotifier.dismissBreakSuggestion()` called when student dismisses

**Test:**
```dart
testWidgets('CognitiveLoadBreakScreen shows countdown', (tester) async {
  await tester.pumpWidget(MaterialApp(
    home: CognitiveLoadBreakScreen(
      suggestedBreakMinutes: 5,
      onReady: () {},
    ),
  ));

  expect(find.textContaining('5:00'), findsOneWidget);
  await tester.pump(Duration(seconds: 60));
  expect(find.textContaining('4:00'), findsOneWidget);
});

testWidgets('Ready button is immediately available', (tester) async {
  bool ready = false;
  await tester.pumpWidget(
    ProviderScope(
      overrides: testOverrides,
      child: MaterialApp(
        home: CognitiveLoadBreakScreen(
          suggestedBreakMinutes: 5,
          onReady: () => ready = true,
        ),
      ),
    ),
  );

  // Button exists from the start
  final button = find.text('Ready for more?');
  expect(button, findsOneWidget);

  await tester.tap(button);
  expect(ready, isTrue);
});

testWidgets('break screen shows session stats', (tester) async {
  final container = ProviderContainer(overrides: testOverrides);
  container.read(sessionProvider.notifier).state = SessionState(
    questionsAttempted: 15,
    questionsCorrect: 11,
  );

  await tester.pumpWidget(
    UncontrolledProviderScope(
      container: container,
      child: MaterialApp(
        home: CognitiveLoadBreakScreen(
          suggestedBreakMinutes: 5,
          onReady: () {},
        ),
      ),
    ),
  );

  expect(find.textContaining('15'), findsWidgets); // questions answered
  expect(find.textContaining('73%'), findsOneWidget); // accuracy
});

testWidgets('custom message displayed when provided', (tester) async {
  await tester.pumpWidget(MaterialApp(
    home: CognitiveLoadBreakScreen(
      suggestedBreakMinutes: 5,
      onReady: () {},
      message: 'אתה עושה עבודה מצוינת! קח הפסקה קצרה.',
    ),
  ));

  expect(find.text('אתה עושה עבודה מצוינת! קח הפסקה קצרה.'), findsOneWidget);
});
```

**Edge Cases:**
- Student presses back button during break — treat as "ready", resume session
- Break timer reaches zero — do NOT auto-resume; wait for explicit student action
- `suggestedBreakMinutes` is 0 — show message without countdown timer

---

### MOB-007.6: Session Summary Sheet & Change Approach Button
**Files:**
- `lib/features/session/widgets/session_summary_sheet.dart`
- `lib/features/session/widgets/change_approach_button.dart`

**Acceptance:**
- [ ] `SessionSummarySheet extends ConsumerWidget` displayed as `DraggableScrollableSheet`:
  - Total questions attempted / correct
  - XP earned with animated counter
  - Concepts mastered (list with celebratory icon)
  - Concepts improved (list)
  - Streak status: maintained or broken
  - Badge earned: flip-reveal animation if `badgeEarned` is not null
  - "Continue to Graph" button -> navigates to knowledge graph screen
  - "Start Another Session" button -> pops and pushes new session
- [ ] `ChangeApproachButton extends ConsumerWidget`:
  - `TextButton.icon` that opens a bottom sheet
  - Options: "Explain it differently", "More practice problems", "Start with something simpler", "Challenge me more"
  - Hebrew labels: "הסבר אחרת", "עוד תרגילים", "התחל ממשהו פשוט יותר", "תאתגר אותי"
  - Selection sends `SwitchApproach` with matching `preferenceHint`: "explain_differently", "more_practice", "simpler_first", "challenge_me"

**Test:**
```dart
testWidgets('SessionSummarySheet displays all stats', (tester) async {
  await tester.pumpWidget(MaterialApp(
    home: Scaffold(
      body: SessionSummarySheet(
        summary: SessionSummary(
          sessionId: 's-001',
          questionsAttempted: 20,
          correctAnswers: 15,
          xpEarned: 150,
          duration: Duration(minutes: 22),
          conceptsMastered: ['Derivatives', 'Integrals'],
          conceptsImproved: ['Limits'],
          streakMaintained: true,
        ),
      ),
    ),
  ));

  expect(find.textContaining('20'), findsWidgets);
  expect(find.textContaining('15'), findsWidgets);
  expect(find.textContaining('150'), findsWidgets);
  expect(find.text('Derivatives'), findsOneWidget);
  expect(find.text('Integrals'), findsOneWidget);
});

testWidgets('ChangeApproachButton sends correct preference hint', (tester) async {
  await tester.pumpWidget(
    ProviderScope(
      overrides: testOverrides,
      child: MaterialApp(home: Scaffold(body: ChangeApproachButton())),
    ),
  );

  // Open bottom sheet
  await tester.tap(find.byType(ChangeApproachButton));
  await tester.pumpAndSettle();

  // Select "Challenge me more"
  await tester.tap(find.text('תאתגר אותי'));
  await tester.pumpAndSettle();

  final captured = verify(() => mockWs.switchApproach(captureAny)).captured.single as SwitchApproach;
  expect(captured.preferenceHint, equals('challenge_me'));
});

testWidgets('SessionSummarySheet shows badge animation when earned', (tester) async {
  await tester.pumpWidget(MaterialApp(
    home: Scaffold(
      body: SessionSummarySheet(
        summary: SessionSummary(
          sessionId: 's-001',
          questionsAttempted: 20,
          correctAnswers: 20,
          xpEarned: 200,
          duration: Duration(minutes: 25),
          conceptsMastered: [],
          conceptsImproved: [],
          streakMaintained: true,
          badgeEarned: Badge(
            id: 'b-perfect',
            name: 'Perfect Session',
            nameHe: 'מושלם!',
            iconAsset: 'assets/icons/perfect.svg',
            description: 'All answers correct',
            earnedAt: DateTime.now(),
            isNew: true,
          ),
        ),
      ),
    ),
  ));

  await tester.pump(Duration(milliseconds: 400)); // mid-animation
  expect(find.text('Perfect Session'), findsOneWidget);
});

testWidgets('summary sheet has navigation buttons', (tester) async {
  await tester.pumpWidget(MaterialApp(
    home: Scaffold(
      body: SessionSummarySheet(summary: _testSummary()),
    ),
  ));

  expect(find.text('Continue to Graph'), findsOneWidget);
  expect(find.text('Start Another Session'), findsOneWidget);
});
```

**Edge Cases:**
- `badgeEarned` is null — badge section hidden entirely
- `conceptsMastered` is empty — show encouraging "Keep practicing!" message
- `ChangeApproachButton` tapped when no session is active — button disabled
- XP counter animation on slow devices — use `TweenAnimationBuilder` with frame budget awareness

---

## Integration Test

```dart
void main() {
  group('MOB-007 Integration: Full session UX flow', () {
    testWidgets('complete session lifecycle on screen', (tester) async {
      await tester.pumpWidget(
        ProviderScope(
          overrides: testOverrides,
          child: MaterialApp(home: SessionScreen(subject: Subject.math)),
        ),
      );

      // 1. Session starts, loading state
      expect(find.byType(CircularProgressIndicator), findsOneWidget);

      // 2. Question arrives
      simulateEvent(mockWs, EventTargets.questionPresented, testQuestionPayload);
      await tester.pumpAndSettle();
      expect(find.byType(QuestionCard), findsOneWidget);
      expect(find.byType(AnswerInput), findsOneWidget);

      // 3. Student selects MCQ answer
      await tester.tap(find.text('x = 2'));
      await tester.pump();
      await tester.tap(find.widgetWithText(ElevatedButton, 'Submit'));

      // 4. Feedback overlay appears
      simulateEvent(mockWs, EventTargets.answerEvaluated, testCorrectAnswerPayload);
      await tester.pumpAndSettle();
      expect(find.byType(FeedbackOverlay), findsOneWidget);

      // 5. Dismiss feedback, next question
      await tester.tap(find.byType(FeedbackOverlay));
      await tester.pumpAndSettle();

      // 6. Cognitive load break
      simulateEvent(mockWs, EventTargets.cognitiveLoadWarning, {
        'fatigueScore': 0.78,
        'suggestedBreakMinutes': 5,
        'message': 'קח הפסקה',
      });
      await tester.pumpAndSettle();
      expect(find.byType(CognitiveLoadBreakScreen), findsOneWidget);

      // 7. Resume
      await tester.tap(find.text('Ready for more?'));
      await tester.pumpAndSettle();
      expect(find.byType(CognitiveLoadBreakScreen), findsNothing);
    });
  });
}
```

## Rollback Criteria
- If `flutter_math_fork` cannot handle complex LaTeX: fall back to `flutter_tex` (WebView-based, slower but more complete)
- If DiagramAnswerInput drawing canvas is too complex for v1: ship without diagram question support, gate behind `FeatureFlags.diagramQuestionsEnabled`
- If RTL bidi mixing causes rendering bugs: use a single `Directionality(textDirection: TextDirection.rtl)` wrapper with explicit LTR overrides for math only

## Definition of Done
- [ ] All 6 subtasks pass their individual tests
- [ ] Full session flow works end-to-end: start -> question -> answer -> feedback -> next -> break -> summary
- [ ] All 5 answer input types render correctly and submit data
- [ ] LaTeX math renders in mixed Hebrew/Arabic text (bidi correct)
- [ ] Feedback overlay animates and auto-dismisses
- [ ] Cognitive load break screen shows and dismisses
- [ ] Session summary displays all stats and navigates correctly
- [ ] Arabic rendering verified with Noto Sans Arabic font
- [ ] Accessibility: all interactive elements have semantic labels
- [ ] PR reviewed by mobile lead
