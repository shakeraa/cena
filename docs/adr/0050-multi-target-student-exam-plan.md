# ADR-0050 — Multi-target student exam plan (grade is per-target, not per-student)

- **Status**: Accepted (draft pending final sign-off)
- **Date proposed**: 2026-04-21
- **Deciders**: Shaker (project owner), claude-code (coordinator); 10-persona review consensus
- **Supersedes**: single-target `StudentPlanConfig` assumption implicit in [PRR-148](../../tasks/pre-release-review/done/TASK-PRR-148-student-input-ui-for-adaptivescheduler-deadline-weekly-ti.md)
- **Related**:
  - [ADR-0001 (tenant isolation)](0001-multi-institute-enrollment.md) — `EnrollmentId?` on ExamTarget
  - [ADR-0002 (SymPy CAS oracle)](0002-sympy-correctness-oracle.md) — applies to SAT math + PET quantitative identically to Bagrut
  - [ADR-0003 (misconception session-scope)](0003-misconception-session-scope.md) — declared-plan data retention delta (bounded at 24m)
  - [ADR-0012 (StudentActor split)](0012-student-actor-split.md) — `StudentPlan` is a successor aggregate
  - [ADR-0026 (LLM three-tier routing)](0026-llm-three-tier-routing.md) — cost ceiling preserved
  - [ADR-0038 (event-sourced RTBF)](0038-event-sourced-right-to-be-forgotten.md) — `ExamTarget*` events crypto-shreddable
  - [ADR-0043 (Bagrut reference-only)](0043-bagrut-reference-only-enforcement.md) — past-Bagrut corpus rules
  - [ADR-0048 (exam-prep time framing)](0048-exam-prep-time-framing.md) — 14-day exam-week lock is scheduler-only, never UX
- **Source**: [docs/design/MULTI-TARGET-EXAM-PLAN-001-discussion.md](../design/MULTI-TARGET-EXAM-PLAN-001-discussion.md) + 10-persona findings under [pre-release-review/reviews/persona-*/multi-target-exam-plan-findings.md](../../pre-release-review/reviews/)
- **Lens consensus**: all 10 personas (2 red verdicts addressed: a11y + ministry)
- **Epic**: [EPIC-PRR-F](../../tasks/pre-release-review/EPIC-PRR-F-multi-target-onboarding-plan.md)

---

## Context

Before this decision, Cena's student aggregate carried `grade` + `track` as direct attributes and `StudentPlanConfig` held a single `{DeadlineUtc, WeeklyTimeBudget}` pair. A 2026-04-21 audit surfaced three facts that invalidate the model:

1. Three of six real student personas (gap-year, self-learner, adult / career-switcher) have **no meaningful "grade"** — the abstraction was Bagrut-specific, projected onto personas it didn't fit.
2. Two high-value personas (Grade-12 student, Bagrut-retake candidate) have **multiple concurrent exam targets** with distinct deadlines and per-target hour allocations — single-target state produced garbage for half their study time.
3. [PRR-148](../../tasks/pre-release-review/done/TASK-PRR-148-student-input-ui-for-adaptivescheduler-deadline-weekly-ti.md) was marked Done 2026-04-20 but its DoD #1 (onboarding integration) was never met — the component exists orphaned, the single-target VO was shipped without its UX, and retrofitting multi-target post-launch would trip the 2026-04-11 "no Phase 1 stub → Phase 1b real" ban.

The 10-persona review confirmed the data model needs to be rewritten before any further engineering, returning 2 red verdicts (persona-a11y on the onboarding UX, persona-ministry on the catalog primary key) and 8 other yellows. The user's 2026-04-21 directive was "all options on release day" — SAT + PET + Arab-stream + humanities all Launch-gated.

---

## Decision

**A student's learning plan is a list of `ExamTarget` records. There is no `grade`, `track`, or `deadline` at the student root.** The following ten items are locked; no design in `src/` violates them without a superseding ADR.

### 1. `ExamTarget` shape (normative)

```csharp
public record ExamTarget(
    ExamTargetId    Id,
    ExamTargetSource Source,          // Student | Classroom | Tenant
    UserId          AssignedById,     // studentId when Source=Student
    EnrollmentId?   EnrollmentId,     // ADR-0001; null for Source=Student
    ExamCode        ExamCode,         // catalog primary key (not display label)
    TrackCode?      Track,            // "2U" | "3U" | "4U" | "5U" | ModuleCode | null
    SittingCode     Sitting,          // {AcademicYear, Season, Moed} tuple
    int             WeeklyHours,      // 1..40
    ReasonTag?      ReasonTag,        // {Retake, NewSubject, ReviewOnly, Enrichment}
    DateTimeOffset  CreatedAt,
    DateTimeOffset? ArchivedAt        // null when active
);

public record StudentPlan(IReadOnlyList<ExamTarget> Targets);
```

No free-text fields. Free-text note was proposed in the discussion brief and **rejected** by four-lens convergence (ethics + privacy + redteam + finops); `ReasonTag` enum replaces it.

### 2. Catalog primary key is the Ministry numeric code (שאלון), not the display label

`ExamCode` is a stable identifier. The catalog carries `ministrySubjectCode` (e.g. `035` for math) + `ministryQuestionPaperCodes[]` (e.g. `035581`, `035582`, `035583` for the three Math 5U שאלונים). Display names are localized metadata per locale. Any downstream artefact claiming Ministry alignment (rubric DSL, coverage matrix, school export) ties to the numeric codes, not the labels.

### 3. Sittings are named tuples, not free dates

`SittingCode` = `{AcademicYear: "תשפ״ו", Season: Summer|Winter, Moed: A|B|C|Special}`. The canonical date is derived by the catalog. Raw `DateTimeOffset` deadlines are banned at the aggregate level — they flatten the moed taxonomy (מועד א / ב / ג / מיוחד) and misalign with Ministry reporting.

### 4. Mastery state is skill-keyed, NOT `(target, skill)`-keyed

Mastery posterior lives at `(studentId, skillId)`. A student preparing for Bagrut Math 5U **and** PET Quant shares skills (Pythagoras, quadratics, combinatorics); their quadratics mastery is one posterior, not two. Per-attempt events retain `targetId` for analytics, but projections aggregate skill-globally. Without this rule, overlap cases produce phantom weakness and double cold-start cost (persona-cogsci blocker).

### 5. Aggregate invariants are server-enforced

Every write through the aggregate validates:

- `sum(active Targets.WeeklyHours) ≤ 40`
- `count(active Targets) ≤ 5` (soft-warn UI at 4, hard cap server at 5)
- `target.Archived ⇒ no further Updated/Archived events` (archived state is terminal)
- `{examCode, sittingCode, track}` unique across active targets per student

Client-only enforcement is a redteam surface; the aggregate is the source of truth.

### 6. Retention: 24 months post-archive, user-extendable to 60 months

Per PPL Amendment 13 purpose-limitation + GDPR Art. 5(1)(e), "indefinite retention" is not permissible for declared-plan data. Default is 24 months after `ArchivedAt`; the student can opt-in `retain_exam_history: true` for 60 months (max). The retention worker (extending [PRR-015](../../tasks/pre-release-review/TASK-PRR-015-register-every-new-misconception-pii-store-with-retentionwor.md)) crypto-shreds per the schedule. This is distinct from [ADR-0003](0003-misconception-session-scope.md)'s misconception-data rule (30-day session scope) — declared-plan is a separate category, but bounded.

### 7. PET, not PSYCHOMETRY

The `ExamCode` for the Psychometric Entrance Test is `PET`. Regulator is NITE (not Ministry of Education). Catalog entries for PET carry `regulator: nite`. This is a correctness requirement, not a stylistic one — downstream reporting paths discriminate by regulator.

### 8. Track enum includes `2U` and supports `ModuleCode`

`TrackCode` is `"2U" | "3U" | "4U" | "5U" | ModuleCode | null`. `"2U"` covers the Israeli mandatory-humanities baseline (persona-educator). `ModuleCode` is a distinct shape ("A" through "G") used by Bagrut English Modules, which are not unit-counted the same way.

### 9. Parent visibility defaults

- Students <13: visible via parent aggregate (COPPA + [ADR-0042](0042-consent-aggregate-bounded-context.md)).
- Students 13–17: **hidden by default**; student can grant explicit share.
- Students ≥18: never visible to parents; share requests are rejected.

On a student's 18th birthday, any existing parent grants auto-revoke.

### 10. 14-day exam-week lock is scheduler-only, never UX

When any active target's `canonical_date - today ≤ 14 days`, the scheduler locks session selection to that target. There is **no student-facing indicator** of this lock — no "exam week", no "days remaining", no countdown. Per [ADR-0048](0048-exam-prep-time-framing.md). The shipgate scanner v2 ([PRR-224](../../tasks/pre-release-review/TASK-PRR-224-shipgate-scanner-v2-multi-target-bans.md)) extends to ban identifier names like `daysUntil`, `countdown`, `streak`, `timeLeft` anywhere in `src/`.

---

## Resolved open questions

### Q1 — Arab-stream (המגזר הערבי) Bagrut variants at Launch

**Resolution**: Both streams at Launch. שאלון code variants per stream encoded in the catalog ([PRR-239](../../tasks/pre-release-review/TASK-PRR-239-arab-stream-bagrut-variants.md)). Arab-stream content authored against Arab-stream past-שאלונים corpus (PRR-242).

### Q2 — PET Russian-verbal section at Launch

**Resolution**: At Launch ([PRR-240](../../tasks/pre-release-review/TASK-PRR-240-pet-russian-verbal-section.md)). Native-Russian authoring, not machine-translated. Serves the FSU-olim population.

### Q3 — Tenant-admin-forced plan lawful basis

**Resolution**: Legitimate interest via active enrollment contract + transparency via `Source=Classroom|Tenant` field on `ExamTarget` (student sees who assigned it). For students <18 the school's DPA with the parent-aggregate ([ADR-0042](0042-consent-aggregate-bounded-context.md)) governs. For students ≥18 the enrollment contract itself is the basis; the student retains the right to archive (not unassign — archive with `Source` preserved for audit) any target at any time. No separate consent dialog per assignment; one-time transparency copy on first classroom-assigned target explains the mechanism.

### Q4 — EPIC-PRR-G content-engineering budget owner + approval

**Resolution**: Approved. Owner: Shaker (project owner). Budget envelope: ~$20-30k one-shot + ~$500-1,500/quarter refresh (revised from unbounded ~$40-60k thanks to PRR-242 corpus enabler). Allocation priority:

1. [PRR-242](../../tasks/pre-release-review/TASK-PRR-242-past-bagrut-corpus-ingestion.md) past-Bagrut corpus ingestion first (~$3-5k infra + SME time). Unlocks downstream cost reduction.
2. SAT + PET item banks (~$8-12k).
3. Arab-stream + humanities extensions (~$8-12k).

### Q5 — Paid-tier pricing floor given ~$3.30/student/month LLM cost ceiling

**Resolution**: Approved unit economics. The 3-tier routing ([ADR-0026](0026-llm-three-tier-routing.md)) stands — no re-architecting for cheaper LLM. Paid-tier pricing will be set above the ~$3.30 floor with margin; the specific public price is a business decision and not this ADR's scope. Cost observability per-target-count via [PRR-233](../../tasks/pre-release-review/TASK-PRR-233-prompt-cache-slo-per-target.md) ensures we detect cache-hit regressions early.

---

## Consequences

### Good

- Multi-target personas (Grade-12, retake candidates, gap-year PET prep, adult SAT prep) get correct scheduling from day one.
- Scheduler and mastery projection align with cognitive-science reality (skill-keyed, spacing via cross-session alternation).
- Retention is legally defensible under PPL + GDPR.
- Catalog is future-proof for tenant overlays without retrofit.
- Past-Bagrut corpus ingestion (PRR-242) cuts content-engineering cost ~50% and establishes a reusable pipeline for future exam catalogs.

### Costs

- Onboarding grows from 6 steps to 8 steps (`exam-targets` + `per-target-plan`).
- Engineering timeline: 12-14 weeks for EPIC-PRR-F (up from 8 weeks pre-expansion).
- Content-engineering timeline: 16-20 weeks for EPIC-PRR-G, parallelizable across SMEs.
- Launch blocked on both engineering and content-engineering critical paths being green. No single-target fallback.

### Risks accepted

- **Scope expansion without slip**: if either critical path slips, launch slips. Per "Honest not complimentary" memory, we do not ship with empty item banks or single-target fallback.
- **Persona-cogsci effect-size honesty**: cross-session alternation gives spacing benefit (Cepeda d≈0.5) but not the Rohrer-cherry-picked d≈1.05 discrimination effect; within-session interleaving in [PRR-237](../../tasks/pre-release-review/TASK-PRR-237-within-session-cross-target-interleaving.md) captures the latter where appropriate. No product copy overstates expected gains.
- **Persona-a11y VDatePicker RTL risk**: Vuetify's date picker has no verified Hebrew locale. Onboarding uses catalog-sitting radio list instead; settings page date-picker work is gated on a three-locale prototype before shipping.
- **Magen capture stays deferred**: per ministry compliance (false-authority risk). Revisit post-Launch only if formal Mashov integration lands with school as authoritative source.

---

## Operational rules

### For code review

- Any PR that introduces `grade` or `track` on the student root is rejected (belongs on `ExamTarget`).
- Any PR that introduces `DateTimeOffset Deadline` on an aggregate boundary is rejected (use `SittingCode`).
- Any PR that introduces a mastery projection key of `(target, skill)` is rejected (skill-global only).
- Any PR that renders `daysUntil`, `countdown`, `streak`, `timeLeft` etc. in a student-facing surface is rejected by the shipgate scanner (PRR-224).

### For new work

Every task that touches StudentActor / AdaptiveScheduler / onboarding / catalog / coverage-matrix / rubric-DSL must reference this ADR in its PR description and demonstrate the rules above are respected.

---

## History

- 2026-04-21 proposed: 10-persona review + user scope-expansion + content-budget approval.
- 2026-04-21 accepted (draft): pending final read-through by Shaker; no material changes expected.
