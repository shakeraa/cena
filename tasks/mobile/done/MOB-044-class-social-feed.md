# MOB-044: Class Achievement Feed

**Priority:** P3.1 — High
**Phase:** 3 — Social Layer (Months 5-8)
**Source:** social-learning-research.md Sections 2, 6
**Blocked by:** MOB-008 (Gamification), ACT-* (Actor System)
**Estimated effort:** L (3-6 weeks)

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** See `tasks/00-master-plan.md`.

## Subtasks

### MOB-044.1: ClassActor (Backend)
- [ ] Virtual actor keyed by `ClassId`
- [ ] Manages class social state: achievement feed, active challenges, members
- [ ] Circular buffer: 200 most recent events
- [ ] Receives `ConceptMastered_V1`, `BadgeEarned_V1`, `StreakMilestone_V1` from StudentActors
- [ ] Privacy: only emits positive events, never errors or failures

### MOB-044.2: SocialFeedActor (Backend)
- [ ] Child of ClassActor
- [ ] Opt-out handling: students who disable social feed are excluded
- [ ] k-anonymity enforcement: activity counters only when k >= 10 students
- [ ] Content moderation: AI pre-filter on any user-generated text

### MOB-044.3: Feed UI
- [ ] Feed tab or section on home screen (opt-in visibility)
- [ ] Card-based feed: "Sarah mastered Quadratic Equations!" with celebration
- [ ] Activity counters: "127 students practiced today"
- [ ] Teacher endorsement cards: highlighted with authority badge
- [ ] Pull-to-refresh, lazy loading

### MOB-044.4: Age Safety
- [ ] Under-13: no free-text, pre-set reactions only (thumbs up, star, clap)
- [ ] 13-15: limited text responses, moderated
- [ ] 16+: full participation with moderation
- [ ] Teacher can disable feed for entire class

### MOB-044.5: Events & NATS
- [ ] NATS: `class.{classId}.feed` for real-time feed updates via SignalR
- [ ] `SocialFeedItemCreated_V1`: `{ClassId, EventType, StudentDisplayName, Detail}`

**Definition of Done:**
- Class feed shows positive achievements from classmates
- Opt-in with age-appropriate safety controls
- k-anonymity enforced on all counters
- Real-time via SignalR

**Test:**
```csharp
[Fact]
public async Task ClassActor_EmitsOnlyPositiveEvents()
{
    var actor = SpawnClassActor(classId: "math-10a");
    await actor.HandleStudentEvent(new AnswerEvaluated_V1 { Correct = false });
    Assert.Empty(actor.FeedItems); // Wrong answers never reach feed

    await actor.HandleStudentEvent(new ConceptMastered_V1 { ConceptId = "quad-eq" });
    Assert.Single(actor.FeedItems);
}
```
