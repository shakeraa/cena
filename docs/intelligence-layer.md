# Intelligence Layer — Data Flywheels & Semantic Search

> **Status:** Specification
> **Last updated:** 2026-03-26
> **Classification:** Cross-cutting concern addressing I8 (Data Moat) and I9 (Search)

---

## 1. The Problem

The architecture claims "per-student methodology effectiveness data" as a competitive moat (see MCM Graph Authoring in `content-authoring.md`), but there is no mechanism for aggregate data to feed back into improving the system. Without this flywheel, Cena is a tutoring app, not a learning intelligence platform.

Separately, students have no way to find concepts. The knowledge graph is browsable but not searchable. A student who wants to review "integration by parts" has to navigate the graph manually.

---

## 2. Data Flywheel Architecture

Six flywheels transform raw interaction events into progressively better adaptive intelligence. Each flywheel has a defined data source, signal, retraining cadence, minimum data threshold, and guard rail.

### Flywheel 1: MCM Graph Improvement (Methodology Selection)

The MCM (Mode x Capability x Methodology) graph maps error types + concept categories to recommended methodology. It is initially hand-crafted (see `content-authoring.md` Section 7). This flywheel makes it data-driven.

- **Data source**: All `MethodologySwitched` events + subsequent `ConceptAttempted` / `ConceptMastered` events
- **Signal**: "After switching from Socratic to Feynman on calculus integration, did the student's mastery velocity increase?"
- **Retraining**: Monthly batch job. For each (concept_category, error_type) pair, compute methodology effectiveness scores across all students who experienced a switch. Update MCM confidence scores.
- **Minimum data**: 100+ methodology switches per (concept_category, error_type) pair before updating confidence
- **Metric**: Methodology recommendation accuracy — does the MCM-recommended method outperform random selection? Track this as a product KPI.

### Flywheel 2: BKT Parameter Refinement

- **Data source**: All `ConceptAttempted` events
- **Retraining**: Quarterly. Run pyBKT on accumulated interaction logs. Re-estimate p_learn, p_slip, p_guess, p_forget per concept.
- **Impact**: Better mastery estimation leads to better item selection leads to faster learning
- **Guard rail**: A/B test new parameters against old before rolling out. New parameters are assigned to a treatment cohort via the existing A/B testing infrastructure on `StudentProfile` actor state (see `architecture-design.md` Section 4.5).

### Flywheel 3: Question Difficulty Calibration

- **Data source**: Per-question accuracy rates, response times, hint usage
- **Signal**: If 95% of students get a question right instantly, it is too easy. If 10% get it right even with hints, it is too hard or poorly worded.
- **Process**: Monthly automated analysis flags questions outside the target difficulty band (30%-80% first-attempt accuracy). Content team reviews and adjusts via the Content Authoring tool.
- **IRT calibration**: Use Item Response Theory (2-parameter model) to estimate question discrimination and difficulty parameters from real response data. Feed calibrated parameters back into item selection, replacing initial hand-estimated difficulty ratings.

### Flywheel 4: Spaced Repetition Half-Life Personalization

- **Data source**: `ConceptMastered` + subsequent `ConceptAttempted` during review sessions
- **Signal**: Actual forgetting curves per concept type (procedural knowledge decays differently from conceptual knowledge)
- **Retraining**: Monthly. Update default half-life parameters per concept category based on aggregate forgetting data. Per-student half-lives already personalize in real-time via the Half-Life Regression model running inside the `OutreachSchedulerActor` (see `architecture-design.md` Section 4.4).
- **Guard rail**: Cap half-life adjustments at +/- 30% per retraining cycle to prevent oscillation.

### Flywheel 5: Stagnation Signal Weights

- **Data source**: `StagnationDetected` events + what happened next (did the student overcome stagnation after the recommended action?)
- **Signal**: Which stagnation sub-signals (accuracy plateau, response time drift, session abandonment, error repetition, annotation sentiment) are most predictive of actual stagnation?
- **Retraining**: Quarterly. Logistic regression on stagnation outcomes to re-weight the composite score used by the `StagnationDetectorActor` (see `architecture-design.md` Section 3.2.3).
- **Minimum data**: 500+ stagnation events with outcome tracking before first retraining.

### Flywheel 6: Engagement Pattern Models

- **Data source**: Session timing, notification response rates, streak behavior, time-of-day performance
- **Signal**: What notification timing maximizes return probability? What session length maximizes mastery gain per minute?
- **Application**: Per-student engagement optimization — when to send nudges, how long sessions should be, what time of day to suggest study. Feeds into the `OutreachSchedulerActor` contact-time optimization.
- **Retraining**: Monthly. Gradient-boosted model trained on engagement outcomes.
- **Guard rail**: Respect parental notification preferences and quiet hours. Never optimize for engagement at the expense of learning outcomes.

---

## 3. Data Pipeline Architecture

```
Event Store (PostgreSQL/Marten)
  --> Nightly S3 export (anonymized Parquet files)
  --> Retraining jobs (scheduled Fargate tasks)
        |-- pyBKT retraining (quarterly)
        |-- MCM graph update (monthly)
        |-- IRT question calibration (monthly)
        |-- HLR half-life update (monthly)
        |-- Stagnation weight optimization (quarterly)
        |-- Engagement model training (monthly)
  --> Updated model artifacts --> S3 (versioned, timestamped)
  --> Proto.Actor silos hot-reload updated models
      (same mechanism as curriculum hot-reload, see content-authoring.md Section 3.6)
```

### Pipeline Details

| Step | Technology | Notes |
|------|-----------|-------|
| **Event export** | Marten async daemon projection to Parquet via custom projector | Runs nightly. Crypto-shredding keys are NOT exported — all data is anonymized at export time. |
| **Storage** | S3, partitioned by date and event type | Parquet format for efficient columnar queries |
| **Compute** | AWS Fargate scheduled tasks | Python scripts using pyBKT, scikit-learn, pyirt. No persistent compute — runs to completion and terminates. |
| **Artifact storage** | S3 with versioning | Each retraining run produces a timestamped artifact. Rollback = point to previous version. |
| **Model loading** | NATS event `ModelArtifactUpdated_V1` | Proto.Actor silos subscribe. On event, download new artifact from S3, swap in-memory model. Same pattern as `CurriculumPublished_V1`. |

### Anonymization Protocol

All data exported for retraining is anonymized:
- Student IDs replaced with opaque UUIDs (different from production IDs)
- No PII (names, emails, school names) included in export
- Concept IDs and question IDs are preserved (they contain no PII)
- Timestamps preserved but date-shifted by a random per-student offset (preserves temporal patterns, prevents re-identification)

---

## 4. Network Effects

Define how more users make the product better:

| User Count | Intelligence Capability |
|-----------|------------------------|
| **1,000** | MCM graph begins getting real methodology switch data. Stagnation detection improves. Engagement models have enough data for coarse time-of-day optimization. |
| **10,000** | IRT calibration produces statistically significant question difficulty estimates. BKT parameters are reliable per concept. MCM graph has 100+ switches for the top 50% of concept categories. |
| **50,000** | Cross-cohort analysis reveals which teaching methodology approach produces best outcomes per concept category. Engagement pattern models become predictive at the per-student level. Half-life parameters are calibrated across all concept types. |
| **100,000+** | The system's adaptive intelligence is demonstrably superior to any new entrant who starts with zero interaction data. MCM, BKT, IRT, HLR, stagnation, and engagement models are all data-driven. **This is the moat.** |

### Why This Is Defensible

A competitor can replicate the architecture. They cannot replicate 100,000+ students' worth of:
- Methodology effectiveness data per (concept, error_type) pair
- Calibrated question difficulty and discrimination parameters
- Forgetting curve data per concept category
- Stagnation signal weights validated against real outcomes

This data compounds. Each retraining cycle makes the next cycle's models better, because better models produce better learning experiences, which produce more engagement, which produces more data.

---

## 5. Search

### 5.1 Search Types

| Type | Example Query | Mechanism |
|------|--------------|-----------|
| **Text search** | "integration by parts" | Keyword match on concept names, descriptions, question text |
| **Semantic search** | "how to find the area under a curve" | Vector similarity finds integration concepts even though the query does not use the word "integration" |
| **Formula search** | `integral x^2 dx` | Embedding of LaTeX/Unicode formula text matched against concept key_formulas field |

### 5.2 Implementation

- **Embedding model**: OpenAI `text-embedding-3-small` (1536 dimensions, $0.02/MTok — negligible cost for a one-time corpus embedding)
- **Vector storage**: `pgvector` extension on the existing PostgreSQL instance (no new infrastructure)
- **Index type**: HNSW (Hierarchical Navigable Small World) index for approximate nearest neighbor search. At 10,000 items, exact search is also feasible; HNSW provides headroom for growth.
- **Hybrid ranking**: Combine pgvector cosine similarity score with Neo4j full-text keyword match score. Weight: 0.6 semantic + 0.4 keyword (tunable).

### 5.3 What Gets Embedded

| Content Type | Source | Approx. Count (MVP) |
|-------------|--------|---------------------|
| Concept descriptions | `display_name` + `common_misconceptions` + `key_formulas` | ~3,500 |
| Question text | Question stem + answer options | ~40,000 |
| Explanation summaries | First paragraph of each explanation template | ~10,500 |

Total embedding cost at MVP: ~2M tokens = ~$0.04. Re-embedding on curriculum publication is negligible.

### 5.4 GraphQL Endpoint

```graphql
type SearchResult {
  conceptId: String!
  displayName: String!
  subject: String!
  depthUnit: Int!
  masteryStatus: MasteryStatus!  # GREEN (mastered), YELLOW (in-progress), GRAY (not started)
  relevanceScore: Float!
  matchType: SearchMatchType!    # TEXT, SEMANTIC, FORMULA
}

enum MasteryStatus { GREEN, YELLOW, GRAY }
enum SearchMatchType { TEXT, SEMANTIC, FORMULA }

type Query {
  searchConcepts(query: String!, limit: Int = 10): [SearchResult!]!
}
```

The `masteryStatus` field is resolved per-student by querying the student's knowledge graph overlay from the `StudentActor`. This means search results are personalized — the student sees which results they have already mastered.

### 5.5 Search UX

- **Location**: Search bar at the top of the knowledge graph view (both mobile and web)
- **Behavior**: Debounced input (300ms). Results appear as an overlay list.
- **Result display**: Concept name, mastery status indicator (green/yellow/gray dot), subject badge, depth level
- **Navigation**: Tapping a result navigates to that concept in the knowledge graph and optionally starts a learning session
- **Empty state**: If no results match, suggest browsing the knowledge graph by subject

### 5.6 Embedding Pipeline

```
Curriculum Published (CurriculumPublished_V1 event)
  --> Embedding worker (Fargate task, triggered by NATS event)
       --> Fetch new/changed concepts from Neo4j
       --> Generate embeddings via text-embedding-3-small
       --> Upsert into pgvector table
       --> NATS event: SearchIndexUpdated_V1
```

The embedding pipeline runs on the same trigger as the curriculum hot-reload, ensuring search results are always in sync with the published knowledge graph.

---

## 6. Relationship to Existing Bounded Contexts

| Context | Relationship to Intelligence Layer |
|---------|-----------------------------------|
| **Learner Context** | Produces the raw events (ConceptAttempted, ConceptMastered, MethodologySwitched, StagnationDetected) that feed all six flywheels |
| **Pedagogy Context** | Consumes updated MCM graph, BKT parameters, stagnation weights. Hot-reloads on model update events. |
| **Curriculum Context** | Consumes IRT-calibrated difficulty parameters. Triggers search re-indexing on publication. |
| **Analytics Context** | Provides the CQRS read models and S3 exports that the retraining pipeline consumes |
| **Content Authoring Context** | Receives flagged questions from IRT calibration (Flywheel 3) into the review queue |
| **Delivery Context** | Search endpoint lives here — it is a read-only query that combines pgvector similarity with student mastery state |

---

## Appendix: Domain Events Added

| Event | Publisher | Consumers |
|-------|-----------|-----------|
| `ModelArtifactUpdated_V1` | Retraining pipeline (Fargate) | Proto.Actor silos (hot-reload models) |
| `SearchIndexUpdated_V1` | Embedding pipeline (Fargate) | Client apps (cache invalidation) |
