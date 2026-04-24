# MST-009: Learning Frontier Calculator

**Priority:** P1 — determines what students learn next
**Blocked by:** MST-004 (prerequisite calculator), DATA-006 (Neo4j graph cache)
**Estimated effort:** 3-5 days (M)
**Contract:** `docs/mastery-engine-architecture.md` section 5 step 1, section 6.2
**Research ref:** `docs/mastery-measurement-research.md` section 4.2

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule. `throw UnimplementedError`, `// TODO: implement`, empty bodies, and mock returns are FORBIDDEN in source code. If you cannot implement it fully, file a blocking dependency instead.

## Context

The learning frontier is the set of concepts a student is ready to learn — concepts whose prerequisites are sufficiently mastered (PSI >= 0.8) but which the student has not yet mastered themselves (mastery < 0.90). The frontier is ranked by information gain, review urgency, and interleaving preference. This computation runs in-memory using the graph cache loaded from Neo4j at actor startup, not via live graph queries. The frontier feeds the item selector (MST-010) and the student dashboard's "N new concepts ready to learn" widget.

## Subtasks

### MST-009.1: Prerequisite Satisfaction Index (PSI)

**Files to create:**
- `src/Cena.Domain/Learner/Mastery/PrerequisiteSatisfactionIndex.cs`

**Acceptance:**
- [ ] `PrerequisiteSatisfactionIndex.Compute(string conceptId, IReadOnlyDictionary<string, ConceptMasteryState> masteryOverlay, IConceptGraphCache graphCache) → float`
- [ ] PSI = average of `effective_mastery(p)` for all direct prerequisites of the concept
- [ ] If concept has no prerequisites, PSI = 1.0 (always ready)
- [ ] If any prerequisite has mastery = 0.0 (never seen), it drags the average down
- [ ] Output in [0.0, 1.0]
- [ ] Method is `static`, zero allocation

### MST-009.2: Frontier Computation

**Files to create:**
- `src/Cena.Domain/Learner/Mastery/LearningFrontierCalculator.cs`
- `src/Cena.Domain/Learner/Mastery/FrontierConcept.cs`

**Acceptance:**
- [ ] `FrontierConcept` record: `ConceptId`, `Name`, `TopicCluster`, `PSI`, `CurrentMastery`, `InformationGain`, `ReviewUrgency`, `CompositeRank`
- [ ] `LearningFrontierCalculator.ComputeFrontier(IReadOnlyDictionary<string, ConceptMasteryState> overlay, IConceptGraphCache graphCache, DateTimeOffset now, int maxResults = 20) → IReadOnlyList<FrontierConcept>`
- [ ] Filter: `PSI(c) >= 0.8 AND mastery(c) < 0.90`
- [ ] Information gain: prefer concepts with fewer attempts (wider confidence interval)
- [ ] Review urgency: for partially-learned concepts, use `review_priority(c)` score
- [ ] Interleaving: tag each concept's `TopicCluster` to enable cross-topic mixing downstream
- [ ] Sorted by composite rank descending

### MST-009.3: Frontier Ranking Algorithm

**Files to create/modify:**
- `src/Cena.Domain/Learner/Mastery/LearningFrontierCalculator.cs` (add ranking logic)

**Acceptance:**
- [ ] Composite rank formula: `0.40 * information_gain + 0.30 * review_urgency + 0.20 * psi + 0.10 * interleaving_bonus`
- [ ] Information gain: `1.0 / (1 + attemptCount)` — fewer attempts = more informative
- [ ] Review urgency: `max(0, 0.85 - recallProbability)` for partially-learned; `0` for not-started
- [ ] Interleaving bonus: `1.0` if concept is from a different topic cluster than the student's last interaction, `0.0` otherwise
- [ ] All weights are configurable via `FrontierConfig` record

**Test:**
```csharp
[Fact]
public void PSI_AllPrereqsMastered_ReturnsHigh()
{
    var graphCache = new FakeGraphCache(prerequisites: new Dictionary<string, List<PrerequisiteEdge>>
    {
        ["concept-C"] = new()
        {
            new PrerequisiteEdge("concept-A", "concept-C", 1.0f),
            new PrerequisiteEdge("concept-B", "concept-C", 1.0f)
        }
    });
    var overlay = new Dictionary<string, ConceptMasteryState>
    {
        ["concept-A"] = new() { MasteryProbability = 0.95f, HalfLifeHours = 200f, LastInteraction = DateTimeOffset.UtcNow },
        ["concept-B"] = new() { MasteryProbability = 0.88f, HalfLifeHours = 150f, LastInteraction = DateTimeOffset.UtcNow }
    };

    var psi = PrerequisiteSatisfactionIndex.Compute("concept-C", overlay, graphCache);

    // avg(0.95, 0.88) ≈ 0.915
    Assert.InRange(psi, 0.91f, 0.92f);
}

[Fact]
public void PSI_MissingPrereq_DragsDown()
{
    var graphCache = new FakeGraphCache(prerequisites: new Dictionary<string, List<PrerequisiteEdge>>
    {
        ["concept-B"] = new()
        {
            new PrerequisiteEdge("concept-A", "concept-B", 1.0f)
        }
    });
    var overlay = new Dictionary<string, ConceptMasteryState>(); // concept-A never encountered

    var psi = PrerequisiteSatisfactionIndex.Compute("concept-B", overlay, graphCache);

    Assert.Equal(0.0f, psi); // avg(0.0) = 0.0
}

[Fact]
public void ComputeFrontier_ReadyConcepts_Included()
{
    var now = DateTimeOffset.UtcNow;
    var graphCache = new FakeGraphCache(
        concepts: new Dictionary<string, ConceptNode>
        {
            ["basics"] = new("basics", "Basics", "math", "algebra", 1, 0.3f, 0.5f, 4),
            ["intermediate"] = new("intermediate", "Intermediate", "math", "algebra", 2, 0.5f, 0.6f, 5),
            ["advanced"] = new("advanced", "Advanced", "math", "calculus", 3, 0.8f, 0.8f, 6),
        },
        prerequisites: new Dictionary<string, List<PrerequisiteEdge>>
        {
            ["intermediate"] = new() { new("basics", "intermediate", 1.0f) },
            ["advanced"] = new() { new("intermediate", "advanced", 1.0f) }
        });

    var overlay = new Dictionary<string, ConceptMasteryState>
    {
        ["basics"] = new()
        {
            MasteryProbability = 0.95f, HalfLifeHours = 200f,
            LastInteraction = now.AddHours(-1), AttemptCount = 20
        },
        ["intermediate"] = new()
        {
            MasteryProbability = 0.40f, HalfLifeHours = 72f,
            LastInteraction = now.AddHours(-5), AttemptCount = 5
        }
    };

    var frontier = LearningFrontierCalculator.ComputeFrontier(overlay, graphCache, now);

    // "intermediate" should be in frontier: PSI(intermediate) = mastery(basics) = 0.95 >= 0.8
    //   and mastery(intermediate) = 0.40 < 0.90
    Assert.Contains(frontier, f => f.ConceptId == "intermediate");
    // "advanced" should NOT be in frontier: PSI(advanced) = mastery(intermediate) = 0.40 < 0.8
    Assert.DoesNotContain(frontier, f => f.ConceptId == "advanced");
    // "basics" should NOT be in frontier: mastery(basics) = 0.95 >= 0.90 (already mastered)
    Assert.DoesNotContain(frontier, f => f.ConceptId == "basics");
}

[Fact]
public void ComputeFrontier_RankedByCompositeScore()
{
    var now = DateTimeOffset.UtcNow;
    var graphCache = new FakeGraphCache(
        concepts: new Dictionary<string, ConceptNode>
        {
            ["root"] = new("root", "Root", "math", "algebra", 1, 0.2f, 0.5f, 3),
            ["fresh"] = new("fresh", "Fresh Concept", "math", "geometry", 1, 0.4f, 0.5f, 4),
            ["reviewed"] = new("reviewed", "Reviewed Concept", "math", "algebra", 1, 0.3f, 0.5f, 3),
        },
        prerequisites: new());

    var overlay = new Dictionary<string, ConceptMasteryState>
    {
        ["fresh"] = new()
        {
            MasteryProbability = 0.20f, HalfLifeHours = 48f,
            LastInteraction = now.AddHours(-1), AttemptCount = 1
        },
        ["reviewed"] = new()
        {
            MasteryProbability = 0.60f, HalfLifeHours = 100f,
            LastInteraction = now.AddHours(-24), AttemptCount = 12
        }
    };

    var frontier = LearningFrontierCalculator.ComputeFrontier(overlay, graphCache, now);

    // "fresh" has higher information gain (fewer attempts), should rank first
    Assert.True(frontier.Count >= 2);
    Assert.Equal("fresh", frontier[0].ConceptId);
}
```
