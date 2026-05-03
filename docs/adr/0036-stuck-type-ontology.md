# ADR-0036: Stuck-Type Ontology

- **Date**: 2026-04-19
- **Status**: Accepted
- **Deciders**: Shaker (user, senior architect) on advice of the 10-persona panel
- **Supersedes**: Implicit single-signal "student pressed help" routing in
  the existing hint ladder (`LearningSessionActor.HintRequest`)
- **Related**: ADR-0002 (CAS oracle), ADR-0003 (misconception
  session-scope — amended by this ADR), ADR-0034 (syllabus advancement —
  the classifier reads chapter context from this aggregate), RDY-063,
  RDY-062 (paused; dependent on this classifier's pilot data)

## Context

Live in-question help in Cena was previously a **single-signal** path:
student presses "hint" → pre-authored level-based hint ladder
(`_hintGenerationService.GenerateHint(...)`). The flattening of every
kind of "stuck" into one signal was identified by the ULTRATHINK pass on
RDY-062 as the root cause of a deeper problem: you cannot decide
whether to route to a teacher or an AI when you don't even know what
kind of help the student needs.

Seven kinds of "stuck" are recognised in the educational-psychology
literature — each with a distinct optimal intervention:

| Type | Source | Optimal scaffold |
|---|---|---|
| Encoding | can't parse the question itself | Rephrase / similar-worded example |
| Recall | can't retrieve theorem/definition | Show the definition |
| Procedural | stuck on a specific step | Show next-step scaffold |
| Strategic | can't pick between known tools | Decomposition prompt |
| Misconception | confidently wrong repeated pattern | Targeted contradiction |
| Motivational | could continue, doesn't want to | Encouragement (no content) |
| MetaStuck | lost, no engagement signal | Step way back, regroup |

Evidence base:

- **Aleven & Koedinger 2001** "help abuse" taxonomy and subsequent
  corpus; help-seeking behaviour is confounded by self-regulation, and
  different stuck-types generate different action patterns that are
  detectable in the trace.
- **Koedinger et al. 2012** "Knowledge-Learning-Instruction" framework:
  knowledge-type and instruction-type must match for learning gain.
- **Pardos & Bhandari 2024** RCT (N=274) on ChatGPT-vs-human hints:
  type-matched hints outperform generic hints on next-item correctness.
- **Kestin et al. 2025** (Nature) and the UK secondary RCT 2025:
  *grounded* LLM tutors match or beat untrained human tutors — but
  "grounded" specifically requires the model to know the student's
  state, including the stuck-type.

## Decision

1. **Adopt the seven-category stuck-type ontology** as a versioned,
   ADR-locked taxonomy. Adding / removing / reordering categories is a
   breaking change requiring a superseding ADR.

2. **Classifier output is label-only.** The classifier returns the
   stuck-type + confidence + a suggested scaffolding strategy + a
   bool `ShouldInvolveTeacher` flag. It **does NOT** emit math claims,
   LaTeX, free-text hints, or scaffolded prose. CAS oracle
   (ADR-0002) remains on the scaffold-generation path, not the
   diagnosis path.

3. **Classifier input is session-scoped only.** No cross-session
   history, no profile fields, no demographics. The caller
   anonymises the student id via HMAC(studentId, sessionId, salt)
   before the context ever reaches the classifier. Persisted
   diagnoses carry the anon id; no codepath in the `Diagnosis/`
   namespace is allowed to import `StudentProfileSnapshot` or
   `AdminUser` (architecture-test enforced).

4. **Hybrid heuristic + Haiku architecture.** A deterministic
   rule-based pre-pass runs first (target: ≥40% of cases handled
   without LLM call). When heuristic confidence is below
   `HeuristicSkipLlmThreshold` (0.7 default), the Claude-Haiku-4.5
   backend classifier is invoked. Agreement escalates confidence;
   disagreement dampens it. The LLM response is JSON-only and
   parsed defensively; malformed or leaky output (math / LaTeX in
   the body) is rejected.

5. **30-day retention on the persisted diagnosis.** ADR-0003 is
   amended to explicitly allow session-scoped stuck-diagnosis
   labels under the same retention carve-out as misconception
   events. No persistence-format field may carry the raw student
   id — the architecture test enforces this.

## Consequences

### Positive

- **Existing hint ladder gets smarter non-invasively.** When RDY-063
  Phase 2 integration lands, the hint ladder consults the classifier
  on the first hint request per item and selects a type-matched
  scaffold. Flag-off path is byte-identical to pre-RDY-063 behaviour.
- **Teacher PD dashboards become possible.** "This classroom is 60%
  strategic-stuck this week" is an actionable curriculum signal
  (aggregate only; no per-student labels on student profile).
- **Item-quality signal.** Items with high encoding-stuck rates are
  plausibly poorly-worded; the metric surfaces in the admin
  dashboard for curriculum review.
- **RDY-062 v2 has a principled routing input.** "Should this go to
  the teacher?" becomes a function of the diagnosed type, not a
  30-second SLA gamble.

### Negative / risks

- **Taxonomy stability unproven.** Seven categories is an educated
  guess; pilot data may force a collapse (e.g., Motivational +
  MetaStuck always co-occur). Revisiting triggers a new ADR.
- **Haiku quality on non-English reasoning is weaker than Sonnet's.**
  If pilot Hebrew/Arabic cases show classification drift, we may
  need a locale-dispatch where Hebrew/Arabic go to Sonnet and
  English stays on Haiku.
- **Confidence-band calibration is manual.** Each heuristic rule
  carries a hard-coded confidence; rules fire exactly when their
  conditions match. Drift from reality will only surface via the
  `cena_stuck_diagnoses_total` + actionable/low-confidence counters
  over time — this is accepted for v1 and revisited post-pilot.

### Amendment to ADR-0003

ADR-0003 (misconception-session-scope) is amended to explicitly
include **stuck-diagnosis labels** in the session-scoped carve-out
with ≤30-day retention. Specifically:

> Session-scoped misconception events **and any derivative labels
> describing transient cognitive state during a session** (including
> but not limited to `StuckDiagnosisDocument`) are retained for
> ≤30 days, never joined to a student profile, and never flow into
> ML training. The anonymisation mechanism MUST break cross-session
> linkability (salt rotation is the enforcement lever).

## Alternatives considered

- **Single flat "I'm stuck" route** (status quo): rejected — flattens
  the signal and forces downstream code to guess.
- **Classifier emits scaffolded text directly** (no template layer):
  rejected — would violate ADR-0002 (any math in emitted text needs
  CAS verification, and we don't want the classifier on the CAS path).
- **Sonnet-only classifier**: rejected — 8× cost, insufficient
  quality delta on this narrow classification task per current
  benchmark data.
- **Pure-heuristic (no LLM)**: rejected — heuristic rules cover ~40%
  of cases; the long tail needs model judgement. Pure-heuristic is
  the fallback when LLM is unavailable.

## Review checkpoints

- After 2 weeks of pilot data: revisit taxonomy coverage (are all
  seven categories firing?), confidence calibration (actionable %?),
  and Haiku quality per locale.
- After 6 weeks: decide whether RDY-062 Phase 1 can start against
  real stuck-type distribution data.

## Links

- Implementation: [src/actors/Cena.Actors/Diagnosis/](../../src/actors/Cena.Actors/Diagnosis/)
- Tests: [src/actors/Cena.Actors.Tests/Diagnosis/](../../src/actors/Cena.Actors.Tests/Diagnosis/)
- Task: [RDY-063](../../tasks/readiness/RDY-063-stuck-type-classifier.md)
- Parent task (paused): [RDY-062](../../tasks/readiness/RDY-062-live-assistance-teacher-first-ai-fallback.md)
- Runbook: [docs/ops/runbooks/stuck-classifier-degraded.md](../ops/runbooks/stuck-classifier-degraded.md)
