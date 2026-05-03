# TASK-PRR-204: Backend — in-session tutor context API + session-scoped pre-seed

**Priority**: P0 — ship-blocker (unblocks prr-207)
**Effort**: M — 4-6 days
**Lens consensus**: persona-privacy, persona-redteam, persona-enterprise, persona-cogsci, persona-sre, persona-ethics
**Source docs**: `docs/adr/0003-misconception-session-scope.md`, `docs/adr/0001-multi-institute-enrollment.md`, `docs/research/cena-question-engine-architecture-2026-04-12.md:§7.1`
**Assignee hint**: claude-subagent-tutor-api (backend)
**Tags**: source=pre-release-review-2026-04-20, epic=epic-prr-e, lens=privacy+redteam+enterprise
**Status**: Not Started
**Source**: Epic PRR-E, 2026-04-20
**Tier**: mvp

---

## Goal

Add a session-bound tutor API that lets the existing `TutorActor` serve an in-session sidekick drawer (prr-207). Messages are pre-seeded with the current question, the student's last attempt, the hint rungs consumed, and the current step state — without leaking that context past the session boundary. Respects ADR-0003 (no profile-scoped misconception persistence), ADR-0001 (tenant isolation), and the "coach, not answer key" invariant.

## Files

- `src/api/Cena.Admin.Api/Sessions/TutorContextEndpoint.cs` (new)
- `src/actors/Cena.Actors/Tutoring/SessionTutorContextBuilder.cs` (new) — assembles the pre-seed from session state + question + rungs consumed
- `src/actors/Cena.Actors/Tutoring/TutorAnswerLeakGuard.cs` (new) — output-side filter that blocks "the answer is X" disclosures on MCQ/step-solver
- `src/actors/Cena.Actors/Tutoring/TutorPromptInjectionGate.cs` (new) — input-side AIMDS integration
- `src/actors/Cena.Actors/Tutoring/TutorPromptScrubber.cs` (extend) — PII scrubber in input path
- `src/actors/Cena.Actors/Tutoring/TutorActor.cs` (extend to accept `SessionTutorContext`)
- `src/actors/Cena.Actors.Tests/Tutoring/SessionTutorContextBuilderTests.cs`
- `src/actors/Cena.Actors.Tests/Tutoring/TutorAnswerLeakGuardTests.cs`

## Non-negotiable references

- ADR-0003 (misconception session scope) — context is read from session state only; session-end emits a flush event that invalidates any cached pre-seed.
- ADR-0001 (tenant isolation) — every request tenant-scoped; cross-institute thread access = 403 + audit log entry.
- ADR-0002 (SymPy oracle) — tutor cannot disclose an MCQ correct answer or a step-solver expected expression as a string; it can only confirm CAS-equivalent expressions the student has submitted.
- ADR-0045 (tutor tier) — tagged `[TaskRouting(3, "socratic_question")]`; shares `ExplanationCacheService` with L3 hints.

## Definition of Done

- Endpoint `POST /api/sessions/{sid}/tutor/turn` takes `{ intent: "explain-question"|"explain-step"|"explain-concept"|"free-form", userMessage?: string, stepIndex?: number }` and returns streaming tutor response.
- `SessionTutorContextBuilder` produces a pre-seed containing: question stem, question methodology, last attempt, rungs consumed, current step state, misconception tally (session-scoped). Nothing else. No profile data. No cross-session history.
- `TutorAnswerLeakGuard` runs on output: detects patterns like "the answer is", "choose B", direct expected-expression strings; on match, re-prompts the tutor with a "coach-only" steer or replaces the output with a canned "I won't hand you the answer, but here's what to notice" response. Guard itself is deterministic (no LLM in the guard).
- `TutorPromptInjectionGate` calls `aidefence_analyze` on every inbound turn; high-confidence injection = reject with a 400 + audit log entry. No silent pass.
- PII scrubbing on user input before persistence of the transcript (`TutorPromptScrubber`).
- Session-end flush: on `SessionCompleted_V2` event, purge any pre-seed cache entries keyed by that session id. Verified by integration test that asserts no entry remains.
- Tenant isolation: cross-tenant access test passes (attempt from tenant A to tenant B's session = 403).
- Metrics: `cena_tutor_turn_total{intent,result}`, `cena_tutor_injection_rejected_total`, `cena_tutor_answer_leak_blocked_total`, `cena_tutor_first_token_seconds`.
- SLO: first-token p99 ≤ 1200ms.
- Tests: answer-leak guard (5 canonical jailbreak transcripts), injection gate, PII scrubber on Hebrew/Arabic/English input, tenant isolation.
- Full `Cena.Actors.sln` clean build.

## Reporting

complete via: `node .agentdb/kimi-queue.js complete <id> --worker claude-subagent-tutor-api --result "<branch>"`

---

## Multi-persona lens review (embedded)

- **persona-privacy**: session-scope flush + PII scrubber owned here. DPA entry verified via prr-035 sub-processor registry before feature-flag flip.
- **persona-redteam**: answer-leak guard + injection gate are the two primary jailbreak defenses; both owned here. Red-team transcripts (≥25 attack strings) in the test suite.
- **persona-enterprise**: tenant isolation test + audit log on cross-tenant attempts. Owned here.
- **persona-cogsci**: productive-failure debounce enforced at endpoint level — the same student cannot request `explain-step` on the same step within 15 seconds of a wrong submission. Owned here.
- **persona-sre**: circuit breaker + first-token SLO owned here.
- **persona-ethics**: "coach, not answer key" invariant enforced by the leak guard; no engagement-mechanic coupling.

## Related

- Parent epic: [EPIC-PRR-E](./EPIC-PRR-E-question-engine-ux-integration.md)
- Consumer: prr-207 (sidekick drawer UI)
- Depends on: existing `TutorActor`, `SocraticCallBudget`, AIMDS

## Implementation Protocol — Senior Architect

See [epic file](./EPIC-PRR-E-question-engine-ux-integration.md#implementation-protocol--senior-architect).
