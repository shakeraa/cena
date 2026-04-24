# MOB-054: Deep Study Mode (45-90 Minute Blocks)

**Priority:** P4.6 — Medium
**Phase:** 4 — Advanced Intelligence (Months 8-12)
**Source:** flow-state-design-research.md Section 7
**Blocked by:** MOB-030 (Session Flow Arc), MOB-031 (FlowMonitorActor)
**Estimated effort:** M (1-3 weeks)

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** See `tasks/00-master-plan.md`.

## Context

Cal Newport-style deep work blocks: 45-90 minute focused sessions structured as 2-3 flow-arc blocks with mandatory recovery breaks between them.

## Subtasks

### MOB-054.1: Deep Study Session Structure
- [ ] Duration selector: 45 / 60 / 75 / 90 minutes
- [ ] Structured as 2-3 flow-arc blocks (MOB-030) with 5-min recovery breaks
- [ ] Block 1: review + new concepts | Break | Block 2: deeper challenges | Break | Block 3: synthesis
- [ ] DND mode auto-enabled for full duration

### MOB-054.2: Focus Timer
- [ ] Ambient timer (corner of screen, opt-in visibility)
- [ ] Block progress indicator: "Block 2 of 3"
- [ ] Break countdown during recovery periods

### MOB-054.3: Recovery Breaks
- [ ] Mandatory 5-min break between blocks
- [ ] Breathing exercise option (existing `CognitiveLoadBreak`)
- [ ] Stretch reminder with illustration
- [ ] Cannot skip break (protective design)

### MOB-054.4: Deep Study Summary
- [ ] Extended summary at end: concepts covered, time in flow, mastery gained
- [ ] Comparison to regular sessions (deep study typically 2-3x more effective)
- [ ] "Deep Thinker" badge after 10 deep study sessions

**Definition of Done:**
- 45-90 min sessions in 2-3 blocks with mandatory breaks
- DND auto-enabled
- Extended summary showing effectiveness vs regular sessions

**Test:**
```dart
test('Deep study session divides into blocks with breaks', () {
  final session = DeepStudySession(durationMinutes: 75);
  expect(session.blocks.length, 3); // 3 x 20min + 2 x 5min breaks
  expect(session.breakCount, 2);
  expect(session.totalBreakMinutes, 10);
});
```
