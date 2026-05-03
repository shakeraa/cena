# MST-010: Item Selector with Elo + 85% Rule

**Priority:** P1 — selects the next question for the student
**Blocked by:** MST-009 (learning frontier), ACT-003 (LearningSessionActor)
**Estimated effort:** 1-2 weeks (L)
**Contract:** `docs/mastery-engine-architecture.md` section 5
**Research ref:** `docs/mastery-measurement-research.md` section 3.2

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule. `throw UnimplementedError`, `// TODO: implement`, empty bodies, and mock returns are FORBIDDEN in source code. If you cannot implement it fully, file a blocking dependency instead.

## Context

The item selector lives in the `LearningSessionActor` (Pedagogy Context) and picks the next assessment item for the student. It uses the learning frontier to choose a target concept, then selects an item calibrated so the student's probability of answering correctly is approximately 0.85 — the "desirable difficulty" sweet spot. Item difficulty uses Elo ratings, updated after each response via a dual-update rule. Interleaving with probability 0.5 ensures the student alternates between different concepts rather than drilling one topic.

## Subtasks

### MST-010.1: Elo-Based Expected Correctness

**Files to create:**
- `src/Cena.Pedagogy/Domain/Services/EloScoring.cs`
- `src/Cena.Pedagogy/Domain/ItemCandidate.cs`

**Acceptance:**
- [ ] `EloScoring.ExpectedCorrectness(float studentTheta, float itemDifficulty) → float`
- [ ] Formula: `1.0f / (1.0f + MathF.Pow(10, (itemDifficulty - studentTheta) / 400f))`
- [ ] `EloScoring.UpdateRatings(float studentTheta, float itemDifficulty, bool isCorrect, float studentK, float itemK) → (float newTheta, float newDifficulty)`
- [ ] Student update: `newTheta = studentTheta + studentK * (score - expected)` where score is 1.0 or 0.0
- [ ] Item update: `newDifficulty = itemDifficulty + itemK * (expected - score)` (inverse of student)
- [ ] K-factors: 40 for new students (< 20 attempts), decreasing to 10; item K always 10
- [ ] `ItemCandidate` record: `ItemId`, `ConceptId`, `BloomLevel`, `DifficultyElo`, `ExpectedCorrectness`

### MST-010.2: Item Selection Algorithm

**Files to create:**
- `src/Cena.Pedagogy/Domain/Services/ItemSelector.cs`

**Acceptance:**
- [ ] `ItemSelector.SelectNext(IReadOnlyList<FrontierConcept> frontier, float studentTheta, IReadOnlyList<ItemCandidate> availableItems, string? lastConceptId, float interleavingProbability = 0.5f) → ItemCandidate`
- [ ] Step 1: Select target concept from frontier (highest composite rank)
- [ ] Step 2: With probability `interleavingProbability`, pick a concept different from `lastConceptId`
- [ ] Step 3: From items assessing the target concept, pick item where `|ExpectedCorrectness - 0.85|` is minimized
- [ ] If no items match the target concept, fall back to next frontier concept
- [ ] Guard: if frontier is empty, return `null` (no items available)

### MST-010.3: Interleaving Logic

**Files to create/modify:**
- `src/Cena.Pedagogy/Domain/Services/ItemSelector.cs` (add interleaving)

**Acceptance:**
- [ ] Uses `Random.Shared` for interleaving coin flip (deterministic seed available for testing)
- [ ] When interleaving triggers, selects a frontier concept from a DIFFERENT `TopicCluster` than `lastConceptId`
- [ ] If no different cluster exists in frontier, falls through to same cluster
- [ ] Interleaving probability is configurable per session (default 0.5)

**Test:**
```csharp
[Fact]
public void ExpectedCorrectness_EqualRatings_Returns50Percent()
{
    var expected = EloScoring.ExpectedCorrectness(
        studentTheta: 1200f, itemDifficulty: 1200f);

    Assert.InRange(expected, 0.49f, 0.51f);
}

[Fact]
public void ExpectedCorrectness_StrongerStudent_ReturnsHigh()
{
    var expected = EloScoring.ExpectedCorrectness(
        studentTheta: 1500f, itemDifficulty: 1200f);

    Assert.True(expected > 0.80f, $"Expected > 0.80, got {expected}");
}

[Fact]
public void UpdateRatings_CorrectAnswer_StudentGoesUp_ItemGoesUp()
{
    var (newTheta, newDiff) = EloScoring.UpdateRatings(
        studentTheta: 1200f, itemDifficulty: 1200f,
        isCorrect: true, studentK: 40f, itemK: 10f);

    Assert.True(newTheta > 1200f, "Student rating should increase after correct");
    Assert.True(newDiff > 1200f, "Item difficulty should increase (student beat it)");
}

[Fact]
public void SelectNext_PicksItemClosestTo85PercentCorrectness()
{
    var frontier = new List<FrontierConcept>
    {
        new("quadratics", "Quadratics", "algebra", 0.95f, 0.30f, 0.5f, 0.0f, 0.85f)
    };
    var items = new List<ItemCandidate>
    {
        new("item-1", "quadratics", 3, 1100f, 0f), // too easy
        new("item-2", "quadratics", 3, 1300f, 0f), // about right
        new("item-3", "quadratics", 4, 1600f, 0f), // too hard
    };
    // Precompute expected correctness for student theta = 1350
    for (int i = 0; i < items.Count; i++)
        items[i] = items[i] with
        {
            ExpectedCorrectness = EloScoring.ExpectedCorrectness(1350f, items[i].DifficultyElo)
        };

    var selected = ItemSelector.SelectNext(frontier, studentTheta: 1350f, items,
        lastConceptId: null, interleavingProbability: 0f);

    // item-2 at Elo 1300 should be closest to 0.85 for a 1350-rated student
    Assert.Equal("item-2", selected.ItemId);
}

[Fact]
public void SelectNext_InterleavingEnabled_SwitchesConcept()
{
    var frontier = new List<FrontierConcept>
    {
        new("quadratics", "Quadratics", "algebra", 0.95f, 0.30f, 0.5f, 0.0f, 0.90f),
        new("triangles", "Triangles", "geometry", 0.90f, 0.25f, 0.4f, 0.0f, 0.80f)
    };
    var items = new List<ItemCandidate>
    {
        new("item-a", "quadratics", 3, 1200f, 0.85f),
        new("item-b", "triangles", 3, 1200f, 0.85f),
    };

    // Force interleaving by setting probability to 1.0
    var selected = ItemSelector.SelectNext(frontier, studentTheta: 1200f, items,
        lastConceptId: "quadratics", interleavingProbability: 1.0f);

    Assert.Equal("triangles", selected.ConceptId);
}

[Fact]
public void SelectNext_EmptyFrontier_ReturnsNull()
{
    var selected = ItemSelector.SelectNext(
        frontier: new List<FrontierConcept>(),
        studentTheta: 1200f,
        availableItems: new List<ItemCandidate>(),
        lastConceptId: null);

    Assert.Null(selected);
}
```
