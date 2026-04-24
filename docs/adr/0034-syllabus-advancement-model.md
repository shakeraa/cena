# ADR-0034: Syllabus Advancement Model

- **Date**: 2026-04-19
- **Status**: Accepted
- **Deciders**: Shaker (user, senior architect) on advice of the 10-persona panel
- **Supersedes**: Implicit grade-based gating (never formally enforced; existed in
  scattered `.Grade` string fields)
- **Related**: ADR-0002 (CAS oracle), ADR-0003 (misconception session-scope —
  amended by this ADR), RDY-061

## Context

Cena's adaptive-learning thesis requires that content be gated by what a
student has learned, not by their calendar year of school. The existing
implementation partially honoured this (`AdaptiveQuestionPool` selects
from the mastery frontier) but had three gaps:

1. No syllabus structure — tracks held flat lists of learning-objective ids
2. `Grade` was a lying label — the same field name held calendar year in
   one place (`AdminUser.Grade = "9"`) and Bagrut track in another
   (`QuestionState.Grade = "5 Units"`)
3. No explicit advancement state — teachers and students couldn't see
   "which chapter is the student on"

The Bagrut domain has two orthogonal axes routinely conflated:
- **School year**: 9/10/11/12 (developmental progression; demographic)
- **Bagrut track**: 3U/4U/5U (difficulty + exam code; curriculum scope)

## Decision

Introduce a syllabus advancement model with three hard rules:

1. **Advancement is gated by prior learning + chapter prereqs, not age.**
   A grade-9 student who mastered functions early sees calculus; a
   grade-11 student who never mastered functions still practices them.

2. **Syllabus definition is a projection from YAML; student advancement
   is an event-sourced aggregate.**
   - `SyllabusDocument` + `ChapterDocument` are definition-layer, re-ingested
     from `config/syllabi/<track>.yaml` on manifest change. No student
     data is touched during re-ingest.
   - `StudentAdvancement` is a per-enrollment aggregate (`advancement-
     {studentId}-{trackId}`) with events: `AdvancementStarted_V1`,
     `ChapterUnlocked_V1`, `ChapterStarted_V1`, `ChapterMastered_V1`,
     `ChapterDecayDetected_V1`, `SpiralReviewCompleted_V1`,
     `ChapterOverriddenByTeacher_V1`.

3. **Expected-vs-actual pacing is a teacher-only signal.**
   Students never see "you are N chapters behind". The shipgate
   dark-pattern ban (no streaks, no loss-aversion, no variable-ratio
   rewards) extends to comparative pacing. Enforced by
   `AdvancementArchitectureTests.StudentSpa_DoesNotReferencePacingDelta`.

## Architecture

```
CurriculumTrackDocument       (existing)
 └── SyllabusDocument          (new — projection from YAML)
      └── ChapterDocument      (new)
           └── LearningObjective (existing)
                └── Concept    (existing)

StudentAdvancement            (new — event-sourced per enrollment)
  - ChapterStatuses: Locked | Unlocked | InProgress | Mastered | NeedsReview
  - CurrentChapterId
  - Retention / questions-attempted per chapter
```

### Aggregate boundaries (Dina's lens)

`StudentAdvancement` is a distinct aggregate from `StudentProfileSnapshot`:
different lifecycle (advancement writes during every session; profile
writes on enrollment / demographic change), different consistency
boundary, different write frequency. Folding them in would cause
stream-size blowup and write contention.

### Chapter transitions

`AdvancementEventSubscriber` hosted service (Actor Host) subscribes to
`cena.events.student.*.concept_mastered_v2`. On each event,
`StudentAdvancementService.ApplyConceptMasteryAsync` cascades:

1. `ChapterStarted_V1` — first attempt in an Unlocked chapter
2. `ChapterMastered_V1` — all concepts in the chapter mastered
3. `ChapterUnlocked_V1` — every chapter whose prereqs are now mastered

`CheckDecayAsync` runs on a scheduled job and emits
`ChapterDecayDetected_V1` when retention (BKT half-life) drops below
threshold; the next session's question pool preferentially includes
spiral-review items from that chapter.

## Grade cleanup

- `AdminUser.Grade` stays as `string?` school year. Demographic only.
  Architecture test (`QuestionState_Grade_NotAssignedWithBagrutTrackLiterals_InNewCode`)
  prevents new writes of Bagrut-track strings to any `.Grade` field.
- `QuestionState.BagrutTrack` enum added (`None` / `ThreeUnit` / `FourUnit`
  / `FiveUnit`). `QuestionState.ParseBagrutTrackFromGradeString()`
  upcasts legacy `"5 Units"`-style strings on stream read.
- `QuestionBankSeedData` is allowlisted in the architecture test — its
  legacy Grade-string values upcast via the parser. A follow-up PR
  migrates the seed to emit `BagrutTrack` directly.

## Consequences

### Positive

- Adaptive-learning thesis is now mechanically enforced
- Teachers get a real "which chapter" signal for grading / parent comms
- Students see a "you're here → next is X" map with progressive disclosure
- `QuestionState.Grade` lying label is contained (upcaster + arch test)

### Negative

- Chapter authoring depends on curriculum-expert bandwidth (Amjad).
  Mitigated by the `syllabus-propose` tool (draft only; expert edits
  override)
- Re-ingesting a manifest that removes chapters can orphan
  `ChapterDocument` rows. `--prune` flag exists but requires operator
  intent

### Neutral

- Grade field remains in `AdminUser` and `QuestionState` for backward
  compatibility. Long-term it becomes strictly demographic; the
  architecture test prevents misuse in the meantime

## ADR-0003 amendment (misconception session-scope)

This ADR co-ships a carve-out to ADR-0003:

> **Advancement trajectories** (sequence of chapter transitions, time-to-
> mastery curves, spiral-review cadence) are **behavioural** and are
> permitted inputs to ReasoningBank pattern distillation under the
> following conditions:
> - **Cohort-aggregated only** — no student-identifying features cross
>   the `AdvancementTrajectoryRedactor` boundary
> - **PII-redactor is load-bearing** — the redactor scans the serialised
>   vector for forbidden substrings (`@`, `studentId`, `email`, `fullName`,
>   etc.) and refuses emission on any hit. Verified by
>   `TrajectoryRedactor_RejectsVectorsContainingPIISubstrings`
> - **30-day retention ceiling** matches the existing misconception
>   retention rule
> - **No affective inference** — we do not derive "student X struggled
>   emotionally" from advancement data. That crosses into profiling
>   territory ADR-0003 bans
> - **Archetype tags are behavioural** (`fast-advancer`, `steady-learner`,
>   `early-explorer`, `onboarding`), never affective

## Architectural Invariants (Rami's "can it be verified?")

Each claim above maps to a test:

| Claim | Test |
|---|---|
| Students never see pacing delta | `StudentSpa_DoesNotReferencePacingDelta` |
| Trajectory redactor refuses PII | `TrajectoryRedactor_RejectsVectorsContainingPIISubstrings` |
| Clean cohort vectors still emit | `TrajectoryRedactor_EmitsCleanVectorForNormalCase` |
| `.Grade = "N Units"` regression blocked | `QuestionState_Grade_NotAssignedWithBagrutTrackLiterals_InNewCode` |

## Migration path

1. **Schema** (landed): `SyllabusDocument` + `ChapterDocument` + advancement events
2. **Manifest ingest** (landed): `Cena.Tools.DbAdmin syllabus-ingest` + Math-5U DRAFT
3. **Service + subscriber** (landed): `StudentAdvancementService` + NATS subscriber
4. **API** (landed): `/api/admin/tracks/{id}/syllabus`, `/api/admin/students/{id}/advancement`, `/api/me/advancement`, override
5. **UI** (landed): student `SyllabusMap` + teacher `StudentSyllabusAdvancement`
6. **Redactor** (landed): `AdvancementTrajectoryRedactor` + PII scan
7. **Grade cleanup** (landed): enum added, upcaster, arch test
8. **Follow-ups**: Amjad-authored canonical Math-5U manifest, ReasoningBank
   consumer pipeline (reads redactor output), expected-vs-actual pacing
   column in teacher dashboard

## References

- RDY-061 task: [tasks/readiness/RDY-061-syllabus-advancement-model.md](../../tasks/readiness/RDY-061-syllabus-advancement-model.md)
- Persona lens: [docs/tasks/pre-pilot/PERSONAS.md](../tasks/pre-pilot/PERSONAS.md)
- ADR-0003 (misconception session-scope — amended): [docs/adr/0003-misconception-session-scope.md](0003-misconception-session-scope.md)
- Shipgate dark-pattern ban: [docs/engineering/shipgate.md](../engineering/shipgate.md)
