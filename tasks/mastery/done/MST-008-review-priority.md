# MST-008: Review Priority Scheduler

**Priority:** P1 — determines which decayed concepts get reviewed first
**Blocked by:** MST-007 (decay timer), DATA-006 (Neo4j graph cache for descendant counts)
**Estimated effort:** 3-5 days (M)
**Contract:** `docs/mastery-engine-architecture.md` section 4.2, section 6.4
**Research ref:** `docs/mastery-measurement-research.md` section 1.3

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule. `throw UnimplementedError`, `// TODO: implement`, empty bodies, and mock returns are FORBIDDEN in source code. If you cannot implement it fully, file a blocking dependency instead.

## Context

When multiple concepts are decaying simultaneously, the review priority formula decides which to surface first. Foundational concepts with many dependents are prioritized even for small decay, because forgetting them has cascading effects through the prerequisite graph. The formula is `review_priority(c) = (0.85 - p_recall(c)) * (1 + log2(descendant_count(c)))`. This produces a ranked list that drives the Outreach Context's review reminders and the learning session's interleaved review items.

## Subtasks

### MST-008.1: Review Priority Calculator

**Files to create:**
- `src/Cena.Domain/Learner/Mastery/ReviewPriorityCalculator.cs`
- `src/Cena.Domain/Learner/Mastery/ReviewPriorityItem.cs`

**Acceptance:**
- [ ] `ReviewPriorityItem` record: `ConceptId`, `RecallProbability`, `DescendantCount`, `Priority`
- [ ] `ReviewPriorityCalculator.ComputePriority(float recallProbability, int descendantCount) → float`
- [ ] Formula: `(0.85 - recallProbability) * (1 + Math.Log2(Math.Max(descendantCount, 1)))`
- [ ] Guard: if `recallProbability >= 0.85`, returns `0.0` (no review needed)
- [ ] Higher priority = higher number (more urgent)
- [ ] Method is `static`, zero allocation

### MST-008.2: Priority Ranking for Student

**Files to create/modify:**
- `src/Cena.Domain/Learner/Mastery/ReviewPriorityCalculator.cs` (add `RankReviewConcepts` method)

**Acceptance:**
- [ ] `ReviewPriorityCalculator.RankReviewConcepts(IReadOnlyDictionary<string, ConceptMasteryState> masteryOverlay, IConceptGraphCache graphCache, DateTimeOffset now, int maxResults = 10) → IReadOnlyList<ReviewPriorityItem>`
- [ ] Filters to concepts where `MasteryProbability >= 0.70` and `RecallProbability(now) < 0.85`
- [ ] Computes priority for each, sorts descending
- [ ] Returns top `maxResults` items
- [ ] Efficient: single pass through overlay, no redundant graph lookups

### MST-008.3: NATS Publication of Review List

**Files to create/modify:**
- `src/Cena.Actors/Learner/StudentActor.DecayTimer.cs` (extend to publish priority list)

**Acceptance:**
- [ ] After decay scan, computes review priority ranking
- [ ] Publishes `ReviewPriorityList` message to NATS subject `learner.mastery.review-priorities`
- [ ] Message includes: `StudentId`, `Items` (top 10 ranked concepts), `Timestamp`
- [ ] Outreach Context subscribes to this for generating review reminders

**Test:**
```csharp
[Fact]
public void ComputePriority_FoundationalConceptWithDecay_HighPriority()
{
    // A foundational concept (many descendants) with moderate decay
    var priority = ReviewPriorityCalculator.ComputePriority(
        recallProbability: 0.50f, descendantCount: 15);

    // (0.85 - 0.50) * (1 + log2(15)) = 0.35 * (1 + 3.91) = 0.35 * 4.91 ≈ 1.72
    Assert.InRange(priority, 1.70f, 1.74f);
}

[Fact]
public void ComputePriority_LeafConceptWithDecay_LowerPriority()
{
    // A leaf concept (no descendants) with same decay
    var priority = ReviewPriorityCalculator.ComputePriority(
        recallProbability: 0.50f, descendantCount: 0);

    // (0.85 - 0.50) * (1 + log2(1)) = 0.35 * (1 + 0) = 0.35
    Assert.InRange(priority, 0.34f, 0.36f);
}

[Fact]
public void ComputePriority_NoDecay_ReturnsZero()
{
    var priority = ReviewPriorityCalculator.ComputePriority(
        recallProbability: 0.90f, descendantCount: 10);

    Assert.Equal(0.0f, priority);
}

[Fact]
public void RankReviewConcepts_OrdersByPriorityDescending()
{
    var now = DateTimeOffset.UtcNow;
    var overlay = new Dictionary<string, ConceptMasteryState>
    {
        ["algebra-basics"] = new()
        {
            MasteryProbability = 0.92f, HalfLifeHours = 72f,
            LastInteraction = now.AddDays(-7)
        },
        ["quadratic-formula"] = new()
        {
            MasteryProbability = 0.88f, HalfLifeHours = 120f,
            LastInteraction = now.AddDays(-5)
        },
        ["calculus-intro"] = new()
        {
            MasteryProbability = 0.30f, HalfLifeHours = 48f,
            LastInteraction = now.AddDays(-3)
        } // low mastery — should be excluded
    };

    var graphCache = new FakeGraphCache(descendantCounts: new Dictionary<string, int>
    {
        ["algebra-basics"] = 20,       // foundational
        ["quadratic-formula"] = 3,     // mid-level
        ["calculus-intro"] = 0          // leaf
    });

    var ranked = ReviewPriorityCalculator.RankReviewConcepts(overlay, graphCache, now);

    // algebra-basics should rank first (more descendants + more decayed)
    Assert.True(ranked.Count >= 1);
    Assert.Equal("algebra-basics", ranked[0].ConceptId);
    // calculus-intro should NOT appear (mastery < 0.70)
    Assert.DoesNotContain(ranked, r => r.ConceptId == "calculus-intro");
}
```
