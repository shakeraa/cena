# MST-016: HLR Weight Trainer (Python)

**Priority:** P2 — improves spaced repetition accuracy once enough data exists
**Blocked by:** MST-003 (HLR decay engine — needs review data to accumulate)
**Estimated effort:** 3-5 days (M)
**Contract:** `docs/mastery-engine-architecture.md` section 11 (Phase 7)
**Research ref:** `docs/mastery-measurement-research.md` section 1.3.2

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule. `throw UnimplementedError`, `// TODO: implement`, empty bodies, and mock returns are FORBIDDEN in source code. If you cannot implement it fully, file a blocking dependency instead.

## Context

At launch, HLR uses hand-tuned default weights (θ = [0.3, 0.5, -0.2, -0.1, 0.1, 0.05], bias=3.0). This offline Python trainer uses scikit-learn logistic regression to learn optimal weights from actual review data. The training target is: given a student's feature vector and time since last review, predict whether they recall the concept correctly. The trainer runs monthly, reads review events from the analytics replica, and outputs a JSON weight file that the .NET `IHlrWeightProvider` loads on next silo restart.

## Subtasks

### MST-016.1: Review Data Export and Feature Engineering

**Files to create:**
- `scripts/training/hlr_data_export.py`
- `scripts/training/hlr_data_export.sql`

**Acceptance:**
- [ ] SQL query exports review interactions: `student_id`, `concept_id`, `is_correct`, `time_since_last_review_hours`, `attempt_count`, `correct_count`, `concept_difficulty`, `prerequisite_depth`, `bloom_level`, `days_since_first_encounter`
- [ ] Python script builds feature matrix X and binary target y (correct/incorrect)
- [ ] Adds `log(time_since_last_review / half_life_estimate)` as the time decay feature
- [ ] Filters: only review events (not first encounters), minimum 1000 total review events
- [ ] Standardizes features using sklearn `StandardScaler` (saves scaler for deployment)

### MST-016.2: Logistic Regression Training

**Files to create:**
- `scripts/training/hlr_trainer.py`

**Acceptance:**
- [ ] Uses `sklearn.linear_model.LogisticRegression` with L2 regularization
- [ ] Feature vector: `[attempt_count, correct_count, concept_difficulty, prerequisite_depth, bloom_level, days_since_first_encounter]`
- [ ] Training target: `P(recall) = sigmoid(θ^T · x + bias)` — maps to HLR's `h = 2^(θ^T · x + bias)`
- [ ] Cross-validation: 5-fold, reports RMSE between predicted recall probability and actual outcome
- [ ] Acceptance criterion: RMSE < 0.15 on held-out test set
- [ ] Rejects trained weights if RMSE worse than default weights (safety net)

### MST-016.3: Weight Export and Deployment

**Files to create:**
- `scripts/training/hlr_export.py`

**Acceptance:**
- [ ] Outputs JSON file: `{ "weights": [float, ...], "bias": float, "scaler_means": [float, ...], "scaler_stds": [float, ...], "trained_at": "ISO8601", "rmse": float, "sample_count": int }`
- [ ] Uploads to S3: `s3://cena-ml-artifacts/hlr-weights/latest.json` + versioned copy
- [ ] .NET `IHlrWeightProvider` loads and applies scaler transforms before dot product
- [ ] Concepts not covered by training data continue using default weights

**Test:**
```python
def test_hlr_trainer_learns_reasonable_weights():
    """Trained HLR weights should produce half-lives in sensible range."""
    import numpy as np
    from hlr_trainer import train_hlr_weights

    np.random.seed(42)
    n = 2000

    # Simulate: more practice + more correct → higher recall
    features = {
        "attempt_count": np.random.randint(1, 30, n),
        "correct_count": np.random.randint(0, 25, n),
        "concept_difficulty": np.random.uniform(0.1, 1.0, n),
        "prerequisite_depth": np.random.randint(1, 5, n),
        "bloom_level": np.random.randint(1, 6, n),
        "days_since_first": np.random.uniform(1, 90, n),
    }
    # Generate recall outcome correlated with features
    skill = (features["correct_count"] / (features["attempt_count"] + 1)) * 0.5 + 0.3
    outcomes = (np.random.uniform(0, 1, n) < skill).astype(int)

    result = train_hlr_weights(features, outcomes)

    assert len(result["weights"]) == 6
    assert isinstance(result["bias"], float)
    assert result["rmse"] < 0.50  # reasonable fit
    # attempt_count weight should be positive (more practice → better recall)
    assert result["weights"][0] > 0, "attempt_count weight should be positive"


def test_hlr_half_life_from_weights():
    """Verify half-life computation from trained weights matches formula."""
    import numpy as np
    from hlr_trainer import compute_half_life

    weights = np.array([0.3, 0.5, -0.2, -0.1, 0.1, 0.05])
    bias = 3.0
    features = np.array([10, 8, 0.5, 2, 3, 14.0])

    h = compute_half_life(features, weights, bias)

    # h = 2^(θ·x + bias)
    expected = 2 ** (np.dot(weights, features) + bias)
    assert abs(h - expected) < 0.01


def test_hlr_export_json_schema():
    """Exported JSON must match schema expected by .NET IHlrWeightProvider."""
    import json
    from hlr_export import export_weights

    result = {
        "weights": [0.3, 0.5, -0.2, -0.1, 0.1, 0.05],
        "bias": 3.0,
        "rmse": 0.12,
        "sample_count": 5000,
    }

    json_str = export_weights(result)
    parsed = json.loads(json_str)

    assert "weights" in parsed
    assert len(parsed["weights"]) == 6
    assert "bias" in parsed
    assert "trained_at" in parsed
    assert "rmse" in parsed
    assert "sample_count" in parsed
    assert "scaler_means" in parsed
    assert "scaler_stds" in parsed


def test_hlr_trainer_rejects_worse_than_default():
    """If trained weights are worse than defaults, keep defaults."""
    import numpy as np
    from hlr_trainer import train_hlr_weights, evaluate_defaults

    # Tiny dataset that won't train well
    features = {
        "attempt_count": np.array([1, 2]),
        "correct_count": np.array([1, 0]),
        "concept_difficulty": np.array([0.5, 0.5]),
        "prerequisite_depth": np.array([1, 1]),
        "bloom_level": np.array([2, 2]),
        "days_since_first": np.array([1.0, 1.0]),
    }
    outcomes = np.array([1, 0])

    default_rmse = evaluate_defaults(features, outcomes)
    result = train_hlr_weights(features, outcomes)

    # With only 2 samples, trained model should not be accepted
    assert result.get("rejected", False) or result["rmse"] <= default_rmse
```
