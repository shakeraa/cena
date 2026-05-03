# TASK-PRR-157: Fix TZ infra before calendar features

**Priority**: P1 — strongly-recommended pre-launch (lens consensus: 1)
**Effort**: M — 1-2 weeks
**Lens consensus**: persona-enterprise
**Source docs**: `AXIS_10_Operational_Integration_Features.md:L111`
**Assignee hint**: claude-code
**Tags**: source=pre-release-review-2026-04-20, lens=enterprise, origin=tight-match-audit, src-audit-id=O-056
**Status**: Done — 2026-04-20
**Source**: tight-match audit 2026-04-20 (O-056)
**Tier**: mvp
**Epic**: EPIC-PRR-A — ADR-0012 StudentActor decomposition

---

## Goal

Fix `FindSystemTimeZoneById("Israel Standard Time")` in [`PushNotificationTriggerService.cs:60`](../../src/actors/Cena.Actors/Notifications/PushNotificationTriggerService.cs#L60) that throws on Linux (actor-system-review L1). Extract shared TZ resolver. Prevent regression across repo.

### Code-reality 2026-04-20 verification

- [`OutreachSchedulerActor.cs:34-37`](../../src/actors/Cena.Actors/Outreach/OutreachSchedulerActor.cs#L34-L37) already has correct try/catch (`Asia/Jerusalem` IANA → `Israel` Windows-fallback)
- [`PushNotificationTriggerService.cs:60`](../../src/actors/Cena.Actors/Notifications/PushNotificationTriggerService.cs#L60) uses Windows-only ID `"Israel Standard Time"` directly — throws on Linux (every CI run, every prod container)

### User decision 2026-04-20 — tightened DoD

1. Extract `src/shared/Cena.Infrastructure/Time/IsraelTimeZoneResolver.cs` — single source of truth with try/catch pattern
2. Fix `PushNotificationTriggerService.cs:60` to consume resolver
3. Refactor `OutreachSchedulerActor.cs:29-39` to consume resolver (remove duplicated try/catch)
4. Linux-container integration test validates resolver on every CI run
5. Architecture test: no direct `FindSystemTimeZoneById` / `ConvertTimeBySystemTimeZoneId` calls outside the resolver
6. Repo-wide grep during implementation — fix any additional unguarded sites

## Files

- `src/shared/Cena.Infrastructure/Time/IsraelTimeZoneResolver.cs` (new)
- `src/actors/Cena.Actors/Notifications/PushNotificationTriggerService.cs`
- `src/actors/Cena.Actors/Outreach/OutreachSchedulerActor.cs`
- `tests/integration/IsraelTimeZoneResolverLinuxTests.cs`
- `tests/architecture/NoDirectSystemTimeZoneCallsTest.cs`

## Definition of Done

1. Resolver utility exists; both call sites consume it
2. Linux integration test green
3. Architecture test green (no direct TZ calls outside resolver)
4. Repo grep confirms zero additional unguarded sites
5. Full `Cena.Actors.sln` builds cleanly; all tests pass

## Rolls up into EPIC-PRR-A Sprint 1 parallel track

Ships alongside LearningSession extraction — calendar/session-start features depend on this primitive.

## Reporting

complete via: node .agentdb/kimi-queue.js complete <id> --worker kimi-coder --result "<branch>"

---

## Non-negotiable references
- None explicitly bound; all baseline non-negotiables from CLAUDE.md still apply.

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
- [Canonical task JSON](../../pre-release-review/reviews/tasks.jsonl) (id: prr-157)
- [Tight-match audit confirmed-orphans](../../pre-release-review/reviews/audit/confirmed-orphans.jsonl)
