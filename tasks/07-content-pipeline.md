# 07 — Content Pipeline Tasks

**Technology:** Neo4j, GPT-4o/Claude (batch), Gemini 2.5 Flash (OCR), Remotion (Node.js), Expert Review Tool
**Contract files:** `contracts/data/neo4j-schema.cypher`, `contracts/llm/diagram-generation-pipeline.py`, `docs/content-authoring.md`, `docs/question-ingestion-specification.md`
**Stage:** Foundation (Weeks 1-4) + ongoing

---

## CNT-001: Math Curriculum Graph (Hebrew + Arabic)
**Priority:** P0 | **Blocked by:** DATA-006 (Neo4j)
- [ ] 2,000 Math concept nodes extracted from Bagrut corpus (Hebrew)
- [ ] Prerequisite edges: DAG, no cycles (validated by cycle detection query)
- [ ] Difficulty ratings per concept (0.0-1.0)
- [ ] Bloom level per concept (recall/comprehension/application/analysis)
- [ ] Arabic concept names added (`name_ar` property) for Arab-sector students

**Test:**

```cypher
// Cycle detection — must return 0 rows
CALL gds.graph.project('prereq', 'Concept', 'PREREQUISITE_OF')
CALL gds.alpha.cycles.stream('prereq') YIELD nodeIds
RETURN count(nodeIds) AS cycles
// Assert: cycles = 0

// Coverage check — all concepts have at least 1 prerequisite or are root
MATCH (c:Concept) WHERE NOT (c)<-[:PREREQUISITE_OF]-()
  AND NOT c:RootConcept
RETURN count(c) AS orphans
// Assert: orphans = 0

// Arabic names populated
MATCH (c:Concept) WHERE c.name_ar IS NULL RETURN count(c) AS missing_arabic
// Assert: missing_arabic = 0
```

## CNT-002: Question Generation (Kimi K2.5 Batch)
**Priority:** P0 | **Blocked by:** CNT-001
- [ ] 8-15 questions per concept (mix of types and Bloom levels)
- [ ] Hebrew question text matching Bagrut exam style
- [ ] Arabic question variants for Arab-sector students
- [ ] Distractor quality: each wrong MCQ option maps to a documented misconception
- [ ] Corpus provenance: each question traces back to source Bagrut exam

**Test:**

```python
def test_question_generation_quality():
    concepts = load_sample_concepts(100)
    questions = batch_generate(concepts)

    # Coverage: 8-15 per concept
    for concept in concepts:
        q_count = len([q for q in questions if q.concept_id == concept.id])
        assert 8 <= q_count <= 15, f"{concept.id}: {q_count} questions"

    # Bloom level coverage: at least 3 levels per concept
    for concept in concepts:
        blooms = set(q.bloom_level for q in questions if q.concept_id == concept.id)
        assert len(blooms) >= 3, f"{concept.id}: only {len(blooms)} Bloom levels"

    # Expert review sample
    sample = random.sample(questions, 100)
    rejection_rate = expert_review_simulation(sample)
    assert rejection_rate < 0.30, f"Rejection rate {rejection_rate:.0%} exceeds 30%"
```

## CNT-003: Expert Review Tool (Admin UI)
**Priority:** P1 | **Blocked by:** CNT-002
- [ ] Internal admin UI: list pending concepts, side-by-side LLM vs source
- [ ] Accept / Edit / Reject per field
- [ ] Bulk approve for high-confidence items (> 0.95)
- [ ] Rejection rate tracking per concept cluster
- [ ] Escalation: rejection > 40% → flag for prompt engineering review
- [ ] **Test:** Advisor reviews 50 concepts in 4 hours (throughput target: ~12/hour)

## CNT-004: QA Pass (Automated)
**Priority:** P1 | **Blocked by:** CNT-002
- [ ] Prerequisite cycle detection
- [ ] Bloom level coverage: each concept has questions at ≥ 3 Bloom levels
- [ ] Difficulty distribution: 30-80% first-attempt accuracy band
- [ ] Hebrew terminology validation (against glossary)
- [ ] No orphan concepts (every concept reachable from root)
- [ ] **Test:** QA script runs against full Math graph → 0 errors; generates report

## CNT-005: Curriculum Publication + Hot-Reload
**Priority:** P1 | **Blocked by:** CNT-004, ACT-008
- [ ] Versioned publication: `math_5u_v1.0.0` → S3 Protobuf artifact
- [ ] Neo4j update (source of truth)
- [ ] NATS `CurriculumPublished_V1` event emitted
- [ ] Actor cluster hot-reloads in-memory graph (no restart)
- [ ] Students in active sessions: continue with old content, next session uses new
- [ ] **Test:** Publish new version → verify actors serve updated graph within 5 seconds

## CNT-006: Physics Curriculum Graph
**Priority:** P2 | **Blocked by:** CNT-001 (pattern proven on Math)
- [ ] 1,500 Physics concept nodes
- [ ] Hebrew + Arabic concept names
- [ ] 12,000-22,500 questions
- [ ] Expert review: 6-12 weeks
- [ ] **Test:** Same QA pass as Math; advisor approval rate > 70%

## CNT-007: Diagram Generation Scheduler

**Priority:** P2 | **Blocked by:** CNT-002

- See `tasks/content/CNT-007-diagram-scheduler.md`

---

## CNT-008: Question Ingestion Pipeline (Extract, OCR, Normalize, Classify, Dedup)

**Priority:** P1 | **Blocked by:** DATA-003, INF-005, CNT-001 | **Effort:** 8 days

- [ ] S3 file watcher + URL fetcher with idempotency (SHA-256 dedup)
- [ ] OCR: Gemini 2.5 Flash primary, Mathpix fallback for complex equations
- [ ] LLM-first question segmentation (multi-part chains preserved)
- [ ] Normalize to Cena Item Schema (~30 fields, LaTeX math, bilingual locale map)
- [ ] 5-stage quality classification: SymPy + SVM Bloom's + LLM difficulty/language/pedagogy
- [ ] 3-level deduplication: exact hash → structural AST → semantic embedding
- [ ] Re-creation engine: generate 3-5 original questions per extracted pattern
- [ ] **Test:** Ingest 10 Bagrut PDF pages → extract ≥ 30 questions → classify all → 0 duplicates served
- See `tasks/content/CNT-008-ingestion.md` for subtasks

## CNT-009: Moderation & Expert Review Workflow

**Priority:** P1 | **Blocked by:** CNT-008, CNT-003 | **Effort:** 5 days

- [ ] Extended review queue (ingested + re-created + batch-generated items)
- [ ] Review actions: approve, edit, reject, escalate, request re-generation
- [ ] Auto-approval engine: quality > 0.95 AND SymPy verified → skip human review (10% spot-check)
- [ ] Bias audit: representational, cultural, gender, stereotype detection
- [ ] Rejection analytics + escalation (20% → alert, 40% → pause generation)
- [ ] Moderator management: assignment by language/subject, inter-rater reliability tracking
- [ ] **Test:** Auto-approval spot-check rejection < 5%; moderator throughput ≥ 12 items/hour
- See `tasks/content/CNT-009-moderation.md` for subtasks

## CNT-010: Adaptive Question Serving (Student-Personalized)

**Priority:** P0 | **Blocked by:** CNT-009, ACT-001, DATA-003 | **Effort:** 6 days

- [ ] Question Pool Actor: in-memory per-subject, hot-reload on publish events
- [ ] Student context: BKT mastery, focus state, ZPD bounds, session history, language, depth unit
- [ ] Multi-criteria selection: concept priority → Bloom's progression → ZPD difficulty → item freshness
- [ ] Focus-aware adaptation: reduce difficulty on declining focus, suggest microbreaks on critical
- [ ] Session goal modes: practice, review, challenge, diagnostic, exam prep
- [ ] REST + SignalR delivery API with localized content (Hebrew or Arabic, shared math)
- [ ] Serving quality gates: published + SymPy verified + 24h cool-off + no student reports
- [ ] **Test:** Selection < 10ms; focus adaptation triggers at correct thresholds; no repeats within session
- See `tasks/content/CNT-010-serving.md` for subtasks
