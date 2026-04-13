# RDY-024: BKT Parameter Calibration Per Subject

- **Priority**: Medium — requires pilot data
- **Complexity**: Senior engineer + data scientist
- **Source**: Expert panel audit — Nadia (Pedagogy)
- **Tier**: 3
- **Effort**: 2-3 weeks (Phase A: data infrastructure pre-pilot, Phase B: calibration post-pilot)

> **Cross-review (Nadia)**: Split into two independent phases. Phase A (data collection infrastructure) can start now. Phase B (parameter estimation + A/B testing) requires pilot data and runs post-pilot.

## Problem

All BKT parameters are defaults (P_Learning=0.10, P_Slip=0.05, P_Guess=0.20, P_Forget=0.02, P_Initial=0.10). No empirical calibration per subject domain. The 0.02 forget rate is noted as non-standard with no research backing.

## Scope

### 1. Data collection during pilot

- Log all concept attempts with: student_id, concept_id, subject, correct/incorrect, hints_used, response_time, session_number
- Export to analysis-ready format (CSV or Parquet)
- Target: 200+ attempts per concept for stable estimates

### 2. Parameter estimation

Using pilot data, estimate per-subject BKT parameters via EM algorithm:
- P_Learning per subject (algebra may have higher learn rate than geometry)
- P_Slip per subject
- P_Guess per subject
- P_Forget per subject (validated against Ebbinghaus curve)

### 3. A/B testing infrastructure

- Feature flag: `BKT_USE_CALIBRATED_PARAMS` (default: false during pilot)
- When enabled, load calibrated parameters from config
- Compare student outcomes: default params vs. calibrated params

### 4. Parameter update pipeline

- Script that takes pilot data → estimates parameters → outputs config
- Runs as scheduled job after each pilot phase
- Parameters version-controlled in `config/bkt-params.json`

## Files to Modify

- `src/actors/Cena.Actors/Services/BktService.cs` — load params from config (not hardcoded)
- New: `scripts/bkt-calibration.py` — EM estimation from pilot data
- New: `config/bkt-params.json` — calibrated parameters per subject
- `src/actors/Cena.Actors.Host/Program.cs` — feature flag for calibrated params

## Acceptance Criteria

- [ ] BKT parameters loadable from config (not hardcoded)
- [ ] Pilot data exported in analysis-ready format
- [ ] Calibration script produces per-subject parameters from pilot data
- [ ] A/B feature flag for calibrated vs. default params
- [ ] Parameters version-controlled in `config/bkt-params.json`
- [ ] Post-pilot: calibrated parameters show improved mastery prediction accuracy
