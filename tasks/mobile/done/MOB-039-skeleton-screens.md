# MOB-039: Skeleton Screens Replacing Spinners

**Priority:** P1.10 — Medium
**Phase:** 1 — Foundation (Months 1-3)
**Source:** microinteractions-emotional-design.md Section 6, mobile-patterns Section 7
**Blocked by:** None
**Estimated effort:** S (< 1 week)

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** See `tasks/00-master-plan.md`.

## Context

`shimmer` package is already in `pubspec.yaml` but not used. Replace all `CircularProgressIndicator` loading states with skeleton screens that hint at incoming content shape.

## Subtasks

### MOB-039.1: Question Card Skeleton
- [ ] Skeleton matching question card layout: gray rectangle for question text, 4 rounded rects for options
- [ ] Shimmer animation using existing `shimmer` dependency
- [ ] Shown while next question is being fetched from actor

### MOB-039.2: Home Screen Skeleton
- [ ] Skeleton for dashboard cards: streak, XP, progress
- [ ] Skeleton for recommended content list
- [ ] Shown on cold start while initial data loads

### MOB-039.3: Knowledge Graph Skeleton
- [ ] Skeleton showing node placeholders in graph layout
- [ ] Shimmer on edges and nodes

### MOB-039.4: Remove All Spinners
- [ ] Grep for `CircularProgressIndicator` and replace with skeletons
- [ ] Educational loading tips below skeletons: "Did you know..." facts

**Definition of Done:**
- All loading states use shimmer skeletons matching content shape
- Zero `CircularProgressIndicator` instances in production screens
- Loading tips shown during longer waits (> 2s)

**Test:**
```dart
testWidgets('Question loading shows skeleton, not spinner', (tester) async {
  await tester.pumpWidget(QuestionScreen(question: null, isLoading: true));
  expect(find.byType(Shimmer), findsWidgets);
  expect(find.byType(CircularProgressIndicator), findsNothing);
});
```
