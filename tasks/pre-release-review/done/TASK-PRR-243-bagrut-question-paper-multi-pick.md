# TASK-PRR-243: Bagrut שאלון multi-pick sub-step + per-שאלון sitting override

**Priority**: P0 — extends ADR-0050 data model; gate for PRR-218 + PRR-221
**Effort**: M (1-2 weeks)
**Lens consensus**: persona-educator, persona-ministry (both already called out that Math 5U = 3 שאלונים)
**Source docs**: [ADR-0050](../../docs/adr/0050-multi-target-student-exam-plan.md), user decision 2026-04-21 (Option B)
**Assignee hint**: kimi-coder + educator review
**Tags**: source=multi-target-exam-plan-001, epic=epic-prr-f, priority=p0, ui, catalog, aggregate
**Status**: Ready (ADR-0050 updated with the data-model delta)
**Source**: User product call 2026-04-21 — Bagrut שאלון model matches Ministry reality
**Tier**: launch
**Epic**: [EPIC-PRR-F](EPIC-PRR-F-multi-target-onboarding-plan.md)

---

## Goal

Bagrut subjects are structured as multiple שאלונים (question papers) per track — e.g. Math 5U = 035581 + 035582 + 035583. A student typically prepares a *subset* of שאלונים for a specific sitting, and may split שאלונים across sittings (e.g. שאלון 1 in Grade 11, שאלונים 2+3 in Grade 12). The single-sitting `ExamTarget` from ADR-0050 (Item 1) collapses this reality. This task adds the שאלון multi-pick sub-step to onboarding + settings and extends `ExamTarget` to carry `QuestionPaperCodes`.

## Scope

### Data model delta (extends ADR-0050 Item 1)

```csharp
public record ExamTarget(
    ExamTargetId    Id,
    ExamTargetSource Source,
    UserId          AssignedById,
    EnrollmentId?   EnrollmentId,
    ExamCode        ExamCode,
    TrackCode?      Track,
    IReadOnlyList<string> QuestionPaperCodes,      // NEW — non-empty for Bagrut; empty for SAT/PET
    SittingCode     Sitting,                        // primary sitting (applies to all papers unless overridden)
    IReadOnlyDictionary<string, SittingCode>? PerPaperSittingOverride, // NEW — e.g. {"035581": Grade11-Summer-A}
    int             WeeklyHours,
    ReasonTag?      ReasonTag,
    DateTimeOffset  CreatedAt,
    DateTimeOffset? ArchivedAt
);
```

Aggregate invariants (server-enforced; extends ADR-0050 Item 5):

- `ExamCode` family is Bagrut ⇒ `QuestionPaperCodes.Count ≥ 1` and each code exists in catalog for `(ExamCode, Track)`.
- `ExamCode` family is Standardized (SAT/PET) ⇒ `QuestionPaperCodes.Count = 0`.
- `PerPaperSittingOverride` keys ⊆ `QuestionPaperCodes` (can't override a paper you didn't pick).
- `PerPaperSittingOverride[code] == Sitting` is rejected as a no-op (keep the map minimal).

### Onboarding sub-step

After a student picks a Bagrut target in `exam-targets`, and after they pick a track in `per-target-plan`:

1. **Show the שאלון list** for that (exam, track) combo, pulled from catalog `question_papers[]`.
2. Each שאלון renders with: code (e.g. `035581`), short topic description in locale (e.g. "algebra, functions, differential calculus"), units label, optional "typically taken in" hint.
3. **Default all checked** (reflects the common case — students prep full set).
4. Student can uncheck any subset; must have ≥1 checked.
5. Optional expand: "Take some שאלונים at different sittings?" reveals per-שאלון sitting override (radio list of alternate sittings per paper). Most students don't toggle this.
6. Non-Bagrut targets (SAT, PET) skip this sub-step entirely.
7. Sub-step fits within the overall 8-step onboarding budget — it's a continuation of `per-target-plan`, not an 9th top-level step.

### Settings (reuses PRR-227 pattern)

- `/settings/study-plan` edit-target form includes the שאלון multi-pick + per-שאלון sitting override controls for Bagrut targets.
- Adding a שאלון post-hoc: explicit event `QuestionPaperAdded` on aggregate.
- Removing a שאלון from an active target: `QuestionPaperRemoved` event. Rejects if it would violate invariants.

### Copy guardrails

- Never render raw שאלון code alone — always with a localized topic description. `<bdi dir="ltr">035581</bdi>` for RTL locales per memory "Math always LTR".
- "Not sure which שאלונים you'll take? Keep them all checked — you can change this later." (persona-ethics: non-shaming default).

## Files

- `docs/adr/0050-multi-target-student-exam-plan.md` — ADR updated with the QuestionPaperCodes + PerPaperSittingOverride fields (this task actually creates the update; initial ADR landed 2026-04-21 without them).
- `src/actors/Cena.Actors/Students/ExamTarget.cs` — extended VO.
- `src/actors/Cena.Actors/Students/ExamTargetEvents.cs` — new events `QuestionPaperAdded`, `QuestionPaperRemoved`, `PerPaperSittingOverrideSet`, `PerPaperSittingOverrideCleared`.
- `src/actors/Cena.Actors/Students/StudentActor.Commands.cs` — command handlers + invariants.
- `src/student/full-version/src/components/onboarding/QuestionPaperMultiPick.vue` (new)
- `src/student/full-version/src/components/onboarding/PerTargetPlanStep.vue` — extend with the sub-step.
- `src/admin/full-version/src/pages/settings/study-plan.vue` — mirror in settings UI.
- Tests: invariants (empty for SAT/PET, non-empty for Bagrut), override map scoped correctly, add/remove events idempotent, onboarding happy-path across all three locales.

## Definition of Done

- Aggregate + events + endpoints live per updated ADR-0050.
- Onboarding sub-step renders for Bagrut, skipped for SAT/PET, passes a11y + RTL review.
- Settings UI allows post-hoc add/remove of שאלונים.
- Per-שאלון sitting override exercised in E2E test for a staged-sitting student.
- Server invariants pass property-tests against malicious inputs.
- ADR-0050 delta committed in the same PR as the aggregate change.
- Full `Cena.Actors.sln` builds cleanly.

## Non-negotiable references

- [ADR-0050](../../docs/adr/0050-multi-target-student-exam-plan.md) (this task extends Items 1 and 5).
- ADR-0001 (tenancy).
- Memory "No stubs — production grade".
- Memory "Math always LTR".

## Reporting

complete via: `node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<branch + ADR-0050 delta sha>"`

## Related

- PRR-218 (aggregate — this task extends its schema).
- PRR-220 (catalog `question_papers[]` exposure).
- PRR-221 (onboarding step; extends scope).
- PRR-227 (settings UI; extends scope).
- ADR-0050.
