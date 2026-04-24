# Cena θ → Bagrut scaled-score calibration study design (DRAFT)

> **🚨 DRAFT — Dr. Yael (psychometrics lead) + product + legal review required 🚨**
>
> Engineering first-pass. Not yet a study protocol. No data has been
> collected. F8 point-estimate display remains **blocked** until this
> study converges.

- **Task**: RDY-080
- **Source**: [panel-review](../research/cena-panel-review-user-personas-2026-04-17.md) Round 2.F8 + Round 4 Item 2 (Dr. Yael's non-negotiable)
- **Status**: DRAFT — awaiting Dr. Yael sign-off
- **Related ADRs**: ADR-0002 (CAS oracle), ADR-0003 (data scope),
  ADR-0032 (IRT calibration), ADR-0037 (affective-signal boundary)

## 1. Why this study

Cena runs a 2-parameter-logistic IRT model (ADR-0032) producing a student
ability estimate θ on a z-scored scale (typical range −3 .. +3). The
panel-review synthesis proposes feature F8 "predicted Bagrut score:
88 ± 5". Dr. Yael's veto is authoritative: we **do not** have a
calibrated mapping from Cena θ to the Ministry's 0–100 Bagrut scaled
score. Shipping a number without one is a dishonest-labelling violation.

The two recognised paths to a defensible mapping are:

| Path | What it requires | Tradeoff |
|---|---|---|
| **A. Longitudinal cohort** | Cena users who sit a real Bagrut; their actual scaled score + timestamped θ | Requires real student cohort + academic-performance consent + months of latency |
| **B. Common-item equating** | Ministry items with known IRT params embedded as anchors in Cena | Requires Ministry licensing or Ministry-published parameter data |

This document designs both paths. Execution is gated on cohort
availability (Path A) or licensing (Path B). Both may run in parallel.

## 2. Non-negotiable constraints

Lifted verbatim from Dr. Yael's Round 4 cross-exam:

1. **Mapping SE ≤ 5 Bagrut points** on the 0–100 scale, or F8
   point-estimate remains permanently blocked.
2. **Pre-registered conversion model** — linear, piecewise, or
   non-linear declared *before* data arrives; no model-shopping post-hoc.
3. **Out-of-sample validation** — held-out cohort actual-vs-predicted
   coverage ≥ 80% within stated confidence interval.
4. **Versioned mapping** — every change to the mapping coefficients
   carries a new version tag with audit trail; historical predictions
   remain explainable by the version that produced them.
5. **No retro re-scoring** — students whose θ was mapped by v1 are not
   silently re-scored by v2; the UI shows "last mapped on <date> with
   mapping v<n>".

## 3. Path A — Longitudinal cohort

### 3.1 Cohort definition

- **Population**: Cena users aged 16–18, 5-unit math track (first
  pass; 4-unit as follow-up if Path A generalises).
- **Enrolment window**: at least 6 months before a real Bagrut sitting.
- **Consent bucket**: academic-performance-sharing (separate from
  general Cena consent; see `calibration-consent-forms.md`).
- **Target N**:
  - Calibration sample: n ≥ 300
  - Held-out validation sample: n ≥ 100
  - Power analysis: see §3.4.

### 3.2 Data collection

| When | What | Source |
|---|---|---|
| T-26 weeks (enrolment) | Baseline θ, prior math background, declared sitting date | Cena |
| T-0 (exam day) | Final θ from most recent session | Cena |
| T+0 (result release) | Actual Bagrut scaled score (0–100, pre-bonus) | Student / parent self-report, verified with Ministry transcript where possible |
| T+12 weeks | Follow-up survey on perceived usefulness | Cena |

**Privacy**: θ trajectory is already retained per ADR-0032 at 30-day
granularity. For the calibration cohort specifically, θ snapshots at
T-26, T-12, T-4, T-0 are retained under the separate academic-consent
bucket for 24 months. Bagrut score is retained under the same bucket
for 24 months. Both are purged at 24 months absent renewed consent.

### 3.3 Conversion model (pre-registered)

**Primary model**: linear regression of Bagrut scaled score on θ at T-0.
```
Bagrut = β₀ + β₁ · θ + ε,    ε ~ N(0, σ²)
```

**Justification**: IRT θ is a monotone-increasing latent measure of
ability; Bagrut scaled score is a monotone transform of Ministry
raw score. Absent strong evidence of non-linearity the simplest
monotone transform is linear. Linear preserves interpretability
("+1 Cena SD ≈ +X Bagrut points") and avoids overfitting with the
~300-student calibration sample.

**Pre-registered tests for model adequacy** (run before accepting
the linear mapping):
- Q-Q of residuals on held-out cohort (Shapiro-Wilk p > 0.05)
- Residuals vs θ for heteroscedasticity (Breusch-Pagan p > 0.05)
- 5-fold CV RMSE ≤ 5 points

**Fallback non-linear model** (only if linear adequacy fails):
- Piecewise-linear with a single knot, knot location pre-registered
  at median θ before fitting
- Isotonic regression as a diagnostic; not the shipping model

### 3.4 Power analysis (skeleton)

To achieve mapping SE ≤ 5 points with 80% power at α = 0.05:

Under the linear model, mapping SE at a given θ is:
```
SE(Bagrut_hat | θ) = σ · √(1/n + (θ - θ̄)² / Σ(θᵢ - θ̄)²)
```

At the population mean θ̄, SE collapses to σ/√n. Assuming
σ ≈ 10 Bagrut points (typical for IRT→score linking where the test has
similar content), we need:
```
σ/√n ≤ 5   ⟹   n ≥ (σ/5)² = (10/5)² = 4         (at mean)
n ≥ 100 at the tails (|θ − θ̄| ≈ 2σ)
```

**Conclusion**: target n = 300 calibration + 100 validation gives
mapping SE ≤ 5 points across the interior of the θ range and ≤ 7
points at the tails. If σ in the pilot run exceeds 12, re-assess.

### 3.5 Out-of-sample validation

After fitting on the calibration cohort, compute for the held-out 100:
- Actual Bagrut in 68% CI around predicted: target ≥ 68% ± 5%
- Actual Bagrut in 95% CI: target ≥ 95% ± 3%
- Mean absolute error ≤ 6 points

If validation fails any of these, the mapping is **rejected** and
F8 point-estimate stays blocked until the next iteration (more data
or better model).

## 4. Path B — Common-item equating

### 4.1 Premise

Ministry publishes item-level statistics on a subset of past Bagrut items
(difficulty b, discrimination a per 2PL or Rasch b per 1PL).
Alternatively, Ministry-approved licensing of anchor items is possible
via Prof. Amjad's channel.

### 4.2 Anchor-item requirements

- ≥ 20 anchor items spanning the 5-unit Bagrut difficulty range
- Each anchor has Ministry-published or Ministry-licensed IRT params
- Anchor items are CAS-verifiable (ADR-0002) — embedded as regular Cena
  questions; students do not know they are anchors
- Anchors are not re-used across Cena ability re-calibrations (rotate
  out + replace to prevent memorisation leakage)

### 4.3 Calibration step

1. Cena students attempt anchors + regular items in mixed sessions
2. Joint calibration via concurrent 2PL fit using the `mirt` R package
   or equivalent — Ministry params fixed, Cena item params free
3. Cena θ scale is anchored to Ministry θ scale via the common items
4. Ministry θ → scaled-score conversion is a Ministry-published table
5. Pipeline: Cena answer stream → Cena θ → Ministry θ → Ministry
   scaled score

### 4.4 Failure modes

- **Ministry refuses to license**: Path B not available; Path A becomes
  the only option.
- **Anchor items leak between students** (Cena is small enough that
  high-performer social networks could memorise): rotate anchors every
  3 months; flag impossible score jumps.
- **Ministry re-calibrates anchors**: whenever Ministry publishes new
  anchor params, re-fit Cena item parameters against the new anchors
  and bump mapping version.

## 5. Mapping version audit trail

Every mapping computed lands in `ConcordanceMapping` rows (see
`src/shared/Cena.Domain/Psychometrics/Calibration/`) with:

- `Version`: monotonic integer, never re-used
- `ModelKind`: `LinearV1` | `PiecewiseV1` | `CommonItemEquatingV1`
- `CoefficientsJson`: model params, JSON-serialised
- `TrainingCohortSize`, `ValidationCohortSize`
- `TrainingCohortHash`: hash of the ordered student-anon-IDs used
  to fit; allows "was this student in the training set?" check without
  storing the list
- `HeldOutRmse`, `HeldOutMae`, `HeldOutCoverage68`, `HeldOutCoverage95`
- `AdequacyTestsJson`: Q-Q, heteroscedasticity, CV-RMSE results
- `ApprovedBy`: Dr. Yael (required sign-off)
- `ApprovedAtUtc`, `SupersededAtUtc`
- `F8PointEstimateEnabled`: bool — whether this mapping version
  clears the adequacy bar to enable F8 point-estimate UI

A prediction event (`BagrutPredictionComputedV1`) references the mapping
version that produced it. UI surfaces "predicted by mapping v3
on 2026-09-15, SE 4.8".

## 6. Research-ethics posture

- Consent is separate from general Cena consent (see `calibration-consent-forms.md`)
- IRB-equivalent review: Cena engages external psychometric advisory;
  protocol approved before enrolment
- Students may withdraw at any point; their calibration data is
  deleted within 30 days of withdrawal request
- Under-18 participation requires verifiable parental consent; under-13
  excluded from Path A (COPPA + Israel PPL Amendment 2024 § 17)
- Bagrut score is high-sensitivity academic data; at rest encrypted,
  in transit TLS 1.2+, access audited via `AuditEventDocument`

## 7. What happens while the study runs

**F8 point-estimate view**: blocked.
**F8 trajectory view (RDY-071 output)**: ships without a point score.
Students see their θ-trajectory in bands ("on track for a 5-unit
pass", "strong 5-unit trajectory") with the band thresholds set by
expert judgement from Dr. Yael + Prof. Amjad, not by calibration.

When Path A or Path B converges + validates + Dr. Yael approves a
mapping version: the admin console's F8 point-estimate toggle flips
from OFF (hard-coded) to ON (config-driven).

## 8. Timeline (indicative, pending cohort availability)

| Milestone | Target | Path A | Path B |
|---|---|---|---|
| Design sign-off | +2 weeks | Dr. Yael + legal + DPO | — |
| Consent forms approved | +4 weeks | Legal + DPO | — |
| Cohort enrolment opens | +6 weeks | Cena launches consent flow | Anchor items licensed |
| Baseline θ snapshot | +8 weeks | First enrolments hit T-26w | — |
| Calibration data complete | +8 months | Bagrut scores received | Anchor data collected |
| Model fit + validation | +9 months | Regression + OOS | Joint IRT fit |
| Dr. Yael approval | +10 months | Per §3.5 criteria | Per §4.3 |
| F8 point-estimate ships | +10.5 months | If approved | If approved |

## 9. Open questions (for Dr. Yael)

1. Is linear the right primary model, or should we pre-register
   piecewise with the knot at θ = 0 from the start? Ministry's own
   concordance tables are often non-linear at the extremes.
2. What σ is realistic for Cena-θ → Bagrut on the 5-unit track? Our
   assumed σ = 10 is a literature estimate; a prior-pilot estimate
   would tighten the power analysis.
3. Should we split the mapping by topic cluster (algebra vs calculus
   etc.), or enforce a single global mapping? A split mapping is
   more faithful but needs ~5× the sample.
4. Is it acceptable to use self-reported Bagrut scores (with parent
   co-report) when Ministry transcript is unavailable? How do we
   estimate the reporting-error σ to fold in?

## 10. What counsel + DPO must fill in

- [ ] Academic-performance-sharing consent copy (Arabic + Hebrew)
- [ ] Israel PPL Amendment 2024 applicability — academic data is
      special-category under § 17; explicit consent form language
- [ ] Data-processing agreement with any external psychometric advisor
- [ ] Retention variance vs the 24-month proposal (legal may prefer
      shorter or longer for audit purposes)

## References

- ADR-0002 (CAS oracle): `docs/adr/0002-sympy-correctness-oracle.md`
- ADR-0032 (IRT model): `docs/adr/0032-irt-2pl-calibration.md`
- ADR-0003 (data scope): `docs/adr/0003-misconception-session-scope.md`
- Panel review: `docs/research/cena-panel-review-user-personas-2026-04-17.md`
- Consent forms: `docs/psychometrics/calibration-consent-forms.md`
- Scaffolding: `src/shared/Cena.Domain/Psychometrics/Calibration/`

---
**Status**: DRAFT — design pending Dr. Yael + legal + DPO sign-off.
**Last touched**: 2026-04-19 (engineering draft)
