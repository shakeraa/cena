# RDY-024b: BKT Calibration Phase B — Post-Pilot Parameter Estimation

- **Priority**: Medium — blocked on pilot data
- **Complexity**: Data scientist + senior engineer
- **Source**: Expert panel audit — Nadia (Pedagogy)
- **Tier**: 3
- **Effort**: 1-2 weeks (after pilot data is available)
- **Depends on**: RDY-024 Phase A (done), pilot completion with 200+ attempts/concept
- **Prerequisite**: Pilot data collected via ConceptAttempted events in Marten

> **Blocked until**: Pilot completes with sufficient data volume (target: 200+ attempts per concept for stable EM estimates).

## Problem

Phase A infrastructure is in place (config loading, feature flag, calibration script, provider wiring). Phase B runs the actual EM calibration on pilot data and validates that calibrated parameters improve mastery prediction accuracy over defaults.

## Scope

### 1. Run data export
```bash
python scripts/bkt-calibration.py export \
  --pg-dsn "host=<prod> port=5432 dbname=cena user=... password=..." \
  --output data/pilot-attempts.csv
```
- Verify 200+ attempts per concept (script warns on sparse concepts)
- Add subject column resolution if not yet in ConceptAttempted events (may need concept→subject mapping from curriculum graph)

### 2. Run EM calibration per subject
```bash
python scripts/bkt-calibration.py calibrate \
  --input data/pilot-attempts.csv \
  --output config/bkt-params.json
```
- Review estimated parameters — P_Forget validated against Ebbinghaus curve
- Version bump in config (version: 2, calibratedAt timestamp)

### 3. Validate improvement
```bash
python scripts/bkt-calibration.py validate \
  --input data/pilot-attempts.csv \
  --params config/bkt-params.json
```
- Default vs. calibrated prediction accuracy per subject
- Ensure calibrated params don't regress any subject

### 4. Enable feature flag + A/B test
- Set `FeatureFlags:BktCalibratedParams: true` in config
- Use `bkt.calibrated_params` flag with rollout % in FeatureFlagActor for gradual rollout
- Compare student outcomes: default params cohort vs. calibrated params cohort
- Metrics: mastery prediction accuracy, time-to-mastery, false mastery rate

### 5. Schedule recurring calibration
- Set up `scripts/bkt-calibration.py calibrate` as a scheduled job after each pilot phase
- Parameters auto-committed to `config/bkt-params.json` via CI

## Files to Modify

- `config/bkt-params.json` — replace Phase A defaults with EM-estimated values
- `scripts/bkt-calibration.py` — may need concept→subject mapping enhancement
- Feature flag rollout via admin API or config change

## Acceptance Criteria

- [ ] EM calibration run on pilot data produces per-subject parameters
- [ ] P_Forget per subject validated against Ebbinghaus curve (not arbitrary)
- [ ] Calibrated params show improved mastery prediction accuracy vs. defaults
- [ ] A/B test confirms no regression in student outcomes
- [ ] Calibrated parameters committed and version-controlled
- [ ] Recurring calibration pipeline documented and schedulable
