# RDY-028: Bagrut Calibration Baseline

- **Priority**: High — validates IRT parameters against real exam data
- **Complexity**: Data scientist + psychometrician
- **Source**: Cross-review — Amjad (Curriculum), Yael (Psychometrics)
- **Tier**: 2
- **Effort**: 2-3 weeks

## Problem

IRT item parameters (difficulty, discrimination) are estimated from generated questions with no anchor to real Bagrut exam difficulty. A "hard" question in Cena may be trivially easy on the actual Bagrut, or vice versa. Without calibration against real exam data, the adaptive engine cannot predict Bagrut readiness.

## Scope

### 1. Obtain Bagrut reference data

- Source past Bagrut exam statistics from Ministry of Education (public domain pass rates by question)
- Map Cena concept IDs to Bagrut exam question numbers
- Build reference difficulty distribution per subject

### 2. Anchor item calibration

- Select 20-30 "anchor items" per subject that closely match real Bagrut questions
- Use known Bagrut pass rates to estimate anchor item difficulty
- Calibrate remaining items relative to anchors via concurrent calibration

### 3. Difficulty band validation

- Verify Cena's Easy/Medium/Hard bands align with actual Bagrut difficulty distribution
- Adjust band thresholds if misaligned
- Document calibration methodology in `docs/psychometrics/`

### 4. Predictive validity baseline

- Define metric: "Given student's Cena mastery level, can we predict Bagrut score bracket?"
- Establish baseline prediction accuracy (to be validated post-pilot)
- Document expected accuracy range and required sample size

## Files to Modify

- New: `scripts/bagrut-calibration.py` — anchor calibration script
- New: `config/bagrut-anchors.json` — anchor items with known difficulty
- New: `docs/psychometrics/calibration-methodology.md`
- `src/actors/Cena.Actors/Services/IrtCalibrationPipeline.cs` — load anchor parameters

## Acceptance Criteria

- [ ] 20-30 anchor items per subject identified and mapped to Bagrut questions
- [ ] Anchor difficulty estimated from public Bagrut pass rate data
- [ ] Remaining items calibrated relative to anchors
- [ ] Easy/Medium/Hard bands validated against Bagrut difficulty distribution
- [ ] Calibration methodology documented
- [ ] Predictive validity metric defined with baseline target
