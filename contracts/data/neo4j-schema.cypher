// ═══════════════════════════════════════════════════════════════════════
// Cena Platform — Neo4j Curriculum Knowledge Graph Schema
// Layer: Data | DB: Neo4j AuraDB 5.x
// Purpose: Curriculum structure, prerequisite relationships, and
//          Methodology-Concept Matrix (MCM) for adaptive routing.
// ═══════════════════════════════════════════════════════════════════════
//
// DEPLOYMENT NOTES:
// - Execute constraints/indexes FIRST (they are idempotent).
// - AuraDB enforces unique constraints as primary indexes — no separate
//   unique index needed.
// - All IDs are application-generated UUIDs (string) to align with
//   Marten event store stream keys.
// - Bloom levels follow revised Bloom's taxonomy:
//   remember → understand → apply → analyze → evaluate → create
// ═══════════════════════════════════════════════════════════════════════


// ─────────────────────────────────────────────────────────────────────
// 1. CONSTRAINTS (idempotent — safe to re-run)
// ─────────────────────────────────────────────────────────────────────

// Concept nodes must have a globally unique ID.
CREATE CONSTRAINT concept_id_unique IF NOT EXISTS
FOR (c:Concept) REQUIRE c.id IS UNIQUE;

// ErrorType nodes (MCM row headers).
CREATE CONSTRAINT error_type_id_unique IF NOT EXISTS
FOR (e:ErrorType) REQUIRE e.id IS UNIQUE;

// ConceptCategory nodes (MCM column context).
CREATE CONSTRAINT concept_category_id_unique IF NOT EXISTS
FOR (cc:ConceptCategory) REQUIRE cc.id IS UNIQUE;

// Methodology nodes (MCM targets).
CREATE CONSTRAINT methodology_id_unique IF NOT EXISTS
FOR (m:Methodology) REQUIRE m.id IS UNIQUE;

// Subject nodes for top-level grouping.
CREATE CONSTRAINT subject_id_unique IF NOT EXISTS
FOR (s:Subject) REQUIRE s.id IS UNIQUE;

// ─────────────────────────────────────────────────────────────────────
// 2. INDEXES (composite and lookup indexes for hot-path queries)
// ─────────────────────────────────────────────────────────────────────

// Fast lookup by subject + difficulty (exercise selection query).
CREATE INDEX concept_subject_difficulty IF NOT EXISTS
FOR (c:Concept) ON (c.subject, c.difficulty);

// Bloom-level filtering for pedagogical sequencing.
CREATE INDEX concept_bloom IF NOT EXISTS
FOR (c:Concept) ON (c.bloom_level);

// MCM lookup: given an error type, find recommended methodologies.
// The relationship properties are the main filter, but indexing the
// source nodes accelerates the traversal start.
CREATE INDEX error_type_name IF NOT EXISTS
FOR (e:ErrorType) ON (e.name);

// Full-text search on concept names (teacher/admin search).
CREATE FULLTEXT INDEX concept_name_fulltext IF NOT EXISTS
FOR (c:Concept) ON EACH [c.name, c.description];


// ─────────────────────────────────────────────────────────────────────
// 3. NODE DEFINITIONS (via MERGE — idempotent)
// ─────────────────────────────────────────────────────────────────────

// ── Subjects ──

MERGE (s:Subject {id: 'subj-math'})
SET s.name       = 'Mathematics',
    s.created_at = datetime();

// ── Error Types (MCM rows) ──
// These align with the ErrorType field in ConceptAttempted_V1:
//   "procedural" | "conceptual" | "motivational" | "none"

MERGE (et1:ErrorType {id: 'err-procedural'})
SET et1.name        = 'procedural',
    et1.description = 'Student knows the concept but makes execution errors (arithmetic slips, sign errors, wrong order of operations)';

MERGE (et2:ErrorType {id: 'err-conceptual'})
SET et2.name        = 'conceptual',
    et2.description = 'Fundamental misunderstanding of the underlying concept';

MERGE (et3:ErrorType {id: 'err-motivational'})
SET et3.name        = 'motivational',
    et3.description = 'Student disengages — skips, low effort answers, declining response quality';

// ── Concept Categories (MCM column context) ──

MERGE (cc1:ConceptCategory {id: 'cat-arithmetic'})
SET cc1.name = 'Arithmetic';

MERGE (cc2:ConceptCategory {id: 'cat-algebra'})
SET cc2.name = 'Algebra';

MERGE (cc3:ConceptCategory {id: 'cat-geometry'})
SET cc3.name = 'Geometry';

// ── Methodologies (MCM targets) ──
// These align with the MethodologyActive field in ConceptAttempted_V1
// and the NewMethodology field in MethodologySwitched_V1.

MERGE (m1:Methodology {id: 'meth-socratic'})
SET m1.name        = 'socratic',
    m1.description = 'Guided questioning — leads student to discover the answer through scaffolded prompts';

MERGE (m2:Methodology {id: 'meth-spaced-rep'})
SET m2.name        = 'spaced_repetition',
    m2.description = 'Memory-optimized review scheduling based on half-life regression';

MERGE (m3:Methodology {id: 'meth-feynman'})
SET m3.name        = 'feynman',
    m3.description = 'Student explains concept in own words; AI identifies gaps in explanation';

MERGE (m4:Methodology {id: 'meth-worked-examples'})
SET m4.name        = 'worked_examples',
    m4.description = 'Step-by-step demonstration followed by faded scaffolding';

MERGE (m5:Methodology {id: 'meth-gamified-drill'})
SET m5.name        = 'gamified_drill',
    m5.description = 'Rapid-fire practice with XP rewards and streak mechanics';


// ─────────────────────────────────────────────────────────────────────
// 4. SAMPLE MATH CONCEPTS (5 concepts with prerequisite chain)
// ─────────────────────────────────────────────────────────────────────
//
// Prerequisite DAG:
//
//   [Addition] ──→ [Multiplication] ──→ [Fractions]
//       │                                    │
//       └──→ [Subtraction] ──→ [Negative Numbers]
//
// ─────────────────────────────────────────────────────────────────────

MERGE (c1:Concept {id: 'math-addition'})
SET c1.name               = 'Addition',
    c1.description        = 'Adding whole numbers including multi-digit with carrying',
    c1.subject            = 'Mathematics',
    c1.difficulty         = 1,
    c1.bloom_level        = 'remember',
    c1.prerequisite_count = 0,
    c1.estimated_minutes  = 30,
    c1.created_at         = datetime(),
    c1.updated_at         = datetime();

MERGE (c2:Concept {id: 'math-subtraction'})
SET c2.name               = 'Subtraction',
    c2.description        = 'Subtracting whole numbers including multi-digit with borrowing',
    c2.subject            = 'Mathematics',
    c2.difficulty         = 1,
    c2.bloom_level        = 'remember',
    c2.prerequisite_count = 1,
    c2.estimated_minutes  = 30,
    c2.created_at         = datetime(),
    c2.updated_at         = datetime();

MERGE (c3:Concept {id: 'math-multiplication'})
SET c3.name               = 'Multiplication',
    c3.description        = 'Multiplying whole numbers including multi-digit and properties (commutative, associative, distributive)',
    c3.subject            = 'Mathematics',
    c3.difficulty         = 2,
    c3.bloom_level        = 'understand',
    c3.prerequisite_count = 1,
    c3.estimated_minutes  = 45,
    c3.created_at         = datetime(),
    c3.updated_at         = datetime();

MERGE (c4:Concept {id: 'math-fractions'})
SET c4.name               = 'Fractions',
    c4.description        = 'Fraction representation, equivalence, addition, subtraction, multiplication of fractions',
    c4.subject            = 'Mathematics',
    c4.difficulty         = 3,
    c4.bloom_level        = 'apply',
    c4.prerequisite_count = 1,
    c4.estimated_minutes  = 60,
    c4.created_at         = datetime(),
    c4.updated_at         = datetime();

MERGE (c5:Concept {id: 'math-negative-numbers'})
SET c5.name               = 'Negative Numbers',
    c5.description        = 'Number line extension, operations with negative integers, absolute value',
    c5.subject            = 'Mathematics',
    c5.difficulty         = 3,
    c5.bloom_level        = 'apply',
    c5.prerequisite_count = 2,
    c5.estimated_minutes  = 45,
    c5.created_at         = datetime(),
    c5.updated_at         = datetime();

// ── Subject membership ──

MERGE (c1)-[:BELONGS_TO]->(s:Subject {id: 'subj-math'});
MERGE (c2)-[:BELONGS_TO]->(s:Subject {id: 'subj-math'});
MERGE (c3)-[:BELONGS_TO]->(s:Subject {id: 'subj-math'});
MERGE (c4)-[:BELONGS_TO]->(s:Subject {id: 'subj-math'});
MERGE (c5)-[:BELONGS_TO]->(s:Subject {id: 'subj-math'});

// ── Category membership ──

MERGE (c1)-[:IN_CATEGORY]->(cc1:ConceptCategory {id: 'cat-arithmetic'});
MERGE (c2)-[:IN_CATEGORY]->(cc1:ConceptCategory {id: 'cat-arithmetic'});
MERGE (c3)-[:IN_CATEGORY]->(cc1:ConceptCategory {id: 'cat-arithmetic'});
MERGE (c4)-[:IN_CATEGORY]->(cc1:ConceptCategory {id: 'cat-arithmetic'});
MERGE (c5)-[:IN_CATEGORY]->(cc2:ConceptCategory {id: 'cat-algebra'});


// ─────────────────────────────────────────────────────────────────────
// 5. PREREQUISITE EDGES
// ─────────────────────────────────────────────────────────────────────
//
// Relationship properties:
//   strength             — 0.0-1.0, how strongly the prerequisite is required
//                          (1.0 = hard gate, <1.0 = recommended but not blocking)
//   empirically_validated — has the prerequisite relationship been confirmed by
//                          learner data (methodology effectiveness projection)?
//   validated_at         — timestamp of last empirical validation
//   sample_size          — number of learner transitions used for validation
//
// Direction: (prerequisite)-[:PREREQUISITE_OF]->(dependent)
//   "Addition is a prerequisite of Multiplication"
// ─────────────────────────────────────────────────────────────────────

MATCH (c1:Concept {id: 'math-addition'}), (c3:Concept {id: 'math-multiplication'})
MERGE (c1)-[r:PREREQUISITE_OF]->(c3)
SET r.strength              = 1.0,
    r.empirically_validated = true,
    r.validated_at          = datetime('2026-01-15T00:00:00Z'),
    r.sample_size           = 2847;

MATCH (c1:Concept {id: 'math-addition'}), (c2:Concept {id: 'math-subtraction'})
MERGE (c1)-[r:PREREQUISITE_OF]->(c2)
SET r.strength              = 0.9,
    r.empirically_validated = true,
    r.validated_at          = datetime('2026-01-15T00:00:00Z'),
    r.sample_size           = 3102;

MATCH (c3:Concept {id: 'math-multiplication'}), (c4:Concept {id: 'math-fractions'})
MERGE (c3)-[r:PREREQUISITE_OF]->(c4)
SET r.strength              = 1.0,
    r.empirically_validated = true,
    r.validated_at          = datetime('2026-02-01T00:00:00Z'),
    r.sample_size           = 1956;

MATCH (c2:Concept {id: 'math-subtraction'}), (c5:Concept {id: 'math-negative-numbers'})
MERGE (c2)-[r:PREREQUISITE_OF]->(c5)
SET r.strength              = 1.0,
    r.empirically_validated = true,
    r.validated_at          = datetime('2026-02-01T00:00:00Z'),
    r.sample_size           = 1483;

MATCH (c4:Concept {id: 'math-fractions'}), (c5:Concept {id: 'math-negative-numbers'})
MERGE (c4)-[r:PREREQUISITE_OF]->(c5)
SET r.strength              = 0.6,
    r.empirically_validated = false,
    r.validated_at          = null,
    r.sample_size           = 0;


// ─────────────────────────────────────────────────────────────────────
// 6. MCM EDGES — Methodology-Concept Matrix
// ─────────────────────────────────────────────────────────────────────
//
// The MCM is the core routing table for adaptive methodology selection.
// Given (ErrorType, ConceptCategory) → ranked list of (Methodology, confidence).
//
// Relationship: (ErrorType)-[:RECOMMENDS {for_category, confidence, rank}]->(Methodology)
//
// The `for_category` property scopes the recommendation to a ConceptCategory.
// This denormalization avoids a 3-way join at query time — the hot-path query
// is: "student made error_type X on concept in category Y → which methodology?"
//
// confidence: 0.0-1.0, derived from MethodologyEffectivenessProjection
//             (async Marten projection aggregating MethodologySwitched_V1 outcomes).
// rank:       1 = primary recommendation, 2 = fallback, etc.
// min_attempts_for_switch: minimum failed attempts before switching to this method.
// ─────────────────────────────────────────────────────────────────────

// ── Procedural errors + Arithmetic → Worked Examples (primary), Gamified Drill (fallback) ──

MATCH (et:ErrorType {id: 'err-procedural'}), (m:Methodology {id: 'meth-worked-examples'})
MERGE (et)-[r:RECOMMENDS]->(m)
SET r.for_category             = 'cat-arithmetic',
    r.confidence               = 0.87,
    r.rank                     = 1,
    r.min_attempts_for_switch  = 3,
    r.last_recalculated        = datetime('2026-03-01T00:00:00Z'),
    r.sample_size              = 4210;

MATCH (et:ErrorType {id: 'err-procedural'}), (m:Methodology {id: 'meth-gamified-drill'})
MERGE (et)-[r:RECOMMENDS]->(m)
SET r.for_category             = 'cat-arithmetic',
    r.confidence               = 0.72,
    r.rank                     = 2,
    r.min_attempts_for_switch  = 5,
    r.last_recalculated        = datetime('2026-03-01T00:00:00Z'),
    r.sample_size              = 3890;

// ── Conceptual errors + Arithmetic → Socratic (primary), Feynman (fallback) ──

MATCH (et:ErrorType {id: 'err-conceptual'}), (m:Methodology {id: 'meth-socratic'})
MERGE (et)-[r:RECOMMENDS]->(m)
SET r.for_category             = 'cat-arithmetic',
    r.confidence               = 0.91,
    r.rank                     = 1,
    r.min_attempts_for_switch  = 2,
    r.last_recalculated        = datetime('2026-03-01T00:00:00Z'),
    r.sample_size              = 2756;

MATCH (et:ErrorType {id: 'err-conceptual'}), (m:Methodology {id: 'meth-feynman'})
MERGE (et)-[r:RECOMMENDS]->(m)
SET r.for_category             = 'cat-arithmetic',
    r.confidence               = 0.78,
    r.rank                     = 2,
    r.min_attempts_for_switch  = 4,
    r.last_recalculated        = datetime('2026-03-01T00:00:00Z'),
    r.sample_size              = 1843;

// ── Motivational errors + Arithmetic → Gamified Drill (primary), Spaced Rep (fallback) ──

MATCH (et:ErrorType {id: 'err-motivational'}), (m:Methodology {id: 'meth-gamified-drill'})
MERGE (et)-[r:RECOMMENDS]->(m)
SET r.for_category             = 'cat-arithmetic',
    r.confidence               = 0.83,
    r.rank                     = 1,
    r.min_attempts_for_switch  = 2,
    r.last_recalculated        = datetime('2026-03-01T00:00:00Z'),
    r.sample_size              = 1567;

MATCH (et:ErrorType {id: 'err-motivational'}), (m:Methodology {id: 'meth-spaced-rep'})
MERGE (et)-[r:RECOMMENDS]->(m)
SET r.for_category             = 'cat-arithmetic',
    r.confidence               = 0.65,
    r.rank                     = 2,
    r.min_attempts_for_switch  = 4,
    r.last_recalculated        = datetime('2026-03-01T00:00:00Z'),
    r.sample_size              = 1102;

// ── Conceptual errors + Algebra → Feynman (primary), Socratic (fallback) ──

MATCH (et:ErrorType {id: 'err-conceptual'}), (m:Methodology {id: 'meth-feynman'})
MERGE (et)-[r2:RECOMMENDS]->(m)
// NOTE: Neo4j allows parallel edges. This is a SECOND RECOMMENDS edge with
// a different for_category. Queries filter on r.for_category to distinguish.
SET r2.for_category            = 'cat-algebra',
    r2.confidence              = 0.85,
    r2.rank                    = 1,
    r2.min_attempts_for_switch = 2,
    r2.last_recalculated       = datetime('2026-03-01T00:00:00Z'),
    r2.sample_size             = 982;

MATCH (et:ErrorType {id: 'err-conceptual'}), (m:Methodology {id: 'meth-socratic'})
MERGE (et)-[r2:RECOMMENDS]->(m)
SET r2.for_category            = 'cat-algebra',
    r2.confidence              = 0.79,
    r2.rank                    = 2,
    r2.min_attempts_for_switch = 4,
    r2.last_recalculated       = datetime('2026-03-01T00:00:00Z'),
    r2.sample_size             = 871;


// ─────────────────────────────────────────────────────────────────────
// 7. UTILITY QUERIES (for reference — not executed at schema time)
// ─────────────────────────────────────────────────────────────────────

// ── HOT PATH: Get methodology recommendation for a student's error ──
// Called by the adaptive engine on every MethodologySwitched decision.
//
// MATCH (et:ErrorType {name: $errorType})-[r:RECOMMENDS]->(m:Methodology)
// WHERE r.for_category = $conceptCategory
// RETURN m.name AS methodology, r.confidence, r.rank, r.min_attempts_for_switch
// ORDER BY r.rank ASC;

// ── Get prerequisite chain for a concept (BFS, max depth 5) ──
//
// MATCH path = (prereq:Concept)-[:PREREQUISITE_OF*1..5]->(target:Concept {id: $conceptId})
// RETURN prereq.id, prereq.name, prereq.bloom_level,
//        [r IN relationships(path) | r.strength] AS strengths,
//        length(path) AS depth
// ORDER BY depth DESC;

// ── Validate DAG integrity (detect cycles — should return 0 rows) ──
//
// MATCH path = (c:Concept)-[:PREREQUISITE_OF*2..]->(c)
// RETURN c.id, c.name, length(path) AS cycle_length;

// ── MCM coverage report: categories without recommendations ──
//
// MATCH (cc:ConceptCategory)
// WHERE NOT EXISTS {
//   MATCH (:ErrorType)-[r:RECOMMENDS]->(:Methodology)
//   WHERE r.for_category = cc.id
// }
// RETURN cc.name AS uncovered_category;
