# MOB-042: Quest System — Daily / Weekly / Monthly

**Priority:** P2.5 — High
**Phase:** 2 — Engagement Layer (Months 3-5)
**Source:** gamification-motivation-research.md Section 7
**Blocked by:** MOB-008 (Gamification)
**Estimated effort:** L (3-6 weeks)

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** See `tasks/00-master-plan.md`.

## Subtasks

### MOB-042.1: Daily Quests (1-2 per day)
- [ ] Auto-generated from student's learning state: "Master 1 new concept", "Review 5 due items"
- [ ] 25-50 XP reward per quest
- [ ] Reset at midnight (student's timezone)
- [ ] At least 1 quest always achievable in a single session

### MOB-042.2: Weekly Missions (2-3 per week)
- [ ] Larger scope: "Complete 5 sessions", "Achieve 80% accuracy in Algebra"
- [ ] 100-200 XP reward
- [ ] Progress bar showing mission advancement
- [ ] Reset on Monday

### MOB-042.3: Monthly Campaigns (narrative-driven)
- [ ] Semester-long narrative arc: "The Knowledge Explorer" journey
- [ ] Monthly chapter with 4-5 milestones
- [ ] Unique badge for campaign completion
- [ ] Tied to curriculum progression (not arbitrary)

### MOB-042.4: Side Quests (enrichment)
- [ ] Optional challenges: "Try a question in a different methodology"
- [ ] Discovery quests: "Explore a concept you haven't touched in 30 days"
- [ ] No pressure — clearly marked as optional

### MOB-042.5: Quest UI
- [ ] Quest panel accessible from home screen (collapsible card)
- [ ] Active quests with progress indicators
- [ ] Completed quest celebration with XP animation
- [ ] Quest log (history of completed quests)

### MOB-042.6: GamificationActor Quest Extension
- [ ] `QuestAccepted_V1`: `{QuestId, QuestType, StudentId, Criteria}`
- [ ] `QuestProgressUpdated_V1`: `{QuestId, Progress, Target}`
- [ ] `QuestCompleted_V1`: `{QuestId, XpAwarded, BadgeAwarded?}`
- [ ] Quest generation logic in `GamificationRotationService` (varied by novelty rotation)

**Definition of Done:**
- Daily quests auto-generated from learning state, reset midnight
- Weekly missions with progress bars
- Monthly narrative campaigns tied to curriculum
- Quest events emitted for analytics

**Test:**
```csharp
[Fact]
public void DailyQuest_GeneratesAchievableGoals()
{
    var state = new StudentState { DueReviewCount = 12, MasteredConcepts = 15 };
    var quests = QuestGenerator.GenerateDaily(state);
    Assert.InRange(quests.Count, 1, 2);
    Assert.All(quests, q => Assert.True(q.IsAchievableInOneSession));
}
```
