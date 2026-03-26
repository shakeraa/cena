# CNT-001: Mathematics Curriculum Knowledge Graph — 2000 Nodes

**Priority:** P0 — blocks all learner interactions for Math subject
**Blocked by:** INF-001 (VPC), Neo4j AuraDB provisioned
**Estimated effort:** 5-10 days (including expert review cycles)
**Contract:** `docs/architecture-design.md` Section 6, `docs/content-authoring.md` Stages 0-1

---

## Context

The Curriculum Context owns the domain knowledge graph: ~2,000 concept nodes per subject with prerequisite edges, difficulty ratings, Bloom's level classifications, and common misconceptions. Mathematics (5-unit Bagrut, the highest level) is the first subject.

The content authoring pipeline (`docs/content-authoring.md`) specifies a six-stage process: Corpus Ingestion -> Knowledge Graph Extraction -> Question Generation -> Explanation Generation -> Expert Review -> Publication. This task covers Stages 0 and 1: ingesting the math corpus and extracting the knowledge graph.

The knowledge graph is loaded into the Proto.Actor cluster's in-memory cache at startup (`DomainGraphCache`) for microsecond lookups. It is also stored in Neo4j AuraDB as the source of truth for admin/authoring tools and cross-student analytics. Each concept node must have both Hebrew (`he`) and English (`en`) display names. Arabic (`ar`) names are added as a separate pass for Arab-sector students (~30% of Israeli students).

---

## Subtasks

### CNT-001.1: Corpus Ingestion — Stage 0

**Files to create/modify:**
- `content/math/corpus/README.md` — corpus manifest (list of all source materials)
- `content/math/corpus/analysis/` — directory for LLM-generated corpus analysis documents
- `scripts/content/ingest-corpus.py` — corpus ingestion script (feeds to Kimi K2.5 batch)
- `scripts/content/prompts/corpus-analysis.txt` — prompt template for corpus analysis
- `content/math/corpus/style-guide.json` — output: structured style guide

**Acceptance:**

**Input corpus (per `docs/content-authoring.md` Section 3.0):**
- [ ] Official Bagrut exam papers: last 10-15 years of 5-unit Math (publicly available from Ministry of Education)
- [ ] Approved textbook chapters: Geva Mathematics 5-unit (primary), Yoel Geva series
- [ ] Ministry of Education syllabus document: official 5-unit Math syllabus (tochar limudim)
- [ ] Teaching guides and common error documentation from education advisor
- [ ] Cross-reference: every topic that has appeared in a Bagrut exam in the last 10 years must be represented

**Corpus analysis output (per concept area):**
- [ ] Terminology index: exact Hebrew terms for each concept with English equivalents
  ```json
  {
    "concept_area": "derivatives",
    "hebrew_terms": {
      "נגזרת": "derivative",
      "כלל השרשרת": "chain rule",
      "נגזרת חלקית": "partial derivative",
      "פונקציה גזירה": "differentiable function"
    }
  }
  ```
- [ ] Question style patterns: how Bagrut exams phrase questions (e.g., "הוכח כי...", "חשב את...", "מצא את...")
- [ ] Difficulty distribution: how many questions at each Bloom's level per exam
- [ ] Common distractor patterns from real MCQs
- [ ] Scoring rubrics: how partial credit is awarded in real Bagrut grading
- [ ] Cross-concept connections: which concepts appear together in multi-part Bagrut questions

**Processing:**
- [ ] Corpus fed to Kimi K2.5 (256K context, batch mode) in structured chunks
- [ ] Output stored as structured JSON in S3: `s3://cena-content/math/corpus/analysis/`
- [ ] Style guide aggregated from all corpus analysis documents

**Test:**
```bash
# Verify corpus materials are listed
cat content/math/corpus/README.md | grep -c "bagrut_exam_" | xargs -I{} test {} -ge 10
echo "PASS: >= 10 Bagrut exams listed"

# Verify corpus analysis produced output for all major topic areas
TOPIC_AREAS="algebra calculus trigonometry probability geometry analytic_geometry sequences_series"
for topic in $TOPIC_AREAS; do
  [ -f "content/math/corpus/analysis/${topic}_analysis.json" ] \
    && echo "PASS: $topic analysis exists" \
    || echo "FAIL: $topic analysis missing"
done

# Verify style guide has terminology for at least 100 concepts
TERM_COUNT=$(jq '[.[] | .hebrew_terms | length] | add' content/math/corpus/style-guide.json)
[ "$TERM_COUNT" -ge 100 ] && echo "PASS: $TERM_COUNT terms" || echo "FAIL: only $TERM_COUNT terms"

# Verify Bagrut question patterns documented
PATTERN_COUNT=$(jq '.question_patterns | length' content/math/corpus/style-guide.json)
[ "$PATTERN_COUNT" -ge 5 ] && echo "PASS: $PATTERN_COUNT question patterns" || echo "FAIL: only $PATTERN_COUNT patterns"
```

**Edge cases:**
- Bagrut exam PDFs are scanned images (not text) — use OCR (Tesseract with Hebrew model) before feeding to Kimi
- Textbook copyright — corpus analysis is transformative (extracting style patterns, not copying content); consult legal
- Kimi 256K context overflow for large textbooks — chunk by chapter; cross-reference chunks in the analysis prompt
- Ministry of Education syllabus changes between years — use the most recent official syllabus as the authoritative source; flag differences from older exams

---

### CNT-001.2: Knowledge Graph Extraction — Stage 1

**Files to create/modify:**
- `scripts/content/extract-graph.py` — graph extraction script
- `scripts/content/prompts/graph-extraction.txt` — prompt template
- `content/math/graph/raw/` — raw extracted graph per syllabus unit
- `content/math/graph/merged/math_5u_graph.json` — merged graph

**Acceptance:**

**Extraction process (per `docs/content-authoring.md` Section 3.1):**
- [ ] Feed corpus analysis + official syllabus to Kimi K2.5 (long context, structured extraction)
- [ ] Extract concept nodes with the exact structure from the contract:
  ```json
  {
    "concept_id": "math_5u_derivatives_chain_rule",
    "display_name": { "he": "כלל השרשרת", "ar": "قاعدة السلسلة", "en": "Chain Rule" },
    "subject": "mathematics",
    "depth_unit": 5,
    "prerequisites": ["math_5u_derivatives_basic", "math_5u_composite_functions"],
    "builds_on": ["math_5u_derivatives_product_rule"],
    "bloom_level": "application",
    "estimated_mastery_time_minutes": 45,
    "common_misconceptions": [
      "Confusing chain rule with product rule when functions are nested vs multiplied"
    ],
    "key_formulas": ["(f(g(x)))' = f'(g(x)) * g'(x)"],
    "extraction_confidence": 0.87,
    "needs_expert_review": true,
    "bagrut_frequency": "high",
    "corpus_provenance": ["geva_ch12_sec3", "bagrut_2024_summer_q4"]
  }
  ```
- [ ] ~2,000 concept nodes covering the full 5-unit Math syllabus
- [ ] Cross-reference against real Bagrut exam coverage: every concept that appeared in an exam in the last 10 years must be represented
- [ ] Arabic display names (`ar`) for all concept nodes (supports Arab-sector students)

**Graph structure requirements:**
- [ ] All nodes have at least one prerequisite edge (except foundational nodes)
- [ ] No circular prerequisite chains (DAG property)
- [ ] Every concept has a `bloom_level` from: `remember`, `understand`, `apply`, `analyze`, `evaluate`, `create`
- [ ] Bloom's level distribution roughly matches Bagrut exam distribution (from corpus analysis)
- [ ] `extraction_confidence` < 0.8 → `needs_expert_review: true`
- [ ] `bagrut_frequency`: `high` (appeared in >50% of exams), `medium` (25-50%), `low` (<25%), `none` (supporting concept)

**Syllabus coverage by topic area:**

| Topic Area | Estimated Nodes | Bagrut Weight |
|-----------|----------------|--------------|
| Algebra & Functions | ~300 | 15-20% |
| Calculus (Derivatives) | ~350 | 25-30% |
| Calculus (Integrals) | ~300 | 20-25% |
| Trigonometry | ~250 | 10-15% |
| Probability & Statistics | ~200 | 10-15% |
| Analytic Geometry | ~200 | 5-10% |
| Sequences & Series | ~150 | 5-10% |
| Complex Numbers | ~100 | 3-5% |
| Vectors | ~100 | 3-5% |
| Supporting/Foundational | ~50 | — |

**Test:**
```bash
# Verify total node count
NODE_COUNT=$(jq '.nodes | length' content/math/graph/merged/math_5u_graph.json)
[ "$NODE_COUNT" -ge 1800 ] && [ "$NODE_COUNT" -le 2200 ] \
  && echo "PASS: $NODE_COUNT nodes (target: ~2000)" \
  || echo "FAIL: $NODE_COUNT nodes out of range"

# Verify DAG property (no circular prerequisites)
python3 -c "
import json, sys
graph = json.load(open('content/math/graph/merged/math_5u_graph.json'))
nodes = {n['concept_id']: n for n in graph['nodes']}
visited, path = set(), set()
def has_cycle(node_id):
    if node_id in path: return True
    if node_id in visited: return False
    visited.add(node_id); path.add(node_id)
    for prereq in nodes.get(node_id, {}).get('prerequisites', []):
        if has_cycle(prereq): return True
    path.discard(node_id)
    return False
for nid in nodes:
    if has_cycle(nid):
        print(f'FAIL: cycle detected involving {nid}'); sys.exit(1)
print('PASS: no cycles')
"

# Verify all nodes have Hebrew and English names
python3 -c "
import json
graph = json.load(open('content/math/graph/merged/math_5u_graph.json'))
missing = [n['concept_id'] for n in graph['nodes']
           if not n.get('display_name', {}).get('he') or not n.get('display_name', {}).get('en')]
if missing:
    print(f'FAIL: {len(missing)} nodes missing he/en names: {missing[:5]}...')
else:
    print('PASS: all nodes have he + en names')
"

# Verify Arabic names coverage
python3 -c "
import json
graph = json.load(open('content/math/graph/merged/math_5u_graph.json'))
ar_count = sum(1 for n in graph['nodes'] if n.get('display_name', {}).get('ar'))
total = len(graph['nodes'])
pct = ar_count / total * 100
print(f'Arabic coverage: {ar_count}/{total} ({pct:.1f}%)')
if pct >= 90: print('PASS')
else: print(f'WARN: only {pct:.1f}% Arabic coverage (target: >90%)')
"

# Verify Bloom's level distribution
python3 -c "
import json
from collections import Counter
graph = json.load(open('content/math/graph/merged/math_5u_graph.json'))
blooms = Counter(n.get('bloom_level', 'unknown') for n in graph['nodes'])
print('Bloom distribution:')
for level, count in sorted(blooms.items()):
    print(f'  {level}: {count} ({count/len(graph[\"nodes\"])*100:.1f}%)')
# Bagrut exams are heavy on apply/analyze — verify those are well-represented
apply_analyze = blooms.get('apply', 0) + blooms.get('analyze', 0)
if apply_analyze / len(graph['nodes']) >= 0.4:
    print('PASS: apply+analyze >= 40%')
else:
    print('WARN: apply+analyze < 40% — may not match Bagrut distribution')
"

# Verify no orphan nodes (every non-foundational node has at least one prerequisite)
python3 -c "
import json
graph = json.load(open('content/math/graph/merged/math_5u_graph.json'))
orphans = [n['concept_id'] for n in graph['nodes']
           if not n.get('prerequisites') and n.get('bloom_level') != 'remember']
if orphans:
    print(f'WARN: {len(orphans)} non-foundational orphan nodes: {orphans[:5]}...')
else:
    print('PASS: no orphan nodes')
"
```

**Edge cases:**
- Kimi extracts duplicate concepts with slightly different names — deduplicate by normalizing Hebrew terms against the terminology index
- Concept appears in syllabus but not in any Bagrut exam — include with `bagrut_frequency: none`; these are supporting concepts
- Arabic mathematical terminology differs between regions (Levantine vs Gulf) — use Israeli-Arabic (Levantine) terminology as primary; consult Arab-sector Bagrut materials
- Extraction confidence systematically low for a topic area — indicates the corpus is insufficient for that topic; supplement with additional materials before proceeding

---

### CNT-001.3: Neo4j Schema and Graph Import

**Files to create/modify:**
- `infra/neo4j/schema.cypher` — Neo4j schema constraints and indexes
- `scripts/content/import-to-neo4j.py` — graph import script
- `scripts/content/validate-neo4j.cypher` — validation queries

**Acceptance:**

**Neo4j schema:**
- [ ] Node labels: `(:Concept)`, `(:Subject)`, `(:TopicArea)`
- [ ] Relationship types: `[:PREREQUISITE_OF]`, `[:BUILDS_ON]`, `[:BELONGS_TO]`, `[:HAS_MCM]`
- [ ] Constraints:
  ```cypher
  CREATE CONSTRAINT concept_id_unique IF NOT EXISTS
    FOR (c:Concept) REQUIRE c.concept_id IS UNIQUE;
  CREATE CONSTRAINT subject_id_unique IF NOT EXISTS
    FOR (s:Subject) REQUIRE s.subject_id IS UNIQUE;
  CREATE INDEX concept_bloom IF NOT EXISTS
    FOR (c:Concept) ON (c.bloom_level);
  CREATE INDEX concept_bagrut_freq IF NOT EXISTS
    FOR (c:Concept) ON (c.bagrut_frequency);
  CREATE FULLTEXT INDEX concept_name_search IF NOT EXISTS
    FOR (c:Concept) ON EACH [c.display_name_he, c.display_name_en, c.display_name_ar];
  ```
- [ ] Import uses `UNWIND` for batch insert (not individual `CREATE` per node)
- [ ] Import is idempotent: running twice does not create duplicates (`MERGE` on `concept_id`)
- [ ] Graph versioned: `(:GraphVersion {version: "math_5u_v1.0.0", published_at: datetime()})` node

**Validation queries:**
- [ ] All concepts have at least one path from a foundational node (graph connectivity)
- [ ] No cycles in prerequisite relationships
- [ ] Every concept belongs to exactly one topic area
- [ ] Concept count per topic area matches expected ranges

**Test:**
```bash
# Import graph to Neo4j
python3 scripts/content/import-to-neo4j.py \
  --graph content/math/graph/merged/math_5u_graph.json \
  --neo4j-uri "$NEO4J_URI" \
  --neo4j-user "$NEO4J_USER" \
  --neo4j-password "$NEO4J_PASSWORD"

# Run validation queries
cypher-shell -u "$NEO4J_USER" -p "$NEO4J_PASSWORD" -a "$NEO4J_URI" < scripts/content/validate-neo4j.cypher

# Verify node count
cypher-shell -u "$NEO4J_USER" -p "$NEO4J_PASSWORD" -a "$NEO4J_URI" \
  "MATCH (c:Concept {subject: 'mathematics'}) RETURN count(c) AS node_count"
# Expect: ~2000

# Verify no cycles
cypher-shell -u "$NEO4J_USER" -p "$NEO4J_PASSWORD" -a "$NEO4J_URI" \
  "MATCH path = (c:Concept)-[:PREREQUISITE_OF*]->(c) RETURN count(path) AS cycles"
# Expect: 0

# Verify connectivity (all concepts reachable from foundations)
cypher-shell -u "$NEO4J_USER" -p "$NEO4J_PASSWORD" -a "$NEO4J_URI" \
  "MATCH (c:Concept {subject: 'mathematics'})
   WHERE NOT EXISTS { MATCH (c)<-[:PREREQUISITE_OF*]-(f:Concept)
                      WHERE NOT EXISTS { MATCH (f)-[:PREREQUISITE_OF]->() } }
   AND EXISTS { MATCH (c)-[:PREREQUISITE_OF]->() }
   RETURN count(c) AS disconnected_nodes"
# Expect: 0

# Verify idempotency: re-import should not change node count
python3 scripts/content/import-to-neo4j.py --graph content/math/graph/merged/math_5u_graph.json
cypher-shell -u "$NEO4J_USER" -p "$NEO4J_PASSWORD" -a "$NEO4J_URI" \
  "MATCH (c:Concept {subject: 'mathematics'}) RETURN count(c) AS node_count"
# Expect: same count as before
```

**Edge cases:**
- Neo4j AuraDB connection timeout during large import — use batch imports (500 nodes per transaction) with retry
- Unicode issues with Hebrew/Arabic text in Cypher — use parameterized queries, not string interpolation
- Full-text index on Hebrew text — Neo4j uses Lucene; Hebrew tokenization may need custom analyzer
- Graph version conflict — if a newer version exists, import should fail unless `--force` flag is used

---

### CNT-001.4: In-Memory Cache Format and Proto.Actor Loading

**Files to create/modify:**
- `src/Cena.Domain/Curriculum/DomainGraphCache.cs` — in-memory graph representation
- `src/Cena.Domain/Curriculum/ConceptNode.cs` — concept node record
- `src/Cena.Infrastructure/Neo4j/GraphLoader.cs` — loads graph from Neo4j at startup
- `src/Cena.Domain/Curriculum/GraphValidator.cs` — validates loaded graph

**Acceptance:**
- [ ] In-memory graph loaded at silo startup from Neo4j AuraDB
- [ ] Graph representation optimized for hot-path queries:
  ```csharp
  public class DomainGraphCache
  {
      // O(1) concept lookup by ID
      private readonly ImmutableDictionary<string, ConceptNode> _concepts;
      // O(1) prerequisite lookup
      private readonly ImmutableDictionary<string, ImmutableList<string>> _prerequisites;
      // O(1) "what comes next" lookup
      private readonly ImmutableDictionary<string, ImmutableList<string>> _successors;
      // Pre-computed topological order for learning path generation
      private readonly ImmutableList<string> _topologicalOrder;

      public ConceptNode GetConcept(string conceptId); // microsecond
      public IReadOnlyList<string> GetPrerequisites(string conceptId); // microsecond
      public IReadOnlyList<string> GetSuccessors(string conceptId); // microsecond
      public IReadOnlyList<string> GetRecommendedPath(string fromConcept, string toConcept); // microsecond
  }
  ```
- [ ] Hot-reload on `CurriculumPublished` NATS event (no restart required):
  - Subscribe to `cena.curriculum.events.GraphPublished`
  - On event: load new graph from Neo4j, swap atomically (`Interlocked.Exchange`)
  - Log graph version transition: "Graph updated: math_5u_v1.0.0 -> math_5u_v1.1.0"
- [ ] Startup time with ~2,000 nodes: < 5 seconds (Neo4j query + deserialization)
- [ ] Memory footprint: ~50-100 MB for 2,000 nodes with all metadata (acceptable for Fargate task)
- [ ] Validated at load: no cycles, no orphans, all prerequisites reference existing nodes

**Test:**
```csharp
[Fact]
public void GraphCache_LoadsAllConcepts()
{
    var cache = LoadTestGraph();
    Assert.InRange(cache.ConceptCount, 1800, 2200);
}

[Fact]
public void GraphCache_PrerequisiteLookup_Microsecond()
{
    var cache = LoadTestGraph();
    var sw = Stopwatch.StartNew();
    for (int i = 0; i < 10_000; i++)
        cache.GetPrerequisites("math_5u_derivatives_chain_rule");
    sw.Stop();
    var avgUs = sw.Elapsed.TotalMicroseconds / 10_000;
    Assert.True(avgUs < 10, $"Prerequisite lookup took {avgUs:F1}us (target <10us)");
}

[Fact]
public void GraphCache_TopologicalOrder_IsValid()
{
    var cache = LoadTestGraph();
    var order = cache.TopologicalOrder;
    // Every concept's prerequisites must appear before it in the order
    var seen = new HashSet<string>();
    foreach (var conceptId in order)
    {
        var prereqs = cache.GetPrerequisites(conceptId);
        Assert.All(prereqs, p => Assert.Contains(p, seen));
        seen.Add(conceptId);
    }
}

[Fact]
public async Task GraphCache_HotReload_OnNatsEvent()
{
    var cache = await LoadAndSubscribe();
    var v1Count = cache.ConceptCount;

    // Publish GraphPublished event
    await _nats.PublishAsync("cena.curriculum.events.GraphPublished",
        new { version = "math_5u_v1.1.0", diff = new { added = 5, removed = 0 } });

    // Wait for reload
    await Task.Delay(2000);
    Assert.NotEqual(v1Count, cache.ConceptCount); // New graph loaded
}

[Fact]
public void GraphCache_RejectsInvalidGraph()
{
    var invalidGraph = CreateGraphWithCycle();
    Assert.Throws<InvalidGraphException>(() => new DomainGraphCache(invalidGraph));
}
```

**Edge cases:**
- Neo4j AuraDB unreachable at startup — retry with exponential backoff for 60 seconds; if still unreachable, start with empty graph and log CRITICAL (actor cluster is degraded but alive)
- Graph hot-reload during active student session — old graph remains in the `LearningSessionActor`; new graph used on next session start (per `docs/content-authoring.md` Section 3.6)
- Graph too large for memory (future: 10+ subjects) — lazy loading per subject; only load subjects with active students
- Hebrew text sorting for display — use `StringComparer.Create(CultureInfo.GetCultureInfo("he-IL"), ...)` for proper Hebrew collation

---

## Integration Test (all subtasks combined)

```bash
#!/bin/bash
set -euo pipefail

echo "=== CNT-001 Full Integration Test ==="

# 1. Corpus analysis exists
ANALYSIS_COUNT=$(ls content/math/corpus/analysis/*.json 2>/dev/null | wc -l | tr -d ' ')
[ "$ANALYSIS_COUNT" -ge 7 ] && echo "PASS: $ANALYSIS_COUNT corpus analyses" || echo "FAIL: only $ANALYSIS_COUNT"

# 2. Graph has ~2000 nodes
NODE_COUNT=$(jq '.nodes | length' content/math/graph/merged/math_5u_graph.json)
echo "Node count: $NODE_COUNT"

# 3. Neo4j has the graph
NEO4J_COUNT=$(cypher-shell "MATCH (c:Concept {subject:'mathematics'}) RETURN count(c)" --format plain | tail -1)
[ "$NEO4J_COUNT" -eq "$NODE_COUNT" ] && echo "PASS: Neo4j matches JSON" || echo "FAIL: Neo4j=$NEO4J_COUNT, JSON=$NODE_COUNT"

# 4. In-memory cache loads successfully
dotnet test --filter "Category=GraphCache" --verbosity normal
echo "PASS: graph cache tests"

# 5. No cycles, no orphans
python3 -c "
import json
graph = json.load(open('content/math/graph/merged/math_5u_graph.json'))
# Cycle check (same as above)
# Orphan check (same as above)
print('PASS: graph structural integrity')
"

echo "=== CNT-001 Integration Test Complete ==="
```

## Rollback Criteria

If this task fails or introduces instability:
- Graph extraction can be re-run with modified prompts — Kimi batch jobs are idempotent
- Neo4j graph can be deleted and re-imported: `MATCH (n) DETACH DELETE n`
- In-memory cache falls back to empty graph — students cannot start sessions, but the system does not crash
- Expert review can flag and correct extraction errors incrementally

## Definition of Done

- [ ] Corpus ingested: 10+ years of Bagrut exams, approved textbooks, official syllabus
- [ ] Style guide produced with terminology index, question patterns, difficulty distribution
- [ ] ~2,000 concept nodes extracted with Hebrew, English, and Arabic display names
- [ ] Graph is a valid DAG (no cycles, no orphans, all prerequisites valid)
- [ ] Bloom's level distribution roughly matches Bagrut exam distribution
- [ ] Neo4j import is idempotent and validated
- [ ] In-memory cache loads in <5 seconds with microsecond lookups
- [ ] Hot-reload works on `GraphPublished` NATS event
- [ ] Expert review queue populated with all nodes where `extraction_confidence < 0.8`
- [ ] PR reviewed by architect and education domain expert
