# MOB-030: Session Flow Arc (Warm-Up / Core / Cool-Down)

**Priority:** P1.1 — Critical
**Phase:** 1 — Foundation (Months 1-3)
**Source:** flow-state-design-research.md Section 3
**Blocked by:** MOB-007 (Session Screen)
**Estimated effort:** M (1-3 weeks)

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** See `tasks/00-master-plan.md`.

## Context

Current session screen presents questions in a flat sequence. Research shows a 3-phase arc maximizes flow state entry and satisfaction (Csikszentmihalyi).

## Subtasks

### MOB-030.1: Warm-Up Phase
- [ ] First 2-3 questions use spaced-repetition review items at P(correct) = 0.80-0.90
- [ ] Visual: calm color temperature (cool blue gradient background)
- [ ] No timer, no difficulty indicator — low-pressure start
- [ ] `SessionPhaseTransition_V1` event emitted on phase change

### MOB-030.2: Core Phase
- [ ] Progressive difficulty escalation from P(correct) = 0.70 → 0.60 target
- [ ] Dynamic adjustment based on FocusLevel from `FocusDegradationService`
- [ ] When in flow (FocusLevel >= 0.8): target P(correct) = 0.55-0.65 (challenge)
- [ ] When fatigued (FocusLevel < 0.4): target P(correct) = 0.75-0.85 (ease up)
- [ ] Visual: warm color temperature shift (amber undertones)

### MOB-030.3: Cool-Down Phase
- [ ] Last 2-3 questions return to easy review items
- [ ] Always end on a success (if last answer wrong, add one more easy question)
- [ ] Session summary with peak-end framing (best moment + final moment)
- [ ] Visual: satisfying green gradient, completion animation

### MOB-030.4: Phase Indicator
- [ ] Subtle phase indicator (not distracting during flow)
- [ ] Progress bar color shifts to reflect phase
- [ ] No explicit "Phase 2" labels — invisible to student

### MOB-030.5: Actor Integration
- [ ] `LearningSessionActor.HandleNextQuestion` respects current phase
- [ ] Phase transitions based on question count and performance
- [ ] `SessionPhaseTransition_V1` event: `{Phase, QuestionIndex, FocusLevel}`

**Definition of Done:**
- Session starts with 2-3 easy review questions, escalates difficulty, ends on success
- Phase transitions emit events; cool-down always ends on correct answer
- 60fps color transitions between phases

**Test:**
```dart
test('Session follows warm-up/core/cool-down arc', () {
  final session = SessionFlowArc(totalQuestions: 15);
  expect(session.phaseAt(0), SessionPhase.warmUp);
  expect(session.phaseAt(1), SessionPhase.warmUp);
  expect(session.phaseAt(5), SessionPhase.core);
  expect(session.phaseAt(13), SessionPhase.coolDown);
  expect(session.phaseAt(14), SessionPhase.coolDown);
});

test('Cool-down adds extra question if last was wrong', () {
  final session = SessionFlowArc(totalQuestions: 15);
  session.recordAnswer(questionIndex: 14, correct: false);
  expect(session.shouldAddRecoveryQuestion, isTrue);
});
```
