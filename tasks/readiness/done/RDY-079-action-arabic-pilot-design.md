# RDY-079 — Action: Arabic-first pilot design (consent, baseline, exit)

- **Wave**: 0 (design work; blocks RDY-068 production pilot)
- **Priority**: HIGH (ethical + legal prerequisite)
- **Effort**: 2 engineer-weeks + legal/DPO + pedagogy review
- **Dependencies**: DPO appointment status (tracked FIND-privacy-014); RDY-068 F2 build
- **Source**: [panel review](../../docs/research/cena-panel-review-user-personas-2026-04-17.md) Round 4 — Dr. Rami's cross-exam, Item 3

## Problem

The synthesis proposes an Arabic-first pilot in 2-3 northern schools. Dr. Rami flagged: "What is the pilot consent flow? What is the exit strategy if the pilot shows negative learning effect for the Arabic cohort? What is the baseline comparison? Without an answer, we are piloting on children without a control."

## Scope

**Deliverable**: `docs/pilot/arabic-first-pilot-design.md` — a pre-registered pilot design.

1. **Pilot scope**
   - 2-3 `School` institutes in northern Israel, Arabic-instruction, 4-unit or 5-unit math
   - ~60-150 students
   - 12-week duration
   - Pre/post assessment + weekly in-platform metrics

2. **Consent flow**
   - Dual-language consent form (Arabic + Hebrew) for parents/guardians
   - Student assent (age-appropriate)
   - DPO signoff before enrollment
   - Explicit exit-without-penalty clause

3. **Baseline comparison**
   - Cohort-matched control within the same schools (pre-existing methods)
   - OR cross-school matched comparison if intra-school randomization rejected
   - Baseline administered pre-pilot: prior Bagrut mock performance, self-reported math confidence, prior platform use

4. **Exit criteria (Rami's demand)**
   - If Arabic-cohort mastery gain trails control by ≥ 0.3 SD at week 6, pause pilot for review
   - If survey reports distress above baseline threshold, pause immediately
   - Pre-define remediation path (offer continued tutoring via Cena or revert to school methods)

5. **Pedagogy review gate**
   - Prof. Amjad reviews pilot lexicon + item bank before deployment
   - Dr. Nadia reviews pedagogy framing + consent copy

## Files to Create / Modify

- `docs/pilot/arabic-first-pilot-design.md`
- `docs/pilot/consent-forms/ar/student-and-parent.md`
- `docs/pilot/consent-forms/he/student-and-parent.md`
- `docs/pilot/baseline-instrument.md`
- `docs/pilot/exit-criteria.md`

## Acceptance Criteria

- [ ] Pilot design doc signed off by DPO, Prof. Amjad, Dr. Nadia, legal
- [ ] Consent forms in Arabic (Levantine) and Hebrew; reviewed by legal
- [ ] Baseline instrument validated (pre-pilot dry run)
- [ ] Exit criteria quantitative and pre-registered
- [ ] Institute MOUs drafted (school-controller agreements per classroom-consumer-split)

## Success Metrics

- **Pilot launches without consent irregularities**: target 100% of enrolled students have valid consent
- **Attrition rate**: target < 20% over 12 weeks
- **Learning signal detected (positive or negative)**: target a reportable result, not ambiguous

## ADR Alignment

- Parental consent per docs/compliance/parental-consent.md
- Classroom-consumer split: `School` + `InstructorLed` + teacher-delegated consent under FERPA school-official exception (adapted to Israeli PPL)
- ADR-0003: pilot data session-scoped + 90-day cap

## Out of Scope

- Nationwide rollout (pilot only)
- Hebrew-only control group (not the point; Arabic-first is the treatment)

## Assignee

Unassigned; DPO + Ran + Prof. Amjad co-own; product lead coordinates institutes.
