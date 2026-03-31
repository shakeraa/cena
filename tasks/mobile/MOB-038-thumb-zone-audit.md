# MOB-038: Thumb Zone Audit + Bottom-Heavy Layout

**Priority:** P1.9 — Medium
**Phase:** 1 — Foundation (Months 1-3)
**Source:** mobile-ux-patterns-research.md Section 1
**Blocked by:** MOB-007 (Session Screen)
**Estimated effort:** S (< 1 week)

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** See `tasks/00-master-plan.md`.

## Subtasks

### MOB-038.1: Critical Actions in Thumb Zone
- [ ] Audit all screens: primary actions must be in bottom 40% of screen
- [ ] Submit/Next button: always bottom of screen (already correct in `AnswerInput`)
- [ ] MCQ options: positioned in lower half of scrollable area
- [ ] Hint chip: bottom-left or bottom-right, not top

### MOB-038.2: RTL Considerations
- [ ] Thumb arc is physical, NOT linguistic — reachability map does NOT flip for RTL
- [ ] Verify FAB placement works for both hands

### MOB-038.3: Navigation to StatefulShellRoute
- [ ] Migrate `router.dart` from flat `GoRoute` to `StatefulShellRoute.indexedStack`
- [ ] Tab state preservation: switching tabs keeps scroll position
- [ ] 5th tab: Knowledge Graph (promote from nested screen to top-level)

**Definition of Done:**
- All primary actions in bottom 40% thumb zone
- Tab switching preserves scroll state
- Knowledge graph promoted to 5th tab

**Test:**
```dart
testWidgets('Submit button is in bottom 40% of screen', (tester) async {
  await tester.pumpWidget(QuestionScreen(question: sampleMcq));
  final submitButton = tester.getRect(find.byType(SubmitButton));
  final screenHeight = tester.view.physicalSize.height / tester.view.devicePixelRatio;
  expect(submitButton.top, greaterThan(screenHeight * 0.6));
});
```
