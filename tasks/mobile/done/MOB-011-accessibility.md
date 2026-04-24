# MOB-011: WCAG 2.1 AA — Semantics, Dynamic Text, Contrast, Shapes

**Priority:** P1 — legal compliance
**Blocked by:** MOB-007 (Session Screen)
**Estimated effort:** 2 days
**Contract:** None (WCAG 2.1 standard)

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule. `throw UnimplementedError`, `// TODO: implement`, empty bodies, and mock returns are FORBIDDEN in source code. If you cannot implement it fully, file a blocking dependency instead.

## Subtasks

### MOB-011.1: Semantic Labels
- [ ] All interactive widgets have `Semantics` labels
- [ ] Question text announced by screen reader
- [ ] Answer options individually focusable and labeled
- [ ] Progress bar announces: "Question 5 of 20, 70% accuracy"

### MOB-011.2: Dynamic Text Scaling
- [ ] App supports 200% text scaling without layout overflow
- [ ] `MediaQuery.textScaleFactor` respected in all text widgets
- [ ] Minimum touch target: 48x48dp

### MOB-011.3: Color Contrast
- [ ] All text meets WCAG AA: 4.5:1 contrast ratio (normal text), 3:1 (large text)
- [ ] Correct/incorrect feedback distinguishable without color (checkmark/X icons)
- [ ] High contrast mode: optional toggle in accessibility settings

### MOB-011.4: Shape-Only Indicators
- [ ] Mastery status uses shapes + color: circle (not started), half-circle (in progress), star (mastered)
- [ ] Error types use distinct shapes in addition to colors
- [ ] Streak flame has semantic label, not just visual

**Test:**
```dart
testWidgets('Accessibility: question card has semantics', (tester) async {
  await tester.pumpWidget(QuestionCard(exercise: testExercise));
  expect(tester.getSemantics(find.byType(QuestionCard)), isNotNull);
});
```

---

## Definition of Done
- [ ] WCAG 2.1 AA compliance verified via Flutter accessibility tools
- [ ] Screen reader testing on iOS VoiceOver and Android TalkBack
- [ ] PR reviewed by architect
