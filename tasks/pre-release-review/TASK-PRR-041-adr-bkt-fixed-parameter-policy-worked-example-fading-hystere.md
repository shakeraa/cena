# TASK-PRR-041: ADR: BKT fixed-parameter policy + worked-example fading hysteresis

**Priority**: P1 — strongly recommended before launch (lens consensus: 2)
**Effort**: M — 1-2 weeks
**Lens consensus**: persona-cogsci, persona-educator
**Source docs**: `axis1_pedagogy_mechanics_cena.md:L441`
**Assignee hint**: human-architect
**Tags**: source=pre-release-review-2026-04-20, lens=cogsci
**Status**: Not Started
**Source**: Synthesized from 10-persona pre-release review (2026-04-20) — see `/pre-release-review/reviews/SYNTHESIS.md`
**Tier**: mvp
**Epic**: EPIC-PRR-A — ADR-0012 StudentActor decomposition

---

## Goal

Author `docs/adr/NNNN-bkt-parameters-and-fading.md` locking Bayesian Knowledge Tracing parameters, worked-example fading hysteresis, and cohort difficulty targets with concrete values. This is a policy ADR — code paths already exist ([`BktService.cs`](../../src/actors/Cena.Actors/Mastery/BktService.cs), [`ScaffoldingService.cs`](../../src/actors/Cena.Actors/Mastery/ScaffoldingService.cs)). The ADR binds the values and enforces "no per-student parameter learning."

### User decision 2026-04-20 — concrete values adopted

ADR must mandate (not merely document) these:

1. **BKT parameters — Koedinger lab literature defaults**:
   - `pInit = 0.3` (initial mastery probability at first exposure)
   - `pLearn = 0.15` (probability of transitioning unmastered → mastered per correct attempt)
   - `pSlip = 0.10` (probability of incorrect response despite mastery)
   - `pGuess = 0.15` (probability of correct response without mastery)
   - Source citation: Koedinger, Corbett, et al. — widely adopted pilot defaults
   - **No per-student parameter learning**. ADR-0003 forbids ML training on session/misconception data; extending that prohibition to BKT params is consistent.
   - Future parameter tuning requires a new ADR + human sign-off. No "analytics said guess=0.25 looks better" paths.

2. **Worked-example fading hysteresis**:
   - Scaffold at level L stays active until **3 consecutive correct answers at current level** → fade to L-1
   - On **any incorrect answer at L-1**, restore to L
   - **Minimum 2 attempts at any level** before evaluation (prevents snap decisions)
   - Hysteresis prevents thrashing when perceived-mastery oscillates near threshold

3. **Cohort difficulty target** (Miriam's recommendation, see persona-educator review):
   - **75% success rate** default for Bagrut cohort (not 60% as some research implies; 60% demoralizes IL Bagrut students per persona-educator finding)
   - **85% pre-exam confidence mode** toggle, active in the 30-day window before registered Bagrut exam dates
   - Motivation-profile-aware: **anxious students +5 percentage points** on both defaults (start at 80% regular, 90% pre-exam)

4. **Architecture test** (ships with the ADR):
   - New test `BktParametersLockedTest.cs` — asserts the four parameter constants live in a single named file and are used by no other code path via string-literal or numeric-literal override
   - Prevents silent tuning; any change to these constants requires a PR that also updates the ADR number referenced in the file's header comment

## Files

- `docs/adr/NNNN-bkt-parameters-and-fading.md` — the ADR
- `src/actors/Cena.Actors/Mastery/BktParameters.cs` (new) — single source of truth for the four constants, with ADR reference in file header comment
- `src/actors/Cena.Actors/Mastery/BktService.cs` — consume `BktParameters.cs` (remove any hardcoded alternate values)
- `src/actors/Cena.Actors/Mastery/ScaffoldingService.cs` — implement hysteresis (3-consecutive-correct + minimum 2 attempts + restore-on-failure)
- `src/actors/Cena.Actors/Mastery/DifficultyTarget.cs` (new or existing) — 75/85 + motivation-profile adjustments
- `tests/architecture/BktParametersLockedTest.cs` (new) — enforces no-override rule
- `tests/integration/ScaffoldingHysteresisTests.cs` (new) — asserts hysteresis behavior under oscillating correctness
- Cross-references: update ADR-0003 (session-scope) to note that BKT parameter policy complements the no-ML-training rule

## Definition of Done

1. ADR accepted; cited from `BktParameters.cs` file header
2. Four BKT constants in `BktParameters.cs`; `BktService.cs` consumes them exclusively
3. Hysteresis wired in `ScaffoldingService.cs`; unit + integration tests green
4. Difficulty target logic in `DifficultyTarget.cs`: returns 75% default, 85% pre-exam (exam-date check against student's registered Bagrut date), +5pp for anxious profile
5. Architecture test `BktParametersLockedTest` green
6. Full `Cena.Actors.sln` builds cleanly; all existing tests pass
7. No 60% target remains anywhere (search + confirm); doc copy reflects the 75/85 framing

## Rolls up into EPIC-PRR-A

Absorbed sub-task. Sequence per epic: after prr-002 (ADR-0012 kickoff, done 2026-04-20) but before ScaffoldingService-touching features land.

## Reporting

complete via: node .agentdb/kimi-queue.js complete <id> --worker human-architect --result "<branch>"

---

## Non-negotiable references
None

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
- [Canonical task JSON](../../pre-release-review/reviews/tasks.jsonl) (id: prr-041)
