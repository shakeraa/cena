# MST-018: MIRT Estimator (Python, Phase 2)

**Priority:** P3 — Phase 2 upgrade, triggers at 10K users
**Blocked by:** MST-015 (BKT trainer — needs sufficient data pipeline), DATA-006 (Neo4j Q-matrix)
**Estimated effort:** 1-2 weeks (L)
**Contract:** `docs/mastery-engine-architecture.md` section 3.2
**Research ref:** `docs/mastery-measurement-research.md` section 1.2

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule. `throw UnimplementedError`, `// TODO: implement`, empty bodies, and mock returns are FORBIDDEN in source code. If you cannot implement it fully, file a blocking dependency instead.

## Context

Multidimensional Item Response Theory (MIRT) replaces BKT as the primary mastery model in Phase 2. It estimates a latent ability vector theta (20-50 dimensions per subject) for each student, derived from the Q-matrix (item-to-concept mapping stored in Neo4j). MIRT provides richer ability profiles, confidence intervals, and better prediction accuracy at scale. The estimator runs offline in Python, publishes theta vectors via NATS JetStream, and the StudentActor uses theta as a Bayesian prior for BKT: `P(L_0) = sigmoid(theta_k)` for cluster k. It triggers when the platform reaches 10K+ students with 50+ interactions each.

## Subtasks

### MST-018.1: Q-Matrix Export from Neo4j

**Files to create:**
- `scripts/training/mirt_q_matrix.py`
- `scripts/training/mirt_q_matrix.cypher`

**Acceptance:**
- [ ] Cypher query: `MATCH (i:Item)-[:ASSESSES]->(c:Concept) RETURN i.id, c.id, c.topic_cluster`
- [ ] Python script connects to Neo4j, exports Q-matrix as binary numpy array
- [ ] Q-matrix dimensions: `(n_items, n_concepts)` where `Q[i,j] = 1` if item i assesses concept j
- [ ] Reduces to topic-cluster dimensions for tractability: 20-50 clusters per subject
- [ ] Validates Q-matrix: every item assesses at least one concept, every concept has at least one item

### MST-018.2: MIRT Model Estimation

**Files to create:**
- `scripts/training/mirt_estimator.py`

**Acceptance:**
- [ ] Uses `mirt` or `mirtjml` Python library for multidimensional IRT estimation
- [ ] Input: response matrix `(n_students, n_items)` + Q-matrix
- [ ] Output: theta matrix `(n_students, n_dimensions)` + item parameters (discrimination, difficulty)
- [ ] Estimation method: marginal maximum likelihood (MML) with EM algorithm
- [ ] Convergence criteria: max 100 iterations or log-likelihood change < 0.001
- [ ] Computes Fisher information matrix for confidence intervals on theta
- [ ] Minimum data threshold: 10K students with 50+ interactions each
- [ ] Falls back gracefully if data is insufficient (logs warning, exits without output)

### MST-018.3: Theta Publishing via NATS

**Files to create:**
- `scripts/training/mirt_publisher.py`
- `src/Cena.Domain/Learner/Events/MirtThetaUpdated.cs`

**Acceptance:**
- [ ] Publishes theta vector per student to NATS JetStream subject `learner.mirt.updated`
- [ ] Message schema: `{ "student_id": string, "subject": string, "theta": [float, ...], "confidence_intervals": [[float, float], ...], "estimated_at": "ISO8601" }`
- [ ] `MirtThetaUpdated` C# event record mirrors the NATS message schema
- [ ] StudentActor handles `MirtThetaUpdated` by updating BKT priors: `P(L_0) = sigmoid(theta_k)` for each concept cluster k
- [ ] Batch publish: up to 1000 students per NATS batch, with back-pressure handling

### MST-018.4: Composite Scoring (Phase 2 Formula)

**Files to create/modify:**
- `src/Cena.Domain/Learner/Mastery/CompositeScoring.cs`

**Acceptance:**
- [ ] `CompositeScoring.Compute(float mirtTheta, float recallProbability, float accuracy10, int bloomLevel, float latencyScore, float errorAbsence, float calibration, float prereqSupport) → float`
- [ ] Phase 2 formula: `0.35*mirt_theta + 0.25*p_recall + 0.15*accuracy_10 + 0.10*(bloom/6) + 0.05*latency_score + 0.05*error_absence + 0.05*calibration`
- [ ] `effective = composite * prereqSupport`
- [ ] `mirt_theta` is normalized to [0, 1] via sigmoid before composition
- [ ] `ConceptMastered` fires only if confidence interval lower bound > 0.90

**Test:**
```python
def test_q_matrix_export_matches_neo4j():
    """Q-matrix must have correct dimensions and non-zero entries."""
    import numpy as np
    from mirt_q_matrix import export_q_matrix

    # Mock Neo4j result: 3 items assessing 2 concepts
    neo4j_edges = [
        {"item_id": "item-1", "concept_id": "algebra"},
        {"item_id": "item-1", "concept_id": "geometry"},
        {"item_id": "item-2", "concept_id": "algebra"},
        {"item_id": "item-3", "concept_id": "geometry"},
    ]

    Q, item_ids, concept_ids = export_q_matrix(neo4j_edges)

    assert Q.shape == (3, 2)  # 3 items, 2 concepts
    assert Q[0, 0] == 1  # item-1 assesses algebra
    assert Q[0, 1] == 1  # item-1 assesses geometry
    assert Q[1, 0] == 1  # item-2 assesses algebra
    assert Q[1, 1] == 0  # item-2 does NOT assess geometry
    assert Q[2, 0] == 0  # item-3 does NOT assess algebra
    assert Q[2, 1] == 1  # item-3 assesses geometry


def test_mirt_estimator_produces_theta_vectors():
    """MIRT estimation should produce theta vectors with correct dimensions."""
    import numpy as np
    from mirt_estimator import estimate_mirt

    np.random.seed(42)
    n_students, n_items, n_dims = 500, 50, 5

    # Generate synthetic Q-matrix (each item loads on 1-2 dimensions)
    Q = np.zeros((n_items, n_dims), dtype=int)
    for i in range(n_items):
        dims = np.random.choice(n_dims, size=np.random.randint(1, 3), replace=False)
        Q[i, dims] = 1

    # Generate synthetic response data
    true_theta = np.random.randn(n_students, n_dims)
    responses = np.random.binomial(1, 0.5, (n_students, n_items))

    result = estimate_mirt(responses, Q, max_iter=10)

    assert result["theta"].shape == (n_students, n_dims)
    assert result["confidence_intervals"].shape == (n_students, n_dims, 2)
    assert result["theta"].dtype == np.float64
    # Theta values should be reasonable (within [-4, 4] for most students)
    assert np.abs(result["theta"]).max() < 10.0


def test_mirt_publisher_batch_format():
    """NATS messages must have correct schema for StudentActor consumption."""
    import json
    import numpy as np
    from mirt_publisher import format_nats_messages

    theta = np.array([[0.5, -0.3, 1.2], [0.1, 0.8, -0.5]])
    ci = np.array([
        [[0.2, 0.8], [-0.6, 0.0], [0.9, 1.5]],
        [[-0.2, 0.4], [0.5, 1.1], [-0.8, -0.2]]
    ])
    student_ids = ["student-1", "student-2"]

    messages = format_nats_messages(student_ids, theta, ci, subject="math")

    assert len(messages) == 2
    msg = json.loads(messages[0])
    assert msg["student_id"] == "student-1"
    assert msg["subject"] == "math"
    assert len(msg["theta"]) == 3
    assert len(msg["confidence_intervals"]) == 3
    assert len(msg["confidence_intervals"][0]) == 2  # [lower, upper]
    assert "estimated_at" in msg
```

```csharp
[Fact]
public void CompositeScoring_Phase2Formula_ProducesValidRange()
{
    var composite = CompositeScoring.Compute(
        mirtTheta: 1.5f,          // raw theta, will be sigmoided
        recallProbability: 0.80f,
        accuracy10: 0.85f,
        bloomLevel: 4,
        latencyScore: 0.70f,
        errorAbsence: 0.90f,
        calibration: 0.75f,
        prereqSupport: 0.95f);

    Assert.InRange(composite, 0.0f, 1.0f);
}

[Fact]
public void CompositeScoring_HighTheta_HighScore()
{
    var high = CompositeScoring.Compute(
        mirtTheta: 2.0f, recallProbability: 0.95f, accuracy10: 0.95f,
        bloomLevel: 5, latencyScore: 0.90f, errorAbsence: 0.95f,
        calibration: 0.90f, prereqSupport: 1.0f);

    var low = CompositeScoring.Compute(
        mirtTheta: -2.0f, recallProbability: 0.30f, accuracy10: 0.40f,
        bloomLevel: 1, latencyScore: 0.20f, errorAbsence: 0.30f,
        calibration: 0.40f, prereqSupport: 1.0f);

    Assert.True(high > low, $"High ability {high} should score higher than low {low}");
}

[Fact]
public void CompositeScoring_ZeroPrereqs_ReturnsZero()
{
    var score = CompositeScoring.Compute(
        mirtTheta: 2.0f, recallProbability: 0.95f, accuracy10: 0.95f,
        bloomLevel: 6, latencyScore: 0.90f, errorAbsence: 0.95f,
        calibration: 0.90f, prereqSupport: 0.0f);

    Assert.Equal(0.0f, score);
}
```
