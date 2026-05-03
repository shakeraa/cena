# MOB-058: Riverpod .select() Performance Optimization

**Priority:** P1 (cross-cutting)
**Phase:** 1 — Foundation
**Source:** mobile-ux-patterns-research.md Section 5
**Blocked by:** MOB-005 (State Management)
**Estimated effort:** S (< 1 week)

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** See `tasks/00-master-plan.md`.

## Context

Current `session_screen.dart` watches entire `sessionProvider` state, causing unnecessary rebuilds when any field changes. Use Riverpod's `.select()` to watch only needed fields.

## Subtasks

### MOB-058.1: Session Screen Optimization
- [ ] Replace `ref.watch(sessionProvider)` with granular selects:
  - `ref.watch(sessionProvider.select((s) => s.currentExercise))`
  - `ref.watch(sessionProvider.select((s) => s.questionsAttempted))`
  - `ref.watch(sessionProvider.select((s) => s.accuracy))`
  - `ref.watch(sessionProvider.select((s) => s.fatigueScore))`
- [ ] Verify reduced rebuild count with Flutter DevTools

### MOB-058.2: Audit All Providers
- [ ] Grep for `ref.watch(` without `.select(` across all screens
- [ ] Add `.select()` where watching full state is unnecessary
- [ ] Document select patterns in code style guide

### MOB-058.3: Performance Baseline
- [ ] Measure rebuild count before/after with Flutter DevTools
- [ ] Target: 50%+ reduction in unnecessary rebuilds during session

**Definition of Done:**
- Session screen uses `.select()` for all provider watches
- 50%+ reduction in unnecessary widget rebuilds
- All screens audited for overly-broad provider watches

**Test:**
```dart
test('Session screen rebuilds only on currentExercise change', () {
  final container = ProviderContainer();
  var rebuildCount = 0;
  container.listen(
    sessionProvider.select((s) => s.currentExercise),
    (_, __) => rebuildCount++,
  );
  // Changing unrelated field should NOT trigger rebuild
  container.read(sessionProvider.notifier).updateFatigueScore(0.5);
  expect(rebuildCount, 0);
});
```
