# MOB-013: Diagnostic Quiz, Partial Save, Resume, Knowledge Graph Reveal

**Priority:** P1 — blocks first-time user experience
**Blocked by:** MOB-007 (Session Screen), CNT-001 (Math Graph)
**Estimated effort:** 2 days
**Contract:** None (UX specification)

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule. `throw UnimplementedError`, `// TODO: implement`, empty bodies, and mock returns are FORBIDDEN in source code. If you cannot implement it fully, file a blocking dependency instead.

## Subtasks

### MOB-013.1: Diagnostic Quiz Flow
- [ ] 20-30 adaptive questions to establish initial mastery map
- [ ] Questions span basic to advanced, BKT updates in real-time
- [ ] Early termination when confidence is sufficient (< 10 questions for clear cases)

### MOB-013.2: Partial Save + Resume
- [ ] Quiz progress saved to local SQLite after each question
- [ ] App crash/close -> resume from last answered question
- [ ] Progress indicator: "Question 5 of ~20"

### MOB-013.3: Knowledge Graph Reveal
- [ ] After quiz: animated reveal of knowledge graph with mastery overlay
- [ ] Concepts light up green (mastered), yellow (in progress), gray (not started)
- [ ] "Start Learning" button targets first concept in unlocked frontier

**Test:**
```dart
testWidgets('Diagnostic quiz saves progress', (tester) async {
  await tester.pumpWidget(DiagnosticQuiz());
  await answerQuestions(tester, count: 5);
  // Simulate app restart
  await tester.pumpWidget(DiagnosticQuiz());
  expect(find.text('Question 6'), findsOneWidget); // Resumes
});
```

---

## Definition of Done
- [ ] Diagnostic quiz establishes initial mastery
- [ ] Partial save/resume working
- [ ] Knowledge graph reveal animation smooth
- [ ] PR reviewed by architect
