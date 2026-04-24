# ADR-0044 — Teacher/Mentor Schedule Override Aggregate

- **Status**: Proposed
- **Date proposed**: 2026-04-20
- **Deciders**: Shaker (project owner), claude-code (coordinator)
- **Supersedes**: none
- **Related**: [ADR-0001](0001-multi-institute-enrollment.md), [ADR-0003](0003-misconception-session-scope.md), [ADR-0012](0012-aggregate-decomposition.md), prr-148 (student plan input), prr-149 (scheduler caller)
- **Task**: prr-150 (`TASK-PRR-150-mentor-tutor-override-aggregate-for-schedule.md`)

---

## Context

Today, teachers and mentors cannot influence what the scheduler does for a specific student. `MethodologyAssignment` overrides *teaching style* (Socratic vs. direct vs. worked-example) but leaves the *schedule content* — which topics to drill, how much time per week, what motivation framing to use — entirely driven by the student's own inputs and the scheduler defaults.

The pre-release-review educator, enterprise, and ministry lenses all flagged this gap independently (R-01): a tutor who sees a student failing log-scale questions cannot tell the system "keep drilling logs for the next three sessions." The workaround today is verbal — the mentor tells the student in session, and the student has to remember during self-study. That is not acceptable for a production teacher workflow.

### Why this needs a new bounded context

Per [ADR-0012](0012-aggregate-decomposition.md), `StudentActor` is the god-aggregate under decomposition. `NoNewStudentActorStateTest` is a standing CI gate that blocks new event handlers inside `StudentActor*.cs`. Any new write-side state — even a cleanly-designed one — has to land in its own bounded context.

The prr-148 `StudentPlanAggregate` (deadline + weekly budget, per-student stream `studentplan-{id}`) is the template pattern we follow here. Its sibling prr-149 reads that state + applies scheduler defaults when building `SchedulerInputs`. This ADR introduces the teacher-side analogue.

---

## Decision

Introduce a new bounded context `Cena.Actors.Teacher.ScheduleOverride` with:

1. An event-sourced aggregate `TeacherOverrideAggregate` keyed per-student (stream key `teacheroverride-{studentAnonId}`).
2. Three event types: `PinTopicRequested_V1`, `BudgetAdjusted_V1`, `MotivationProfileOverridden_V1`.
3. A command surface `TeacherOverrideCommands` that **hard-enforces the ADR-0001 tenant invariant** before appending any event.
4. A scheduler-facing bridge `IOverrideAwareSchedulerInputsBridge` that merges active overrides on top of prr-149's base `SchedulerInputs`.
5. Three admin POST endpoints behind the `ModeratorOrAbove` policy, at `/api/admin/teacher/override/{pin-topic,budget,motivation}`.

### Decision 1 — Bounded-context boundary

The override context is per student (not per teacher): the scheduler's question at session start is always "what did the mentor say about THIS student?", so keying by student matches the read path. One teacher overriding multiple students creates multiple streams, which is cheap because the streams are small and append-only.

Events on the stream live in `Cena.Actors.Teacher.ScheduleOverride.Events` and carry the teacher's pseudonymous id + the institute id for audit and tenant lineage. They do NOT live on the student's profile (ADR-0003 discipline — student profile stays thin).

### Decision 2 — Tenant invariant (ADR-0001, hard)

A teacher at institute A may never override schedule content for a student enrolled at institute B, regardless of whether they happen to know the student's pseudonymous id. Enforcement happens in `TeacherOverrideCommands.VerifyTenantScope`:

1. Resolve the student's active-enrollment institute via `IStudentInstituteLookup`.
2. Compare to the teacher's `institute_id` / `school_id` claim.
3. On mismatch (or missing enrollment, to prevent existence leaks), throw `CrossTenantOverrideDeniedException`, emit a SIEM warning log, and return 403 to the caller.

The architecture test `TeacherOverrideNoCrossTenantTest` statically verifies every public command method calls `VerifyTenantScope` BEFORE `_store.AppendAsync`. Adding a new command without the guard fails CI.

### Decision 3 — Precedence rules

```
effective = teacher override > student plan input > scheduler default
```

Concretely, the `OverrideAwareSchedulerInputsBridge` applies each dimension independently:

| Dimension | Source when no override | Source when override active |
|---|---|---|
| Weekly budget | prr-148 `StudentPlanConfig.WeeklyBudget` | `BudgetAdjusted_V1.NewWeeklyBudget` |
| Motivation profile | RDY-057 onboarding (default `Neutral`) | `MotivationProfileOverridden_V1.OverrideProfile`, scoped by session-type |
| Topic prioritisation | ability-estimate driven | active pins inject sentinel `AbilityEstimate` so the topic is scheduled |

Motivation overrides carry a `SessionTypeScope` discriminator (`"all"` | `"diagnostic"` | `"drill"` | …). An exact scope match wins; `"all"` is the fallback. Unknown scopes fall through to no-override, which is forward-compatible.

### Decision 4 — Audit trail

Every override endpoint is a POST under `/api/admin/**`, which is already captured by `AdminActionAuditMiddleware` (user id, role, tenant id, action, target type/id, client IP, result). No bespoke audit plumbing is added. Cross-tenant denials are logged at WARN with the `[TEACHER_OVERRIDE_CROSS_TENANT_DENIED]` SIEM tag so redteam forensics can correlate abuse attempts across tenants.

### Decision 5 — Storage (Phase 1 → Phase 2)

Phase 1 ships `InMemoryTeacherOverrideStore` — same rationale as prr-148's in-memory StudentPlan store: the read path is on the hot scheduler loop, pod-restart durability loss translates to "teacher re-applies the override", and shipping Marten plumbing now would entrench schema decisions before the aggregate catalog stabilises under ADR-0012.

Phase 2 swaps in a Marten-backed store. The interface (`ITeacherOverrideStore`) does not change; the implementation does. No consumer migration is required.

`IStudentInstituteLookup` is likewise in-memory in Phase 1 (registered via `AddTeacherOverrideServices`). The admin API host is expected to replace it with a Marten-backed concrete that queries `EnrollmentDocument` for the student's active enrollment — this is the Phase-2 follow-up that unlocks production use.

---

## Consequences

### Positive
- Teachers gain a real write surface for schedule content without touching `StudentActor` (ADR-0012 compliant).
- Tenant isolation is enforced by construction at the command layer and verified by an architecture test, not a runtime checklist.
- Override history is a proper event stream — no lost intent, full replay for forensics or re-simulation.
- The scheduler's base logic (prr-149) is unchanged; the bridge is an opt-in layer, so existing Student.Api.Host callers continue to work without override wiring.

### Negative / trade-offs
- Phase-1 in-memory stores mean overrides are lost on Admin API pod restart. Accepted: teachers re-apply; follow-up task TENANCY-related will add Marten backing.
- `IStudentInstituteLookup` must be replaced with a Marten-backed implementation before real multi-institute use. The in-memory default is safe for single-institute Phase-1 deployments but will silently no-op tenant checks (deny-all) against a real Marten-backed student roster until the replacement lands.
- The architecture test pins the command regex to three known DTO types. Adding a fourth command requires updating both the commands class and the regex — this is intentional friction for a tenant-isolation gate.

### Failure modes (3am-on-Bagrut-morning runbook)
- **Override not applying**: check `GET /api/admin/teacher/override/...` returned 200 (look at the `AdminActionAuditMiddleware` Marten query). If 200 but the scheduler ignored it: verify `AddTeacherOverrideServices()` is registered in both Admin.Api.Host AND whichever host runs the session plan generator (prr-149 already reads the bridge). Missing registration → bridge is null → base inputs pass through unchanged.
- **Cross-tenant 403 storm**: check `cena_teacher_override_cross_tenant_denied_total` Prometheus counter. A spike almost always means a teacher's institute claim is wrong (backfill missed them, or they switched institutions without a re-login).
- **Pin not biasing scheduler**: pins only add a sentinel `AbilityEstimate` when the slug is absent from the student's own estimates. If the student has a strong estimate for the pinned slug already, the scheduler will still rank it correctly — pins are a floor, not a ceiling.

---

## Out of scope (follow-ups)

- Pin consumption/decrement: Phase 1 does not wire the session-end signal that decrements `RemainingSessions`. Tracked as prr-150-follow-up.
- Vue admin UI: Phase 1 ships a minimal skeleton (`StudentScheduleOverride.vue`) wired to the endpoints; full UX polish (student-picker, in-place edit, history view) is a follow-up.
- Marten-backed `IStudentInstituteLookup`: see Decision 5 above.
- Parent visibility into teacher overrides: not in scope here — parents do not see schedule internals (ADR-0041 role split).
