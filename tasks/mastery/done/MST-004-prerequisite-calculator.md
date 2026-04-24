# MST-004: Prerequisite Support Calculator

**Priority:** P0 — effective mastery depends on prerequisite gating
**Blocked by:** MST-001 (ConceptMasteryState), DATA-006 (Neo4j graph cache)
**Estimated effort:** 3-5 days (M)
**Contract:** `docs/mastery-engine-architecture.md` section 3.1 step 4, section 4.3, section 6.1
**Research ref:** `docs/mastery-measurement-research.md` section 4.1

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule. `throw UnimplementedError`, `// TODO: implement`, empty bodies, and mock returns are FORBIDDEN in source code. If you cannot implement it fully, file a blocking dependency instead.

## Context

Prerequisite support quantifies how well a student has mastered the foundational concepts required for a given concept. The formula is `min(effective_mastery(p) for p in prerequisites(c))`. When a prerequisite decays, all downstream concepts' effective mastery drops automatically. The calculator uses an in-memory graph cache loaded from Neo4j at actor startup — no network calls on the hot path. A weighted penalty fallback handles the Phase 1 graduated penalty: `effective_mastery(c) = measured_mastery(c) * product(max(mastery(p)/0.85, 1.0))` for prerequisites.

## Subtasks

### MST-004.1: Graph Cache Interface and Prerequisite Lookup

**Files to create:**
- `src/Cena.Domain/Learner/Mastery/IConceptGraphCache.cs`
- `src/Cena.Domain/Learner/Mastery/ConceptNode.cs`

**Acceptance:**
- [ ] `IConceptGraphCache` interface: `GetPrerequisites(string conceptId) → IReadOnlyList<PrerequisiteEdge>`, `GetDescendants(string conceptId) → IReadOnlyList<string>`, `GetDepth(string conceptId) → int`
- [ ] `ConceptNode` record: `Id`, `Name`, `Subject`, `TopicCluster`, `DepthLevel`, `IntrinsicLoad`, `BagrutWeight`, `BloomMax`
- [ ] `PrerequisiteEdge` record: `(string SourceConceptId, string TargetConceptId, float Strength)`
- [ ] `IConceptGraphCache` also exposes `Concepts` as `IReadOnlyDictionary<string, ConceptNode>`
- [ ] All lookups are O(1) dictionary-based (no graph traversal on hot path for direct prerequisites)

### MST-004.2: Prerequisite Support Computation

**Files to create:**
- `src/Cena.Domain/Learner/Mastery/PrerequisiteCalculator.cs`

**Acceptance:**
- [ ] `PrerequisiteCalculator.ComputeSupport(string conceptId, IReadOnlyDictionary<string, ConceptMasteryState> masteryOverlay, IConceptGraphCache graphCache) → float`
- [ ] If concept has no prerequisites, returns `1.0` (no gating)
- [ ] Primary formula: `min(effective_mastery(p) for p in prerequisites)` where effective mastery uses `MasteryProbability` from overlay
- [ ] If a prerequisite has no entry in the overlay, treat its mastery as `0.0` (never encountered)
- [ ] Return value clamped to [0.0, 1.0]
- [ ] Method is `static`, zero allocation on hot path (uses `Span<float>` or inline min)

### MST-004.3: Weighted Penalty Fallback (Phase 1)

**Files to create/modify:**
- `src/Cena.Domain/Learner/Mastery/PrerequisiteCalculator.cs` (add `ComputeWeightedPenalty` method)

**Acceptance:**
- [ ] `PrerequisiteCalculator.ComputeWeightedPenalty(string conceptId, IReadOnlyDictionary<string, ConceptMasteryState> masteryOverlay, IConceptGraphCache graphCache) → float`
- [ ] Formula: `product(max(mastery(p) / 0.85, 1.0)) for p in prerequisites`
- [ ] When all prerequisites are at or above 0.85 mastery, penalty is 1.0 (no reduction)
- [ ] When a prerequisite is at 0.42, penalty factor for that edge is `0.42 / 0.85 ≈ 0.494`
- [ ] Multiple weak prerequisites compound multiplicatively
- [ ] Guard: empty prerequisites returns `1.0`

**Test:**
```csharp
[Fact]
public void ComputeSupport_NoPrerequisites_ReturnsOne()
{
    var graphCache = new FakeGraphCache(prerequisites: new Dictionary<string, List<PrerequisiteEdge>>());
    var overlay = new Dictionary<string, ConceptMasteryState>();

    var support = PrerequisiteCalculator.ComputeSupport("concept-A", overlay, graphCache);

    Assert.Equal(1.0f, support);
}

[Fact]
public void ComputeSupport_AllPrereqsMastered_ReturnsMinMastery()
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
        ["concept-A"] = new() { MasteryProbability = 0.95f },
        ["concept-B"] = new() { MasteryProbability = 0.80f }
    };

    var support = PrerequisiteCalculator.ComputeSupport("concept-C", overlay, graphCache);

    Assert.Equal(0.80f, support); // min(0.95, 0.80)
}

[Fact]
public void ComputeSupport_MissingPrereq_ReturnsZero()
{
    var graphCache = new FakeGraphCache(prerequisites: new Dictionary<string, List<PrerequisiteEdge>>
    {
        ["concept-B"] = new()
        {
            new PrerequisiteEdge("concept-A", "concept-B", 1.0f)
        }
    });
    var overlay = new Dictionary<string, ConceptMasteryState>(); // concept-A not in overlay

    var support = PrerequisiteCalculator.ComputeSupport("concept-B", overlay, graphCache);

    Assert.Equal(0.0f, support); // concept-A never encountered → 0.0
}

[Fact]
public void ComputeSupport_ThreeConceptChain_DecayPropagates()
{
    // A → B → C. If A decays, C's support drops.
    var graphCache = new FakeGraphCache(prerequisites: new Dictionary<string, List<PrerequisiteEdge>>
    {
        ["concept-B"] = new() { new PrerequisiteEdge("concept-A", "concept-B", 1.0f) },
        ["concept-C"] = new() { new PrerequisiteEdge("concept-B", "concept-C", 1.0f) }
    });
    var overlay = new Dictionary<string, ConceptMasteryState>
    {
        ["concept-A"] = new() { MasteryProbability = 0.40f }, // decayed
        ["concept-B"] = new() { MasteryProbability = 0.90f }
    };

    var supportC = PrerequisiteCalculator.ComputeSupport("concept-C", overlay, graphCache);

    Assert.Equal(0.90f, supportC); // direct prereq is B at 0.90
    // But B's effective mastery is gated by A at 0.40 — captured at effective mastery layer
}

[Fact]
public void ComputeWeightedPenalty_AllAboveGate_ReturnsOne()
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
        ["concept-A"] = new() { MasteryProbability = 0.92f },
        ["concept-B"] = new() { MasteryProbability = 0.88f }
    };

    var penalty = PrerequisiteCalculator.ComputeWeightedPenalty("concept-C", overlay, graphCache);

    Assert.Equal(1.0f, penalty); // both above 0.85 gate → no penalty
}

[Fact]
public void ComputeWeightedPenalty_WeakPrereq_AppliesPenalty()
{
    var graphCache = new FakeGraphCache(prerequisites: new Dictionary<string, List<PrerequisiteEdge>>
    {
        ["concept-B"] = new() { new PrerequisiteEdge("concept-A", "concept-B", 1.0f) }
    });
    var overlay = new Dictionary<string, ConceptMasteryState>
    {
        ["concept-A"] = new() { MasteryProbability = 0.42f }
    };

    var penalty = PrerequisiteCalculator.ComputeWeightedPenalty("concept-B", overlay, graphCache);

    // 0.42 / 0.85 ≈ 0.494
    Assert.InRange(penalty, 0.49f, 0.50f);
}
```
