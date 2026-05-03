# MOB-034: Progressive Disclosure — 4 Levels

**Priority:** P1.5 — High
**Phase:** 1 — Foundation (Months 1-3)
**Source:** cognitive-load-progressive-disclosure-research.md Section 3
**Blocked by:** MOB-007 (Session Screen)
**Estimated effort:** M (1-3 weeks)

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** See `tasks/00-master-plan.md`.

## Context

Cowan's working memory limit is 4±1 chunks. Every screen must limit visible elements to 6-8 max. Information is layered across 4 disclosure levels, loaded lazily as students need it.

## Subtasks

### MOB-034.1: Define Disclosure Levels
- [ ] **Level 1 (Core Action):** Question text + answer options only. Max 4-6 elements.
- [ ] **Level 2 (Context):** Hints, approach selector, methodology badge. Revealed on demand.
- [ ] **Level 3 (Deep Dive):** Full solution, theory explanation, related concepts. Bottom sheet.
- [ ] **Level 4 (Meta):** Analytics, progress, settings. Separate screen.

### MOB-034.2: Level 1 — Question Screen Audit
- [ ] Remove all non-essential elements from default question view
- [ ] Only: question text, answer input, submit button, subtle progress bar
- [ ] Methodology badge → moved to Level 2 (after answer, per MOB-032)
- [ ] Timer → opt-in (per MOB-032)

### MOB-034.3: Level 2 — Contextual Reveal
- [ ] "Need a hint?" chip appears after 10s of no interaction
- [ ] Approach selector appears on demand (tap hint chip)
- [ ] Hint progression: clue → bigger clue → solution (3 levels with XP decay: 100% → 80% → 50%)

### MOB-034.4: Level 3 — Bottom Sheet Deep Dive
- [ ] After answer evaluation: expandable bottom sheet with full explanation
- [ ] Related concepts as tappable chips
- [ ] "Why this answer?" explanation from LLM
- [ ] Lazy-loaded — only fetched when sheet is expanded

### MOB-034.5: Disclosure Budget Enforcement
- [ ] Lint rule or widget audit: no screen exceeds 8 visible interactive elements
- [ ] Document disclosure level for each widget in component library

**Definition of Done:**
- Question screen shows max 6 elements by default
- Hints appear contextually after 10s idle
- Full explanation available via bottom sheet (Level 3)
- No screen exceeds 8 visible interactive elements

**Test:**
```dart
testWidgets('Question screen shows max 6 elements', (tester) async {
  await tester.pumpWidget(QuestionScreen(question: sampleMcq));
  final interactiveWidgets = find.byWidgetPredicate(
    (w) => w is ElevatedButton || w is OptionTile || w is TextField
  );
  expect(interactiveWidgets, findsAtMost(6));
});

testWidgets('Hint chip appears after 10s idle', (tester) async {
  await tester.pumpWidget(QuestionScreen(question: sampleMcq));
  expect(find.text('Need a hint?'), findsNothing);
  await tester.pump(const Duration(seconds: 11));
  expect(find.text('Need a hint?'), findsOneWidget);
});
```
