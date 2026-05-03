# Compression Diagnostic + Adaptive Scheduler — Design (Phase 1A DRAFT)

> **Status**: Phase 1A design locked for the domain foundation.
> Phase 1B (full week packer + Vue views) + Phase 1C (motivation-safe
> UX validation with Yael/Daniel personas) land in separate tasks.

- **Task**: RDY-073 F7
- **Related**: RDY-071 (mastery trajectory — ships the AbilityEstimate
  this consumes), RDY-080 (calibration — unlocks the F8 score view
  the scheduler never emits itself), RDY-057 (MotivationProfile
  source of truth for anxious vs confident framing)
- **Panel-review source**: Round 2.F7 + Round 4 Items 2, 3

## 1. Why this exists

Two panel personas motivate the compression wedge:

- **Daniel** (post-mechina, 14 weeks to moed chaf): cannot afford a
  full-syllabus retread. Needs weakness × topic-weight × time
  targeting.
- **Noa** (5-unit ambitious, time-scarce): has the ability; wants the
  ROI-per-hour question answered honestly.

Hebrew incumbents (Geva, GOOL) sell ₪1500+ full-syllabus packages.
Cena's wedge is NOT "cheaper full syllabus" — it is "targeted
compression that cites why each topic is on your plan."

## 2. Phase 1A scope (this commit)

### 2.1 Domain

- `BagrutTopicWeights.cs` — typed 5-unit catalogue with
  `WeightSource` (MinistryPublished / HistoricalAnalysis /
  ExpertJudgment) + free-text citation per row. CI enforces the
  invariant that all 5-unit weights sum to 1.0 ± 0.001.
- `DiagnosticRun.cs` — aggregate with lifecycle state machine
  (NotStarted → InProgress → Completed / Aborted), per-item attempt
  log, per-topic checkpoint map, `HasSufficientEvidence()` gate at
  ≥ 70% of topics reaching SE(θ) ≤ 0.3 per Dr. Yael's spec.
- `AdaptiveScheduler.cs` — `PrioritizeTopics()` computes score =
  `weakness × topicWeight × prerequisiteUrgency`. Emits `PlanEntry`
  with a server-authored rationale string. Motivation-safe framing
  branches on `MotivationProfile` (Anxious / Confident / Neutral).

### 2.2 Tests (19/19 pass)

- `BagrutTopicWeightsTests.cs` — sum-to-one invariant, citation
  presence, unknown-topic returns null.
- `DiagnosticRunTests.cs` — lifecycle state transitions,
  not-in-progress record rejection, `HasSufficientEvidence` below
  threshold vs at-threshold.
- `AdaptiveSchedulerTests.cs` — ordering, unknown-topic skip,
  mastery-target zero-weakness, three-profile rationale framing,
  zero-banned-phrase invariant (cross-check vs RDY-071 gate).

### 2.3 Non-goals (for Phase 1A)

- Week-by-week packing algorithm → Phase 1B (needs per-item time
  estimates + syllabus prerequisite DAG wiring)
- Item-selection strategy (maximum-information + Sympson-Hetter) →
  Phase 1B
- Vue views (`DiagnosticRun.vue`, `CompressedPlan.vue`) → Phase 1B
- Weekly re-adaptation loop → Phase 1B
- Motivation-safe UX validation with Yael-like + Daniel-like
  personas → Phase 1C

## 3. Priority formula

For each topic T with student S's ability estimate θ_T:

```
score(T, S) = weakness(θ_T) × topicWeight(T) × prerequisiteUrgency(T)

where
  weakness(θ)                = clamp(masteryTargetTheta − θ, 0, 2)
  masteryTargetTheta         = +0.5  (top of MEDIUM bucket, RDY-071)
  topicWeight(T)             = BagrutTopicWeights.ForFiveUnit(T)?.Weight ?? 0
  prerequisiteUrgency(T)     = 1.0   (Phase 1A placeholder)
                             = real DAG-based urgency in Phase 1B
```

**Why clamped weakness**: an unbounded weakness would let a
far-below-target student's plan be dominated by the single topic
they're weakest in, blocking attainable topics that produce early
wins (motivation-safe principle). Clamp at 2 keeps the topic high
priority without crowding out the rest of the plan.

**Why zero-weight means skip, not default**: we never schedule a
topic we can't justify the weight of. Unknown topics carry no
Ministry citation, so the scheduler refuses to add them even when
the student struggles — the rationale text would have nothing to
stand on.

## 4. Motivation-safe framing (Dr. Nadia's hard requirement)

### 4.1 Profile sources

`MotivationProfile` is derived from the `OnboardingSelfAssessmentDocument`
(RDY-057) per ADR-0037:

| SelfAssessment signal | Profile |
|---|---|
| ConfidenceLikert ≥ 4 on this subject | Confident |
| Reported "anxious" on this subject's topic cluster | Anxious |
| Anything else / no assessment | Neutral |

Per ADR-0037:
- Profile is **session-scoped** on the scheduler — never persisted
  on the student profile across sessions
- Teacher dashboards show aggregate rollups only, never per-student
  profile attribution

### 4.2 Rationale templates per profile

**Anxious** (diagnostic is OPT-IN, never auto-launched):
> Here's where we'll start with {topic} — this is a foundation step
> that pays off across multiple exam question types.

No percentages. No "you're weak in X". Strengths-forward opener
before any weakness surfaces anywhere in the plan.

**Confident**:
> Targeting {topic} because your answers suggest this is a gap, and
> Ministry guidance gives it {weight:P0} of the 5-unit exam weight.

Percentage-forward, direct, respects Daniel-type's preference for
data.

**Neutral** (default):
> Focusing on {topic}: you're still building mastery here, and it
> carries {weight:P0} of the 5-unit exam weight per Ministry
> guidance.

Where the weight is `ExpertJudgment` source, an extra clause:
> (weight is an expert-judgment estimate pending Ministry
> confirmation)

## 5. Honest-framing guard (cross-check vs RDY-071)

Every rationale string emitted by the scheduler is server-authored
from the components the scheduler used to prioritise. This keeps the
plan's copy and its priority math in lockstep AND prevents a future
render path from constructing a "predicted Bagrut score" string (the
banned phrases from `docs/engineering/mastery-trajectory-honest-framing.md`
never appear in any rationale by construction — enforced by the
`Rationale_never_contains_RDY_071_banned_phrases` Theory test across
all 3 motivation profiles).

## 6. Phase 1B plan (next claim)

- Wire `SyllabusDocument.PrerequisiteChapterSlugs` into
  `prerequisiteUrgency()` so scheduling respects the DAG
- Per-item time estimates from the question-bank metadata (we
  already track this on `QuestionState`; plumb it into the plan
  entries)
- Week-by-week packing: greedy knapsack over the weekly time budget
  with the priority score as value
- `DiagnosticRun.vue` + `CompressedPlan.vue` student views
- Weekly re-adaptation: after N completed sessions, rebuild the
  plan from the updated θ estimates

## 7. Acceptance (phase 1A only; full task acceptance needs 1B/1C)

- [x] Domain types + priority formula shipped
- [x] 19/19 tests passing
- [x] Every topic weight carries a citation
- [x] Scheduler rationale strings pass RDY-071 honest-framing gate
- [x] Full task's phase 1B/1C scope documented + queued

Full acceptance deferred to Phase 1B + Phase 1C.

## 8. References

- Panel review: `docs/research/cena-panel-review-user-personas-2026-04-17.md`
  (Round 2.F7, Round 4 Items 2+3)
- ADR-0037: `docs/adr/0037-affective-signal-in-pedagogy.md`
- RDY-071 honest-framing: `docs/engineering/mastery-trajectory-honest-framing.md`
- RDY-057 self-assessment: `tasks/readiness/done/RDY-057-onboarding-self-assessment.md`
- Syllabus structure: `config/syllabi/math-bagrut-5unit.yaml`
