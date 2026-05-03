# MOB-036: SRSActor — FSRS Spaced Repetition Scheduling

**Priority:** P1.7 — Critical
**Phase:** 1 — Foundation (Months 1-3)
**Source:** learning-science-srs-research.md Sections 1-2
**Blocked by:** MST-* (Mastery Engine), ACT-* (Actor System)
**Estimated effort:** L (3-6 weeks)

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** See `tasks/00-master-plan.md`.

## Context

Current HLR calculator provides forgetting curves but lacks a full scheduling system. FSRS (Free Spaced Repetition Scheduler) with 15 learnable parameters is the recommended upgrade path. This actor manages review scheduling per student per concept.

## Subtasks

### MOB-036.1: FSRS Algorithm Implementation
- [ ] Implement FSRS-4.5 with 15 learnable parameters (w0-w14)
- [ ] State per card: `{Stability, Difficulty, ElapsedDays, ScheduledDays, Reps, Lapses, State}`
- [ ] States: `New`, `Learning`, `Review`, `Relearning`
- [ ] Rating: automatic based on response time + correctness (no self-rating — Dunning-Kruger)
- [ ] Auto-grading: correct+fast → `Easy`, correct+slow → `Good`, wrong+close → `Hard`, wrong+far → `Again`

### MOB-036.2: SRSActor (Backend)
- [ ] Virtual actor keyed by `{StudentId, SubjectId}`
- [ ] State: per-concept FSRS parameters, review queue, next review timestamps
- [ ] `ScheduleReview` message → computes next review date
- [ ] `GetDueItems` message → returns concepts due for review, sorted by overdue factor
- [ ] Max 50 due items per day (prevents overwhelming)

### MOB-036.3: Review Session Integration
- [ ] Warm-up phase of session arc (MOB-030) pulls from SRS due items
- [ ] Review items interleaved with new learning (ratio: 30% review / 70% new)
- [ ] "Review Due" badge on home screen shows count of due items

### MOB-036.4: FSRS Parameter Training
- [ ] Batch job: re-estimate w0-w14 from student's review history (weekly)
- [ ] Start with population defaults, personalize after 100+ reviews
- [ ] Convergence check: RMSE of predicted retention vs actual

### MOB-036.5: Events
- [ ] `ReviewScheduled_V1`: `{StudentId, ConceptId, NextReviewAt, Stability, Difficulty}`
- [ ] `ReviewCompleted_V1`: `{StudentId, ConceptId, Rating, ResponseTimeMs, OldStability, NewStability}`

**Definition of Done:**
- FSRS algorithm schedules reviews with personalized stability/difficulty
- Due items surface in warm-up phase and on home screen badge
- Auto-grading based on response time + correctness (no self-rating)
- Parameters retrain weekly from review history

**Test:**
```csharp
[Fact]
public void FSRS_SchedulesReview_WithIncreasingIntervals()
{
    var fsrs = new FsrsScheduler(defaultWeights);
    var card = Card.New();

    var after1 = fsrs.Schedule(card, Rating.Good);
    Assert.InRange(after1.ScheduledDays, 1, 3);

    var after2 = fsrs.Schedule(after1, Rating.Good);
    Assert.True(after2.ScheduledDays > after1.ScheduledDays);

    var after3 = fsrs.Schedule(after2, Rating.Good);
    Assert.True(after3.ScheduledDays > after2.ScheduledDays);
}
```
