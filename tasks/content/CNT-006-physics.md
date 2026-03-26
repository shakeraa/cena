# CNT-006: Physics Curriculum — 1500 Nodes, Hebrew+Arabic, Following Math Pattern

**Priority:** P2 — second subject after math
**Blocked by:** CNT-001 (Math Graph pattern established)
**Estimated effort:** 4 days
**Contract:** `contracts/data/neo4j-schema.cypher` (same schema, new Subject node)

---

## Context

The physics curriculum follows the same DAG pattern as math: 1500 concept nodes covering Mechanics, Electricity & Magnetism, Waves & Optics, Thermodynamics, and Modern Physics. Cross-subject prerequisites link to math concepts (e.g., trigonometry -> vector decomposition).

## Subtasks

### CNT-006.1: Physics Curriculum Mapping

**Files to create/modify:**
- `content/physics/curriculum_map.json` — 1500 concepts
- `content/physics/prerequisites.csv` — prerequisite edges (intra-physics + cross-subject to math)

**Acceptance:**
- [ ] 1500 concept nodes: Mechanics (400), E&M (350), Waves (250), Thermo (250), Modern Physics (250)
- [ ] Hebrew + Arabic names for all concepts
- [ ] Cross-subject prerequisites: physics concepts link to math prerequisites (e.g., `math-trigonometry` -> `phys-vector-decomposition`)
- [ ] DAG validation: 0 cycles including cross-subject edges

**Test:**
```python
def test_physics_completeness():
    concepts = load_curriculum_map("content/physics/curriculum_map.json")
    assert len(concepts) >= 1500

def test_cross_subject_prerequisites():
    edges = load_prerequisites("content/physics/prerequisites.csv")
    cross_subject = [e for e in edges if e["from"].startswith("math-")]
    assert len(cross_subject) >= 50  # At least 50 cross-subject links
```

---

### CNT-006.2: Neo4j Import + Subject Registration

**Files to create/modify:**
- `scripts/content/import_physics_graph.py`

**Acceptance:**
- [ ] New Subject node: `{ id: 'subj-physics', name: 'Physics' }`
- [ ] All physics concepts linked via `BELONGS_TO` to physics subject
- [ ] Physics concept categories created: `cat-mechanics`, `cat-em`, `cat-waves`, `cat-thermo`, `cat-modern`
- [ ] MCM edges created for physics-specific error types

**Test:**
```python
def test_physics_subject_created():
    with neo4j_session() as session:
        result = session.run("MATCH (s:Subject {id: 'subj-physics'}) RETURN s")
        assert result.single() is not None
```

---

### CNT-006.3: Question Generation for Physics

**Files to create/modify:**
- `src/llm_acl/batch/physics_question_prompts.py`

**Acceptance:**
- [ ] Physics-specific prompt templates (diagrams, unit analysis, vector problems)
- [ ] 8-15 questions per concept, Hebrew + Arabic
- [ ] Diagram references linked to generated SVGs (CNT-006 depends on LLM-009 for diagrams)

**Test:**
```python
def test_physics_questions_generated():
    result = generate_questions_for_concept("phys-newtons-second-law")
    assert len(result.questions) >= 8
```

---

## Rollback Criteria
- Physics content can be disabled independently of math; students see "Coming Soon" for physics

## Definition of Done
- [ ] 1500+ physics concepts in Neo4j
- [ ] Cross-subject prerequisites validated
- [ ] Question pool generated and QA-passed
- [ ] PR reviewed by architect + physics curriculum expert
