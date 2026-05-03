# TASK-PRR-007: IRT theta architecturally isolated from student-visible DTOs

**Priority**: P0 — ship-blocker (lens consensus: 4)
**Effort**: M — 1-2 weeks
**Lens consensus**: persona-ministry, persona-redteam, persona-ethics, persona-cogsci
**Source docs**: `AXIS_6_Assessment_Feedback_Research.md:L169`, `cena_competitive_analysis.md:L122`
**Assignee hint**: claude-subagent-theta-isolation
**Tags**: source=pre-release-review-2026-04-20, lens=ministry
**Status**: Done — 2026-04-20
**Source**: Synthesized from 10-persona pre-release review (2026-04-20) — see `/pre-release-review/reviews/SYNTHESIS.md`
**Tier**: mvp

---

## Goal

Enforce RDY-080 prediction-surface ban as architecture invariant. Theta / raw ability estimates / exam-readiness predictions never reach student, teacher, or parent DTOs. Readiness outputs clamped to ordinal buckets at the single mapper seam.

### Code-reality correction (2026-04-20 verification) — NOT preventive, active leaks exist

Two shipped API endpoints leak raw theta today:

- [`DiagnosticEndpoints.cs:77`](../../src/api/Cena.Student.Api.Host/Endpoints/DiagnosticEndpoints.cs#L77) — `Theta: Math.Round(ability.Theta, 3)` in response DTO
- [`TrajectoryEndpoints.cs:137`](../../src/api/Cena.Student.Api.Host/Endpoints/TrajectoryEndpoints.cs#L137) — `Theta: p.Theta` in trajectory response payload
- [`DiagnosticEndpoints.cs:85`](../../src/api/Cena.Student.Api.Host/Endpoints/DiagnosticEndpoints.cs#L85) — logs theta per subject (`"{Subject}={Theta:F2}"`); SIEM/analytics exfil vector

This is not theoretical — student clients currently receive raw IRT theta scalars. Every day this ships is a Ministry-defensibility day with no cover.

### User decision 2026-04-20 — tightened DoD, P0 severity confirmed, remediation-first

**Scope A — REMEDIATE existing leaks (P0 urgency)**:
- Rewrite `DiagnosticEndpoints` response DTO: replace `Theta: double` with `Readiness: ReadinessBucket` (enum: `Emerging | Developing | Proficient | ExamReady`)
- Rewrite `TrajectoryEndpoints` response DTO: same replacement
- Scrub log line 85: log bucket only, or `[theta-redacted]` sentinel; extend `PiiLogSanitizer` with theta-scalar detection
- Coordinate client changes: student-web + admin-web that consume these endpoints need DTO-shape migration — schedule in same PR to avoid broken builds

**Scope B — PREVENT regression (arch invariant)**:
- `tests/architecture/PredictionSurfaceTests.cs` / `NoThetaInOutboundDto` — scans types reachable from `*Endpoint`, `*Hub`, `*EventDto`; fails if any field is named `theta|Theta|Theta*` OR typed as `AbilityEstimate`/`IrtAbilityEstimate` OR typed as `double` with a name matching `theta*|ability*|score*|prediction*|atRisk*|bagrutReadiness*`
- Single seam: `ThetaMasteryMapper.ToReadinessBucket(theta, confidenceInterval) → ReadinessBucket`. Any surface wanting to expose readiness goes through this mapper; arch test enforces no other code path constructs a bucket
- Same policy applies to **teacher and parent surfaces** — a teacher export that leaks to a school-district audit or parent dashboard carries the same Ministry-defensibility risk. Buckets for everyone non-internal.

**Scope C — ADR**:
- `docs/adr/NNNN-prediction-surface-ban.md`: policy + rationale + the four ordinal bucket definitions + the single-mapper-seam contract
- Cross-link from RDY-080 doc and CLAUDE.md

## Files

- `src/api/Cena.Student.Api.Host/Endpoints/DiagnosticEndpoints.cs` — rewrite DTO (remediation)
- `src/api/Cena.Student.Api.Host/Endpoints/TrajectoryEndpoints.cs` — rewrite DTO (remediation)
- `src/actors/Cena.Actors/Mastery/ThetaMasteryMapper.cs` — single seam, add `ToReadinessBucket` function
- `src/actors/Cena.Actors/Mastery/ReadinessBucket.cs` (new) — enum definition
- `src/shared/Cena.Infrastructure/Compliance/PiiLogSanitizer.cs` — theta-scalar detection
- `tests/architecture/PredictionSurfaceTests.cs` (new)
- `src/student/full-version/src/composables/useTrajectory.ts` (+ admin equivalents) — DTO consumer updates
- `docs/adr/NNNN-prediction-surface-ban.md`

## Definition of Done

1. Both leak endpoints rewritten; response DTOs carry ordinal buckets, no raw theta
2. Log scrub active; no theta scalar in any structured log
3. `ThetaMasteryMapper.ToReadinessBucket` is the only constructor of `ReadinessBucket` (enforced by arch test)
4. `NoThetaInOutboundDto` arch test green; scans all outbound DTO types
5. ADR accepted with bucket definitions + seam contract
6. Client changes merged alongside DTO changes — no broken UI state
7. Full `Cena.Actors.sln` builds; all tests pass
8. Integration test: call `/api/diagnostic/estimate` and `/api/trajectory` — response contains `Readiness: "Developing"` (or similar bucket), never a theta scalar

## Blocks / Coordinate

- Coordinate with prr-013 (at-risk redesign) — both touch student-visible performance surfaces; bucket semantics must be consistent
- Blocks any new feature that would expose theta — e.g. IRT-CAT implementations, exam-readiness dashboards (currently in post-launch or parked)

## Reporting

complete via: node .agentdb/kimi-queue.js complete <id> --worker claude-subagent-theta-isolation --result "<branch>"

---

## Non-negotiable references
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
- [Canonical task JSON](../../pre-release-review/reviews/tasks.jsonl) (id: prr-007)
