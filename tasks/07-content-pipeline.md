# 07 — Content Pipeline Tasks

**Technology:** Neo4j, Kimi K2.5 (batch), Remotion (Node.js), Expert Review Tool
**Contract files:** `contracts/data/neo4j-schema.cypher`, `contracts/llm/diagram-generation-pipeline.py`, `docs/content-authoring.md`
**Stage:** Foundation (Weeks 1-4) + ongoing

---

## CNT-001: Math Curriculum Graph (Hebrew + Arabic)
**Priority:** P0 | **Blocked by:** DATA-006 (Neo4j)
- [ ] 2,000 Math concept nodes extracted from Bagrut corpus (Hebrew)
- [ ] Prerequisite edges: DAG, no cycles (validated by cycle detection query)
- [ ] Difficulty ratings per concept (0.0-1.0)
- [ ] Bloom level per concept (recall/comprehension/application/analysis)
- [ ] Arabic concept names added (`name_ar` property) for Arab-sector students
- [ ] **Test:** `CALL gds.alpha.cycles.stream({...})` returns 0 cycles; all concepts have prerequisites or are root nodes

## CNT-002: Question Generation (Kimi K2.5 Batch)
**Priority:** P0 | **Blocked by:** CNT-001
- [ ] 8-15 questions per concept (mix of types and Bloom levels)
- [ ] Hebrew question text matching Bagrut exam style
- [ ] Arabic question variants for Arab-sector students
- [ ] Distractor quality: each wrong MCQ option maps to a documented misconception
- [ ] Corpus provenance: each question traces back to source Bagrut exam
- [ ] **Test:** 100 sample questions reviewed by education advisor; rejection rate < 30%

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
