---
persona: educator
subject: MULTI-TARGET-EXAM-PLAN-001
date: 2026-04-21
verdict: yellow
---

## Summary

The list-of-targets model is correct and overdue — grade was never a student attribute in the Israeli system; it is always per-subject per-sitting. But the brief treats "Bagrut sitting" as a free-form date picker, which is wrong: the Ministry schedules **named moadim** (מועד א / מועד ב / חורף / קיץ) and a student in real life registers for one, not for a calendar date. The catalog is also incomplete for any genuine teacher-classroom use case (no Hebrew language, no history, no literature, no civics, no Tanakh), which means the "classroom-assigned plan" pathway cannot ship v1 even if the plumbing is built — there's nothing for the homeroom teacher to assign. 3U/4U/5U labeling is wrong-shape for English and should never appear on any non-Bagrut subject. SAT + Psychometry as launch blockers is acceptable in principle but the brief underestimates how much of the catalog-metadata + rubric work is Bagrut-specific and does not transfer. Verdict yellow because the data model decision is right but the catalog, sitting taxonomy, and classroom story are not ship-ready.

## Section 9.1 answers

### Is the exam catalog complete for the v1 market?

No. The v1 catalog as listed covers the STEM Bagrut track plus SAT/PET. That is **not** a teacher-classroom catalog — it is an AI-tutor catalog. For any secondary-school teacher to assign a class plan, at minimum the following Ministry subjects need to exist in the catalog even if item-bank coverage is deferred (they can be marked `itemBankStatus: reference-only` so the system stops pretending):

- `BAGRUT_HEBREW_EXPRESSION` (הבעה עברית / לשון) — 2U mandatory, the single most-taught Bagrut exam in Israel
- `BAGRUT_LITERATURE` (ספרות) — 2U mandatory for non-secular and secular tracks
- `BAGRUT_HISTORY` (היסטוריה) — 2U mandatory
- `BAGRUT_CIVICS` (אזרחות) — 2U mandatory
- `BAGRUT_TANAKH` (תנ"ך) — 2U mandatory in state-religious and 2U in many state-secular schools
- `BAGRUT_ARABIC_AS_L2` (ערבית) — elective, large population in state-secular schools

Omitting these and calling v1 "multi-target" is the kind of labels-don't-match-data bug the user repeatedly flags. Recommendation: include them in the catalog as entries whose `itemBankStatus = reference-only`; the onboarding can accept them as declared targets, but the scheduler explicitly declines to schedule sessions ("Cena doesn't practice [Literature] yet — we track the date so your study plan stays realistic"). Honest, shippable, and unlocks the classroom assignment flow for homeroom teachers.

### Is 3U/4U/5U the right abstraction?

Yes for math and physics; **wrong-shape for English and the humanities**. In canonical Ministry terminology the unit counts are יחידות לימוד (yechidot limud, "study units"). For Math the rungs are genuinely 3/4/5. For English the Ministry uses **modules** (Module A–G / E/F/G tiers), and the 3U/4U/5U shorthand maps loosely but is how schools and students actually talk — so for English treat 3U/4U/5U as the user-facing label but pin the underlying module set in the catalog metadata so rubric and item-bank selection know which modules to target. For Biology/Chemistry/CS the only real Bagrut rung is 5U (3U variants essentially don't exist in practice), so the brief is right to list only 5U — but be ready to add a `5U+לאב` (laboratory-component) attribute, because the 30%-lab portion is a real sub-track that changes the syllabus. For Hebrew/History/Civics/Literature/Tanakh the canonical rungs are 2U base / 3U extended / 5U major — so the `TrackCode` needs to admit `"2U"` too, not just `"3U" | "4U" | "5U"`. Right now 2U isn't in the enum and that quietly blocks the catalog expansion above.

### Does the deadline picker surface all legitimate Bagrut sitting windows?

**No**, and this is the one thing I would fail the brief on outright. Section 4's "Jan, Jun, Aug" is wrong-grain. The Ministry's canonical sitting taxonomy is:

- **מועד קיץ (Moed Summer / Moed A)** — May–June, the primary sitting
- **מועד ב' (Moed Bet)** — July–August, second-chance sitting for students who missed or failed Moed A; this is a **distinct named sitting** with its own registration, not "Jun and Aug" as though they were interchangeable
- **מועד חורף (Moed Winter)** — January, for math/English (mostly retake candidates and 12th-grade staggered-exam tracks)
- **מועד ג' (Moed Gimel)** — rare, IDF-service candidates and exceptional circumstance; catalog can defer but the taxonomy needs to exist
- **מועד מיוחד** — special makeup sittings for specific cohorts

A student in the real world does not pick "June 15"; they register for "קיץ תשפ"ו מועד א'" and the Ministry assigns the exact date. The data model needs a `SittingCode` (e.g. `SUMMER_2027_MOED_A`) with a resolved date, not a free `Deadline: DateTimeOffset`. The free-date picker is an American-SAT abstraction that leaks back onto Bagrut incorrectly. Resolved date can still live on the record for scheduler math, but the **identity** of the sitting is `(examCode, year, moedCode)`. This also matters for the Moed Bet retake pathway (section 9.1 prompt 3), for Ministry reporting alignment with persona-ministry, and for what the homeroom teacher actually tells the class ("we're targeting מועד א' קיץ תשפ"ו").

### Teacher-assigned class plan vs student personal picks — override or coexist?

**Coexist, with discriminator — and this should be decided now, not v2, or you will retrofit ugly.** Realistic teacher workflow: the homeroom teacher (mechanech/mechanechet) assigns "Bagrut Math 5U, מועד א' קיץ תשפ"ז" to the whole class. The student is **also** doing Bagrut Physics 5U on their own, and privately preparing for Psychometry. The teacher's assigned target cannot be silently edited or archived by the student (it's not theirs to archive), but the student's personal targets are their own. The clean way is `ExamTarget.source: student | classroom | tenant` with `editableBy` rules:

- `source = classroom` → student can see it, can see assigned weekly hours, cannot delete/archive (only the teacher can); can adjust own weekly-hour *allocation* within the classroom-assigned envelope
- `source = student` → full student control
- `source = tenant` → institute-admin-forced (v2 is fine, but the discriminator shape must exist v1)

Without this discriminator the first teacher rollout will immediately produce the bug "my student archived the class's Bagrut Math target because it cluttered their dashboard." Interacts with PRR-058 teacher accommodations and PRR-037 grade-passback — both assume class-level authority over a student's plan.

### Are "not sure yet — skip" and "add later" acceptable under teacher workflows?

Acceptable for the self-learner/gap-year/Psychometry personas. **Not acceptable** when the student joined via a classroom code — in that path the classroom-assigned target is non-skippable by definition (the teacher has already decided). So the skip button must be conditional: if `classroomCode != null` and the classroom has assigned targets, those are pre-populated and locked; the student can still add personal targets or skip the personal-target step, but cannot skip past the classroom ones. Teachers will also want a "plan-lock window" (e.g. last 6 weeks before Bagrut Moed A, students cannot remove classroom-assigned targets) — defer that UI to v2 but put the `lockedUntil: DateTimeOffset?` field on `ExamTarget` now.

## Additional findings

- **The syllabus-coverage-matrix (PRR-072) and rubric DSL (PRR-033) must be per-`(exam, track, moed-year)` triples, not per-`(exam, track)` pairs.** Ministry revises syllabi between academic years; the Bagrut 2027 Math 5U syllabus differs from 2026 on specific units. Pinning the catalog entry to an academic year (תשפ"ו / תשפ"ז) is the right primary key.
- **Magen grade (ציון מגן) is not captured anywhere and should be.** It's the teacher-assessed 30% (or sometimes 50%) component that combines with the Bagrut exam itself to produce the final grade. Cena is in the perfect position to surface a projected magen (based on class-level performance, teacher-weighted) but only via the teacher dashboard, never the student dashboard — student-facing magen projection is a score-prediction dark-pattern and PRR-019 bans it. Add `ExamTarget.magenCapturedByTeacher: bool` now as a placeholder even if v1 doesn't surface it.
- **"Bagrut retake candidate" persona** (section 2) needs a first-class retake marker. A retake has a prior grade, a specific Moed Bet or Moed Winter sitting, and a different study pattern (targeted gap-fill, not full syllabus). Model it as `ExamTarget.priorAttempt: { grade, moedCode } | null` — retakes are common in Israel.
- **Language-of-exam is missing from the data model.** Arabic-sector students sit a Hebrew Bagrut in *Arabic-language* exam booklets for many subjects. This is not a UI-language preference, it's a Ministry-issued version of the exam with different language-of-instruction and sometimes different rubrics. `ExamTarget.examLanguage: he | ar | en` is needed distinct from `student.uiLanguage`.
- **"Weekly hours" per target on its own is a bad planning abstraction for classroom-assigned plans** because the teacher controls class time, not the student. A classroom-assigned target should have `teacherAllocatedHours` separate from `studentAllocatedHours` so the scheduler can reason about both.
- **ExamPlanStep.vue vs SyllabusMap.vue**: the brief is right to retire OnboardingCatalogPicker.vue, but I would keep SyllabusMap.vue — students need to see topic-level coverage inside a target (which 5U Math units they've covered, which are still untouched), and that's the natural home for it. Wire it into `PerTargetPlanStep` as a preview under "expand syllabus" rather than retiring it.

## Section 10 positions

- **(3) Max targets cap — 4 or higher?** I'd go **5**, not 4. Real ceiling: 3 Bagrut STEM (Math + Physics + CS) + 1 mandatory humanity (Literature or History, once the catalog is extended) + 1 standardized test (SAT or PET) = 5. Retake candidates can legitimately stack 2 retakes + 2 current + 1 PET. Capping at 4 forces the student to lie on onboarding. Keep the "total weekly hours" warning at >40h as the real constraint; let the target count breathe.
- **(4) "Not sure yet, skip" during onboarding** — conditional, not binary. Require ≥1 target **if** the signup path is `classroomCode != null` (the teacher already picked). Allow skip for standalone signups, and nag on first home visit (but not as a modal — as a dashboard card). This matches the honest-UX rule: we don't pretend they have a plan when they don't.
- **(5) Classroom-assigned targets v1 or v2?** The full teacher UI can be v2. The `source` discriminator + `classroomCode` linkage + "locked-by-teacher" flag on ExamTarget **must be v1** — because retrofitting a discriminator onto an event-sourced aggregate after real user events exist is painful and ugly. Put the shape in now, even if the teacher UI that writes to it ships v2. One-day-of-work difference now, six-months-of-pain difference later.
- **(1) SAT + PET full v1** (already resolved) — my only add: the Ministry's Psychometry (the PET) is administered by NITE (המרכז הארצי לבחינות ולהערכה), not the Ministry of Education. The sittings are April / July / September / December as listed. The Verbal Reasoning section has **three exam languages** (Hebrew / Arabic / Russian — yes, Russian is still an official PET language as of 2026 for olim) and the English section is fixed. If v1 ships only Hebrew Verbal, say so out loud in the catalog metadata (`availableLanguages: ["he"]`) and don't let an Arabic-speaker tap-select PET only to hit an empty item bank. That's the no-stubs rule applied to catalog honesty.

## Recommended new PRR tasks

| ID-placeholder | Title | Why | Priority | Effort |
|---|---|---|---|---|
| PRR-220 | Ministry sitting taxonomy (`SittingCode`) in catalog + data model | Replace free-form deadline with named moadim; required for Ministry reporting alignment and teacher workflows | P0 | M |
| PRR-221 | Extend catalog with humanities Bagrut entries (Hebrew, Literature, History, Civics, Tanakh, Arabic-L2) as `itemBankStatus: reference-only` | Without these the classroom-assignment story can't ship — homeroom teachers teach these, not math | P0 | S |
| PRR-222 | Add `ExamTarget.source` discriminator (`student` / `classroom` / `tenant`) + `lockedUntil` field to v1 schema, even if classroom-write UI is v2 | Retrofitting a discriminator post-launch onto event-sourced aggregate is painful; shape it now | P0 | S |
| PRR-223 | Add `"2U"` to `TrackCode` enum + allow it on humanities catalog entries | Unblocks humanities catalog; current enum quietly forbids legitimate tracks | P1 | S |
| PRR-224 | `ExamTarget.priorAttempt` field for retake candidates (grade + moedCode) | Retakes are a first-class persona in the brief; currently invisible in the data model | P1 | S |
| PRR-225 | `ExamTarget.examLanguage` separate from UI language | Arabic-sector Bagrut is sat in Arabic; not the same as UI language. Ministry-issued variant | P1 | S |
| PRR-226 | `ExamTarget.magenCapturedByTeacher` placeholder + teacher-only magen projection (post-launch UI, field-only v1) | Magen is 30–50% of final grade; student-facing projection is banned by PRR-019, teacher-facing projection is valid | P2 | S |
| PRR-227 | Catalog version pinning to academic year (תשפ"ו / תשפ"ז) in PRR-072 coverage matrix and PRR-033 rubric DSL | Syllabi change between years; without year-pin we quietly mis-grade | P1 | M |
| PRR-228 | SAT + PET catalog `availableLanguages` honesty metadata + onboarding gate | No-stubs rule: don't let an Arabic-speaker pick PET if only Hebrew verbal ships v1 | P1 | S |
| PRR-229 | "Not sure, skip" conditional logic: required ≥1 target if classroomCode present, skip allowed otherwise | Teacher-path vs self-signup asymmetry; matters for classroom rollout | P2 | S |

## Blockers / non-negotiables

- **Blocker 1**: Free-form `Deadline: DateTimeOffset` on `ExamTarget` without a `SittingCode` ships a Bagrut system that doesn't speak the Ministry's taxonomy. This must be fixed before ADR-0049 is drafted, not after. Interacts directly with the persona-ministry lens.
- **Blocker 2**: Catalog without Hebrew/History/Literature/Civics/Tanakh as at least `reference-only` entries means "multi-target" is a math-and-standardized-tests product, not a Bagrut product — which contradicts the brief's framing. Ship the catalog entries even if the item bank is empty; otherwise the classroom-assignment story has no surface area.
- **Non-negotiable**: `source` discriminator on `ExamTarget` must be in the v1 event schema. Retrofitting discriminators onto event-sourced aggregates post-launch is the single most expensive class of data migration and the user has already made his opinion on stub/retrofit patterns clear (memory: "no stubs — production grade", 2026-04-11).

## Questions back to decision-holder

1. **Moed Bet (מועד ב') as a first-class sitting type**: confirm this goes into v1 taxonomy, not v2. Retake candidates use this sitting almost exclusively and they're persona #6 in section 2.
2. **Classroom-code signup path**: is the existing `classroomCode` field in ExamPlanStep.vue (currently hardcoded `null`) wired to a real classroom lookup in v1, or is classroom-join deferred entirely? If deferred, then the `source` discriminator can ship v1 with only `student` values in practice — but the field still needs to exist.
3. **Magen grade capture**: do you want teacher-entered magen in v1 (even if only visible to teacher), or strictly out of scope until post-launch? This affects whether `magenCapturedByTeacher` is a data-model field or a pure future placeholder.
4. **PET Russian verbal**: is a Russian-speaking verbal item bank in scope for v1 given the olim population, or does v1 ship Hebrew + Arabic verbal only with explicit `availableLanguages` honesty? Russian is a non-trivial content-engineering commitment on top of the already-declared SAT + PET scope.
