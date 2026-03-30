# CNT-004: Automated QA — Cycle Detection, Bloom Coverage, Difficulty Distribution, Hebrew Validation

**Priority:** P1 — blocks content publication
**Blocked by:** CNT-001 (Math Graph), CNT-002 (Questions)
**Estimated effort:** 2 days
**Contract:** `contracts/data/neo4j-schema.cypher` (DAG validation queries)

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule. `throw UnimplementedError`, `// TODO: implement`, empty bodies, and mock returns are FORBIDDEN in source code. If you cannot implement it fully, file a blocking dependency instead.

## Context

Before publishing content to production, automated QA checks verify structural integrity of the curriculum graph and question pool. This prevents broken prerequisite chains, uneven difficulty distribution, and linguistically invalid Hebrew content from reaching students.

## Subtasks

### CNT-004.1: Graph Integrity Checks

**Files to create/modify:**
- `scripts/content/qa_graph_integrity.py` — Neo4j-based integrity checks
- `tests/content/test_graph_qa.py`

**Acceptance:**
- [ ] Cycle detection: 0 cycles in prerequisite graph (Cypher query from neo4j-schema.cypher)
- [ ] Orphan detection: 0 concepts unreachable from any root
- [ ] Duplicate detection: no two concepts with identical `name_he`
- [ ] Prerequisite depth: no concept requires > 15 prerequisites in chain
- [ ] Bloom level monotonicity: prerequisites should not have higher Bloom level than dependents

**Test:**
```python
def test_no_cycles(): ...
def test_no_orphans(): ...
def test_bloom_monotonicity():
    violations = check_bloom_monotonicity()
    assert len(violations) == 0, f"Bloom violations: {violations}"
```

---

### CNT-004.2: Question Pool Coverage Checks

**Files to create/modify:**
- `scripts/content/qa_question_coverage.py`

**Acceptance:**
- [ ] Every concept has >= 8 approved questions
- [ ] Bloom level distribution per concept: at least 2 questions per level (remember/understand/apply)
- [ ] Difficulty distribution per topic: uniform-ish spread across 1-10
- [ ] No concept has > 30 questions (waste of generation budget)

**Test:**
```python
def test_minimum_questions_per_concept():
    coverage = compute_question_coverage()
    undercovered = [c for c in coverage if c.question_count < 8]
    assert len(undercovered) == 0
```

---

### CNT-004.3: Hebrew Linguistic Validation

**Files to create/modify:**
- `scripts/content/qa_hebrew_validation.py`

**Acceptance:**
- [ ] Hebrew text passes basic grammar check (no mixed LTR/RTL corruption)
- [ ] Mathematical notation in correct format (LaTeX, not Hebrew transliteration)
- [ ] Bagrut terminology alignment verified against `HEBREW_MATH_GLOSSARY`
- [ ] Arabic text RTL rendering verified

**Test:**
```python
def test_hebrew_no_ltr_corruption():
    questions = load_all_questions()
    for q in questions:
        assert not contains_ltr_override(q.question_he)
```

---

## Rollback Criteria
- QA failures block publication pipeline; fix issues and re-run

## Definition of Done
- [ ] All 3 subtask checks pass on full curriculum
- [ ] QA pipeline runs in < 5 minutes
- [ ] Zero cycles, zero orphans, >= 8 questions per concept
- [ ] PR reviewed by architect
