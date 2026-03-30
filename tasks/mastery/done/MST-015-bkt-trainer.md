# MST-015: BKT Parameter Trainer (Python)

**Priority:** P2 — improves mastery accuracy once enough data exists
**Blocked by:** DATA-002 (Marten event store — needs ConceptAttempted event data)
**Estimated effort:** 3-5 days (M)
**Contract:** `docs/mastery-engine-architecture.md` section 11 (Phase 7)
**Research ref:** `docs/mastery-measurement-research.md` section 1.1

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule. `throw UnimplementedError`, `// TODO: implement`, empty bodies, and mock returns are FORBIDDEN in source code. If you cannot implement it fully, file a blocking dependency instead.

## Context

At launch, BKT uses hand-tuned default parameters (P_L0=0.10, P_T=0.20, P_S=0.05, P_G=0.25). Once sufficient interaction data accumulates (target: 500+ responses per knowledge component), this offline Python trainer uses pyBKT to learn optimal parameters per KC. The trainer runs as a weekly batch job, reads `ConceptAttempted` events from the PostgreSQL analytics replica, and outputs a JSON parameter file that the .NET BktParameterProvider loads on next silo restart.

## Subtasks

### MST-015.1: Data Export and Preprocessing

**Files to create:**
- `scripts/training/bkt_data_export.py`
- `scripts/training/bkt_data_export.sql`

**Acceptance:**
- [ ] SQL query exports `ConceptAttempted` events: `student_id`, `concept_id`, `is_correct`, `timestamp`, `attempt_sequence` (per student per concept)
- [ ] Python script reads from PostgreSQL analytics replica via `psycopg2`
- [ ] Output: DataFrame with columns `user_id`, `skill_name`, `correct`, `order` (pyBKT expected format)
- [ ] Filters: minimum 500 responses per KC, minimum 10 students per KC
- [ ] Handles missing data gracefully (logs warnings, skips KCs with insufficient data)

### MST-015.2: pyBKT Model Training

**Files to create:**
- `scripts/training/bkt_trainer.py`

**Acceptance:**
- [ ] Uses `pyBKT` library (`from pyBKT.models import Model`)
- [ ] Trains per-KC parameters: `P(L_0)`, `P(T)`, `P(S)`, `P(G)`
- [ ] Validates identifiability constraint: `P(S) + P(G) < 1`
- [ ] Cross-validation: 5-fold, reports AUC for next-response prediction
- [ ] Rejects trained parameters if AUC < default parameters' AUC (safety net)
- [ ] Logs training metrics: AUC, parameter values, KC coverage

### MST-015.3: Parameter Export and Deployment

**Files to create:**
- `scripts/training/bkt_export.py`

**Acceptance:**
- [ ] Outputs JSON file: `{ "parameters": { "<concept_id>": { "P_L0": float, "P_T": float, "P_S": float, "P_G": float }, ... }, "trained_at": "ISO8601", "auc": float, "coverage": int }`
- [ ] Uploads to S3 bucket `s3://cena-ml-artifacts/bkt-parameters/latest.json`
- [ ] Also writes versioned file: `bkt-parameters/{date}.json`
- [ ] .NET `BktParameterProvider` loads this file at silo startup
- [ ] Concepts without trained parameters fall back to defaults

**Test:**
```python
def test_bkt_trainer_produces_valid_parameters():
    """Trained BKT parameters must satisfy identifiability constraints."""
    import json
    import numpy as np
    from bkt_trainer import train_bkt_parameters

    # Simulated interaction data: student gradually learns concept
    data = {
        "user_id": np.repeat(range(50), 20),  # 50 students, 20 attempts each
        "skill_name": ["algebra-basics"] * 1000,
        "correct": np.concatenate([
            np.random.binomial(1, 0.3 + 0.03 * i, 50) for i in range(20)
        ]),
        "order": np.tile(range(20), 50),
    }

    params = train_bkt_parameters(data, min_responses=100)

    assert "algebra-basics" in params
    p = params["algebra-basics"]
    assert 0.0 < p["P_L0"] < 1.0
    assert 0.0 < p["P_T"] < 1.0
    assert 0.0 < p["P_S"] < 0.5
    assert 0.0 < p["P_G"] < 0.5
    assert p["P_S"] + p["P_G"] < 1.0, "Identifiability constraint violated"


def test_bkt_trainer_rejects_insufficient_data():
    """KCs with fewer than min_responses should be skipped."""
    import numpy as np
    from bkt_trainer import train_bkt_parameters

    data = {
        "user_id": list(range(5)),
        "skill_name": ["rare-concept"] * 5,
        "correct": [1, 0, 1, 1, 0],
        "order": list(range(5)),
    }

    params = train_bkt_parameters(data, min_responses=500)

    assert "rare-concept" not in params


def test_bkt_export_json_schema():
    """Exported JSON must match the schema expected by .NET BktParameterProvider."""
    import json
    from bkt_export import export_parameters

    params = {
        "algebra-basics": {"P_L0": 0.12, "P_T": 0.22, "P_S": 0.04, "P_G": 0.20}
    }

    json_str = export_parameters(params, auc=0.78, coverage=45)
    parsed = json.loads(json_str)

    assert "parameters" in parsed
    assert "trained_at" in parsed
    assert "auc" in parsed
    assert parsed["auc"] == 0.78
    assert "coverage" in parsed

    p = parsed["parameters"]["algebra-basics"]
    assert set(p.keys()) == {"P_L0", "P_T", "P_S", "P_G"}
    assert all(isinstance(v, float) for v in p.values())
```
