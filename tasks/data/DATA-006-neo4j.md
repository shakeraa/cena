# DATA-006: Neo4j Curriculum Knowledge Graph Schema & .NET Driver

**Priority:** P1 — blocks exercise selection and MCM routing
**Blocked by:** INF-002 (Neo4j AuraDB instance)
**Estimated effort:** 3 days
**Contract:** `contracts/data/neo4j-schema.cypher`

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule. `throw UnimplementedError`, `// TODO: implement`, empty bodies, and mock returns are FORBIDDEN in source code. If you cannot implement it fully, file a blocking dependency instead.

## Context
The Neo4j knowledge graph stores the curriculum structure (concepts, prerequisites, categories), methodology recommendations (MCM edges), and is queried on every exercise selection decision. The hot-path query is: "given error_type X on concept in category Y, which methodology should we switch to?" This task implements the full schema from `neo4j-schema.cypher`, seeds sample data, provides the .NET driver wrapper for the actor system, and validates DAG integrity (no cycles in prerequisite chains).

## Subtasks

### DATA-006.1: Constraints, Indexes & Node Creation
**Files to create/modify:**
- `src/Cena.Data/KnowledgeGraph/Neo4jSchemaSetup.cs` — executes constraint/index creation from contract
- `src/Cena.Data/KnowledgeGraph/SchemaScripts/constraints.cypher` — idempotent constraints
- `src/Cena.Data/KnowledgeGraph/SchemaScripts/indexes.cypher` — composite and fulltext indexes

**Acceptance:**
- [ ] 5 unique constraints created (idempotent — safe to re-run):
  - `concept_id_unique` on `(c:Concept) REQUIRE c.id IS UNIQUE`
  - `error_type_id_unique` on `(e:ErrorType) REQUIRE e.id IS UNIQUE`
  - `concept_category_id_unique` on `(cc:ConceptCategory) REQUIRE cc.id IS UNIQUE`
  - `methodology_id_unique` on `(m:Methodology) REQUIRE m.id IS UNIQUE`
  - `subject_id_unique` on `(s:Subject) REQUIRE s.id IS UNIQUE`
- [ ] 4 indexes created:
  - `concept_subject_difficulty` composite on `(c:Concept) ON (c.subject, c.difficulty)`
  - `concept_bloom` on `(c:Concept) ON (c.bloom_level)`
  - `error_type_name` on `(e:ErrorType) ON (e.name)`
  - `concept_name_fulltext` fulltext on `(c:Concept) ON EACH [c.name, c.description]`
- [ ] Node types created via MERGE (idempotent):
  - 1 Subject: `subj-math` (Mathematics)
  - 3 ErrorTypes: `err-procedural`, `err-conceptual`, `err-motivational` (matching `ErrorType` enum in `acl-interfaces.py`)
  - 3 ConceptCategories: `cat-arithmetic`, `cat-algebra`, `cat-geometry`
  - 5 Methodologies: `meth-socratic`, `meth-spaced-rep`, `meth-feynman`, `meth-worked-examples`, `meth-gamified-drill` (matching `MethodologyId` enum from `acl-interfaces.py`)
- [ ] All IDs are application-generated strings (not Neo4j internal IDs) to align with Marten stream keys

**Test:**
```csharp
[Fact]
public async Task SchemaSetup_CreatesConstraints()
{
    var driver = CreateTestDriver();
    await SchemaSetup.ApplyConstraintsAsync(driver);

    await using var session = driver.AsyncSession();
    var result = await session.RunAsync("SHOW CONSTRAINTS");
    var constraints = await result.ToListAsync();

    var constraintNames = constraints.Select(r => r["name"].As<string>()).ToList();
    Assert.Contains("concept_id_unique", constraintNames);
    Assert.Contains("error_type_id_unique", constraintNames);
    Assert.Contains("methodology_id_unique", constraintNames);
}

[Fact]
public async Task SchemaSetup_CreatesAllNodes()
{
    var driver = CreateTestDriver();
    await SchemaSetup.ApplySchemaAsync(driver);

    await using var session = driver.AsyncSession();

    // Verify error types
    var errors = await session.RunAsync("MATCH (e:ErrorType) RETURN e.name AS name");
    var errorNames = (await errors.ToListAsync()).Select(r => r["name"].As<string>()).ToList();
    Assert.Contains("procedural", errorNames);
    Assert.Contains("conceptual", errorNames);
    Assert.Contains("motivational", errorNames);

    // Verify methodologies
    var methods = await session.RunAsync("MATCH (m:Methodology) RETURN m.name AS name");
    var methodNames = (await methods.ToListAsync()).Select(r => r["name"].As<string>()).ToList();
    Assert.Contains("socratic", methodNames);
    Assert.Contains("feynman", methodNames);
    Assert.Contains("worked_examples", methodNames);
    Assert.Contains("gamified_drill", methodNames);
    Assert.Contains("spaced_repetition", methodNames);
}

[Fact]
public async Task SchemaSetup_IsIdempotent()
{
    var driver = CreateTestDriver();
    // Run twice — should not throw
    await SchemaSetup.ApplySchemaAsync(driver);
    await SchemaSetup.ApplySchemaAsync(driver);

    await using var session = driver.AsyncSession();
    var result = await session.RunAsync("MATCH (m:Methodology) RETURN count(m) AS c");
    var count = (await result.SingleAsync())["c"].As<int>();
    Assert.Equal(5, count);  // Not doubled
}
```

---

### DATA-006.2: Sample Concept DAG & Prerequisite Edges
**Files to create/modify:**
- `src/Cena.Data/KnowledgeGraph/SchemaScripts/sample_concepts.cypher` — 5 sample concepts with prerequisite chain
- `src/Cena.Data/KnowledgeGraph/SeedData.cs` — executes sample data scripts

**Acceptance:**
- [ ] 5 sample concepts created from contract:
  - `math-addition`: difficulty=1, bloom=`remember`, prerequisite_count=0, estimated_minutes=30
  - `math-subtraction`: difficulty=1, bloom=`remember`, prerequisite_count=1, estimated_minutes=30
  - `math-multiplication`: difficulty=2, bloom=`understand`, prerequisite_count=1, estimated_minutes=45
  - `math-fractions`: difficulty=3, bloom=`apply`, prerequisite_count=1, estimated_minutes=60
  - `math-negative-numbers`: difficulty=3, bloom=`apply`, prerequisite_count=2, estimated_minutes=45
- [ ] All concepts linked to Subject `subj-math` via `BELONGS_TO`
- [ ] Category membership: addition/subtraction/multiplication/fractions -> `cat-arithmetic`, negative-numbers -> `cat-algebra`
- [ ] 5 prerequisite edges with properties from contract:
  - `math-addition` -[PREREQUISITE_OF]-> `math-multiplication`: strength=1.0, empirically_validated=true, sample_size=2847
  - `math-addition` -[PREREQUISITE_OF]-> `math-subtraction`: strength=0.9, validated=true, sample_size=3102
  - `math-multiplication` -[PREREQUISITE_OF]-> `math-fractions`: strength=1.0, validated=true, sample_size=1956
  - `math-subtraction` -[PREREQUISITE_OF]-> `math-negative-numbers`: strength=1.0, validated=true, sample_size=1483
  - `math-fractions` -[PREREQUISITE_OF]-> `math-negative-numbers`: strength=0.6, validated=false, sample_size=0
- [ ] DAG has no cycles (verified by query)

**Test:**
```csharp
[Fact]
public async Task PrerequisiteDAG_HasNoCycles()
{
    var driver = CreateTestDriver();
    await SchemaSetup.ApplySchemaAsync(driver);
    await SeedData.SeedSampleConceptsAsync(driver);

    await using var session = driver.AsyncSession();
    var result = await session.RunAsync(
        "MATCH path = (c:Concept)-[:PREREQUISITE_OF*2..]->(c) RETURN count(path) AS cycles"
    );
    var cycles = (await result.SingleAsync())["cycles"].As<int>();
    Assert.Equal(0, cycles);
}

[Fact]
public async Task PrerequisiteChain_TraversesCorrectly()
{
    var driver = CreateTestDriver();
    await SchemaSetup.ApplySchemaAsync(driver);
    await SeedData.SeedSampleConceptsAsync(driver);

    await using var session = driver.AsyncSession();
    // Get prerequisites for math-negative-numbers (should include subtraction, addition, fractions)
    var result = await session.RunAsync(@"
        MATCH path = (prereq:Concept)-[:PREREQUISITE_OF*1..5]->(target:Concept {id: 'math-negative-numbers'})
        RETURN prereq.id AS id, length(path) AS depth
        ORDER BY depth DESC
    ");
    var prereqs = await result.ToListAsync();
    var ids = prereqs.Select(r => r["id"].As<string>()).ToList();
    Assert.Contains("math-subtraction", ids);
    Assert.Contains("math-addition", ids);
}

[Fact]
public async Task PrerequisiteEdge_HasCorrectProperties()
{
    var driver = CreateTestDriver();
    await SchemaSetup.ApplySchemaAsync(driver);
    await SeedData.SeedSampleConceptsAsync(driver);

    await using var session = driver.AsyncSession();
    var result = await session.RunAsync(@"
        MATCH (:Concept {id: 'math-addition'})-[r:PREREQUISITE_OF]->(:Concept {id: 'math-multiplication'})
        RETURN r.strength AS strength, r.empirically_validated AS validated, r.sample_size AS n
    ");
    var record = await result.SingleAsync();
    Assert.Equal(1.0, record["strength"].As<double>());
    Assert.True(record["validated"].As<bool>());
    Assert.Equal(2847, record["n"].As<int>());
}
```

**Edge cases:**
- Attempt to add a cycle (e.g., fractions -> addition) -> application-level check rejects it
- Prerequisite with strength < 1.0 -> recommended but not blocking (soft gate)
- Empty graph (no concepts) -> hot-path queries return empty results, not errors

---

### DATA-006.3: MCM Edges (Methodology-Concept Matrix)
**Files to create/modify:**
- `src/Cena.Data/KnowledgeGraph/SchemaScripts/mcm_edges.cypher` — MCM RECOMMENDS edges from contract
- `src/Cena.Data/KnowledgeGraph/McmRepository.cs` — .NET query wrapper for MCM lookup

**Acceptance:**
- [ ] 8 MCM RECOMMENDS edges from contract:
  - `err-procedural` -> `meth-worked-examples` (cat-arithmetic, confidence=0.87, rank=1, min_attempts=3)
  - `err-procedural` -> `meth-gamified-drill` (cat-arithmetic, confidence=0.72, rank=2, min_attempts=5)
  - `err-conceptual` -> `meth-socratic` (cat-arithmetic, confidence=0.91, rank=1, min_attempts=2)
  - `err-conceptual` -> `meth-feynman` (cat-arithmetic, confidence=0.78, rank=2, min_attempts=4)
  - `err-motivational` -> `meth-gamified-drill` (cat-arithmetic, confidence=0.83, rank=1, min_attempts=2)
  - `err-motivational` -> `meth-spaced-rep` (cat-arithmetic, confidence=0.65, rank=2, min_attempts=4)
  - `err-conceptual` -> `meth-feynman` (cat-algebra, confidence=0.85, rank=1, min_attempts=2)
  - `err-conceptual` -> `meth-socratic` (cat-algebra, confidence=0.79, rank=2, min_attempts=4)
- [ ] RECOMMENDS edge properties: `for_category`, `confidence`, `rank`, `min_attempts_for_switch`, `last_recalculated`, `sample_size`
- [ ] Hot-path query implemented in `McmRepository`:
  ```cypher
  MATCH (et:ErrorType {name: $errorType})-[r:RECOMMENDS]->(m:Methodology)
  WHERE r.for_category = $conceptCategory
  RETURN m.name AS methodology, r.confidence, r.rank, r.min_attempts_for_switch
  ORDER BY r.rank ASC
  ```
- [ ] MCM results cached in Redis (see DATA-007) with key `cena:kg:mcm:${errorType}:{${categoryId}}`

**Test:**
```csharp
[Fact]
public async Task McmLookup_ReturnsRankedMethodologies()
{
    var driver = CreateTestDriver();
    await SchemaSetup.ApplySchemaAsync(driver);
    await SeedData.SeedSampleConceptsAsync(driver);
    await SeedData.SeedMcmEdgesAsync(driver);

    var repo = new McmRepository(driver);
    var results = await repo.GetRecommendationsAsync("procedural", "cat-arithmetic");

    Assert.Equal(2, results.Count);
    Assert.Equal("worked_examples", results[0].Methodology);
    Assert.Equal(0.87, results[0].Confidence, precision: 2);
    Assert.Equal(1, results[0].Rank);
    Assert.Equal("gamified_drill", results[1].Methodology);
    Assert.Equal(2, results[1].Rank);
}

[Fact]
public async Task McmLookup_ReturnsEmpty_ForUnknownCategory()
{
    var driver = CreateTestDriver();
    await SchemaSetup.ApplySchemaAsync(driver);
    await SeedData.SeedMcmEdgesAsync(driver);

    var repo = new McmRepository(driver);
    var results = await repo.GetRecommendationsAsync("procedural", "cat-nonexistent");
    Assert.Empty(results);
}

[Fact]
public async Task McmLookup_AlgebraConceptual_ReturnsFeymanFirst()
{
    var driver = CreateTestDriver();
    await SchemaSetup.ApplySchemaAsync(driver);
    await SeedData.SeedMcmEdgesAsync(driver);

    var repo = new McmRepository(driver);
    var results = await repo.GetRecommendationsAsync("conceptual", "cat-algebra");

    Assert.Equal("feynman", results[0].Methodology);
    Assert.Equal(0.85, results[0].Confidence, precision: 2);
    Assert.Equal("socratic", results[1].Methodology);
}

[Fact]
public async Task McmCoverage_AllErrorTypesHaveRecommendations()
{
    var driver = CreateTestDriver();
    await SchemaSetup.ApplySchemaAsync(driver);
    await SeedData.SeedMcmEdgesAsync(driver);

    var repo = new McmRepository(driver);
    foreach (var errorType in new[] { "procedural", "conceptual", "motivational" })
    {
        var results = await repo.GetRecommendationsAsync(errorType, "cat-arithmetic");
        Assert.NotEmpty(results);
    }
}
```

**Edge cases:**
- Parallel RECOMMENDS edges (same ErrorType -> same Methodology, different categories) -> `for_category` property distinguishes them
- MCM confidence recalculation (async job updates edge properties) -> new confidence reflected on next query
- MCM lookup for error_type "none" -> no recommendations (correct answer, no switch needed)

---

### DATA-006.4: .NET Neo4j Driver Configuration
**Files to create/modify:**
- `src/Cena.Data/KnowledgeGraph/Neo4jConfiguration.cs` — driver factory, connection pooling, DI registration
- `src/Cena.Data/KnowledgeGraph/Neo4jHealthCheck.cs` — health check for readiness probe

**Acceptance:**
- [ ] Neo4j .NET Driver v5.x configured via DI:
  ```csharp
  services.AddSingleton<IDriver>(sp => GraphDatabase.Driver(
      uri: config["NEO4J_URI"],           // bolt://localhost:7687 or neo4j+s://xxx.auradb.io
      authToken: AuthTokens.Basic(config["NEO4J_USER"], config["NEO4J_PASSWORD"]),
      configBuilder => configBuilder
          .WithMaxConnectionPoolSize(50)
          .WithConnectionAcquisitionTimeout(TimeSpan.FromSeconds(5))
          .WithMaxTransactionRetryTime(TimeSpan.FromSeconds(15))
  ));
  ```
- [ ] Connection pool: max 50 connections, acquisition timeout 5s
- [ ] Retry policy: max 15s for transient failures (network blips)
- [ ] Health check: `GET /health/ready` includes Neo4j connectivity status
  - Executes `RETURN 1` query
  - Timeout: 3 seconds
  - Unhealthy -> ECS stops routing traffic
- [ ] Driver disposed on application shutdown (IAsyncDisposable)
- [ ] Supports both AuraDB (production) and local Docker Neo4j (development)

**Test:**
```csharp
[Fact]
public async Task Neo4jHealthCheck_ReturnsHealthy()
{
    var driver = CreateTestDriver();
    var healthCheck = new Neo4jHealthCheck(driver);
    var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());
    Assert.Equal(HealthStatus.Healthy, result.Status);
}

[Fact]
public async Task Neo4jHealthCheck_ReturnsUnhealthy_WhenDown()
{
    var driver = GraphDatabase.Driver("bolt://nonexistent:7687");
    var healthCheck = new Neo4jHealthCheck(driver);
    var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());
    Assert.Equal(HealthStatus.Unhealthy, result.Status);
}

[Fact]
public void Neo4jDriver_RegisteredAsSingleton()
{
    var services = new ServiceCollection();
    services.AddCenaKnowledgeGraph("bolt://localhost:7687", "neo4j", "test");
    var provider = services.BuildServiceProvider();

    var driver1 = provider.GetRequiredService<IDriver>();
    var driver2 = provider.GetRequiredService<IDriver>();
    Assert.Same(driver1, driver2);
}
```

**Edge cases:**
- AuraDB connection string with `neo4j+s://` (TLS required) -> driver handles automatically
- Network partition to Neo4j -> retry with backoff up to 15s, then fail with clear error
- All connections exhausted -> acquisition timeout (5s) then throw, caller handles gracefully

---

## Integration Test (all subtasks combined)

```csharp
[Fact]
public async Task FullNeo4jSetup_EndToEnd()
{
    var driver = CreateTestDriver();

    // 1. Apply schema (constraints, indexes, nodes)
    await SchemaSetup.ApplySchemaAsync(driver);

    // 2. Seed sample concepts and MCM edges
    await SeedData.SeedSampleConceptsAsync(driver);
    await SeedData.SeedMcmEdgesAsync(driver);

    // 3. Verify DAG integrity
    await using var session = driver.AsyncSession();
    var cycles = await session.RunAsync(
        "MATCH path = (c:Concept)-[:PREREQUISITE_OF*2..]->(c) RETURN count(path) AS n"
    );
    Assert.Equal(0, (await cycles.SingleAsync())["n"].As<int>());

    // 4. MCM hot-path query works
    var repo = new McmRepository(driver);
    var recommendations = await repo.GetRecommendationsAsync("conceptual", "cat-arithmetic");
    Assert.Equal("socratic", recommendations[0].Methodology);
    Assert.Equal(0.91, recommendations[0].Confidence, precision: 2);

    // 5. Prerequisite chain works
    var prereqs = await repo.GetPrerequisitesAsync("math-negative-numbers", maxDepth: 5);
    Assert.True(prereqs.Count >= 2);

    // 6. Health check passes
    var healthCheck = new Neo4jHealthCheck(driver);
    var health = await healthCheck.CheckHealthAsync(new HealthCheckContext());
    Assert.Equal(HealthStatus.Healthy, health.Status);
}
```

## Rollback Criteria
If Neo4j causes latency or availability issues:
- Fall back to in-memory concept graph loaded from JSON file at startup
- MCM lookup falls back to hardcoded mapping table (same data, no graph traversal)
- Prerequisite chains cached aggressively in Redis (DATA-007) to minimize Neo4j round-trips

## Definition of Done
- [ ] All 4 subtasks pass their individual tests
- [ ] Integration test passes
- [ ] `dotnet test --filter "Category=Neo4j"` -> 0 failures
- [ ] All 5 constraints, 4 indexes, and 8 MCM edges created correctly
- [ ] DAG cycle detection query returns 0
- [ ] MCM hot-path query returns ranked methodologies in < 10ms (local)
- [ ] .NET driver registered as singleton with proper connection pooling
- [ ] Health check integrated with readiness probe
- [ ] PR reviewed by architect (you)
