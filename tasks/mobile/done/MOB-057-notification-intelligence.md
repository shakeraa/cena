# MOB-057: Smart Notification Suppression & Personalization

**Priority:** P2.9 — High
**Phase:** 2 — Engagement Layer (Months 3-5)
**Source:** habit-loops-hook-model-research.md Section 5
**Blocked by:** MOB-014 (Push Notifications), MOB-041 (Habit Stacking)
**Estimated effort:** M (1-3 weeks)

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** See `tasks/00-master-plan.md`.

## Subtasks

### MOB-057.1: Hard Budget
- [ ] Max 2 notifications per day per student
- [ ] Priority ranking: streak-at-risk > review-due > quest-available > social > marketing
- [ ] Only highest-priority notifications sent within budget

### MOB-057.2: Smart Suppression Rules
- [ ] Suppress if student already practiced today
- [ ] Suppress during detected quiet hours (learned from usage)
- [ ] Suppress on Shabbat/holidays (configurable, default off for secular students)
- [ ] Suppress after 3 consecutive dismissals → 50% frequency reduction for 7 days
- [ ] Stop all after 60 days of inactivity (graduated farewell, then silence)

### MOB-057.3: Notification Copy Quality
- [ ] Positive framing: "Your streak is at 7 days!" not "Don't lose your streak!"
- [ ] Personalized: reference specific subject or concept
- [ ] Action buttons: "Start Review" (one-tap to session), "Snooze 2h"

### MOB-057.4: Re-Engagement Campaigns
- [ ] Day 3 inactive: "We've saved your progress! Pick up where you left off"
- [ ] Day 7: "Your knowledge graph misses you" with graph preview
- [ ] Day 14: "X new concepts added to your course"
- [ ] Day 30: "Your classmates have been learning — join them"
- [ ] Day 60+: silence (respect the departure)

**Definition of Done:**
- Max 2 notifications/day with priority ranking
- 7 suppression rules enforced
- Graduated re-engagement that stops at 60 days
- Positive, personalized notification copy

**Test:**
```csharp
[Fact]
public void NotificationBudget_LimitsTo2PerDay()
{
    var scheduler = new NotificationScheduler(dailyBudget: 2);
    scheduler.Enqueue(NotificationType.StreakAtRisk);
    scheduler.Enqueue(NotificationType.ReviewDue);
    scheduler.Enqueue(NotificationType.QuestAvailable);
    var sent = scheduler.GetScheduled();
    Assert.Equal(2, sent.Count);
    Assert.Contains(sent, n => n.Type == NotificationType.StreakAtRisk); // Highest priority
}
```
