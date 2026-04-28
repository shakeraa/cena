# TASK-PRR-247: ADR-0060 acceptance + SessionMode discriminator wiring

**Priority**: P0 — gates PRR-246 and the reference library variant route in ADR-0059
**Effort**: M (1 week; ADR sign-off + contract change + back-compat shim)
**Lens consensus**: implied by ADR-0050 + freestyle-as-peer-mode decision 2026-04-27
**Source docs**: [ADR-0060 (proposed)](../../docs/adr/0060-session-mode-exam-prep-vs-freestyle.md), trace conversation 2026-04-27/28
**Assignee hint**: claude-code (coordinator owns ADR sign-off + contract change); backend-eligible for kimi-coder after sign-off
**Tags**: source=trace-2026-04-27, epic=epic-prr-f, priority=p0, adr, contract-change, back-compat
**Status**: Ready (ADR-0060 drafted; awaits Shaker acceptance)
**Tier**: launch
**Epic**: [EPIC-PRR-F](EPIC-PRR-F-multi-target-onboarding-plan.md) — sub-task

---

## Goal

Move ADR-0060 from Proposed to Accepted and wire the `SessionMode = ExamPrep | Freestyle` discriminator end-to-end through the SessionStart request shape, server-side validator, and frontend SessionSetupForm. Unblocks PRR-246 (the actual filter behavior) and ADR-0059 (variant route depends on Freestyle session mode).

## Scope

### ADR

1. ADR-0060 read-through by Shaker; address any deltas; flip Status: Proposed → Accepted; record sign-off in History.

### Contract

2. Update `SessionStartRequest` in [src/api/Cena.Api.Contracts/Sessions/SessionDtos.cs](../../src/api/Cena.Api.Contracts/Sessions/SessionDtos.cs#L78-L81) per ADR-0060 §Decision:
   ```csharp
   public sealed record SessionStartRequest(
       string[] Subjects,
       int DurationMinutes,
       SessionMode Mode,
       SessionPedagogy Pedagogy,
       string? ActiveExamTargetId = null);
   ```
   Plus the two enums `SessionMode` and `SessionPedagogy` in the same namespace.
3. Back-compat shim: accept the legacy shape `{Subjects, DurationMinutes, Mode: "practice|challenge|review|diagnostic"}` for **one release cycle**. Map per ADR-0060: legacy `Mode` → new `Pedagogy`; new `Mode` derives from whether the calling student has any active `ExamTarget` (yes → ExamPrep with the most-recently-active target; no → Freestyle). Document the shim in `docs/api/CHANGELOG-2026-04-28-session-start.md` with a removal milestone (next release).
4. Server validator: reject `Mode == ExamPrep && ActiveExamTargetId == null` with 400; reject `Mode == Freestyle && ActiveExamTargetId != null` with 400. Lives at endpoint, not in pool loader.

### Backend wiring

5. Update [src/api/Cena.Student.Api.Host/Endpoints/SessionEndpoints.cs](../../src/api/Cena.Student.Api.Host/Endpoints/SessionEndpoints.cs) — endpoint reads `Mode`, resolves `ActiveExamTargetId` to `QuestionPaperCodes` via the StudentPlan aggregate read, passes both to the (new) `MartenQuestionPool.LoadAsync` overload (delivered by PRR-246 — this task fixes the wire path; PRR-246 fixes the filter behavior).
6. SignalR session-summary, replay export, history list endpoints — confirm they accept the new shape; update DTO consumers.

### Frontend

7. [src/student/full-version/src/components/session/SessionSetupForm.vue](../../src/student/full-version/src/components/session/SessionSetupForm.vue) — add a mode toggle (Vuetify segmented button: ExamPrep | Freestyle) with smart default per ADR-0060 §Risks (default ExamPrep when student has ≥1 active target, else Freestyle).
8. When ExamPrep is selected, surface an active-target picker (defaults to most-recently-active; chevron opens the multi-target list). When Freestyle, hide the target picker.
9. Localize new copy in en/he/ar with `<bdi dir="ltr">` around שאלון codes.

### Tests

10. Contract round-trip tests — legacy and new shapes both deserialize correctly within the shim window; new shape only after window closes.
11. Validator unit tests — every invalid combo from §4 returns 400.
12. Endpoint integration test — ExamPrep without active target → 400; Freestyle with active target id → 400.
13. Frontend Vitest — toggle behavior, default-to-ExamPrep when targets exist, default-to-Freestyle when not.
14. E2E — two flows: student with targets in ExamPrep launches a session; same student switches to Freestyle and launches.

## Files

### Modified
- `docs/adr/0060-session-mode-exam-prep-vs-freestyle.md` — Status flip
- `src/api/Cena.Api.Contracts/Sessions/SessionDtos.cs` — new shape + enums
- `src/api/Cena.Student.Api.Host/Endpoints/SessionEndpoints.cs` — validator + wire path
- `src/student/full-version/src/components/session/SessionSetupForm.vue` — mode toggle
- `src/student/full-version/src/plugins/i18n/locales/{en,he,ar}.json` — copy

### New
- `docs/api/CHANGELOG-2026-04-28-session-start.md` — back-compat shim removal milestone
- Tests across contract / validator / E2E.

## Definition of Done

- ADR-0060 Accepted with Shaker sign-off recorded.
- Contract change + shim merged; legacy clients confirmed working under the shim.
- All call sites of the old shape updated; type system compiles green across `Cena.Actors.sln`.
- Frontend mode toggle ships and passes a11y review for keyboard / screen-reader.
- E2E test exercises both modes successfully.
- Coordinated handoff to PRR-246 documented in queue (PRR-246 unblocks once this lands).

## Blocking

- ADR-0060 read-through and acceptance by Shaker (1-2 days).

## Non-negotiable references

- [ADR-0060](../../docs/adr/0060-session-mode-exam-prep-vs-freestyle.md), Memory "No stubs — production grade", Memory "Math always LTR".

## Reporting

complete via: `node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<branch + ADR-0060 acceptance sha + CHANGELOG sha>"`

## Related

- ADR-0060, PRR-246 (downstream), ADR-0059 (Freestyle variant route depends on this), PRR-218 (StudentPlan aggregate read).
