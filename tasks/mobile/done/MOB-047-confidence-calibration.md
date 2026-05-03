# MOB-047: Confidence Calibration Tracking

**Priority:** P4.1 — High
**Phase:** 4 — Advanced Intelligence (Months 8-12)
**Source:** learning-science-srs-research.md Section 7
**Blocked by:** MOB-036 (SRSActor)
**Estimated effort:** M (1-3 weeks)

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** See `tasks/00-master-plan.md`.

## Context

Students are notoriously poor at judging their own knowledge (Kruger & Dunning, 1999). Confidence calibration helps build metacognitive awareness — the ability to know what you know.

## Subtasks

### MOB-047.1: Confidence Rating UI
- [ ] After answering (before seeing result): "How confident are you?" slider (1-5)
- [ ] Optional — appears every 3rd question (not every question to avoid fatigue)
- [ ] Quick tap targets, not precise slider (3 levels for under-14: "Guess", "Think so", "Sure")

### MOB-047.2: Calibration Computation
- [ ] Compare confidence vs actual correctness over rolling 50 questions
- [ ] Overconfidence score: avg confidence when wrong
- [ ] Under-confidence score: avg lack-of-confidence when right
- [ ] Calibration graph: confidence (x-axis) vs actual accuracy (y-axis)
- [ ] Perfect calibration = diagonal line

### MOB-047.3: MetacognitionActor (Backend)
- [ ] Child of StudentActor
- [ ] Tracks: confidence ratings, calibration scores, JOL (judgment of learning)
- [ ] Alerts when overconfidence detected: "You might want to review this concept more"
- [ ] Uses existing `SelfConfidence` field on `ConceptMasteryState`

### MOB-047.4: Progress Dashboard
- [ ] Calibration graph in progress/analytics screen
- [ ] "Your metacognition is improving!" feedback when calibration improves
- [ ] Subject-specific calibration (may be well-calibrated in math, not in physics)

**Definition of Done:**
- Confidence ratings collected every 3rd question
- Calibration computed and visualized (confidence vs accuracy graph)
- MetacognitionActor tracks and alerts on overconfidence
- Age-appropriate UI (3 levels for under-14, 5 for 14+)

**Test:**
```csharp
[Fact]
public void Calibration_DetectsOverconfidence()
{
    var tracker = new CalibrationTracker();
    // Student rates 5/5 confidence but gets 40% wrong
    for (int i = 0; i < 50; i++)
        tracker.Record(confidence: 5, correct: i % 5 != 0); // 80% correct

    // When they rate 5 but get wrong, that's overconfidence
    tracker.Record(confidence: 5, correct: false);
    Assert.True(tracker.IsOverconfident); // confidence > accuracy
}
```
