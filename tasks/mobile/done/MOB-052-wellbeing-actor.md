# MOB-052: WellbeingActor — Digital Wellbeing & Screen Time

**Priority:** P3 (cross-cutting)
**Phase:** 3 — Social Layer (integrates across phases)
**Source:** ethical-persuasion-research.md Section 3
**Blocked by:** ACT-* (Actor System)
**Estimated effort:** M (1-3 weeks)

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** See `tasks/00-master-plan.md`.

## Subtasks

### MOB-052.1: WellbeingActor (Backend)
- [ ] Per-student actor, child of StudentActor
- [ ] Tracks daily study time, session patterns, break compliance
- [ ] Streak anxiety detection: flags if student shows distress patterns
- [ ] Notification throttling: enforces 2/day hard budget

### MOB-052.2: Session Time Limits
- [ ] Tiered limits: 90 min (under-13), 120 min (13-15), 180 min (16+)
- [ ] Gentle warning at 80%: "You've been studying for a while — great work!"
- [ ] Soft stop at 100%: "Time for a break. Your progress is saved."
- [ ] Hard stop configurable by parent/teacher

### MOB-052.3: Wellbeing Dashboard
- [ ] Weekly study time summary (framed as accomplishment)
- [ ] Break compliance rate
- [ ] Best study times (from routine profile)
- [ ] "Well-balanced" badge for consistent, moderate study patterns

### MOB-052.4: Bedtime Mode
- [ ] Auto-enable after configurable time (default 10 PM)
- [ ] Warm color filter, reduced brightness
- [ ] No new sessions — review-only mode
- [ ] "Good night! Your concepts will be here tomorrow."

### MOB-052.5: Parent Controls
- [ ] Parent sets daily/weekly time limits from parent dashboard
- [ ] Parent receives weekly summary email
- [ ] Parent can enforce bedtime mode time

**Definition of Done:**
- WellbeingActor tracks daily screen time per student
- Tiered session limits with gentle warnings
- Bedtime mode auto-enabled at configurable time
- Parent-configurable limits

**Test:**
```csharp
[Fact]
public void WellbeingActor_EnforcesSessionLimit_ForUnder13()
{
    var actor = SpawnWellbeingActor(studentAge: 11, dailyLimitMinutes: 90);
    actor.RecordStudyTime(TimeSpan.FromMinutes(85));
    Assert.Equal(WellbeingState.Warning, actor.CurrentState);

    actor.RecordStudyTime(TimeSpan.FromMinutes(91));
    Assert.Equal(WellbeingState.SoftStop, actor.CurrentState);
}
```
