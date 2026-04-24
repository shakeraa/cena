# RDY-071 — F8: Mastery trajectory (renamed from "grade prediction")

- **Wave**: C (Dr. Yael blocked the "grade prediction" naming until calibration)
- **Priority**: MED
- **Effort**: 2 engineer-weeks
- **Dependencies**: RDY-080 calibration study (for eventual point-estimate mode)
- **Source**: [panel review](../../docs/research/cena-panel-review-user-personas-2026-04-17.md) Round 2.F8

## Problem

Dr. Yael blocked F8 as originally named. We do NOT have a calibrated mapping from Cena θ to Bagrut scaled score. Shipping "Predicted Bagrut: 88 ± 5" without calibration is dishonest. Rachel (product-manager mother) wants signal — she doesn't actually want the number; she wants confidence her daughter is on track.

## Scope

Ship **mastery trajectory** framing (not point-estimate grade prediction):

- **Student view**: trajectory chart of overall mastery over time, per Bagrut topic-weighted composite
- **Confidence band**: explicit "based on N problems over M weeks" caption
- **Label bucket**: "high / medium / low mastery" with 80% confidence, NOT a numeric Bagrut score
- **Parent view**: same trajectory, same buckets, same honest framing

**Calibration path**:
- When RDY-080 (concordance study) completes, unlock point-estimate view behind a flag
- Until then, any "predicted Bagrut score" surface is blocked in CI

**Honest UI framing** (Dr. Yael + Dr. Rami demand):
- "Mastery level: HIGH (80% confidence, 142 problems, 6 weeks)"
- NOT "Predicted score: 88 ± 5"
- When confidence wide: "We need more data — keep practicing for a clearer read"

## Files to Create / Modify

- `src/shared/Cena.Domain/Psychometrics/AbilityEstimate.cs` — θ + SE(θ) per topic
- `src/shared/Cena.Domain/Psychometrics/MasteryTrajectoryProjection.cs`
- `src/student/full-version/src/views/mastery/TrajectoryDashboard.vue`
- `src/admin/full-version/src/views/parent/TrajectoryParentView.vue`
- `docs/engineering/mastery-trajectory-honest-framing.md` — CI banned-phrase list
- `tests/ShipgateHonestyTests.cs` — banned-phrase CI rule

## Acceptance Criteria

- [ ] Student dashboard shows trajectory with HIGH/MED/LOW bucket + explicit sample-size caption
- [ ] Point-estimate Bagrut score display flag is OFF and CI blocks any PR that turns it on without RDY-080 evidence
- [ ] "Predicted Bagrut" / "Expected grade" / "Your score will be" phrases blocked by shipgate scanner
- [ ] Parent view mirrors student view; no extra detail that isn't in student view
- [ ] 80% confidence interval computed correctly from IRT θ posterior (Dr. Yael review)

## Success Metrics

- **False-confidence rate in user perception surveys**: students who can correctly answer "does Cena predict my Bagrut score?" (correct answer: not yet). Target >80% correct.
- **Parent-digest correlation**: parents who read the digest should match their student's bucket +/- 1 level with 90% accuracy
- **Zero shipgate violations** for predicted-score phrases in production renders

## ADR Alignment

- ADR-0002: mastery derived from CAS-verified items
- ADR-0003: trajectory = rolling mastery projection, not persistent misconception profile
- Honest-labeling principle (Dr. Rami's lens from PERSONAS.md)

## Out of Scope

- Point-estimate Bagrut score view (blocked on RDY-080)
- Cross-cohort comparison ("better than 60% of students") — banned by GD-004
- IRT re-calibration workflow (separate task)

## Assignee

Unassigned; Dr. Yael leads, coder implements projection + UI.
