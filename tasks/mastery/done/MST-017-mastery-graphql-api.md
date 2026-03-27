# MST-017: Mastery GraphQL API

**Priority:** P1 — required for all frontend visualization
**Blocked by:** MST-006 (StudentActor mastery handler), DATA-006 (Neo4j graph cache)
**Estimated effort:** 3-5 days (M)
**Contract:** `docs/mastery-engine-architecture.md` section 7, section 6
**Research ref:** `docs/mastery-measurement-research.md` section 6

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule. `throw UnimplementedError`, `// TODO: implement`, empty bodies, and mock returns are FORBIDDEN in source code. If you cannot implement it fully, file a blocking dependency instead.

## Context

The Mastery GraphQL API exposes mastery data to the frontend applications (mobile knowledge graph, student dashboard, teacher dashboard). It queries the `StudentActor` via Proto.Actor's grain client for live mastery data and the graph cache for concept structure. The API provides four primary queries: `studentMastery` (full mastery overlay for a subject), `topicProgress` (aggregated per topic cluster), `learningFrontier` (concepts ready to learn), and `decayAlerts` (concepts needing review). All queries respect authorization — students see only their own data, teachers see their class.

## Subtasks

### MST-017.1: GraphQL Types and Schema

**Files to create:**
- `src/Cena.Api/GraphQL/Mastery/MasteryTypes.cs`
- `src/Cena.Api/GraphQL/Mastery/MasterySchema.graphql`

**Acceptance:**
- [ ] `ConceptMasteryType` GraphQL object: `conceptId`, `name`, `topicCluster`, `masteryProbability`, `recallProbability`, `effectiveMastery`, `halfLifeHours`, `bloomLevel`, `qualityQuadrant`, `lastInteraction`, `attemptCount`, `correctCount`, `masteryLevel` (enum string)
- [ ] `TopicProgressType` GraphQL object: `topicClusterId`, `name`, `conceptCount`, `masteredCount`, `averageMastery`, `weakestConcept`
- [ ] `FrontierConceptType` GraphQL object: `conceptId`, `name`, `topicCluster`, `psi`, `currentMastery`, `rank`
- [ ] `DecayAlertType` GraphQL object: `conceptId`, `name`, `recallProbability`, `hoursSinceReview`, `reviewPriority`
- [ ] All types use HotChocolate conventions

### MST-017.2: Query Resolvers

**Files to create:**
- `src/Cena.Api/GraphQL/Mastery/MasteryQueries.cs`

**Acceptance:**
- [ ] `studentMastery(studentId: ID!, subject: String!) → [ConceptMasteryType!]!`
  - Queries StudentActor for all concept states in the given subject
  - Computes `recallProbability` and `effectiveMastery` at request time
  - Joins with graph cache for concept names and topic clusters
- [ ] `topicProgress(studentId: ID!, topicClusterId: String!) → TopicProgressType!`
  - Aggregates mastery across all concepts in the topic cluster
  - Weighted average by `bagrut_weight`
  - Returns weakest concept in the cluster
- [ ] `learningFrontier(studentId: ID!, maxResults: Int = 10) → [FrontierConceptType!]!`
  - Delegates to `LearningFrontierCalculator`
  - Returns ranked frontier concepts
- [ ] `decayAlerts(studentId: ID!) → [DecayAlertType!]!`
  - Filters mastery overlay for concepts with `recallProbability < 0.85`
  - Returns sorted by review priority descending

### MST-017.3: Authorization and Error Handling

**Files to create/modify:**
- `src/Cena.Api/GraphQL/Mastery/MasteryQueries.cs` (add auth)

**Acceptance:**
- [ ] Students can only query their own `studentId` (validated against JWT claim)
- [ ] Teachers can query any student in their class roster
- [ ] Returns GraphQL error (not exception) if unauthorized
- [ ] Returns empty arrays (not null) for students with no mastery data
- [ ] Handles StudentActor unavailability gracefully (timeout → GraphQL error with retry hint)

**Test:**
```csharp
[Fact]
public async Task StudentMastery_ReturnsAllConceptStates()
{
    var executor = await CreateTestSchemaExecutor();
    SeedStudentActor("student-1", new Dictionary<string, ConceptMasteryState>
    {
        ["quadratics"] = new()
        {
            MasteryProbability = 0.85f, HalfLifeHours = 168f,
            LastInteraction = DateTimeOffset.UtcNow.AddHours(-24),
            AttemptCount = 15, CorrectCount = 13, BloomLevel = 4
        },
        ["derivatives"] = new()
        {
            MasteryProbability = 0.45f, HalfLifeHours = 72f,
            LastInteraction = DateTimeOffset.UtcNow.AddHours(-48),
            AttemptCount = 8, CorrectCount = 4, BloomLevel = 2
        }
    });

    var result = await executor.ExecuteAsync(@"
        query {
            studentMastery(studentId: ""student-1"", subject: ""math"") {
                conceptId
                masteryProbability
                effectiveMastery
                masteryLevel
            }
        }
    ");

    Assert.Null(result.Errors);
    var mastery = result.Data["studentMastery"] as IReadOnlyList<object>;
    Assert.Equal(2, mastery.Count);
}

[Fact]
public async Task TopicProgress_ReturnsAggregatedMastery()
{
    var executor = await CreateTestSchemaExecutor();
    SeedStudentWithClusterData("student-1", "algebra-cluster",
        conceptMasteries: new float[] { 0.95f, 0.80f, 0.60f, 0.30f });

    var result = await executor.ExecuteAsync(@"
        query {
            topicProgress(studentId: ""student-1"", topicClusterId: ""algebra-cluster"") {
                conceptCount
                masteredCount
                averageMastery
                weakestConcept { conceptId }
            }
        }
    ");

    Assert.Null(result.Errors);
    var progress = result.Data["topicProgress"];
    Assert.Equal(4, (int)progress["conceptCount"]);
    Assert.Equal(1, (int)progress["masteredCount"]); // only 0.95 is >= 0.90
}

[Fact]
public async Task DecayAlerts_ReturnsSortedByPriority()
{
    var executor = await CreateTestSchemaExecutor();
    var now = DateTimeOffset.UtcNow;
    SeedStudentActor("student-1", new Dictionary<string, ConceptMasteryState>
    {
        ["concept-a"] = new()
        {
            MasteryProbability = 0.92f, HalfLifeHours = 72f,
            LastInteraction = now.AddDays(-7)
        },
        ["concept-b"] = new()
        {
            MasteryProbability = 0.88f, HalfLifeHours = 168f,
            LastInteraction = now.AddDays(-3)
        }
    });

    var result = await executor.ExecuteAsync(@"
        query {
            decayAlerts(studentId: ""student-1"") {
                conceptId
                recallProbability
                reviewPriority
            }
        }
    ");

    Assert.Null(result.Errors);
    var alerts = result.Data["decayAlerts"] as IReadOnlyList<object>;
    // concept-a has h=72h, elapsed=168h → recall ≈ 0.088 (severely decayed)
    // concept-b has h=168h, elapsed=72h → recall ≈ 0.73 (mild decay)
    Assert.True(alerts.Count >= 1);
}

[Fact]
public async Task StudentMastery_UnauthorizedStudent_ReturnsError()
{
    var executor = await CreateTestSchemaExecutor(authenticatedAs: "student-2");

    var result = await executor.ExecuteAsync(@"
        query {
            studentMastery(studentId: ""student-1"", subject: ""math"") {
                conceptId
            }
        }
    ");

    Assert.NotNull(result.Errors);
    Assert.Contains(result.Errors, e => e.Message.Contains("unauthorized"));
}
```
