# MST-013: KST Onboarding Diagnostic Engine

**Priority:** P1 — first impression of the system; determines initial learning path
**Blocked by:** DATA-006 (Neo4j graph cache), MST-001 (ConceptMasteryState)
**Estimated effort:** 1-2 weeks (L)
**Contract:** `docs/mastery-engine-architecture.md` section 9.1
**Research ref:** `docs/mastery-measurement-research.md` section 5.1

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule. `throw UnimplementedError`, `// TODO: implement`, empty bodies, and mock returns are FORBIDDEN in source code. If you cannot implement it fully, file a blocking dependency instead.

## Context

During onboarding (Step 2 of signup flow), an adaptive 10-15 question diagnostic uses Knowledge Space Theory to efficiently determine what the student already knows. The algorithm maintains a posterior distribution over feasible knowledge states (downward-closed subsets of the concept graph) and selects the most informative concept to test at each step. After the diagnostic, the MAP estimate is used to populate the student's initial mastery overlay. For ~200 concepts, full state enumeration is intractable, so we use approximate posterior tracking with top-K states.

## Subtasks

### MST-013.1: Knowledge State Space Builder

**Files to create:**
- `src/Cena.Domain/Learner/Mastery/KnowledgeStateSpace.cs`
- `src/Cena.Domain/Learner/Mastery/KnowledgeState.cs`

**Acceptance:**
- [ ] `KnowledgeState` record: immutable `ImmutableHashSet<string>` of mastered concept IDs
- [ ] `KnowledgeStateSpace.BuildFeasibleStates(IConceptGraphCache graphCache, int maxStates = 500) → IReadOnlyList<KnowledgeState>`
- [ ] A state is feasible (downward-closed) if for every concept in the state, all its prerequisites are also in the state
- [ ] For small graphs (< 20 concepts), enumerate all feasible states
- [ ] For large graphs (>= 20 concepts), use sampling: generate `maxStates` random feasible states by topological-order inclusion
- [ ] Empty set and full set are always included as feasible states

### MST-013.2: Adaptive Question Selector

**Files to create:**
- `src/Cena.Pedagogy/Domain/Services/DiagnosticEngine.cs`
- `src/Cena.Pedagogy/Domain/DiagnosticResult.cs`

**Acceptance:**
- [ ] `DiagnosticEngine.SelectNextConcept(IReadOnlyList<KnowledgeState> feasibleStates, float[] posterior) → string`
- [ ] Selects the concept that maximally splits the posterior distribution (maximum entropy reduction)
- [ ] For each candidate concept: compute `P(concept in state)` weighted by posterior → pick concept closest to 0.5 (maximum information)
- [ ] `DiagnosticEngine.UpdatePosterior(float[] posterior, IReadOnlyList<KnowledgeState> states, string testedConceptId, bool isCorrect, bool isSkip = false) → float[]`
- [ ] Correct: multiply weight by 0.9 for states containing concept, 0.1 for states not containing it
- [ ] Incorrect: multiply weight by 0.1 for states containing concept, 0.9 for states not containing it
- [ ] Skip: multiply weight by 0.3 for states containing concept, 0.7 for states not containing it (weak signal of gap)
- [ ] Normalize posterior after update

### MST-013.3: Full Diagnostic Flow

**Files to create/modify:**
- `src/Cena.Pedagogy/Domain/Services/DiagnosticEngine.cs` (add `RunDiagnostic`)
- `src/Cena.Pedagogy/Domain/DiagnosticResult.cs`

**Acceptance:**
- [ ] `DiagnosticEngine.RunDiagnostic(IConceptGraphCache graphCache, Func<string, bool?> askQuestion, int minQuestions = 10, int maxQuestions = 15) → DiagnosticResult`
- [ ] `DiagnosticResult` record: `MasteredConcepts` (IReadOnlySet<string>), `GapConcepts` (IReadOnlySet<string>), `Confidence` (float), `QuestionsAsked` (int)
- [ ] Iterates: select concept → ask question → update posterior → repeat
- [ ] Stops after `maxQuestions` or when posterior entropy drops below threshold (high confidence)
- [ ] Final MAP estimate: select the knowledge state with highest posterior probability
- [ ] `askQuestion` returns `true` (correct), `false` (incorrect), or `null` (skip)

**Test:**
```csharp
[Fact]
public void SelectNextConcept_PicksMostInformative()
{
    // 3-concept linear chain: A → B → C
    // Feasible states: {}, {A}, {A,B}, {A,B,C}
    var states = new List<KnowledgeState>
    {
        new(ImmutableHashSet<string>.Empty),
        new(ImmutableHashSet.Create("A")),
        new(ImmutableHashSet.Create("A", "B")),
        new(ImmutableHashSet.Create("A", "B", "C"))
    };
    var posterior = new float[] { 0.25f, 0.25f, 0.25f, 0.25f }; // uniform

    var concept = DiagnosticEngine.SelectNextConcept(states, posterior);

    // B appears in 2 of 4 states → P(B) = 0.50 → maximum information
    Assert.Equal("B", concept);
}

[Fact]
public void UpdatePosterior_CorrectAnswer_IncreasesStatesWithConcept()
{
    var states = new List<KnowledgeState>
    {
        new(ImmutableHashSet<string>.Empty),
        new(ImmutableHashSet.Create("A")),
        new(ImmutableHashSet.Create("A", "B")),
        new(ImmutableHashSet.Create("A", "B", "C"))
    };
    var prior = new float[] { 0.25f, 0.25f, 0.25f, 0.25f };

    var posterior = DiagnosticEngine.UpdatePosterior(prior, states, "A", isCorrect: true);

    // States with A: indices 1, 2, 3 should increase; index 0 should decrease
    Assert.True(posterior[0] < prior[0], "State without A should decrease");
    Assert.True(posterior[1] > prior[1], "State with A should increase");
}

[Fact]
public void UpdatePosterior_Skip_WeakSignal()
{
    var states = new List<KnowledgeState>
    {
        new(ImmutableHashSet<string>.Empty),
        new(ImmutableHashSet.Create("A"))
    };
    var prior = new float[] { 0.50f, 0.50f };

    var posteriorSkip = DiagnosticEngine.UpdatePosterior(prior, states, "A",
        isCorrect: false, isSkip: true);
    var posteriorWrong = DiagnosticEngine.UpdatePosterior(prior, states, "A",
        isCorrect: false, isSkip: false);

    // Skip should shift less than a wrong answer
    Assert.True(posteriorSkip[1] > posteriorWrong[1],
        "Skip should be less punishing than incorrect");
}

[Fact]
public void RunDiagnostic_KnownStudent_IdentifiesMasteredConcepts()
{
    // Student knows A and B but not C in a chain A → B → C
    var graphCache = new FakeGraphCache(
        concepts: new Dictionary<string, ConceptNode>
        {
            ["A"] = new("A", "Basics", "math", "algebra", 1, 0.3f, 0.5f, 3),
            ["B"] = new("B", "Intermediate", "math", "algebra", 2, 0.5f, 0.6f, 4),
            ["C"] = new("C", "Advanced", "math", "algebra", 3, 0.7f, 0.7f, 5),
        },
        prerequisites: new Dictionary<string, List<PrerequisiteEdge>>
        {
            ["B"] = new() { new("A", "B", 1.0f) },
            ["C"] = new() { new("B", "C", 1.0f) }
        });

    // Simulate: student answers correctly for A, B; incorrectly for C
    var answers = new Dictionary<string, bool> { ["A"] = true, ["B"] = true, ["C"] = false };

    var result = DiagnosticEngine.RunDiagnostic(graphCache,
        conceptId => answers.GetValueOrDefault(conceptId, false),
        minQuestions: 3, maxQuestions: 5);

    Assert.Contains("A", result.MasteredConcepts);
    Assert.Contains("B", result.MasteredConcepts);
    Assert.Contains("C", result.GapConcepts);
}
```
