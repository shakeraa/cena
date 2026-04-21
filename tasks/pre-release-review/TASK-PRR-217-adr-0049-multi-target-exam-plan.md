# TASK-PRR-217: ADR-0049 — Multi-target exam plan + Ministry codes + sitting tuple

**Priority**: P0 — foundation ADR; blocks all other epic sub-tasks
**Effort**: M (1-2 weeks — ADR drafting + 5 open-question resolutions)
**Lens consensus**: all 10 personas; red verdicts from persona-a11y + persona-ministry converge here
**Source docs**: [docs/design/MULTI-TARGET-EXAM-PLAN-001-discussion.md §14](../../docs/design/MULTI-TARGET-EXAM-PLAN-001-discussion.md), persona-ministry findings (Ministry שאלון codes), persona-educator findings (sitting taxonomy)
**Assignee hint**: human-architect + kimi-coder (ADR writing support)
**Tags**: source=multi-target-exam-plan-001, type=adr, epic=epic-prr-f, priority=p0, blocker
**Status**: Blocked — awaiting decision-holder resolution of 5 open questions (§14.5)
**Source**: 10-persona review of MULTI-TARGET-EXAM-PLAN-001
**Tier**: mvp
**Epic**: [EPIC-PRR-F](EPIC-PRR-F-multi-target-onboarding-plan.md)

---

## Goal

Author ADR-0049 locking the data model and non-negotiable principles for multi-target student exam plans, resolving the persona-review findings before any aggregate code is written.

## Scope

ADR-0049 MUST lock:

1. **Primitive**: `StudentPlan` is a list of `ExamTarget` records. No `grade`, `track`, or `deadline` at the student root.
2. **ExamTarget shape**:
   ```
   ExamTarget {
     Id: ExamTargetId
     Source: Student | Classroom | Tenant
     AssignedById: UserId              // student-id when Source=Student
     EnrollmentId: EnrollmentId?       // ADR-0001 alignment; null for Source=Student
     ExamCode: ExamCode                // catalog primary key (not display label)
     Track: TrackCode?                 // "3U" | "4U" | "5U" | "2U" | ModuleCode | null
     SittingCode: SittingCode          // {AcademicYear, Season, Moed} tuple
     WeeklyHours: int                  // 1..40
     ReasonTag: ReasonTag?             // {Retake, NewSubject, ReviewOnly, Enrichment}
     CreatedAt: DateTimeOffset
     ArchivedAt: DateTimeOffset?
   }
   ```
3. **Catalog primary key**: Ministry numeric codes (שאלון) are authoritative. `ExamCode` carries `ministrySubjectCode` + `ministryQuestionPaperCodes[]`. Display labels are localized metadata, never identifiers.
4. **Sitting tuple**: `{AcademicYear: "תשפ\"ו", Season: Summer|Winter, Moed: A|B|C|Special}`. Dereferences to a canonical date via catalog metadata. Raw `DateTimeOffset` deadlines are banned at the aggregate level.
5. **Mastery state is skill-keyed, NOT (target, skill)-keyed** (persona-cogsci blocker).
6. **Aggregate invariants**: `sum(Targets.WeeklyHours) ≤ 40`, `count(Targets) ≤ 5`, `target.Archived ⇒ immutable`. Server-enforced.
7. **Retention**: archived targets retained 24 months, user-extendable via opt-in (ADR-0003 delta).
8. **PET naming**: exam code is `PET`, not `PSYCHOMETRY`. Regulator is NITE, not Ministry of Education.
9. **Track enum**: includes `"2U"` (mandatory humanities baseline) + `ModuleCode` variant for English Modules A–G.
10. **Parent visibility default**: hidden for students ≥13; visible <13 via parent aggregate (EPIC-PRR-C); student-grants at ≥18.

## Open questions blocking ADR lock (§14.5)

Before ADR-0049 can be signed off, decision-holder resolves:

1. Arab-stream (המגזר הערבי) question-paper variant codes — Launch scope one or both streams?
2. PET Russian-verbal — Launch or Post-Launch?
3. Tenant-admin-forced plan lawful basis (privacy).
4. SAT + PET content-engineering budget owner + approval (blocks EPIC-PRR-G).
5. Paid-tier pricing floor given per-student-per-month LLM cost ceiling ~$3.30.

## Files

- `docs/adr/0049-multi-target-student-exam-plan.md` (new ADR)
- `docs/adr/INDEX.md` or similar (if maintained)
- Cross-reference updates:
  - `docs/adr/0003-misconception-session-scope.md` (declared-plan data retention delta)
  - `docs/adr/0048-exam-prep-time-framing.md` (exam-week lock clarification: scheduler-only, never UX)
  - `docs/design/MULTI-TARGET-EXAM-PLAN-001-discussion.md` (mark superseded/locked by ADR-0049)

## Definition of Done

- ADR-0049 merged to `docs/adr/` with all 10 locked items above.
- 5 open questions resolved and recorded in ADR.
- Cross-references into ADR-0001, ADR-0002, ADR-0003, ADR-0012, ADR-0048 explicit.
- Sign-off from decision-holder + persona-ministry (שאלון code accuracy) + persona-privacy (retention policy).
- Discussion brief marked "superseded by ADR-0049" in header.

## Non-negotiable references

- ADR-0001 (tenancy isolation) — `EnrollmentId?` field.
- ADR-0002 (SymPy CAS oracle).
- ADR-0003 (misconception session-scope) — retention delta documented.
- ADR-0012 (StudentActor split) — StudentPlan is a successor aggregate.
- ADR-0048 (exam-prep positive framing).
- Memory "No stubs — production grade".
- Memory "Honest not complimentary".

## Reporting

complete via: `node .agentdb/kimi-queue.js complete <id> --worker <you> --result "ADR-0049 merged at <sha>"`

## Related

- [EPIC-PRR-F](EPIC-PRR-F-multi-target-onboarding-plan.md)
- persona-ministry finding on שאלון codes
- persona-educator finding on sitting taxonomy
- persona-cogsci finding on skill-keyed mastery
