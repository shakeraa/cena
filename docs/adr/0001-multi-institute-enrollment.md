# ADR-0001 — Multi-institute enrollment with per-track mastery state

- **Status**: Decision 1 locked · Decision 2 locked (Model C — seepage with decay)
- **Date proposed**: 2026-04-11
- **Deciders**: Shaker (project owner), claude-code (coordinator)
- **Supersedes**: none (first ADR)
- **Related reviews**: [docs/reviews/cena-review-2026-04-11.md](../reviews/cena-review-2026-04-11.md), [docs/references.md](../references.md)

---

## Context

The Cena data model as of commit `0f71389` assumes **one student belongs to exactly one `SchoolId`**. `StudentProfileSnapshot.SchoolId` is set once via the `SessionStarted_V1` Apply handler ([StudentProfileSnapshot.cs:153](../../src/actors/Cena.Actors/Events/StudentProfileSnapshot.cs#L153) — `SchoolId ??= e.SchoolId; // REV-014: school never changes`) and is never updated afterwards. `ClassroomDocument` is a flat row with one `TeacherId`, one `SchoolId`, a `Subjects[]` array, and a join code. There is no `Enrollment`, no `Institute`, no `CurriculumTrack` aggregate anywhere in the system.

The product needs:

1. A **Mentor** (instructor / institute manager) can create one or more **Institutes**. Each Institute has one or more **Classrooms**.
2. A **Student** can be a member of **multiple Institutes simultaneously** — for example, a 17-year-old preparing for both the Israeli *bagrut* matriculation exam at their school AND the SAT or the *psychometry* (מבחן פסיכומטרי) at a private tutor. Different institutes, different curriculum goals, overlapping subjects.
3. A Student's learning path is scoped to a **CurriculumTrack** — "MATH-BAGRUT-5UNIT", "MATH-SAT-700", "MATH-PSYCHOMETRY-QUANTITATIVE". Two enrollments for the same student on the same subject but different tracks are pedagogically distinct.

The current schema cannot express (1), (2), or (3).

---

## Decision 1 — student ∈ multiple institutes (**LOCKED**)

We introduce a proper M:N relationship between `Student` and `Institute`, mediated by an `Enrollment` aggregate.

```
Student 1 ──────── M  Enrollment  M ──────── 1 Institute
                        │
                        └── 1 CurriculumTrack
```

### Shape (draft — subject to ADR-0002 once Decision 2 lands)

```csharp
// New aggregates — event-sourced, follow the FIND-data-007 CQRS pattern
public class Institute
{
    public string InstituteId { get; set; } = "";
    public string Name { get; set; } = "";
    public string? ParentInstituteId { get; set; }  // nullable hierarchy root
    public string MentorId { get; set; } = "";      // owner
    public string Country { get; set; } = "";       // "IL", "US", etc.
    public DateTimeOffset CreatedAt { get; set; }
}

public class CurriculumTrack
{
    public string TrackId { get; set; } = "";       // e.g. "MATH-BAGRUT-5UNIT"
    public string Title { get; set; } = "";
    public string Subject { get; set; } = "";
    public string? TargetExam { get; set; }         // "bagrut-5unit" | "sat-math" | "psychometry-quantitative" | null
    public string[] LearningObjectiveIds { get; set; } = Array.Empty<string>();  // FIND-pedagogy-008
    public string[] StandardMappings { get; set; } = Array.Empty<string>();
}

public class Enrollment
{
    public string EnrollmentId { get; set; } = "";
    public string StudentId { get; set; } = "";
    public string InstituteId { get; set; } = "";
    public string TrackId { get; set; } = "";
    public string Status { get; set; } = "active";  // active | paused | withdrawn | completed
    public DateTimeOffset EnrolledAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
}
```

### New events (add to `LearnerEvents.cs` / new `EnrollmentEvents.cs`)

- `InstituteCreated_V1 (InstituteId, MentorId, Name, Country, CreatedAt)`
- `InstituteMentorChanged_V1` — ownership transfer
- `CurriculumTrackPublished_V1 (TrackId, Title, Subject, TargetExam, LearningObjectiveIds)`
- `EnrollmentCreated_V1 (EnrollmentId, StudentId, InstituteId, TrackId, EnrolledAt)`
- `EnrollmentStatusChanged_V1 (EnrollmentId, NewStatus, ChangedAt, Reason?)`
- `EnrollmentEnded_V1 (EnrollmentId, EndedAt, Reason)`

### Tenant scoping changes

- `TenantScope.GetSchoolFilter(user) → string?` is **replaced** by `TenantScope.GetInstituteFilter(user) → IReadOnlyList<string>` (plural). A mentor sees all their institutes; a student sees only the institutes they are actively enrolled in; an admin sees all.
- Firebase custom claims: `{ role: "mentor" | "instructor" | "student" | "admin", institutes: [{ instituteId, role }] }`.
- Every admin query that currently filters by `SchoolId` becomes `InstituteId IN (...)`.

### Auth roles

- **Mentor**: owns one or more Institutes. Can create classrooms, publish tracks, invite instructors, view aggregate analytics across their institutes.
- **Instructor**: scoped to a classroom (or a set of classrooms) within a single institute. Can view student progress, author questions, approve enrollments. Does not see other instructors' classrooms.
- **Student**: unchanged on the surface — but their `TenantScope` now returns *the institutes they are enrolled in*, plural.

### Why LOCKED

- The product requirement is unambiguous: bagrut + SAT + psychometry are real, separate, widely-coexisting needs in the Israeli market. A student's Cena account has to span them or the product is useless for the core target demographic.
- Retrofitting M:N after shipping 1:1 is far more expensive than starting from M:N and defaulting existing rows to a single-institute view. Phase 1 below pays the upfront cost now.

---

## Decision 2 — mastery-state sharing across tracks (**LOCKED — Model C, 2026-04-13**)

### The question

When a student is enrolled in two tracks that share a concept — e.g. `linear-equations` in both `MATH-BAGRUT-5UNIT` and `MATH-SAT-700` — should the BKT / Elo / HLR state be **shared**, **isolated**, or **seeded-but-divergent**?

### Three candidate models

| Model | Description | Pro | Con |
|---|---|---|---|
| **A. Fully shared** | `ConceptMastery[conceptId]` stays globally keyed by concept id. Both tracks read and write the same state. | Fastest onboarding (no cold start on shared concepts). Matches the intuition that "math is math". | Ignores exam-specific strategy transfer: SAT math requires speed + MC-specific heuristics, bagrut wants rigorous written proofs. A 0.9 mastery on bagrut doesn't predict SAT accuracy. |
| **B. Fully isolated** | `ConceptMastery[(enrollmentId, conceptId)]`. Each enrollment gets its own BKT + Elo + HLR state from scratch. | Accurate: each exam gets its own predictive model. No contamination. | Hostile cold start: a student who's already bagrut-fluent starts at zero when they begin SAT prep. Wastes data. |
| **C. Seeded but divergent** (working preference) | New enrollment's state is **seeded** from any existing enrollment on the same concept (weighted by `SharedConceptCount(trackA, trackB)`), then diverges independently after the first attempt. | Combines A's cold-start benefit with B's per-exam accuracy. Matches how human tutors actually work. | More complex: needs a per-concept *transfer coefficient* that's empirical, not a guess. Upcast path is non-trivial. |

The user's stated preference (2026-04-11): "**usually should be yes** [mastery shared], but verify before we commit."

This ADR treats that preference as the *null hypothesis* and requires verification before it becomes the locked decision.

### What "verification" requires

Before Decision 2 is committed, we need **citable evidence** and **a data check** against the three candidate models. The verification task (filed separately in the queue, see "Next" below) must produce:

#### Literature review (must cite real papers from [docs/references.md](../references.md))

The transfer-of-learning literature is dense and opinionated. The candidates I expect to matter here:

1. **Thorndike, E. L. & Woodworth, R. S. (1901)**. "The influence of improvement in one mental function upon the efficiency of other functions." *Psychological Review*, 8(3), 247–261. DOI: [10.1037/h0074898](https://doi.org/10.1037/h0074898) — original **identical elements** theory: transfer happens only to the degree two tasks share concrete components. Predicts Model B as the *default* with A only when tasks are literally the same.
2. **Perkins, D. N. & Salomon, G. (1992)**. "Transfer of Learning." In *International Encyclopedia of Education* (2nd ed.). — **near vs far transfer**, **low-road vs high-road**. Near transfer (same domain, similar context) is empirically reliable; far transfer requires deliberate bridging. Predicts Model C with high weight on shared concepts and low weight on disparate ones.
3. **Barnett, S. M. & Ceci, S. J. (2002)**. "When and where do we apply what we learn? A taxonomy for far transfer." *Psychological Bulletin*, 128(4), 612–637. DOI: [10.1037/0033-2909.128.4.612](https://doi.org/10.1037/0033-2909.128.4.612) — **9-dimension taxonomy** of transfer contexts. Gives a concrete framework for *how similar* two tracks must be before you share state.
4. **Singley, M. K. & Anderson, J. R. (1989)**. *The Transfer of Cognitive Skill.* Harvard University Press. ISBN: 978-0674903401 — **ACT-R production-rule transfer**: procedural skills (algorithms, computations) transfer narrowly across tasks that share production rules. Predicts that `solve-linear-equation` transfers across tracks (same rule) but `choose-SAT-answer-strategy` does not.
5. **Schwartz, D. L., Bransford, J. D. & Sears, D. (2005)**. "Efficiency and Innovation in Transfer." In *Transfer of Learning from a Modern Multidisciplinary Perspective.* IAP. — **preparation for future learning** framing: the question isn't "does state X transfer to state Y?" but "does state X make Y faster to learn?" Supports Model C's seeding semantics.

The verification report must either:
- Confirm the null hypothesis (Model C — seeded-but-divergent, with a transfer coefficient ≈ 1.0 for near-identical concepts and ≤ 0.5 for exam-specific strategies), **OR**
- Refute it by showing the literature supports Model A or Model B.

"Research shows..." without a name + year + DOI is not acceptable evidence per the 2026-04-11 review standard.

#### Data check (when we have real data to check against)

Not applicable today — we have zero real user data. But the verification must define what evidence would refute the chosen model *once we have data*:

- **Refuting Model A (full share)**: if we find that a student's cross-track accuracy is systematically lower than their intra-track accuracy on the same concept (e.g. bagrut-trained student drops >10% accuracy on SAT-framed versions of the same linear-equation), A is wrong.
- **Refuting Model B (full isolate)**: if students who enroll in a second track start with near-target accuracy on shared concepts on their first attempt, the cold-start penalty B imposes is unjustified.
- **Refuting Model C (seeded-divergent)**: if the transfer-coefficient calibration never converges (i.e. per-concept optimal weights are all over the place), the seeded model is overfit.

#### Implementation gate

The verification must produce a recommended concrete design:
- How is the state keyed? `(conceptId)`, `(enrollmentId, conceptId)`, or hybrid?
- If seeded: what's the seed function? `newPMastery = sharedConceptWeight * existingPMastery + (1 - sharedConceptWeight) * prior`
- If seeded: where does `sharedConceptWeight` come from? Literature default? Authored by track designer? Learned from data?

### Verification complete — Model C locked (2026-04-13)

VERIFY-0001 literature review ([docs/research/VERIFY-0001-transfer-of-learning.md](../research/VERIFY-0001-transfer-of-learning.md)) confirmed the null hypothesis. **Model C (seepage with decay)** is the locked decision.

#### Chosen design

| Parameter | Value | Source |
|-----------|-------|--------|
| Key structure | `enrollmentId:conceptId` | TENANCY-P2a implementation |
| Same-subject seepage factor | 0.60 | Thorndike & Woodworth (1901), Singley & Anderson (1989) |
| Cross-subject seepage factor | 0.20 | Barnett & Ceci (2002), Perkins & Salomon (1992) |
| Max seeded PKnown | 0.50 | Never skip prerequisite validation |
| Time decay | Ebbinghaus (BKT+ half-life) | Schwartz, Bransford & Sears (2005) |
| Seepage timing | One-time at enrollment | Then tracks evolve independently |
| EloRating | Student-wide singleton (not re-keyed) | Elo measures overall ability, not per-track |

#### Implementation (shipped)

- `MasteryKeys.Key(enrollmentId, conceptId)` — composite key helper
- `MasterySeepageService` — applies seepage at enrollment time
- `ConceptAttempted_V3`, `ConceptMastered_V2` — events with EnrollmentId
- V2→V3 and V1→V2 upcasters — legacy flat keys → `"default":conceptId`
- `MasterySeepageApplied_V1` — audit event with full provenance
- `ConceptMasteryState.SourceEnrollmentId` + `SeepageFactor` — audit fields

#### Refutation criteria (for when we have real data)

- **Refute Model C**: if per-concept optimal transfer weights show no convergence pattern (all over the place), the seeded model is overfit. Revisit with per-category weights.
- **Refute 0.60 factor**: if cross-track first-attempt accuracy on shared concepts is >80% of intra-track accuracy, the discount is too aggressive — raise toward Model A.
- **Refute 0.20 cross-subject factor**: if cross-subject students show zero benefit from prior mastery, lower to 0.0 (Model B for cross-subject).

---

## Phased rollout

### Phase 1 — schema only, zero behavior change

- Add `Institute`, `CurriculumTrack`, `Enrollment` Marten documents + the 6 new events.
- Event upcasters: every existing stream gets a synthetic `EnrollmentCreated_V1(defaultEnrollmentId, studentId, defaultInstituteId, defaultTrackId)` replayed first so `Apply` handlers can rely on at least one enrollment existing.
- `TenantScope.GetInstituteFilter` initially returns a single-element list: `[student.DefaultInstituteId]` — same semantics as today.
- Every admin query keeps working against a single institute. No UI changes.
- **Decision 2 is now locked (Model C).** Mastery state is re-keyed to `enrollmentId:conceptId` via TENANCY-P2a. Legacy streams upcasted to `"default":conceptId`.
- Migration risk: **low**. Pure additive schema + upcasters.
- Ships independently of Decisions 2 and 3.

### Phase 2 — cross-enrollment reads (Decision 2 locked, gate cleared)

- ~~Block on ADR-0002 (the verification report) that locks Decision 2.~~ **Done**: VERIFY-0001 complete, Model C locked 2026-04-13.
- Re-key mastery state per whatever Model 2 picks.
- New student-side onboarding step: "pick your track" (bagrut / SAT / psychometry / other).
- New `/api/me/enrollments` + `POST /api/me/enrollments/{trackId}` endpoints.
- Admin analytics queries get an extra `EnrollmentId IN (...)` filter.
- Migration risk: **medium-high**. Depends entirely on how Model 2 lands.

### Phase 3 — mentor admin surface

- New Mentor and Instructor roles in Firebase custom claims.
- Mentor dashboard: create institute, publish tracks, invite instructors, cross-institute analytics.
- Instructor view: classroom-scoped rollups.
- Seeded curriculum tracks for the three canonical cases: `MATH-BAGRUT-5UNIT`, `MATH-SAT-700`, `MATH-PSYCHOMETRY-QUANTITATIVE` — each with real `LearningObjectiveId[]` lists (leverages FIND-pedagogy-008).
- Migration risk: **high** (auth surface + new admin UI + real content per track).

---

## Consequences

### Positive

- Student accounts finally match real Israeli market reality (bagrut + SAT + psychometry coexist in the same kid).
- Analytics, leaderboards, and mastery rollups become per-enrollment, which means they can be per-exam — vastly more useful than "math grade 11" as a monolith.
- Curriculum tracks become first-class and swappable, unblocking future expansion (Arabic-speaking schools, college-level courses, etc.).
- Every assessment item still traces to a `LearningObjective` (FIND-pedagogy-008), but now also traces to the `CurriculumTrack` that adopted it — cleaner standards alignment.

### Negative

- **StudentProfileSnapshot re-key is the biggest migration Cena has faced so far.** It touches every BKT projection, every Elo rating, every HLR record. We need a rebuild + backfill plan *per phase*, not a big-bang rebuild.
- Phase 2 is blocked on a literature review + data check that hasn't been done yet — schedule risk.
- Mentor + Instructor auth roles are new; we need Firebase custom-claim wiring that doesn't exist. Sec review required.
- Every admin query that already filters by `SchoolId` needs updating to `InstituteId IN (...)`. This is grep-able (see FIND-sec-005 sweep pattern) but tedious.

### Neutral / unresolved

- **Classroom scoping**: is a `Classroom` a child of `Institute` (strict containment) or a cross-cutting group that can pull students from multiple institutes? Current proposal: strict containment. Revisit if a use-case appears.
- **Visibility between institutes**: should institutes see each other's existence at all? (Probably no — a bagrut school and a private SAT tutor are commercial competitors.) This is a privacy decision that needs its own review.
- **Billing**: out of scope for this ADR, but whoever owns the Institute owns the bill, which may conflict with "student brings their own data across institutes". Flag for later.

---

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| **Keep 1:1** and add a `CurriculumTrackId` to `StudentProfileSnapshot` | Forces students to pick ONE track forever. Defeats the product requirement (student does both bagrut and SAT). |
| **Model institutes as tags on the student, no Enrollment aggregate** | No place to put per-enrollment state (mastery, Elo, HLR). Loses the event-sourced history of *when* a student joined/left an institute. |
| **One mega-schema with every possible exam baked in** | Brittle; each new exam is a schema migration. `CurriculumTrack` as a first-class entity is the right generalisation. |
| **Keep the simple model and add mastery-namespace as a string field** | Shifts the complexity onto every query site without actually solving the modelling problem. Half-measure. |

---

## Next steps

Filed as a task in the kimi-queue (see `Next` stub below):

1. **VERIFY-0001** — literature + design verification for Decision 2. Produces ADR-0002 (mastery sharing model). Priority: `high`. Assignee: `unassigned` (takes a researcher, not a coder).
2. **Phase 1 schema scaffold** — new `Institute` / `CurriculumTrack` / `Enrollment` docs + events + upcasters + synthetic default-enrollment back-fill. Priority: `high`. Depends on: nothing (schema-only). Owner: TBD.
3. **Phase 2 re-key** — depends on ADR-0002 landing. Priority: `high`. Blocked.
4. **Phase 3 admin surface** — depends on Phase 2. Priority: `normal`.

---

---

## Finalized decisions (2026-04-11 session 2)

After a UI/flows conversation with the project owner, the following were committed on top of the original Decision 1 / Decision 2 split. These supersede any conflicting detail in the body above.

### Role naming — unified "Mentor"

"Mentor" is the single role for any adult guiding students. Their capabilities are not fixed by the role; they come from the **classroom mode** and a **capability flag set** per classroom. Institute ownership and classroom assignment are orthogonal permissions on top of the Mentor role, not separate roles. Student is unchanged.

This collapses the earlier "Mentor vs Instructor vs Coach" three-way split — the schema stores one role and configures capabilities per relationship.

### Two-level class hierarchy — Program + Classroom

The product owner clarified that a class is **two things**: what's being taught (authored once) and who is in the room this round (a cohort). Flat `Classroom` was collapsing them and forcing authoring duplication.

```
Institute
  ├── CurriculumTrack            ← target-exam level (MATH-BAGRUT-5UNIT …)
  │     └── LearningObjective[]  ← FIND-pedagogy-008
  │
  └── Program                    ← the course: "Grade 10 Algebra 2026-27"
        │  authored once: content, lesson plans, question bank, schedule template
        │  points at 1 CurriculumTrack; owned by 1..N mentors
        │
        └── Classroom (cohort)   ← "Round 1", "Group 2", "Mon-Wed 9am"
              │  per-cohort: roster, join code, approval mode, schedule, analytics
              │
              └── Enrollment (student ↔ classroom)
```

Enrollment keys become `(StudentId, ClassroomId)`. Track is derived through `Classroom → Program → CurriculumTrack`, not stored on the enrollment — removes a drift source.

### Three classroom modes

```csharp
public enum ClassroomMode
{
    SelfPaced,            // student alone with a platform Program; no mentor
    InstructorLed,        // cohort with curriculum authoring; 1..N mentors, 5..40 students
    PersonalMentorship    // light 1-on-1; mentor attaches to student's existing platform enrollment
                          // to provide tasks + notes; does NOT author curriculum
}
```

`PersonalMentorship` explicitly **does not** create new curriculum — it attaches a mentor's guidance layer on top of a student's existing `SelfPaced` platform enrollment.

### Mentor capability flags (deferrable feature set)

```csharp
[Flags]
public enum MentorCapability
{
    None          = 0,
    PushTasks     = 1 << 0,  // assignments — "do these 10 questions by Thursday"
    LeaveNotes    = 1 << 1,  // markdown notes anchored to sessions or questions
    Chat          = 1 << 2,  // text chat channel (piggybacks SignalR infrastructure)
    ViewProgress  = 1 << 3,  // read student analytics (mastery, Elo, sessions)
    SendReminders = 1 << 4   // push notification for session/due-date
}
```

Default bundles per mode:
- `SelfPaced` → `None` (no mentor attached)
- `InstructorLed` → `PushTasks | LeaveNotes | Chat | ViewProgress | SendReminders` (full)
- `PersonalMentorship` → `PushTasks | LeaveNotes | ViewProgress` (no chat by default; flip per-relationship)

### Classroom join approval — per-classroom setting

```csharp
public enum ClassroomJoinApproval
{
    AutoApprove,   // any valid join code → instant enrollment (default)
    ManualApprove, // instructor sees a pending-request queue, approves one by one
    InviteOnly     // no code works — signed invite links only (1-on-1 tutoring, private cohorts)
}
```

Lives on the `Classroom` (not `Institute` or `Program`). Default: `AutoApprove`. `PersonalMentorship` classrooms default to `InviteOnly`.

### Platform-owned canonical programs (self-learner path)

Cena itself owns a platform-type Institute ("Cena Platform") that ships a canonical set of `Program`s. Self-learners enroll directly in a `Mode: SelfPaced` classroom under a platform program — no synthetic per-student institute is created.

**Initial canonical seed set (day 1)**:
- `MATH-BAGRUT-3UNIT` — Bagrut Math 3-unit
- `MATH-BAGRUT-4UNIT` — Bagrut Math 4-unit
- `MATH-BAGRUT-5UNIT` — Bagrut Math 5-unit
- `MATH-SAT-700` — SAT Math
- `MATH-PSYCHOMETRY-QUANTITATIVE` — Psychometry Quantitative

Non-math subjects (Bagrut English, Hebrew, History, etc.) are **not** day-one — the BKT tracer, Elo calibration, and question bank are math-only today.

Third-party institutes can:
- **Reference** a platform program (default) — use as-is, receive automatic minor-version updates
- **Fork** — clone into the institute's namespace, edit freely, no automatic updates
- **Author** — build from a bare `CurriculumTrack`

Platform program updates:
- Minor version bump → pushed immediately to all Reference institutes
- Major version bump → Reference institutes see a "review and accept" gate on the mentor dashboard
- `Program.ContentPackVersion` drives this (semver-ish)

### Initial canonical seed defaults

These were committed as "accepted defaults" rather than deferred questions:

1. **Psychometry Quantitative in day-1 seed**: yes
2. **Self-paced cohort shape**: one shared `Mode: SelfPaced` classroom per platform program, leaderboard shows anonymized handles only
3. **Platform program updates**: immediate push for minor versions, review gate for major versions
4. **Non-math day 1**: math-only

### AssignmentDocument is Phase 2, not Phase 1

Assignments (`PushTasks` capability) are a user-facing feature, not schema scaffolding. They belong with `PersonalMentorship` in Phase 2, not with the zero-behavior-change schema lift in Phase 1. `InstructorLed` classrooms today have no assignment concept anyway — nothing regresses.

### Revised phasing

| Phase | Modes delivered | Features | Blocks on |
|---|---|---|---|
| **1 — Schema scaffold** | `SelfPaced`, `InstructorLed` (existing behavior preserved) | 4 new docs + extend `ClassroomDocument` + 8 new events + idempotent upcaster + platform seed data (Institute + 5 programs + 5 self-paced classrooms) | nothing — ships independently |
| **2 — Cross-enrollment + PersonalMentorship + Assignments** | + `PersonalMentorship` | Re-key mastery state per ADR-0002, new `AssignmentDocument`, optional `MentorNoteDocument`, enrollment switcher UI, `/api/me/enrollments` surface, onboarding V2 with platform catalog picker | ADR-0002 (VERIFY-0001) |
| **3 — Mentor admin surface** | All three modes | Mentor + Instructor Firebase custom claims, mentor dashboard UI, `Chat` capability wire-up, pending-request queue UI, fork/reference mentor workflows | Phase 2 |

### Revised Phase 1 schema (committed)

New document types:
- `InstituteDocument` — `InstituteId`, `Name`, `Type` (`platform | school | private-tutor | cram-school | ngo`), `Country`, `MentorIds[]` (owners), `CreatedAt`
- `CurriculumTrackDocument` — `TrackId`, `Code`, `Title`, `Subject`, `TargetExam`, `LearningObjectiveIds[]`, `StandardMappings[]`
- `ProgramDocument` — `ProgramId`, `InstituteId`, `TrackId`, `Title`, `Description`, `Origin` (`Platform | Forked | Custom`), `ParentProgramId?`, `ContentPackVersion`, `CreatedByMentorId`, `CreatedAt`
- `EnrollmentDocument` — `EnrollmentId`, `StudentId`, `ClassroomId`, `Status`, `EnrolledAt`, `EndedAt?`

Modified document types:
- `ClassroomDocument` — add `InstituteId`, `ProgramId`, `Mode` (`SelfPaced | InstructorLed | PersonalMentorship`), `MentorIds[]` (rename from `TeacherId`), `JoinApprovalMode`, `Status` (`active | archived | completed`), `StartDate`, `EndDate`. Keep existing `JoinCode`, `Grade`, `Subjects[]` for back-compat.

New events (in `EnrollmentEvents.cs`):
- `InstituteCreated_V1 (InstituteId, Type, Name, Country, MentorId, CreatedAt)`
- `CurriculumTrackPublished_V1 (TrackId, Code, Title, Subject, TargetExam, LearningObjectiveIds)`
- `ProgramCreated_V1 (ProgramId, InstituteId, TrackId, Title, Origin, ParentProgramId, ContentPackVersion, CreatedByMentorId)`
- `ProgramForkedFromPlatform_V1 (NewProgramId, ParentProgramId, InstituteId, ForkedByMentorId)`
- `ClassroomCreated_V1 (ClassroomId, InstituteId, ProgramId, Mode, MentorIds, JoinApprovalMode)`
- `ClassroomStatusChanged_V1 (ClassroomId, NewStatus, ChangedAt, Reason?)`
- `EnrollmentCreated_V1 (EnrollmentId, StudentId, ClassroomId, EnrolledAt)`
- `EnrollmentStatusChanged_V1 (EnrollmentId, NewStatus, ChangedAt, Reason?)`

Seed data (in `Cena.Infrastructure/Seed/`):
- `PlatformInstituteSeedData` — creates the "Cena Platform" institute (InstituteId `"cena-platform"`, Type `Platform`, MentorIds `[]`)
- `PlatformProgramSeedData` — creates 5 canonical programs + their tracks + their self-paced classrooms (one per program)
- Migration upcaster: on first read of an existing student stream, prepend a synthetic `EnrollmentCreated_V1` binding the student to whichever platform classroom best matches their stated subjects (default to Bagrut 5-unit if ambiguous; log the mapping)

Tenant scoping:
- Add `TenantScope.GetInstituteFilter(user) → IReadOnlyList<string>` returning `[student.DefaultInstituteId]` in Phase 1 (single-element list). Keep `GetSchoolFilter` as a thin alias until Phase 2 re-keys.

### Revision log

- 2026-04-11 (session 2): three classroom modes, unified Mentor role, MentorCapability flags, Program/Classroom two-level hierarchy, platform-owned canonical programs, committed seed defaults, AssignmentDocument deferred to Phase 2. Phase 1 executable as committed.
- 2026-04-11 (session 1): initial draft; Decision 1 locked per project-owner confirmation; Decision 2 staged for verification.
