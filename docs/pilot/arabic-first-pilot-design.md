# Arabic-first pilot design (DRAFT)

> **🚨 DRAFT — DPO + Prof. Amjad + Dr. Nadia + legal sign-off required
> before enrolment 🚨**
>
> Engineering + product first-pass. No enrolment happens until every
> §11 sign-off box is checked.

- **Task**: RDY-079
- **Source**: [panel-review](../research/cena-panel-review-user-personas-2026-04-17.md) Round 4 Item 3 (Dr. Rami's cross-exam)
- **Wave**: 0 (blocks RDY-068 F2 Arabic-first UX production rollout)
- **Status**: DRAFT — protocol pending ethics review

## 1. Why a pilot, not a launch

The panel-review synthesis proposes Arabic-first UX in 2–3 northern
schools. Dr. Rami's adversarial review flagged three gaps:

1. **No consent flow** — what does a student/parent agree to?
2. **No exit strategy** — what happens if the Arabic cohort
   *underperforms* the Hebrew cohort?
3. **No baseline comparison** — without one, any observed effect is
   uninterpretable.

Without answers, rolling out would be piloting on children without a
control. This document answers all three.

## 2. Scope

- **Population**: 2–3 Israeli Arab schools, northern region,
  Arabic-instruction, 4-unit or 5-unit math tracks.
- **Size**: 60–150 students total (20–50 per school, one or two
  classes per school).
- **Duration**: 12 weeks.
- **Treatment**: Arabic-first Cena UX (RDY-068 build), lexicon
  aligned to Palestinian-Arab curriculum conventions per Prof. Amjad.
- **Control**: cohort-matched within-school (see §4).

## 3. Consent flow

### 3.1 Bucket

Separate consent bucket from general Cena platform consent:
**`pilot-participation-arabic-2026`**.

### 3.2 Consent-holder mapping

| Student age | Who signs |
|---|---|
| 18+ | Student directly |
| 16–17 | Parent/guardian + student assent |
| Under 16 | Parent/guardian + student assent (age-appropriate explanation) |
| Under 13 | Excluded from pilot (heightened-protection cohort) |

### 3.3 Multi-party consent

For each enrolled minor, three parties must agree:
1. **Parent / guardian** — signs dual-language form (§ 5)
2. **Student** — signs assent form (age-appropriate copy, §§6)
3. **School** (InstructorLed track) — principal or teacher co-signs
   that Cena is a permitted supplement to classroom instruction
   (classroom-consumer split per ADR-0001 tenancy model)

All three granted → student enrolled. Any withheld → student excluded
from pilot but retains normal Cena access (if they already had it).

### 3.4 Forms live under

- `docs/pilot/consent-forms/ar/student-and-parent.md` — Arabic
  (Levantine, reviewable by a native speaker)
- `docs/pilot/consent-forms/he/student-and-parent.md` — Hebrew
- `docs/pilot/consent-forms/en/student-and-parent.md` — English
  reference copy

## 4. Baseline comparison

### 4.1 Primary design — within-school matched cohort

For each participating class:
- Identify N_participants students whose parents consent
- Identify N_matched non-participants — matched on: prior math grade
  (latest school transcript), Bagrut practice-exam score if any, and
  stated home language (Arabic / Hebrew / mixed)
- Matched controls continue with existing school instruction only
- Both groups take the same pre-test + post-test (§ 4.3)

### 4.2 Fallback — cross-school matched cohort

If a school rejects intra-school randomisation or control assignment:
- Pair schools with similar baseline performance + demographics
- One school receives Cena, the other continues existing instruction
- Statistical inference uses the pair as the unit (small-N, matched-pair t-test)

### 4.3 Baseline instrument

- **Math skills inventory**: 25 items, 5-unit aligned, drawn from
  public Ministry practice items (reference-only per
  memory:bagrut_reference_only; items are Cena-authored recreations,
  CAS-verified)
- **Self-reported confidence**: 1–5 Likert on each of 5 topic
  clusters
- **Prior platform use**: has the student used any adaptive learning
  tool before? Duration?
- **Demographics**: age, grade, home language(s), declared
  Bagrut-track

Administered in the 2 weeks pre-pilot. Same instrument re-administered
post-pilot for the delta.

Full baseline spec: `docs/pilot/baseline-instrument.md`.

## 5. Exit criteria (Rami's demand)

These are **hard, quantitative, pre-registered**. If any trips, the
pilot pauses (or stops). No post-hoc relaxation.

### 5.1 Trip 1 — Underperformance

At week 6 midpoint, compare mastery gain between Arabic-Cena cohort
and matched controls:
- **If Cena cohort trails control by ≥ 0.3 SD in mastery gain**:
  **pause** pilot, convene review (Dr. Yael, Dr. Nadia, Prof. Amjad)
  within 5 business days
- **If differential persists or worsens at week 8**: **stop** pilot;
  revert students to existing instruction

### 5.2 Trip 2 — Distress

Weekly in-platform affective self-check (1 question, 3-point scale).
- **If aggregate reported distress > baseline + 1 SD for two
  consecutive weeks**: **pause** immediately, investigate root cause
- **If distress tied to a specific product surface**: roll back that
  surface

### 5.3 Trip 3 — Ministry / school concern

- **If a principal, teacher, or Ministry representative formally
  objects** (in writing, any reason): **pause** at that school;
  convene with them within 5 business days; resume only with written
  agreement

### 5.4 Trip 4 — Engagement collapse

- **If < 30% of enrolled students log in at least once per week by
  week 4**: **pause**, investigate fit. Low engagement may mean the
  tool isn't useful, or the onboarding failed — either way, not
  something to continue piloting.

### 5.5 Remediation on pause / stop

- Students continue unimpeded access to existing school instruction
- Cena offers continued one-on-one tutoring access for 8 weeks
  post-stop (goodwill; not conditional on anything)
- Data collected up to the stop point is reported honestly in the
  internal summary (negative findings especially); no silent
  disappearance

## 6. Statistical analysis plan (pre-registered)

### 6.1 Primary outcome

**Mastery gain on 5-unit topic inventory (post − pre)**, on a 0–25
scale. Compared between treatment and control groups via
analysis-of-covariance (ANCOVA) with pre-score as covariate.

- **Null hypothesis H₀**: mean mastery gain is equal across groups
- **Alternative H₁**: Cena cohort mastery gain > control by ≥ 0.2 SD
  (effect size of educational interest, per Cohen's guidelines)

### 6.2 Secondary outcomes

- Self-reported confidence delta (Likert pre vs post)
- Self-reported Arabic-language-UX preference vs Hebrew (within-treatment only)
- Session completion rate (sessions completed ÷ sessions started)
- Attrition (target < 20% over 12 weeks)

### 6.3 Multiple testing

Pre-registered: one primary + four secondary outcomes = 5 tests.
Bonferroni correction to α = 0.05 / 5 = 0.01 for each secondary
outcome. Primary uses α = 0.05.

### 6.4 Sample size

For ANCOVA with moderate pre-post correlation (r = 0.5), detecting an
effect size of d = 0.3 at 80% power with α = 0.05:
- **n = 176 total** (88 per group)
- Accommodates up to 20% attrition (target 148 after attrition)

Sits inside the 60–150 scope; with 3 schools averaging 40 enrolments
and 50/50 split, we're at n = 60 per group → powered for d ≈ 0.4
(slightly above the educationally-interesting threshold). Document
this limit explicitly in the final report.

### 6.5 Intention-to-treat

Every enrolled student is included in the primary analysis under
their original assignment. Non-completers' final scores are
imputed using last-observation-carried-forward for the pre-test +
session-level imputation for the post.

## 7. Data governance

- **Retention**: all pilot data capped at 12 months post-pilot-end,
  after which aggregated only (no individual traces). Cohort-IDs
  replaced by an HMAC on export.
- **Access**: pilot-team members only (named list in MOU). No
  Anthropic access to pilot data beyond what general processor
  consent covers (cf. `docs/legal/dpa-anthropic-draft.md` §7 — no ML
  training).
- **Sharing**: results shared internally (intra-Cena + Prof. Amjad +
  consenting schools) at pilot-end; publication only with DPO +
  school MOU sign-off.
- **DSR**: students and parents may request deletion of pilot-specific
  data at any time; fulfilled within 30 days per RDY-005 §7.

## 8. Pedagogy review gate (before enrolment)

- **Prof. Amjad**: reviews Arabic lexicon + item bank + rationale
  copy for curricular alignment
- **Dr. Nadia**: reviews pedagogy framing + consent forms for
  age-appropriate language
- **DPO**: reviews consent forms + data-flow diagram + retention
  schedule
- **Legal counsel**: reviews MOU + consent forms for Israel PPL +
  UK-GDPR applicability

All four reviews must pass before enrolment opens.

## 9. Institute MOU

Each participating school signs a memorandum of understanding
covering:
- Consent that Cena is a permitted classroom supplement
- School's data-sharing authority (transcript grades for baseline
  matching)
- Opt-out clause — school may withdraw at any time without penalty
- Principal signs as InstructorLed tenant administrator (per
  ADR-0001 tenancy model)

Template: `docs/pilot/institute-mou-template.md` (engineering-TBD;
legal owns the final text).

## 10. Roles

| Role | Owner | Responsibility |
|---|---|---|
| **Study design** | Dr. Rami (honesty) + Dr. Yael (statistics) + Dr. Nadia (pedagogy) | This doc's §§4–6 sign-off |
| **Lexicon + items** | Prof. Amjad | Arabic curriculum fidelity |
| **DPO** | TBD (FIND-privacy-014) | Consent + retention + DSR |
| **Legal** | TBD | Israel PPL / UK-GDPR compliance |
| **Product lead** | TBD | School recruitment + logistics |
| **Engineering** | claude-code (this task) + Kimi (F2 build) | Tooling, instrumentation |
| **Exit review board** | Dr. Yael + Dr. Nadia + Prof. Amjad | If any §5 trip fires |

## 11. Sign-off checklist

Enrolment MUST NOT begin until every box is checked:

- [ ] Prof. Amjad: lexicon + items + rationale copy signed off
- [ ] Dr. Nadia: pedagogy + age-appropriate consent signed off
- [ ] DPO: consent forms + data flow + retention signed off
- [ ] Legal: Israeli PPL + UK-GDPR compliance signed off
- [ ] Dr. Yael: statistical analysis plan signed off
- [ ] Dr. Rami: adversarial review passes (no unanswered Round-4 items)
- [ ] School MOUs signed by 2+ schools
- [ ] Baseline instrument pre-tested (dry run, 10 students, debrief)
- [ ] Cena F2 build frozen at a release tag (RDY-068)
- [ ] Exit-criteria tripwires wired in the admin dashboard (alerts on
      weekly mastery-gain differential + distress metric)

## 12. Out of scope

- Nationwide rollout — pilot only
- Hebrew-only matched cohort from the same schools as "pure control"
  — pilot compares Arabic-Cena vs existing-instruction, not vs
  Hebrew-Cena
- Other subjects beyond math — Arabic-first math is the first pilot
- Longer-than-12-week observation — follow-up is a separate study

## 13. Open questions

1. **Matched control vs randomised**: can we get principal buy-in for
   randomised assignment within a class, or is that politically
   untenable and we must use matched non-participants?
2. **Transcript access**: how does a school share student grades with
   Cena for baseline matching without FERPA-equivalent violations?
   Likely needs parent co-consent.
3. **Levantine vs MSA**: the Arabic UX is MSA per RDY-068, but
   student onboarding copy may feel stilted in MSA. Do we have a
   "Levantine-friendly" variant for welcome screens only?
4. **Ministry notification**: do we notify the Ministry of Education
   before the pilot? Legal to confirm.

## References

- Baseline: `docs/pilot/baseline-instrument.md`
- Exit criteria: `docs/pilot/exit-criteria.md`
- Consent forms (draft): `docs/pilot/consent-forms/*/student-and-parent.md`
- ADR-0001 (tenancy): `docs/adr/0001-multi-institute-tenancy.md`
- ADR-0003 (data scope): `docs/adr/0003-misconception-session-scope.md`
- Parental consent policy: `docs/compliance/parental-consent.md`
- Panel review: `docs/research/cena-panel-review-user-personas-2026-04-17.md`

---
**Status**: DRAFT — awaiting the §11 sign-offs.
**Last touched**: 2026-04-19 (engineering draft)
