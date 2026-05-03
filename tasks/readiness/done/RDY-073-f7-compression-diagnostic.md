# RDY-073 — F7: Compression diagnostic with adaptive scheduler

- **Wave**: C
- **Priority**: MED (premium-willing-to-pay wedge)
- **Effort**: 3-4 engineer-weeks
- **Dependencies**: RDY-071 mastery trajectory + ability estimate; existing BKT infra
- **Source**: [panel review](../../docs/research/cena-panel-review-user-personas-2026-04-17.md) Round 2.F7

## Problem

Daniel (post-mechina, 14 weeks to moed chaf) and Noa (5-unit ambitious, time-scarce) both need weakness-targeted study compression, not full-syllabus retreads. Hebrew incumbents sell ₪1500+ full-syllabus packages. The compression wedge = target each student's weakness × topic weight × time available.

## Scope

**Diagnostic run** (40 min, Dr. Yael's realistic estimate):
- Maximum-information item selection (MIS) over topic tree
- Exposure control (Sympson-Hetter style)
- Outputs θ per topic ± SE
- Cold-start students: starting prior from syllabus-weighted average

**Adaptive scheduler**:
- Input: topic-level θ, student's deadline, student's weekly time budget
- Output: N-week plan prioritizing (weakness × topic-weight-on-Bagrut × prerequisite position)
- Weekly re-adaptation based on mastery gains

**Motivation-safe framing** (Dr. Nadia's demand):
- For Daniel-type (confident learner): "60% already mastered, 40% to drill"
- For Yael-type (anxious learner): opt-in only, NEVER auto-launched; alternate framing "here's where we'll start"
- Default for all: celebrate strengths before surfacing weaknesses

**Topic weight** (Rami's demand — not handwaved):
- Publish `BagrutTopicWeights.cs` with weightings sourced from Ministry-published exam-weight tables where available; flag expert-judgment estimates otherwise

## Files to Create / Modify

- `src/shared/Cena.Domain/StudyPlan/StudyPlanAggregate.cs` — event-sourced
- `src/shared/Cena.Domain/StudyPlan/AdaptiveScheduler.cs`
- `src/shared/Cena.Domain/Diagnostics/DiagnosticRun.cs`
- `src/shared/Cena.Domain/Syllabus/BagrutTopicWeights.cs` — source-cited weights
- `src/student/full-version/src/views/diagnostic/DiagnosticRun.vue`
- `src/student/full-version/src/views/plan/CompressedPlan.vue`
- `docs/design/compression-diagnostic-design.md`

## Acceptance Criteria

- [ ] Diagnostic completes in ≤ 40 min with ≤ 30 items for most students
- [ ] SE(θ) per topic reaches ±0.3 for >70% of students within 40 min
- [ ] Scheduler outputs weekly plan with rationale per topic ("targeting because weak AND high-weight AND prerequisite for X")
- [ ] Motivation-safe framing verified via user research with 1 Yael-like and 1 Daniel-like persona
- [ ] Topic weights cited in source code comments (Ministry ref or expert-judgment flag)
- [ ] Weekly re-adaptation updates plan after sufficient answer evidence

## Success Metrics

- **Time-to-first-mastery-gain**: target < 2 hours of plan execution
- **14-week plan adherence**: target ≥ 60% of scheduled sessions completed (Daniel-type cohort)
- **Grade delta** (compared to non-compressed baseline): target measurable uplift for moed chaf retakers
- **Motivation survey** (pre vs post-diagnostic): target net positive for anxious learners when opt-in

## ADR Alignment

- ADR-0002: all items CAS-verified
- ADR-0003: diagnostic results session-scoped + current plan; no long-term diagnostic-performance profile
- GD-004: no timer pressure during diagnostic

## Out of Scope

- 4-unit and 3-unit compression (5-unit first, Noa + Daniel cohort)
- In-session rescheduling (weekly cadence is enough for v1)
- Auto-launch diagnostic (must be opt-in due to anxiety concern)

## Assignee

Unassigned; Dr. Nadia + Dr. Yael co-lead design; backend coder for StudyPlan aggregate.
