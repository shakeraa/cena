# MOB-035: Training Wheels Mode (Sessions 1-3)

**Priority:** P1.6 — High
**Phase:** 1 — Foundation (Months 1-3)
**Source:** cognitive-load-progressive-disclosure-research.md Section 4
**Blocked by:** MOB-033 (Onboarding V2)
**Estimated effort:** S (< 1 week)

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** See `tasks/00-master-plan.md`.

## Context

New users face maximum cognitive load: learning the app AND learning content simultaneously. For sessions 1-3, reduce app complexity while they build familiarity.

## Subtasks

### MOB-035.1: Reduced UI for New Users
- [ ] Sessions 1-3: hide approach selector, knowledge graph link, skip button
- [ ] Show only: question, options, submit, progress bar
- [ ] Contextual tooltips: "Tap here to answer" (session 1 only)
- [ ] Feature unlock schedule: session 2 adds hints, session 4 adds approach selector

### MOB-035.2: Scaffolding Fade
- [ ] Track `completedSessionCount` in `StudentActor` state
- [ ] Features unlock based on session count, not time
- [ ] BKT mastery threshold override: unlock approach selector early if P(known) > 0.6

### MOB-035.3: Guided First Session
- [ ] First question has inline coach mark: "Read the question and tap your answer"
- [ ] First correct answer: extended celebration (1.5x normal duration)
- [ ] First session completion: special "First Session Complete!" celebration

**Definition of Done:**
- Sessions 1-3 show simplified UI with progressive feature unlocks
- Coach marks appear only in session 1
- Features unlock by session count with mastery-based early unlock

**Test:**
```dart
test('Training wheels hides features for new users', () {
  final config = TrainingWheelsConfig(completedSessions: 1);
  expect(config.showApproachSelector, isFalse);
  expect(config.showHints, isFalse);
  expect(config.showSkipButton, isFalse);
});

test('Features unlock after session 3', () {
  final config = TrainingWheelsConfig(completedSessions: 4);
  expect(config.showApproachSelector, isTrue);
  expect(config.showHints, isTrue);
});
```
