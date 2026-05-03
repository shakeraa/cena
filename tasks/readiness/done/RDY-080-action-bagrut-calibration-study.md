# RDY-080 — Action: Cena θ → Bagrut scaled-score calibration study

- **Wave**: 0 (design now; execution blocked on real student cohort data)
- **Priority**: MED (blocks F8 point-estimate mode; RDY-071 mastery trajectory ships without it)
- **Effort**: 3 engineer-weeks design + multi-month data collection
- **Dependencies**: real student cohort who use Cena AND sit a real Bagrut; OR Ministry-published items for concordance
- **Source**: [panel review](../../docs/research/cena-panel-review-user-personas-2026-04-17.md) Round 1 + Round 2.F8 — Dr. Yael's non-negotiable

## Problem

Dr. Yael blocked F8 "predicted Bagrut score: 88 ± 5" naming: we do NOT have a calibrated mapping from Cena ability θ to Bagrut scaled score. Shipping F8 with a number is dishonest. The ONLY honest path is a proper concordance/calibration study.

## Scope

**Two calibration paths (choose one or both):**

**Path A — Longitudinal cohort**
- Enroll cohort of Cena users who plan to sit real Bagrut
- Collect θ estimates at multiple timepoints
- Collect final Bagrut score (with student+parent consent for academic-performance sharing)
- Regress Bagrut on θ with appropriate uncertainty quantification

**Path B — Common-item equating**
- License or pilot-use representative Ministry items with known parameters (a, b, c or Rasch b)
- Embed as anchor items in Cena item bank
- Calibrate Cena items against anchors
- Convert Cena θ to Ministry θ to scaled score via published Ministry conversion tables

**Statistical rigor** (Dr. Yael):
- Sample size per path to achieve mapping SE ≤ 5 points on Bagrut 0-100 scale
- Pre-register conversion model (linear, non-linear, piecewise)
- Validate out-of-sample on held-out cohort

**Output**:
- If converged: enable F8 point-estimate view with explicit SE
- If not converged: permanent block on F8 point-estimate; RDY-071 mastery trajectory stays as the final UX

## Files to Create / Modify

- `docs/psychometrics/calibration-study-design.md`
- `docs/psychometrics/calibration-consent-forms.md`
- `src/shared/Cena.Domain/Psychometrics/Calibration/ConcordanceMapping.cs` — once data exists
- `src/shared/Cena.Domain/Psychometrics/Calibration/CalibrationEvidence.cs` — audit trail for mapping version

## Acceptance Criteria

- [ ] Calibration study design doc signed off by Dr. Yael
- [ ] Sample size & power analysis included
- [ ] Consent forms for academic-performance sharing (separate from general Cena consent)
- [ ] Path A or Path B or both launched
- [ ] Mapping SE ≤ 5 on Bagrut scale OR permanent block on F8 point-estimate
- [ ] Concordance mapping version-controlled with audit trail

## Success Metrics

- **Calibration precision**: mapping SE ≤ 5 Bagrut points
- **Out-of-sample validation**: held-out cohort actual-vs-predicted within stated CI ≥ 80% of the time
- **Honest UI compliance**: when calibration is insufficient, F8 point-estimate remains blocked

## ADR Alignment

- ADR-0002: items in study are CAS-verified
- ADR-0003: calibration data governed by separate academic-consent bucket; not in general student profile
- Honest-labeling principle

## Out of Scope

- 4-unit and 3-unit calibration (5-unit first)
- Real-time θ → score display before mapping converges
- Partial-credit item calibration (dichotomous first)

## Assignee

Unassigned; Dr. Yael leads; product lead finds pilot cohort or licenses anchor items.
