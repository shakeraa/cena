# MOB-040: Quality-Gated Streaks

**Priority:** P2.1 — Critical
**Phase:** 2 — Engagement Layer (Months 3-5)
**Source:** habit-loops-hook-model-research.md Section 4
**Blocked by:** MOB-008 (Gamification)
**Estimated effort:** M (1-3 weeks)

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** See `tasks/00-master-plan.md`.

## Context

Current streak only requires any activity. Research shows this creates "zombie sessions" — rapid-tapping through easy questions to maintain streak. Quality gate requires genuine effort.

## Subtasks

### MOB-040.1: Quality Gate Logic
- [ ] Streak day counts only if: >= 3 questions with genuine effort (avg response time > 5s)
- [ ] At least 1 new concept attempted (not all review)
- [ ] `ZombieSessionDetected_V1` event when avg response time < 5s
- [ ] Zombie session rate KPI target: < 5%

### MOB-040.2: Enhanced Streak Features
- [ ] Streak freeze earning: 1 freeze per 7-day streak (max 3 banked)
- [ ] 24-hour streak repair window: if missed yesterday, complete 2x today's goal
- [ ] Weekday-only streak option (for students with weekend commitments)
- [ ] No-shame messaging on streak break: "Welcome back! Let's start fresh."

### MOB-040.3: Streak Anxiety Escape Valve
- [ ] Settings toggle: "Switch to Consistency Score" (shows % of target days in rolling 30)
- [ ] Settings toggle: "Switch to Momentum Meter" (rolling 7-day weighted average)
- [ ] Both alternatives available from streak widget long-press
- [ ] KPI: streak anxiety rate (students switching away) < 10%

### MOB-040.4: HabitEngineActor Integration
- [ ] Habit formation stage detection: Novice (<7 days), Developing (7-21), Established (21-66), Habitual (66+)
- [ ] Fragility monitoring: 3 consecutive misses → re-engagement intervention
- [ ] Internal trigger ratio tracking: sessions from notification vs organic open

**Definition of Done:**
- Streak requires 3+ genuine-effort questions per day
- Zombie sessions detected and flagged
- Momentum meter and consistency score available as alternatives
- No-shame messaging on all streak breaks

**Test:**
```csharp
[Fact]
public void QualityGate_RejectsZombieSessions()
{
    var gate = new StreakQualityGate();
    var zombieSession = new SessionResult(
        QuestionsAnswered: 10,
        AvgResponseTimeMs: 2500, // < 5s = zombie
        NewConceptsAttempted: 0
    );
    Assert.False(gate.QualifiesForStreak(zombieSession));
}

[Fact]
public void QualityGate_AcceptsGenuineEffort()
{
    var gate = new StreakQualityGate();
    var realSession = new SessionResult(
        QuestionsAnswered: 8,
        AvgResponseTimeMs: 12000,
        NewConceptsAttempted: 2
    );
    Assert.True(gate.QualifiesForStreak(realSession));
}
```
