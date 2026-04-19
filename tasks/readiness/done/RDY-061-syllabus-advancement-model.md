# RDY-061: Syllabus Advancement Model

- **Status**: Requested — not started
- **Priority**: High — the adaptive-learning thesis depends on this
- **Source**: Shaker 2026-04-18 — "I prefer Syllabus advancements" (over grade-gating)
- **Tier**: 1-2 (data-model + pedagogy foundation; content work depends on it)
- **Effort**: 7-10 days end-to-end (not counting Amjad's authoring hours)
- **Depends on**:
  - RDY-003 Prerequisite graph (landed) — the concept-level DAG this layers on
  - RDY-018 Sympson-Hetter exposure (landed) — exposure stays per-concept, unchanged
  - RDY-024 BKT calibration (landed) — mastery signal feeds chapter transitions
  - RDY-034 CAS-gated ingestion (landed) — chapter question banks inherit the gate
- **Co-ships with**: ADR-0034 Syllabus Advancement Model (new) + ADR-0003 amendment
  (explicit carve-out for chapter advancement telemetry in ReasoningBank)

## Problem

Cena's adaptive-learning thesis says content should be gated by **what a
student has learned**, not by their **birthday or calendar year in school**.
The current implementation partly honors this — `AdaptiveQuestionPool`
serves items from the mastery frontier — but has three gaps:

1. **No syllabus structure.** Tracks carry a flat list of `LearningObjectiveIds`;
   there's no concept of "Chapter 4: Derivatives of Trigonometric Functions"
   grouping objectives together the way Bagrut / Yoel Geva / Ministry pacing
   guides do. Teachers + students think in chapters; the UI can't render
   one.
2. **Grade is a lying label.** `QuestionState.Grade` holds
   `"3 Units" / "4 Units" / "5 Units"` — that's the Bagrut track, not a
   school year. `AdminUser.Grade` holds `"9" / "10" / "11"` — that's a
   school year. Same field name, two orthogonal concepts, nothing
   enforces which. Classic 2026-04-13 labels-match-data violation.
3. **No explicit advancement state on students.** Where is a student in
   their syllabus? Which chapters are locked / unlocked / in-progress /
   mastered / need-review? We can't answer this without running a graph
   traversal on every dashboard load.

The philosophical ask: **move from grade-based gating (which we don't
really do but UI implies) to syllabus-based advancement (what the
pedagogy actually requires)**.

## Design

### Layer A — Syllabus definition (projection from YAML)

Authored `SyllabusManifest.yaml` per track, versioned in git. A DbAdmin
tool ingests it into two Marten documents. Re-deriving on manifest
change is free; no student data touches:

```yaml
# config/syllabi/math-bagrut-5unit.yaml
track: track-math-bagrut-5unit
chapters:
  - id: ch-5u-01-algebra
    order: 1
    title:
      en: "Algebra Review"
      he: "חזרה באלגברה"
      ar: "مراجعة الجبر"
    learningObjectiveIds: [lo-alg-001, lo-alg-002, lo-alg-003]
    prerequisiteChapters: []
    expectedWeeks: 3
    ministryCode: "806.1"
  - id: ch-5u-02-functions
    order: 2
    learningObjectiveIds: [lo-fn-001, lo-fn-002, ...]
    prerequisiteChapters: [ch-5u-01-algebra]
    expectedWeeks: 4
    ministryCode: "806.2"
```

New documents:

- **SyllabusDocument** `{TrackId, Version, Chapters[], CreatedAt, CreatedBy}`
- **ChapterDocument** `{Id, SyllabusId, Order, TitleByLocale, LearningObjectiveIds[], PrerequisiteChapterIds[], ExpectedWeeks, MinistryCode, QualityGate}`

The chapter-level prereq DAG is independent of the concept-level DAG.
A chapter is unlocked when all predecessor chapters are mastered (or
a teacher override fires).

### Layer B — Student advancement (event-sourced aggregate)

`StudentAdvancement` is a separate aggregate from `StudentProfile`
(different lifecycle, different consistency boundary — per Dina's
review lens). One aggregate per enrollment (a student in both Math-5U
and Physics-5U gets two rows).

Events:

```
ChapterUnlocked_V1       { AdvancementId, ChapterId, UnlockedAt, Reason }
ChapterStarted_V1        { AdvancementId, ChapterId, FirstAttemptAt }
ChapterMastered_V1       { AdvancementId, ChapterId, MasteredAt,
                           MasteryScore, QuestionsAttempted }
ChapterDecayDetected_V1  { AdvancementId, ChapterId, DetectedAt,
                           CurrentRetention, TriggeredReview }
SpiralReviewCompleted_V1 { AdvancementId, ChapterId, ReviewedAt,
                           RetentionAfterReview }
ChapterOverriddenByTeacher_V1 { AdvancementId, ChapterId, OverriddenBy,
                                 NewStatus, Rationale }
```

State projection:

```csharp
public class StudentAdvancementState {
  string AdvancementId;              // {studentId}:{trackId}
  string StudentId;
  string TrackId;
  string SyllabusId;                 // resolves at write time (syllabus versioning)
  Dictionary<string, ChapterStatus> ChapterStatuses;
  string? CurrentChapterId;          // highest in-progress
  DateTimeOffset CreatedAt;
  DateTimeOffset LastAdvancedAt;
}

public enum ChapterStatus {
  Locked,
  Unlocked,          // predecessors mastered; no student activity yet
  InProgress,        // at least one question attempted
  Mastered,          // mastery threshold hit on all objectives in chapter
  NeedsReview        // mastered but retention decayed below threshold
}
```

### Pacing — dual track

- **ExpectedPacing**: from `Chapter.ExpectedWeeks` + school-year start date
  (per school, from `ClassroomDocument`). Deterministic.
- **ActualPacing**: from the student's advancement history.
- **Delta**: `expected - actual` in days / chapters.

**Visibility rule**:

- **Teacher dashboard**: see delta. `"Student X is 2 chapters / 6 weeks
  behind expected"` is a real signal teachers need.
- **Student UI**: never. Surfacing "you're behind" is a dark-pattern per
  the 2026-04-11 shipgate ban + my 2026-04-13 "ethical persuasion" memory.
  Student sees "you're here → next up is [chapter]" and nothing
  comparative.

### Chapter transitions

Session actor reacts to `ConceptMastered_V1` events. After each, it
projects forward:

1. If all objectives in the current chapter are mastered
   → emit `ChapterMastered_V1`
2. For every chapter whose `prerequisiteChapterIds` subset is now
   mastered → emit `ChapterUnlocked_V1`
3. For every mastered chapter whose retention (from BKT decay) has
   dropped below threshold → emit `ChapterDecayDetected_V1` + schedule
   spiral-review insertion in the next session

This logic lives in a **new reducer**, not in `AdaptiveMasteryActor`,
to keep advancement evolution decoupled from mastery scoring.

### Authoring flow — manual primary, derived aid

Amjad (Prof. Amjad, Bagrut curriculum lens per personas) authors the
manifest. A proposal tool exists to reduce time:

```bash
# Propose chapters from topology + Ministry codes
npx tsx scripts/syllabus-propose.ts \
  --track track-math-bagrut-5unit \
  --source ministry-code \
  > config/syllabi/math-bagrut-5unit.proposed.yaml

# Amjad edits → commits final
git mv math-bagrut-5unit.proposed.yaml math-bagrut-5unit.yaml
```

Proposal tool uses:
- Concept prereq topology (already in `ConceptDocument.PrerequisiteIds`)
- Ministry code tagging on LOs (where present)
- Topological ordering + weak community detection (Louvain / label-prop)

It does **not** ship as the default. It's a starting point; Amjad's
edits override. Manifests live in `config/syllabi/`.

### Grade field cleanup (co-shipped)

- `QuestionState.Grade` string containing `"5 Units"` → **migrate to
  `QuestionState.BagrutTrack`** (new typed enum `ThreeUnit / FourUnit / FiveUnit`).
  Emit `QuestionTrackReclassified_V1` events. Upcast on stream read so
  old events still replay.
- `AdminUser.Grade` stays as `string?` school year (9 / 10 / 11 / 12).
  Add explicit comment: "demographic; never used for content scope".
- **Architecture test**: fail the build if any source file sets
  `QuestionState.Grade = "[0-9] ?Unit"` or reads `AdminUser.Grade` in
  a file under `src/actors/**/Serving/` or `src/api/Cena.Student.Api.Host/Endpoints/`.
  Catches regressions to the old confusion.

### ReasoningBank integration

Advancement trajectories are valuable pattern sources:

- "Archetype X students who start Chapter 4 before mastering Chapter 2
  have 3× disengagement at turn 7" — schedule-level pattern
- "Spiral-review at 21 days retains 78%; 14 days retains 92% at 2×
  cost" — retention tuning
- "Chapter 6 benefits from a mini-prereq on Chapter 3 geometry outside
  the canonical DAG" — hidden-prereq discovery

**Policy (requires ADR-0003 amendment, co-shipped)**:

- Advancement *trajectories* (sequence of {chapter, timing, mastery curve}) are behavioral and fair game for pattern distillation
- Vectors written to ReasoningBank must be **cohort-aggregated**, never
  student-identifying
- PII redactor runs at the event boundary (`AdvancementTrajectoryRedactor`)
  before hand-off
- Retention: same 30-day ceiling as other learning data per ADR-0003
- **Never**: affective inference from advancement data ("Student X is
  struggling emotionally") — that crosses into misconception-profile
  territory that ADR-0003 bans

## Scope

### Phase 1 — Schema + manifest (1-2 days, no student impact)

- `SyllabusDocument` + `ChapterDocument` + `SyllabusManifest` YAML
  schema
- Tool: `npx tsx scripts/syllabus-ingest.ts` — YAML → Marten docs
- Tool: `npx tsx scripts/syllabus-propose.ts` — topology → draft YAML
- Ministry-code-aware seed manifest for Math-5U + Math-4U from Amjad's
  input (human work, ~4-8 hours on his side)

### Phase 2 — Advancement aggregate (2-3 days)

- New events + reducer + projection (all under `src/actors/Cena.Actors/Advancement/`)
- Event handlers wired to `ConceptMastered_V1`
- `StudentAdvancementService` for admin-api + student-api reads
- Tests: chapter unlock cascades, decay detection, teacher override

### Phase 3 — API surfaces (1-2 days)

- `GET /api/admin/tracks/{trackId}/syllabus` — syllabus tree
- `GET /api/admin/students/{id}/advancement?trackId=X` — per-enrollment
  state
- `GET /api/me/advancement` — student's own view
- `POST /api/admin/students/{id}/advancement/override` — teacher override,
  audit-logged
- Integrate with RDY-060 live-stream (chapter transitions push events
  to teacher dashboards in real time)

### Phase 4 — UI (2 days)

- Student: syllabus map view ("you're here"), unlocked chapters
  highlighted, locked chapters greyed with "masters [prereq] to unlock"
- Teacher: per-class syllabus heatmap, per-student advancement tile,
  pacing delta column (expected vs actual)
- Admin (Amjad): syllabus manifest editor with diff view on re-ingest

### Phase 5 — Grade cleanup (1 day)

- Typed `BagrutTrack` enum + upcaster for QuestionState
- Architecture tests for mis-use prevention
- Migration script: walk existing `QuestionState.Grade` values, emit
  reclassified events

### Phase 6 — ReasoningBank pipeline (1-2 days)

- `AdvancementTrajectoryRedactor` (PII scrub at event boundary)
- Cohort-aggregated trajectory vectors hitting the ReasoningBank
  writer
- Test: known-bad redactor input must strip studentId, email, and all
  name fields before vector hand-off

## Acceptance Criteria

### Data model
- [ ] `SyllabusDocument` + `ChapterDocument` persist and re-derive
  idempotently from YAML manifest
- [ ] `StudentAdvancementDocument` event-sourced; at least one
  manifest (Math-5U) fully seeded
- [ ] `QuestionState.BagrutTrack` typed enum; upcaster reads legacy
  `Grade` string values; architecture test prevents new writes to
  `Grade = "5 Units"` etc.

### Behavior
- [ ] Mastering a concept cascades to chapter-status updates within
  100ms (event-sourced, tested)
- [ ] Spiral review triggers when retention decays below threshold;
  review items preferentially selected by question pool on next session
- [ ] Teacher override updates state + writes to `[AUDIT]` stream
- [ ] Dual pacing: teacher sees delta; student never does (UI test)

### Integration
- [ ] Live stream (RDY-060) fans `ChapterUnlocked_V1` to teacher
  dashboard within 500ms
- [ ] Diagnostic quiz (RDY-023) places student at correct initial
  chapter, not just theta
- [ ] Self-assessment (RDY-057, pending) — "which chapters feel shaky"
  reconciled against actual advancement state

### Guard rails (the Rami test — "can it be verified?")
- [ ] Architecture test: no student-facing UI file references pacing
  delta fields
- [ ] Architecture test: ReasoningBank writer only receives
  PII-redacted trajectory vectors
- [ ] Integration test: known-adversarial grade-9 student mastering
  5U advanced content is not artificially gated

### ADR
- [ ] ADR-0034 (Syllabus Advancement Model) merged, cross-referenced
  from ADR-0002 (CAS oracle) and ADR-0003 (misconception session-scope)
- [ ] ADR-0003 amendment defining the advancement-telemetry carve-out
  and its PII guarantees

## Out of Scope (explicit)

- **Grade-based content gating.** Not happening. School-year is
  demographic metadata for compliance + teacher analytics only.
- **Dark-pattern pacing nags.** "You're behind" nudges to students are
  banned per shipgate.
- **Per-student persistent misconception profiling derived from
  advancement.** ADR-0003 session-scope rule stays intact.
- **Ministry-published pacing as a hard deadline.** Used as expected
  pacing signal only; never blocks a student from next chapter.
- **Cross-track transfer learning in the data model.** Belongs to
  ReasoningBank pattern distillation; not on `StudentAdvancement`.
- **Syllabus editor as a multi-author collaborative tool** — v1 is
  file-based + git; Notion/GDocs-style co-editing is a later task.

## Persona review lens check

- **Amjad**: authoring workflow matches Ministry reality; manifest
  ingest is idempotent; derived proposal is a *draft* not a *spec*
- **Dr. Nadia**: advancement respects ZPD / prereq-met-ness;
  spiral-review timing calibrated against Ebbinghaus decay curves in
  BKT
- **Dr. Yael**: IRT calibration stays per-concept; no psychometric
  property attributed to chapters; DIF analysis continues at concept
  level
- **Dina**: `StudentAdvancement` as a distinct aggregate from
  `StudentProfile`; projection definition decoupled from event history
- **Oren**: new endpoints typed, versioned, paginated where applicable
- **Tamar**: chapter titles are per-locale in the manifest; RTL
  chapter-map rendering tested
- **Dr. Lior**: syllabus map UI uses progressive disclosure (don't
  render 60 chapters at once); celebration on chapter-mastery transition
- **Ran**: teacher-override endpoint audit-logged with reason;
  advancement-telemetry redactor tested against adversarial PII input
- **Iman**: chapter transition events have SLOs + dashboards; retention
  worker respects advancement lifecycle when erasing
- **Rami**: every claim above has a named test; "labels match data" is
  architecturally enforced, not policy-ed

## Notes

- **Why not put this on `StudentProfile`?** Different aggregate lifecycle.
  Student advancement can change every 15 minutes during a study session;
  `StudentProfile` changes on enrollment / demographic updates. Mixing
  them causes stream-size blowup + write contention on `StudentProfile`.
  Dina's review lens would reject the fold-in.
- **Why not derive advancement from mastery reads on every request?**
  Because the dashboards need real-time push, and teacher-override
  (explicit state mutation, not mastery-derived) is load-bearing.
  Event-sourcing gives us both.
- **Why not just use the existing concept prereq graph and skip
  chapters entirely?** Because humans don't think in 200-node DAGs,
  and because the Bagrut exam is *chaptered* — the exam structure
  itself has sections aligned with textbook chapters. Matching that
  structure in the product makes every downstream teacher task easier.

## Links

- Track doc: [src/shared/Cena.Infrastructure/Documents/CurriculumTrackDocument.cs](../../src/shared/Cena.Infrastructure/Documents/CurriculumTrackDocument.cs)
- LO doc: [src/shared/Cena.Infrastructure/Documents/LearningObjectiveDocument.cs](../../src/shared/Cena.Infrastructure/Documents/LearningObjectiveDocument.cs)
- Enrollment: [src/shared/Cena.Infrastructure/Documents/EnrollmentDocument.cs](../../src/shared/Cena.Infrastructure/Documents/EnrollmentDocument.cs)
- Mastery engine: `src/actors/Cena.Actors/Sessions/AdaptiveMasteryActor.cs`
- Question serving: `src/actors/Cena.Actors/Serving/AdaptiveQuestionPool.cs`
- Referenced in memory: `project_bagrut_reference_only.md`, `feedback_shipgate_banned_terms.md`
- Personas: [docs/tasks/pre-pilot/PERSONAS.md](../../docs/tasks/pre-pilot/PERSONAS.md)
- CAS ADR: `docs/adr/0002-sympy-correctness-oracle.md`
- Misconception scope ADR (amendment target): `docs/adr/0003-misconception-session-scope.md`
