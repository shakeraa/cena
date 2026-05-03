# RDY-024: BKT Parameter Calibration Per Subject (Phase A — DONE)

- **Priority**: Medium — requires pilot data
- **Complexity**: Senior engineer + data scientist
- **Source**: Expert panel audit — Nadia (Pedagogy)
- **Tier**: 3
- **Effort**: 2-3 weeks (Phase A: data infrastructure pre-pilot, Phase B: calibration post-pilot)
- **Completed**: 2026-04-14 (Phase A only)
- **Follow-up**: RDY-024b (Phase B — post-pilot calibration)

> **Cross-review (Nadia)**: Split into two independent phases. Phase A (data collection infrastructure) can start now. Phase B (parameter estimation + A/B testing) requires pilot data and runs post-pilot.

## Problem

All BKT parameters are defaults (P_Learning=0.10, P_Slip=0.05, P_Guess=0.20, P_Forget=0.02, P_Initial=0.10). No empirical calibration per subject domain. The 0.02 forget rate is noted as non-standard with no research backing.

## Phase A Deliverables (Done)

### Config-driven BKT parameters
- `config/bkt-params.json` — version-controlled defaults per subject (algebra, geometry, trigonometry, calculus, statistics) with `BktCalibration` section and `FeatureFlags:BktCalibratedParams` gate
- `BktCalibrationOptions.cs` — `IOptions<T>` binding with `SubjectBktParams.ToBktParameters()` conversion
- `IBktCalibrationProvider` / `ConfigurableBktCalibrationProvider` — feature-flag gated provider in Services namespace, returns `Services.BktParameters` (the double-precision production type)
- `DefaultBktCalibrationProvider` — fallback returning `BktParameters.Default`

### Feature flag
- `bkt.calibrated_params` registered as default flag (disabled) in `FeatureFlagActor`
- Static config gate via `FeatureFlags:BktCalibratedParams` for service-level DI (allocation-free hot path)

### Production wiring
- `StudentActor` constructor accepts `IBktCalibrationProvider`; inline BKT fallback in `StudentActor.Commands.cs` uses `_bktCalibrationProvider.GetParameters(conceptId)` instead of `BktParameters.Default`
- `Program.cs` loads `config/bkt-params.json`, registers options + provider

### Calibration pipeline
- `scripts/bkt-calibration.py` — three commands: `export` (PG → CSV), `calibrate` (EM per subject → JSON), `validate` (accuracy comparison)

### Data collection
- Already robust: `ConceptAttempted_V3` events log StudentId, ConceptId, SessionId, IsCorrect, ResponseTimeMs, HintCountUsed, Duration, Timestamp, EnrollmentId
- Export script queries Marten event store directly

### Tests
- 9 new tests in `ConfigurableBktCalibrationProviderTests` — flag off/on, per-subject, case-insensitive, fallback, options binding
- Existing BktServiceTests (16) and StudentActorPersistTimeoutTests (3) still pass

## Acceptance Criteria

- [x] BKT parameters loadable from config (not hardcoded)
- [x] Pilot data exported in analysis-ready format
- [x] Calibration script produces per-subject parameters from pilot data
- [x] A/B feature flag for calibrated vs. default params
- [x] Parameters version-controlled in `config/bkt-params.json`
- [ ] Post-pilot: calibrated parameters show improved mastery prediction accuracy → **RDY-024b**
