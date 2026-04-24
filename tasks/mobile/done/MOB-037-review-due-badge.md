# MOB-037: Review Due Badge + Daily Review Session

**Priority:** P1.8 — High
**Phase:** 1 — Foundation (Months 1-3)
**Source:** learning-science-srs-research.md Section 1, habit-loops Section 4
**Blocked by:** MOB-036 (SRSActor)
**Estimated effort:** M (1-3 weeks)

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** See `tasks/00-master-plan.md`.

## Subtasks

### MOB-037.1: Home Screen Review Badge
- [ ] Badge on home screen "Learn" tab showing count of due review items
- [ ] Red dot when > 0 items due; number shown when > 5
- [ ] Tapping badge starts a dedicated review session
- [ ] "All caught up!" state when 0 items due

### MOB-037.2: Dedicated Review Session
- [ ] Review-only session mode: only SRS due items, no new content
- [ ] Flashcard-style swipe UI for review (swipe right = knew it, swipe left = forgot)
- [ ] Session ends when all due items reviewed
- [ ] Completion celebration proportional to items reviewed

### MOB-037.3: Knowledge Decay Visualization
- [ ] In knowledge graph: concept nodes show "memory strength" as opacity/glow
- [ ] Concepts due for review pulse subtly
- [ ] Concepts never reviewed after mastery show "fading" visual

### MOB-037.4: Review Notification
- [ ] Morning notification: "You have X concepts to review today"
- [ ] Sent only if > 5 items due (avoid notification fatigue for 1-2 items)
- [ ] One-tap "Start Review" action button on notification

**Definition of Done:**
- Home screen badge shows due review count from SRSActor
- Dedicated review session with swipe UI clears all due items
- Knowledge graph shows memory strength decay visually

**Test:**
```dart
testWidgets('Review badge shows due count', (tester) async {
  when(mockSrsService.getDueCount()).thenReturn(12);
  await tester.pumpWidget(HomeScreen());
  expect(find.text('12'), findsOneWidget);
  expect(find.byType(ReviewDueBadge), findsOneWidget);
});
```
