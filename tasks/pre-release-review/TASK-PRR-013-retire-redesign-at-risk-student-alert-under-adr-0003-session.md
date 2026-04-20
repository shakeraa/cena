# TASK-PRR-013: Redesign 'At-Risk Student Alert' — honest + supportive + legal

**Priority**: P0 — ship-blocker (lens consensus: 5)
**Effort**: S — 1-2 days
**Lens consensus**: persona-redteam, persona-ministry, persona-ethics, persona-educator, persona-cogsci
**Source docs**: `cena_competitive_analysis.md:L91`, `cena_competitive_analysis.md:L122`
**Assignee hint**: claude-subagent-adr-authoring
**Tags**: source=pre-release-review-2026-04-20, lens=ministry, product-stance=honest-supportive-legal
**Status**: Backend retirement complete — 2026-04-20; Vue SPA retirement pending (Phase 2)

**Backend completion 2026-04-20 (this pass)**:

- `AtRiskStudentDocument` moved to `src/shared/Cena.Infrastructure/Documents/Legacy/AtRiskStudentDocument.Legacy.cs` with `[Obsolete]`; schema registration removed from `MartenConfiguration.cs`.
- `ClassMasteryRollupDocument.AtRiskCount` field dropped from the persisted rollup.
- `AdminAnalyticsSeedData` no longer seeds `AtRiskStudentDocument` records and no longer sets `AtRiskCount`.
- `/api/admin/mastery/at-risk` mapped to a 410 Gone response with a `retired per prr-013` message for ~6 months so in-flight clients fail loudly.
- `ExamSimulationSubmitted_V2` is the only shape new emitters construct (no readiness bounds on-stream). V1 is `[Obsolete]`, retained only for historical Marten replay; a codebase grep confirmed zero production emitters. V1→V2 migration note added to ADR-0043 (sibling change section).
- `NoAtRiskPersistenceTest` / `NoThetaInOutboundDtoTest` allowlist comments updated: V1 readiness fields remain allowlisted (read-only legacy); V2 is NOT allowlisted so any regression fails the build.
- New integration test: `src/actors/Cena.Actors.Tests/Assessment/ExamSimulationSubmittedV2Tests.cs` covers (a) V2 shape has no readiness fields, (b) no production code path constructs V1, (c) V1 historical replay is still constructable and projects down to V2 shape by dropping readiness.

**Still pending (Phase 2, separate follow-up task)**:

- Vue SPA at-risk pages under `src/admin/full-version/src/` (admin dashboard "Students Needing Intervention" view, any composables / stores still referencing the retired route).
- The session-actor-side `SessionRiskAssessment` wiring into a live in-session teacher view (scaffold exists; live computation path is RDY-080 / EPIC-PRR-A Sprint-2 work).
- ADR `NNNN-at-risk-session-surface.md` if the session-scope + in-surface + CI-bounded policy needs its own dedicated ADR beyond the ADR-0003 / RDY-080 anchors.

**Source**: Synthesized from 10-persona pre-release review (2026-04-20) — see `/pre-release-review/reviews/SYNTHESIS.md`. **User decision 2026-04-20**: the original swarm recommendation to "soften the label" is overridden — harsh-reality numbers ARE the target voice; persistence, externalization, and confidence-binding are the hard constraints.
**Tier**: mvp
**Epic**: EPIC-PRR-A — ADR-0012 StudentActor decomposition

---

## Goal

Redesign the "At-Risk Student" concept under three hard constraints (session-scope, in-surface-only, confidence-bound) using **honest reality-based language**, not soft euphemism. Author an ADR that locks all three constraints + the language policy.

### Product stance (user-directed, do not soften)

- Tell the student and teacher the **actual numbers** in session. "You answered 40% of mastery-threshold problems correctly today — that's below the 5-unit trajectory." Harsh is fine; patronizing is not.
- Euphemism ("needs support", "room to grow", "opportunity area") is the **anti-pattern** here. It reads as complimentary-avoidance and damages trust when the data eventually contradicts the soft label.
- Show the uncertainty: every harsh number ships with its confidence interval and sample size. A number without uncertainty is dishonest.

### Hard constraints (NOT tone-negotiable)

1. **Session-scoped only** — assessment expires at session end. Never persisted to student profile, never stored in any aggregate beyond the session actor, never rebuilt from event history. (ADR-0003.)
2. **In-surface only** — student + teacher see it during the session. Never emitted to parent SMS/WhatsApp, never passed back to Google Classroom / Mashov, never loaded in a dashboard tomorrow. (RDY-080, ministry defensibility.)
3. **Confidence-bounded** — risk assessments carry interval + sample size. UI must render both. No naked point estimates.

## Files

- `docs/adr/NNNN-at-risk-session-surface.md` — new ADR covering all three constraints + language policy
- `src/actors/Cena.Actors/Sessions/LearningSessionActor.cs` — session-scoped risk assessment computation, no persistence beyond session
- `src/actors/Cena.Actors/Sessions/SessionRiskAssessment.cs` — new value object carrying `(point, confidenceInterval, sampleSize, generatedAt)`
- `src/student/full-version/src/components/session/TrajectoryIndicator.vue` — honest-language UI component (template for student view)
- `src/admin/full-version/src/views/apps/session/TeacherSessionView.vue` — teacher-side view (same data, teacher framing)
- `tests/arch/NoAtRiskPersistenceTest.cs` — architecture test asserting no DTO outside the session boundary carries a risk field
- `tests/arch/NoAtRiskExternalEmissionTest.cs` — architecture test asserting no outbound adapter (parent notification, SIS passback, webhook) emits risk fields
- `src/student/full-version/tests/e2e/trajectory-indicator-honest-language.spec.ts` — e2e asserting the UI shows numbers + CI, no softening copy
- AXIS_5 / AXIS_6 / cena_competitive_analysis doc updates — reflect the redesign; remove "at-risk" dashboard framing

## Definition of Done
- ADR accepted and committed.
- `SessionRiskAssessment` value object exists; carries point + CI + sample size; lives inside the session actor; is NOT included in `StudentState` or any snapshot.
- Two architecture tests green: (a) no field named `risk*` / `atRisk*` / `bagrutRisk*` appears on any type reachable from persistence or external DTO contracts; (b) no outbound-adapter payload contract references risk fields.
- Teacher session view + student session view both render the honest-language number with CI; e2e test asserts the exact copy template is used, and that none of the banned soft words (`support`, `needs`, `opportunity`, `room to grow`, `at risk`, `concerning`) appear in the trajectory component.
- Ship-gate scanner extended with the banned-euphemism list; CI fails on any commit introducing them in trajectory copy.
- Cross-check: `RetentionWorker` confirms nothing under the risk-assessment namespace persists past session end.
- Full `Cena.Actors.sln` builds cleanly; every test passes.

## Reporting
complete via: node .agentdb/kimi-queue.js complete <id> --worker claude-subagent-adr-authoring --result "<branch>"

---

## Non-negotiable references
- #3: No dark-pattern engagement (streaks, loss-aversion, variable-ratio banned)
- RDY-080

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
- [Canonical task JSON](../../pre-release-review/reviews/tasks.jsonl) (id: prr-013)
