# MOB-032: Immersive Session Mode

**Priority:** P1.3 — High
**Phase:** 1 — Foundation (Months 1-3)
**Source:** flow-state-design-research.md Section 4, cognitive-load Section 5
**Blocked by:** MOB-007 (Session Screen)
**Estimated effort:** S (< 1 week)

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** See `tasks/00-master-plan.md`.

## Context

During learning sessions, hide bottom navigation, suppress notifications, and enter distraction-free mode. Reduces extraneous cognitive load and supports flow entry.

## Subtasks

### MOB-032.1: Hide Navigation During Sessions
- [ ] Push `SessionScreen` as `fullscreenDialog: true` in GoRouter
- [ ] Bottom nav bar hidden during active question-answering
- [ ] Status bar dimmed/hidden in full-screen mode
- [ ] Safe exit: swipe-down or back button shows "Exit session?" confirmation

### MOB-032.2: DND Integration
- [ ] Request DND access on session start (Android `NotificationManager`)
- [ ] Suppress push notifications during active session
- [ ] Restore notification state on session end/exit

### MOB-032.3: Methodology Badge Relocation
- [ ] Move methodology indicator from question display to feedback phase
- [ ] During question: only show question content, options, and progress
- [ ] After answer: show methodology context, hints, explanations

### MOB-032.4: Timer Opt-In
- [ ] Timer hidden by default (reduces anxiety, per ADHD accommodation research)
- [ ] Optional toggle in session settings to show timer
- [ ] When shown: subtle, non-prominent placement

**Definition of Done:**
- Session screen is full-screen with no bottom nav
- Notifications suppressed during session
- Methodology badge only visible in feedback phase
- Timer opt-in, not default

**Test:**
```dart
testWidgets('Session screen hides bottom navigation', (tester) async {
  await tester.pumpWidget(AppWithSession());
  await tester.tap(find.text('Start Session'));
  await tester.pumpAndSettle();
  expect(find.byType(BottomNavigationBar), findsNothing);
});
```
