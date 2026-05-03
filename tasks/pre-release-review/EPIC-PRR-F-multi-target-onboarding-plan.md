# EPIC-PRR-F: Multi-target student exam plan + onboarding

**Priority**: P0
**Effort**: XL (epic-level: 4-8 weeks aggregate across 18 sub-tasks)
**Lens consensus**: persona-educator, persona-cogsci, persona-ethics, persona-a11y (red), persona-enterprise, persona-ministry (red), persona-sre, persona-redteam, persona-privacy (yellow→red), persona-finops
**Source docs**: [docs/design/MULTI-TARGET-EXAM-PLAN-001-discussion.md](../../docs/design/MULTI-TARGET-EXAM-PLAN-001-discussion.md), persona findings under [pre-release-review/reviews/persona-*/multi-target-exam-plan-findings.md](../../pre-release-review/reviews/)
**Assignee hint**: human-architect (ADR-0049 lock) + parallel subagents per sub-task
**Tags**: source=multi-target-exam-plan-001, type=epic, epic=epic-prr-f
**Status**: Not Started
**Source**: 10-persona review 2026-04-21 of MULTI-TARGET-EXAM-PLAN-001 discussion brief. Replaces the deficient single-target assumption from PRR-148.

---

## Epic goal

Cena's student learning plan is a **list of ExamTargets**, not a single deadline+hours pair. Grade is per-target, not per-student. Onboarding captures one or more targets from a catalog (Bagrut subjects, PET, SAT); each target has its own sitting + weekly budget + mastery track. The scheduler picks a target per session via a silent proximity/round-robin policy. All downstream surfaces (coverage matrix, rubric DSL, scheduler, settings, parent visibility, RTBF cascade) become per-target.

## Architectural substrate

- `StudentPlan` aggregate (in StudentActor's ADR-0012 successor) owns a list of `ExamTarget` records.
- `ExamTarget` is event-sourced. Key shape: `{ExamTargetId, Source, AssignedById, EnrollmentId?, Exam, Track, SittingCode, WeeklyHours, ReasonTag?, CreatedAt, ArchivedAt?}`. No free-text.
- `SittingCode` is a canonical tuple `{AcademicYear, Season, Moed}` that dereferences to a date + Ministry שאלון codes.
- Mastery state is **skill-keyed**, not `(target, skill)`-keyed (see PRR-222).
- Catalog is server-driven with Global + TenantOverlay shape (see PRR-220).
- Retention: 24 months post-archive, user-extendable (see PRR-229).

## Launch blocker status

This epic is a **launch blocker** for the multi-target product positioning. It also has a **parallel launch blocker** in [EPIC-PRR-G](EPIC-PRR-G-sat-pet-content-engineering.md) (SAT + PET item bank content). Shipping requires both epics green.

## Absorbed / superseded tasks

- **PRR-148** — Student-input UI for AdaptiveScheduler. Marked Done but DoD-incomplete on onboarding wiring. Superseded by this epic; close-out is PRR-234.

## Sub-task map

| ID | Title | Priority | Role |
|---|---|---|---|
| [PRR-217](TASK-PRR-217-adr-0049-multi-target-exam-plan.md) | ADR-0049 — Multi-target exam plan + Ministry codes + sitting tuple | P0 | foundation ADR |
| [PRR-218](TASK-PRR-218-studentplan-aggregate-events.md) | StudentPlan aggregate + ExamTarget events (source, enrollment, sitting, reason-tag) | P0 | foundation |
| [PRR-219](TASK-PRR-219-migration-safety-net.md) | Migration safety net (feature-flagged staged upcast + retry + DLQ) | P0 | foundation |
| [PRR-220](TASK-PRR-220-exam-catalog-service.md) | Catalog service (Global + TenantOverlay + offline fallback + CDN runbook) | P0 | foundation |
| [PRR-221](TASK-PRR-221-onboarding-ui-exam-targets-per-target-plan.md) | Onboarding UI: `exam-targets` + `per-target-plan` steps (a11y + RTL VDatePicker prototype/fallback) | P0 | feature |
| [PRR-222](TASK-PRR-222-skill-keyed-mastery-state.md) | Skill-keyed mastery state + dedup invariant | P0 | foundation |
| [PRR-223](TASK-PRR-223-rtbf-cascade-exam-target.md) | RTBF cascade for ExamTarget + derived projections | P0 | privacy |
| [PRR-224](TASK-PRR-224-shipgate-scanner-v2-multi-target-bans.md) | Shipgate scanner v2 — ban `streak`/`countdown`/`daysUntil`/`days-remaining`/`timeLeft` identifiers + amber/red CSS on nag | P0 | ship-gate |
| [PRR-225](TASK-PRR-225-remove-streak-leak-progress-time.md) | Remove pre-existing streak leak in `src/student/.../pages/progress/time.vue` | P0 | bug fix |
| [PRR-226](TASK-PRR-226-scheduler-active-exam-target.md) | Scheduler ActiveExamTargetId + silent exam-week lock + TZ-safe determinism | P1 | feature |
| [PRR-227](TASK-PRR-227-settings-edit-exam-targets.md) | `/settings/study-plan` edit UI (archive + add + edit) | P1 | feature |
| [PRR-228](TASK-PRR-228-per-target-diagnostic-blocks.md) | Per-target diagnostic blocks (replaces unified diagnostic) | P1 | feature |
| [PRR-229](TASK-PRR-229-retention-policy-24-months.md) | 24-month post-archive retention policy + user-extend opt-in | P1 | privacy |
| [PRR-230](TASK-PRR-230-parent-visibility-default-hidden.md) | Parent visibility default-hidden 13+, visible <13 (consent surface) | P1 | privacy |
| [PRR-231](TASK-PRR-231-amend-capacity-plan-sat-pet.md) | Amend PRR-053 capacity plan (SAT+PET 7-window compound calendar) | P1 | SRE |
| [PRR-232](TASK-PRR-232-realize-prr-032-numerals-preference.md) | Realize PRR-032 (numerals preference task — create the ghost reference) | P1 | a11y |
| [PRR-233](TASK-PRR-233-prompt-cache-slo-per-target.md) | Prompt cache hit SLO per target + finops observability | P1 | finops |
| [PRR-234](TASK-PRR-234-close-out-prr-148-superseded.md) | Close out PRR-148 (mark superseded + legacy `StudentPlanConfig` removal) | P2 | housekeeping |
| [PRR-235](TASK-PRR-235-ministry-reporting-export-spec.md) | Ministry reporting export endpoint shape (spec-only at Launch) | P2 | spec |
| [PRR-236](TASK-PRR-236-classroom-assigned-target-teacher-ui.md) | Classroom-assigned target teacher UI | P1 | teacher feature (promoted to Launch 2026-04-21) |
| [PRR-237](TASK-PRR-237-within-session-cross-target-interleaving.md) | Within-session cross-target interleaving | P1 | scheduler (promoted to Launch 2026-04-21) |
| [PRR-238](TASK-PRR-238-retake-cohort-surface.md) | Retake-cohort surface + retrieval-strength framing | P1 | pedagogy (promoted to Launch 2026-04-21) |
| [PRR-243](TASK-PRR-243-bagrut-question-paper-multi-pick.md) | Bagrut שאלון multi-pick sub-step + per-שאלון sitting override (extends ADR-0050) | P0 | aggregate + UI (added 2026-04-21) |
| [PRR-247](TASK-PRR-247-adr-0060-session-mode-wiring.md) | ADR-0060 acceptance + SessionMode discriminator wiring (gates PRR-246) | P0 | contract + ADR (added 2026-04-28) |
| [PRR-246](TASK-PRR-246-marten-question-pool-exam-target-filter.md) | MartenQuestionPool exam-target filter — closes original 2026-04-27 trace gap | P0 | backend + projection (added 2026-04-28) |

## Blockers outbound

- ADR-0049 draft (PRR-217) is gated on 5 open decision-holder questions from brief §14.5 — Arab-stream variants, PET Russian-verbal scope, tenant-admin-forced plan lawful basis, SAT+PET content budget owner, paid-tier pricing floor.

## Non-negotiable references

- ADR-0001 (tenancy isolation) — `EnrollmentId?` on ExamTarget.
- ADR-0002 (SymPy CAS oracle) — applies to SAT math + PET quantitative identically to Bagrut.
- ADR-0003 (misconception session-scope) — declared-plan data is distinct category, but retention bounded (24 mo).
- ADR-0012 (StudentActor split) — StudentPlan is a successor aggregate, coordinated with PRR-002.
- ADR-0026 (3-tier LLM routing) — per-target context must not flip Haiku → Sonnet.
- ADR-0048 (exam-prep positive framing) — 14-day exam-week lock is scheduler-only, never UX copy.
- Memory "No stubs — production grade" (2026-04-11) — no "phase 1 stub → phase 1b real" split allowed.
- Memory "Full sln build gate" (2026-04-13) — full Cena.Actors.sln builds before merge.
- Memory "Honest not complimentary" (2026-04-20) — harsh-honest numbers + CIs; no soft euphemism.

## Implementation protocol

Per [tasks/pre-release-review/README.md](README.md#implementation-protocol-senior-architect). Each sub-task requires "Ask why / Ask how / Before committing" in its PR description.

## Related

- [MULTI-TARGET-EXAM-PLAN-001 discussion brief](../../docs/design/MULTI-TARGET-EXAM-PLAN-001-discussion.md)
- [EPIC-PRR-G — SAT + PET content engineering](EPIC-PRR-G-sat-pet-content-engineering.md)
- Persona findings under [pre-release-review/reviews/persona-*/multi-target-exam-plan-findings.md](../../pre-release-review/reviews/)
- Superseded: [PRR-148](done/TASK-PRR-148-student-input-ui-for-adaptivescheduler-deadline-weekly-ti.md)
