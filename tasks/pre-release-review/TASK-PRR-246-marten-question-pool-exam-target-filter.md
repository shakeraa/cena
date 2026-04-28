# TASK-PRR-246: MartenQuestionPool exam-target filter (the original session-start gap)

**Priority**: P0 — load-bearing bug; today exam-prep students get target-blind question pools
**Effort**: M (1-2 weeks; backend + projection rebuild + tests)
**Lens consensus**: implied by ADR-0050 + ADR-0060
**Source docs**: [ADR-0050](../../docs/adr/0050-multi-target-student-exam-plan.md), [ADR-0060](../../docs/adr/0060-session-mode-exam-prep-vs-freestyle.md), trace conversation 2026-04-27 (MartenQuestionPool.cs:41-43)
**Assignee hint**: kimi-coder (backend specialty + Marten projection familiarity); claude-code reviews
**Tags**: source=trace-2026-04-27, epic=epic-prr-f, priority=p0, backend, projection-rebuild, marten, bug-fix
**Status**: Blocked on PRR-247 (ADR-0060 wiring lands first; this task adds the filter behavior)
**Tier**: launch (multi-target plan is meaningless without this filter)
**Epic**: [EPIC-PRR-F](EPIC-PRR-F-multi-target-onboarding-plan.md) — sub-task

---

## Goal

Today, `MartenQuestionPool.LoadAsync` at [src/actors/Cena.Actors/Serving/MartenQuestionPool.cs:41-43](../../src/actors/Cena.Actors/Serving/MartenQuestionPool.cs#L41-L43) filters only on `q.Subject == subject && q.Status == "Published"`. A student with `ExamTarget = Bagrut Math 5U {035581, 035582, 035583}` gets the same pool as a freestyle math student. The exam-target plumbing (`ExamTarget.QuestionPaperCodes`, populated by PRR-243 onboarding) is dead-end metadata.

This task wires `SessionMode == ExamPrep` to a `QuestionPaperCodes`-aware filter and rebuilds the read-model projection to support it.

## Scope

### Backend filter

1. Extend `QuestionReadModel` ([src/actors/Cena.Actors/Questions/QuestionReadModel.cs](../../src/actors/Cena.Actors/Questions/QuestionReadModel.cs)) with `public List<string> QuestionPaperCodes { get; set; } = new();` — sourced from `QuestionDocument.BagrutAlignment.ExamCode` plus `QuestionDocument.BagrutAlignment.QuestionPaperCodes[]` if present (extend the `BagrutAlignment` record to carry the multi-paper list per ADR-0050 §1).
2. Update `QuestionListProjection` to populate `QuestionPaperCodes` on every projection event, including the rebuild path.
3. Trigger one-shot Marten projection rebuild on deploy (idempotent; document under `docs/ops/migrations/2026-04-28-question-paper-codes-backfill.md`).
4. Update `MartenQuestionPool.LoadAsync` signature per ADR-0060:
   ```csharp
   public static async Task<MartenQuestionPool> LoadAsync(
       IDocumentStore store,
       string[] subjects,
       SessionMode mode,
       IReadOnlyList<string>? questionPaperCodes,
       ILogger logger,
       CancellationToken ct = default);
   ```
   Filter behavior:
   - `ExamPrep`: `q.Subject == subject && q.Status == "Published" && q.QuestionPaperCodes.Any(c => questionPaperCodes.Contains(c))`.
   - `Freestyle`: unchanged — `q.Subject == subject && q.Status == "Published"`.
5. Update the **3 call sites** in [src/api/Cena.Student.Api.Host/Endpoints/SessionEndpoints.cs](../../src/api/Cena.Student.Api.Host/Endpoints/SessionEndpoints.cs#L179) at lines 179, 652, 1031. Each derives `mode` + `questionPaperCodes` from `SessionStartRequest` (delivered by PRR-247).
6. **No silent fallback**: if `mode == ExamPrep && (questionPaperCodes is null || questionPaperCodes.Count == 0)` the endpoint returns 400 with `error=invalid_exam_target_codes`. Validator runs ahead of pool load.

### Tests

7. Unit tests for the new `LoadAsync` overload — both modes, empty pool, large pool, non-overlapping subjects.
8. Projection rebuild integration test — confirms `QuestionPaperCodes` populated for every `Published` question after rebuild.
9. **Real E2E** (per memory "Real browser E2E with diagnostics"): two student fixtures —
   - `studentA`: `ExamTarget = Bagrut Math 5U {035581}` only.
   - `studentB`: `ExamTarget = Bagrut Math 5U {035582, 035583}`.
   Both call `POST /api/sessions/start` with `Mode: ExamPrep, Subjects: ["math"]`. Assert disjoint pool slices; assert `studentA` gets only 035581-tagged items, `studentB` only 035582/035583.
10. Endpoint validator test — `Mode: ExamPrep` without `ActiveExamTargetId` returns 400; `Mode: Freestyle` with non-null `ActiveExamTargetId` returns 400.
11. Performance regression test — projection rebuild on a 10k-question fixture completes within `OPS_BUDGET_REBUILD_SECONDS` (set per the PRR-053 capacity plan, default 60s).

### Adjacent

12. **`BagrutAlignment` record** at [src/shared/Cena.Infrastructure/Documents/QuestionDocument.cs:258](../../src/shared/Cena.Infrastructure/Documents/QuestionDocument.cs#L258) currently has `ExamCode: string` (singular). Extend to `QuestionPaperCodes: List<string>` (or keep `ExamCode` for primary subject code and add `QuestionPaperCodes`); coordinate with the in-flight `BAGRUT-ALIGN-001` task referenced at [src/shared/Cena.Infrastructure/Documents/StepSolverQuestionDocument.cs:123](../../src/shared/Cena.Infrastructure/Documents/StepSolverQuestionDocument.cs#L123). Locate that task before merge — do not duplicate work.
13. `IPromptCacheKeyContext.ExamTargetCode` already exists; verify the new pool-aware path threads it through prompt caching for variant generation hits (PRR-047 SLO floor).

## Files

### Modified
- `src/actors/Cena.Actors/Serving/MartenQuestionPool.cs` — new overload + filter
- `src/actors/Cena.Actors/Questions/QuestionReadModel.cs` — add `QuestionPaperCodes`
- `src/actors/Cena.Actors/Questions/QuestionListProjection.cs` — populate the new field
- `src/api/Cena.Student.Api.Host/Endpoints/SessionEndpoints.cs` — three call sites
- `src/shared/Cena.Infrastructure/Documents/QuestionDocument.cs` — extend `BagrutAlignment`

### New
- `docs/ops/migrations/2026-04-28-question-paper-codes-backfill.md`
- `src/actors/Cena.Actors.Tests/Serving/MartenQuestionPoolTests.cs` — extended
- E2E: `tests/e2e-flow/specs/exam-target-pool-disjoint.spec.ts`

## Definition of Done

- New overload + 3 call sites + validator merged.
- `QuestionPaperCodes` populated on 100% of Published questions post-rebuild (verified by metric query).
- `Cena.Actors.sln` full build green (per memory "Full sln build gate").
- Real-browser E2E with disjoint-pool assertion green in chrome with console + page errors capture (per memory "Real browser E2E with diagnostics").
- No silent fallback path introduced (ADR-0060 enforcement).
- BAGRUT-ALIGN-001 task located + coordinated; no duplicated work.

## Blocking

- PRR-247 (ADR-0060 wiring + `SessionStartRequest` shape) lands first.
- ADR-0060 acceptance.

## Non-negotiable references

- [ADR-0050](../../docs/adr/0050-multi-target-student-exam-plan.md), [ADR-0060](../../docs/adr/0060-session-mode-exam-prep-vs-freestyle.md), Memory "No stubs — production grade", Memory "Real browser E2E with diagnostics", Memory "Full sln build gate", Memory "Verify data E2E".

## Reporting

complete via: `node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<branch + projection rebuild migration sha + E2E spec sha>"`

## Related

- ADR-0060, PRR-247 (wiring), PRR-243 (filter source — done), PRR-218 (StudentPlan aggregate), BAGRUT-ALIGN-001 (in-flight, locate before merge).
