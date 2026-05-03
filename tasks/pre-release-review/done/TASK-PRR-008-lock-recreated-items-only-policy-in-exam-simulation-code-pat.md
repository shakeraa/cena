# TASK-PRR-008: Lock 'recreated items only' policy in exam-simulation code path

**Priority**: P0 — ship-blocker (lens consensus: 3)
**Effort**: M — 1-2 weeks
**Lens consensus**: persona-enterprise, persona-ministry, persona-educator
**Source docs**: `AXIS_6_Assessment_Feedback_Research.md:L73`
**Assignee hint**: claude-subagent-bagrut-fidelity
**Tags**: source=pre-release-review-2026-04-20, lens=ministry
**Status**: Done — 2026-04-20
**Source**: Synthesized from 10-persona pre-release review (2026-04-20) — see `/pre-release-review/reviews/SYNTHESIS.md`
**Tier**: mvp

---

## Goal

Enforce 2026-04-15 "Bagrut reference-only" decision at compile-time + runtime + test layers. Substrate already built (`BagrutRecreation.cs`, `ExamSimulationMode.cs`, `ExamSimulationEvents.cs`, tests); the delivery-side invariant enforcement is the gap.

### User decision 2026-04-20 — tightened DoD

- **Compile-time**: `Provenance` enum (`AiRecreated | TeacherAuthoredOriginal | MinistryBagrut`); phantom-type / DU prevents `MinistryBagrut` from ever being `Deliverable<T>`
- **Runtime**: single `IItemDeliveryGate.AssertDeliverable` chokepoint at last-moment-before-serialization; throws on non-recreated + SIEM-logs with actor/session/tenant/item-id
- **Arch test**: no student-facing DTO field reachable from MinistryBagrut-provenanced source
- **Negative integration test**: seed Ministry item → attempt delivery → gate throws + SIEM log + no event persisted + 5xx response (bug, not graceful fallback)
- **Cross-ref ADR-0032** (write-side CAS ingestion) in new invariant's code comment

## Files

- `src/actors/Cena.Actors/Content/Provenance.cs` (new)
- `src/actors/Cena.Actors/Assessment/IItemDeliveryGate.cs` (new + impl)
- `src/actors/Cena.Actors/Assessment/ExamSimulationMode.cs` (consume gate)
- `src/actors/Cena.Actors/Events/ExamSimulationEvents.cs` (`DeliveredItem` records provenance)
- `tests/architecture/BagrutRecreationOnlyTests.cs`
- `tests/integration/ExamSimulation.ReferenceOnlyEnforcement.Tests.cs`
- `docs/adr/NNNN-bagrut-reference-only-enforcement.md` (optional small ADR)

## Definition of Done

1. `Provenance` compile-time phantom-type makes `MinistryBagrut` un-deliverable
2. `IItemDeliveryGate` chokepoint routes every student-delivery path
3. Arch test + negative integration test both green
4. SIEM captures actor/session/tenant/item-id on attempt
5. Full `Cena.Actors.sln` builds; existing tests pass
6. ADR (if written) cross-links ADR-0032 + non-negotiable #4

## Reporting

complete via: node .agentdb/kimi-queue.js complete <id> --worker claude-subagent-bagrut-fidelity --result "<branch>"

---

## Non-negotiable references
- Bagrut reference-only policy

## Implementation Protocol — Senior Architect

Implementation of this task must be driven by a senior-architect mindset, not a checklist. Before writing any code, the implementer (human or agent) must answer both sets of questions in writing — either in a task-comment, the PR description, or a `docs/decisions/` note:

### Ask why
- **Why does this task exist?** Read the source-doc lines cited above and the persona reviews in `/pre-release-review/reviews/persona-*/` that raised it. If you cannot restate the motivation in one sentence, do not start coding.
- **Why this priority?** Read the lens-consensus list. Understand which persona lens raised it and what evidence they cited.
- **Why these files?** Trace the data flow end-to-end. Verify the files listed are the right seams. A bad seam invalidates the whole task.
- **Why are the non-negotiables above relevant?** Show understanding of how each constrains the solution, not just that they exist.

### Ask how
- **How does this interact with existing aggregates and bounded contexts?** Name them.
- **How does it respect tenant isolation (ADR-0001), event sourcing, the CAS oracle (ADR-0002), and session-scoped misconception data (ADR-0003)?**
- **How will it fail?** What's the runbook at 03:00 on a Bagrut exam morning? If you cannot describe the failure mode, the design is incomplete.
- **How will it be verified end-to-end, with real data?** Not mocks. Query the DB, hit the APIs, compare field names and tenant scoping — see user memory "Verify data E2E" and "Labels match data".
- **How does it honor the <500 LOC per file rule, the no-stubs-in-prod rule, and the full `Cena.Actors.sln` build gate?**

### Before committing
- Full `Cena.Actors.sln` must build cleanly (branch-only builds miss cross-project errors — learned 2026-04-13).
- Tests cover golden path **and** edge cases surfaced in the persona reviews.
- No cosmetic patches over root causes. No "Phase 1 stub → Phase 1b real" pattern (banned 2026-04-11).
- No dark-pattern copy (ship-gate scanner must pass).
- If the task as-scoped is wrong in light of what you find, **push back** and propose the correction via a task comment — do not silently expand scope, shrink scope, or ship a stub.

### If blocked
- Fail loudly: `node .agentdb/kimi-queue.js fail <task-id> --worker <you> --reason "<specific blocker, not 'hard'>"`.
- Do not silently reduce scope. Do not skip a non-negotiable. Do not bypass a hook with `--no-verify`.

### Definition of done is higher than the checklist above
- Labels match data (UI label = API key = DB column intent).
- Root cause fixed, not masked.
- Observability added (metrics, structured logs with tenant/session IDs, runbook entry).
- Related personas' cross-lens handoffs addressed or explicitly deferred with a new task ID.

**Reference**: full protocol and its rationale live in [`/tasks/pre-release-review/README.md`](../../tasks/pre-release-review/README.md#implementation-protocol-senior-architect) (this section is duplicated there for skimming convenience).

---

## Related
- [Full synthesis](../../pre-release-review/reviews/SYNTHESIS.md)
- [Retired proposals](../../pre-release-review/reviews/retired.md)
- [Conflicts needing decision](../../pre-release-review/reviews/conflicts.md)
- [Canonical task JSON](../../pre-release-review/reviews/tasks.jsonl) (id: prr-008)
