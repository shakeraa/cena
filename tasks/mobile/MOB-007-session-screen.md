# MOB-007: QuestionCard, AnswerInput, FeedbackOverlay, MathText, CognitiveLoadBreak

**Priority:** P0 — the core learning UX
**Blocked by:** MOB-005 (State), MOB-001 (scaffold)
**Estimated effort:** 4 days
**Contract:** `contracts/mobile/lib/features/session/session_screen.dart`

---

## Context

The session screen is the core learning loop: question -> answer -> feedback -> next. Supports MCQ, free text, numeric, proof, and diagram question types. RTL-aware for Hebrew. Includes cognitive load break screen when fatigue is detected.

## Subtasks

### MOB-007.1: SessionScreen Container + ProgressBar

**Files to create/modify:**
- `src/mobile/lib/features/session/session_screen.dart`
- `src/mobile/lib/features/session/widgets/progress_bar.dart`

**Acceptance:**
- [ ] Layout per contract: progress bar, methodology indicator, question card, answer input, action buttons
- [ ] Progress bar shows: questions attempted, accuracy %, fatigue indicator (color gradient green->yellow->red)
- [ ] Timer tracking time spent on current question (for AttemptConcept.timeSpentMs)
- [ ] RTL layout when locale is Hebrew or Arabic

**Test:**
```dart
testWidgets('SessionScreen shows progress bar', (tester) async {
  await tester.pumpWidget(wrapWithProviders(SessionScreen()));
  expect(find.byType(ProgressBar), findsOneWidget);
});
```

---

### MOB-007.2: QuestionCard Widget

**Files to create/modify:**
- `src/mobile/lib/features/session/widgets/question_card.dart`

**Acceptance:**
- [ ] Renders question text with MathText widget for LaTeX
- [ ] MCQ: numbered option list, tap to select, visual feedback
- [ ] Free text: scrollable text area
- [ ] Numeric: number input with units dropdown
- [ ] Diagram: image + drawing canvas overlay

**Test:**
```dart
testWidgets('MCQ question shows 4 options', (tester) async {
  await tester.pumpWidget(QuestionCard(exercise: mcqExercise));
  expect(find.byType(OptionTile), findsNWidgets(4));
});
```

---

### MOB-007.3: AnswerInput + Submission

**Files to create/modify:**
- `src/mobile/lib/features/session/widgets/answer_input.dart`

**Acceptance:**
- [ ] Input type adapts to question type (MCQ tap, text field, number field)
- [ ] Submit button enabled only when answer is non-empty
- [ ] Loading state during evaluation (spinner on submit button)
- [ ] Answer submitted via `SessionNotifier.submitAnswer(answer, timeSpentMs)`

**Test:**
```dart
testWidgets('Submit button disabled when empty', (tester) async {
  await tester.pumpWidget(AnswerInput(questionType: QuestionType.freeText));
  expect(tester.widget<ElevatedButton>(find.byType(ElevatedButton)).enabled, isFalse);
});
```

---

### MOB-007.4: FeedbackOverlay

**Files to create/modify:**
- `src/mobile/lib/features/session/widgets/feedback_overlay.dart`

**Acceptance:**
- [ ] Green overlay + checkmark for correct answers
- [ ] Red overlay + X for incorrect answers with error explanation (Hebrew)
- [ ] Partial credit: yellow overlay with score display
- [ ] Auto-dismiss after 3 seconds or tap to dismiss
- [ ] Haptic feedback: success vibration for correct, gentle pulse for incorrect

**Test:**
```dart
testWidgets('FeedbackOverlay shows correct animation', (tester) async {
  await tester.pumpWidget(FeedbackOverlay(result: AnswerResult(isCorrect: true)));
  expect(find.byIcon(Icons.check_circle), findsOneWidget);
});
```

---

### MOB-007.5: CognitiveLoadBreak Screen

**Files to create/modify:**
- `src/mobile/lib/features/session/widgets/cognitive_load_break.dart`

**Acceptance:**
- [ ] Shown when `SessionState.isBreakSuggested == true`
- [ ] Breathing animation (expand/contract circle, 4s cycle)
- [ ] Timer: suggested break duration (from CognitiveLoadService)
- [ ] "Continue" button to dismiss and resume session
- [ ] "End Session" button to end early
- [ ] Calming color palette (soft blue/green)

**Test:**
```dart
testWidgets('Break screen shows breathing animation', (tester) async {
  await tester.pumpWidget(CognitiveLoadBreak(suggestedMinutes: 5));
  expect(find.text('Take a break'), findsOneWidget);
});
```

---

### MOB-007.6: Action Buttons (Hint, Skip, Change Approach)

**Files to create/modify:**
- `src/mobile/lib/features/session/widgets/action_buttons.dart`

**Acceptance:**
- [ ] Hint button: shows hint level (0-3), each press reveals more
- [ ] Skip button: confirmation dialog, reason optional
- [ ] Change Approach button: bottom sheet with methodology options (student-friendly labels)
- [ ] All buttons disabled during loading/evaluation

**Test:**
```dart
testWidgets('Hint button increments hint level', (tester) async {
  await tester.pumpWidget(wrapWithProviders(ActionButtons()));
  await tester.tap(find.text('Hint'));
  verify(mockSessionNotifier.requestHint()).called(1);
});
```

---

## Rollback Criteria
- Simplified session screen with text-only questions (no diagrams, no animations)

## Definition of Done
- [ ] All 6 subtask widgets implemented
- [ ] RTL layout verified for Hebrew
- [ ] Session flow: question -> answer -> feedback -> next working end-to-end
- [ ] PR reviewed by architect
