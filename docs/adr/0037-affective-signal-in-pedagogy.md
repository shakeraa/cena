# ADR-0037: Affective Signal in Pedagogy — Scope, Boundaries, Use

- **Date**: 2026-04-19
- **Status**: Accepted
- **Deciders**: Shaker (user, senior architect)
- **Related**: ADR-0003 (misconception session-scope), ADR-0036 (stuck-type
  ontology), RDY-057 / RDY-057b / RDY-057c

## Context

RDY-057 added an onboarding self-assessment capturing students'
self-reported affective state: subject confidence (1-5 Likert), strengths,
friction points, per-topic feelings (solid/unsure/anxious/new), optional
free-text. RDY-057b began consuming this signal — applying a 1.15× ZPD
penalty to concepts the student flagged as anxious (LearningSessionActor
HandleNextQuestion).

This ADR locks the policy on **what affective self-signal may and may
not do** inside the pedagogy pipeline. Without an explicit boundary,
every future feature ends up debating "can we also use the self-reported
anxious flag for X?" The answer is almost always no, and the ADR makes
that default explicit.

## Decision

Affective self-signal from OnboardingSelfAssessmentDocument is permitted
to act as a **tie-breaker in item selection only**, never as a primary
decision driver, never as an input to the cognitive-mastery model, and
never as a persistent profile attribute visible to the student or
teacher at per-student granularity.

Specifically — for each axis:

### 1. Item selection (ZPD) — PERMITTED with boundaries

- A concept the student self-reported as anxious may receive a mild
  ZPD-score penalty (≤1.15× multiplier; effectively a tie-breaker).
- **Must never veto** a concept. A strong ZPD signal on an anxious
  concept still wins over a weak ZPD signal on a non-anxious concept.
- **Must log** every adjustment through a structured
  `[ANXIOUS_OPENER]` (or equivalent) line so the penalty's
  contribution to selection is auditable.

### 2. BKT mastery model — PROHIBITED

- Affective self-signal MUST NOT adjust `P_Initial`, `P_Learning`,
  `P_Slip`, `P_Guess`, or `P_Forget`. The mastery model is cognitive;
  mixing self-signal in pollutes the inference and makes mastery
  inferences non-reproducible.
- RDY-057's original acceptance criterion §3 ("BKT prior adjustment
  for subject confidence") is **explicitly deferred** by this ADR.
  If we ever reconsider, a superseding ADR documents the weighting
  policy + Ran's COPPA sign-off on using affective fields as
  adaptive input.

### 3. Cross-student / cohort use — PROHIBITED AT PER-STUDENT LEVEL

- Teacher dashboards may show aggregate roll-ups
  (`ClassroomRollupResponse` from RDY-057b) — counts, histograms,
  top-N tags — with no per-student attribution.
- Teachers must never see "Student Sara reports anxiety on algebra."
  The data sharing axis changes on that view (parent-consent boundary
  per COPPA).

### 4. ML training — PROHIBITED

- `OnboardingSelfAssessmentDocument` carries `[MlExcluded]` per
  ADR-0003 Decision 4. Derivative labels and rollups inherit the
  same exclusion. No fine-tuning corpus, no embedding training, no
  recommendation model uses these fields.

### 5. LLM tutor context — PROHIBITED

- The AI tutor's prompt context MUST NOT include the student's
  self-assessment. Reasons:
  1. Misconception-scope boundary (ADR-0003) — affective state is
     the same class of sensitive data.
  2. Prompt injection + leakage — affective fields in an LLM prompt
     are a candidate for unintended cross-student behavior change
     that CAS oracle cannot catch.
  3. Student trust — a tutor that knows "you said you're anxious"
     and references it back violates the student's expectation of
     privacy on that disclosure.

### 6. Retention

- `OnboardingSelfAssessmentDocument`: default 90-day retention unless
  the student opts in to longer persistence.
- `LearningSessionQueueProjection.AnxiousConceptIds`: session-scoped;
  deleted with the session doc.
- `ClassroomRollupResponse`: computed on-demand; never persisted.

## Consequences

### Positive

- Clear privacy contract for a sensitive data class.
- Selection tie-break is additive — the existing ZPD logic still
  dominates. Strong cognitive signal beats weak affective signal,
  which matches the pedagogical literature (self-report is noisy).
- ML + training boundary is bright-line; no debate at review time.

### Negative / risks

- **Deferring BKT `P_Initial` tiebreaker**: RDY-057 listed this as
  acceptance §3. Students entering a subject with low self-confidence
  get the same cognitive starting prior as confident peers. Evidence
  from one pilot may reconsider this; the superseding ADR path is
  open.
- **Aggregate roll-up is a weak signal**: 30-student classrooms
  produce thin histograms. Teachers may struggle to act on them
  without per-student detail (which we forbid).
- **Tutor context prohibition**: could leave affective signal
  untapped by the system's most adaptive component. Accepted trade-off
  for privacy; revisit if a pilot shows students would benefit
  explicitly from tutors knowing their self-reported state AND the
  ADR-0003 misconception-scope guarantee can be preserved.

### Amends ADR-0003

ADR-0003 (misconception-session-scope) is clarified to include:

> Affective self-signal (onboarding self-assessment, topic-feeling
> labels) is treated with the same privacy + ML-exclusion guarantees
> as misconception events. Retention ≤ 90 days on the primary document,
> session-scoped on any derivative that rides on the pedagogy hot
> path. Teacher aggregate views are permitted at classroom granularity,
> never at per-student.

## Alternatives considered

- **Mix affective signal into BKT prior** (as RDY-057 originally
  sketched) — rejected per §2 above; pollutes the mastery model.
- **Show self-assessment on the teacher's per-student view** —
  rejected per §3; changes the COPPA data-sharing axis.
- **Feed to AI tutor context** — rejected per §5; three-way problem
  (scope, injection, student trust).

## Review checkpoints

- After 2-week pilot: revisit whether the 1.15× tie-break materially
  alters selection (aggregate `[ANXIOUS_OPENER]` counts vs total
  selections).
- After 6-week pilot: reconsider the BKT `P_Initial` tiebreaker
  deferral with real data on whether self-confidence predicts actual
  mastery trajectories.

## Links

- Implementation (selection tie-break): `src/actors/Cena.Actors/Sessions/LearningSessionActor.cs`
- Data doc: `src/shared/Cena.Infrastructure/Documents/OnboardingSelfAssessmentDocument.cs`
- Session pipeline hook: `src/api/Cena.Student.Api.Host/Endpoints/SessionEndpoints.cs`
- Classroom rollup: `src/api/Cena.Admin.Api/SelfAssessmentRollup/SelfAssessmentRollupEndpoints.cs`
- ADR-0003 (amended): `docs/adr/0003-misconception-session-scope.md`
- ADR-0036 (stuck-type ontology, analogue): `docs/adr/0036-stuck-type-ontology.md`
