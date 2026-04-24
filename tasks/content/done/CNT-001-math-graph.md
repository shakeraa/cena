# CNT-001: Math Curriculum — 2000 Concept Nodes, Prerequisites, Arabic Names, Bloom Levels

**Priority:** P0 — blocks all content-dependent tasks
**Blocked by:** DATA-003 (Neo4j schema)
**Estimated effort:** 5 days
**Contract:** `contracts/data/neo4j-schema.cypher` (Concept nodes, PREREQUISITE_OF edges, BELONGS_TO, IN_CATEGORY)

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule. `throw UnimplementedError`, `// TODO: implement`, empty bodies, and mock returns are FORBIDDEN in source code. If you cannot implement it fully, file a blocking dependency instead.

## Context

The Israeli Bagrut math curriculum (3-unit, 4-unit, 5-unit) needs to be modeled as a directed acyclic graph (DAG) in Neo4j. Each concept node carries Hebrew and Arabic names, Bloom's taxonomy level, difficulty rating, and subject metadata. Prerequisites form directed edges. This graph is the foundation for adaptive learning — BKT mastery tracking, HLR spaced repetition scheduling, and MCM methodology routing all depend on it.

## Subtasks

### CNT-001.1: Curriculum Mapping — Topic Decomposition

**Files to create/modify:**
- `content/math/curriculum_map.json` — 2000 concepts structured by topic/subtopic
- `content/math/bloom_mapping.csv` — concept -> Bloom level mapping
- `docs/content/math-curriculum-methodology.md` — how the decomposition was done

**Acceptance:**
- [ ] 2000 concept nodes covering: Arithmetic (200), Algebra (400), Geometry (300), Trigonometry (200), Calculus (300), Probability & Statistics (200), Sequences & Series (150), Analytic Geometry (150)
- [ ] Each concept has: `id` (UUID), `name_en`, `name_he`, `name_ar`, `description_en`, `subject`, `topic`, `subtopic`
- [ ] Bloom levels assigned per concept: remember, understand, apply, analyze, evaluate, create
- [ ] Difficulty rating 1-10 aligned with Bloom level (remember=1-2, understand=3-4, apply=5-6, analyze=7-8, evaluate/create=9-10)
- [ ] Bagrut exam tag: concepts mapped to 3-unit/4-unit/5-unit exam sections
- [ ] Arabic names reviewed by native Arabic-speaking math teacher
- [ ] Hebrew names aligned with standard Bagrut terminology (per `HEBREW_MATH_GLOSSARY` in prompt-templates.py)
- [ ] Estimated mastery time per concept (minutes)

**Test:**
```python
def test_curriculum_completeness():
    concepts = load_curriculum_map("content/math/curriculum_map.json")
    assert len(concepts) >= 2000
    assert all(c["name_he"] for c in concepts), "Missing Hebrew names"
    assert all(c["name_ar"] for c in concepts), "Missing Arabic names"
    assert all(c["bloom_level"] in VALID_BLOOM_LEVELS for c in concepts)
    assert all(1 <= c["difficulty"] <= 10 for c in concepts)

def test_topic_coverage():
    concepts = load_curriculum_map("content/math/curriculum_map.json")
    topics = {c["topic"] for c in concepts}
    required = {"arithmetic", "algebra", "geometry", "trigonometry", "calculus",
                "probability_statistics", "sequences_series", "analytic_geometry"}
    assert required.issubset(topics)
```

**Edge cases:**
- Some concepts span multiple topics (e.g., "trigonometric equations" is both trig and algebra) -> primary topic assigned, secondary topic as tag
- Arabic math terminology varies by region -> use Palestinian/Israeli Arabic standard
- Bagrut curriculum updates mid-year -> versioned concept maps, hot-reload support

---

### CNT-001.2: Prerequisite Graph Construction

**Files to create/modify:**
- `content/math/prerequisites.csv` — directed edges: `from_concept_id, to_concept_id, strength`
- `scripts/content/validate_dag.py` — cycle detection, orphan detection

**Acceptance:**
- [ ] Prerequisite edges form a valid DAG (no cycles)
- [ ] Each edge has `strength` (0.0-1.0): 1.0 = hard gate, <1.0 = recommended
- [ ] Average prerequisites per concept: 1-3 (avoid over-constraining)
- [ ] Maximum prerequisite chain depth: 15 (from basic arithmetic to advanced calculus)
- [ ] No orphan concepts (every concept reachable from at least one root or connected to graph)
- [ ] Root concepts (no prerequisites): basic counting, number recognition (~20 concepts)
- [ ] Leaf concepts (no dependents): Bagrut exam application problems (~50 concepts)
- [ ] Cross-topic prerequisites supported (e.g., algebra -> calculus)

**Test:**
```python
def test_no_cycles():
    edges = load_prerequisites("content/math/prerequisites.csv")
    graph = build_digraph(edges)
    cycles = detect_cycles(graph)
    assert cycles == [], f"Cycles detected: {cycles}"

def test_no_orphans():
    concepts = load_curriculum_map("content/math/curriculum_map.json")
    edges = load_prerequisites("content/math/prerequisites.csv")
    graph = build_digraph(edges)
    concept_ids = {c["id"] for c in concepts}
    connected = get_connected_components(graph)
    orphans = concept_ids - connected
    assert len(orphans) == 0, f"Orphan concepts: {orphans}"

def test_max_depth():
    edges = load_prerequisites("content/math/prerequisites.csv")
    graph = build_digraph(edges)
    max_depth = longest_path_length(graph)
    assert max_depth <= 15
```

---

### CNT-001.3: Neo4j Bulk Import

**Files to create/modify:**
- `scripts/content/import_math_graph.py` — Neo4j bulk import script
- `scripts/content/import_math_graph.cypher` — Cypher MERGE statements

**Acceptance:**
- [ ] All 2000 concepts imported as `:Concept` nodes with all properties
- [ ] All prerequisite edges imported as `PREREQUISITE_OF` relationships with `strength` property
- [ ] All concepts linked to `:Subject {id: 'subj-math'}` via `BELONGS_TO`
- [ ] All concepts linked to appropriate `:ConceptCategory` via `IN_CATEGORY`
- [ ] Import is idempotent (MERGE, not CREATE) — safe to re-run
- [ ] Import time: < 60 seconds for full 2000 concepts
- [ ] Post-import validation: cycle detection query returns 0 rows
- [ ] Indexes and constraints from `neo4j-schema.cypher` applied before import

**Test:**
```python
def test_import_creates_all_concepts():
    run_import_script()
    with neo4j_session() as session:
        result = session.run("MATCH (c:Concept) WHERE c.subject = 'Mathematics' RETURN count(c) AS cnt")
        assert result.single()["cnt"] >= 2000

def test_import_creates_prerequisites():
    with neo4j_session() as session:
        result = session.run("MATCH ()-[r:PREREQUISITE_OF]->() RETURN count(r) AS cnt")
        assert result.single()["cnt"] >= 3000  # ~1.5 edges per concept average
```

---

### CNT-001.4: Graph Versioning + Hot-Reload Support

**Files to create/modify:**
- `src/Cena.Data/CurriculumGraph/GraphVersionManager.cs` — version tracking
- `src/Cena.Data/CurriculumGraph/GraphChangePublisher.cs` — NATS event on graph update

**Acceptance:**
- [ ] Each graph import creates a version: `{ version: "v1.0.0", importedAt, conceptCount, edgeCount, hash }`
- [ ] Version stored in Neo4j as a `:GraphVersion` node
- [ ] On graph update: `cena.curriculum.events.GraphPublished` published to NATS
- [ ] Subscribers (Redis cache, actor graph cache) invalidate on version change
- [ ] Active learning sessions continue with old version until session ends
- [ ] New sessions use latest version
- [ ] Rollback: revert to previous version by re-importing old dataset

**Test:**
```csharp
[Fact]
public async Task GraphUpdate_PublishesNatsEvent()
{
    var natsMessages = CaptureNatsMessages("cena.curriculum.events.GraphPublished");
    await _versionManager.PublishNewVersion("v1.1.0", conceptCount: 2010);
    Assert.Single(natsMessages);
    Assert.Equal("v1.1.0", natsMessages[0].Version);
}
```

---

## Rollback Criteria
If the curriculum graph has errors discovered post-import:
- Revert to previous graph version
- Flag affected student mastery maps for recalculation
- Publish `GraphPublished` event to trigger cache invalidation

## Definition of Done
- [ ] All 4 subtasks pass their individual tests
- [ ] 2000+ concept nodes in Neo4j with Hebrew + Arabic names
- [ ] DAG validation passes (0 cycles, 0 orphans)
- [ ] Bulk import completes in < 60 seconds
- [ ] Graph version published to NATS on update
- [ ] PR reviewed by architect + math curriculum expert
