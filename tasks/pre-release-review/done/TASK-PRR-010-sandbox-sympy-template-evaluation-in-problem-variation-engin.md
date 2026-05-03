# TASK-PRR-010: Sandbox SymPy template evaluation in problem-variation engine

**Priority**: P0 — ship-blocker (lens consensus: 2)
**Effort**: M — 1-2 weeks
**Lens consensus**: persona-redteam, persona-enterprise
**Source docs**: `AXIS_8_Content_Authoring_Quality_Research.md:L98`, `AXIS_8_Content_Authoring_Quality_Research.md:L92`
**Assignee hint**: kimi-coder
**Tags**: source=pre-release-review-2026-04-20, lens=redteam
**Status**: Done — 2026-04-20 (Layers 2+3 shipped; Layer 1 container hardening deferred to ops)
**Source**: Synthesized from 10-persona pre-release review (2026-04-20) — see `/pre-release-review/reviews/SYNTHESIS.md`
**Tier**: mvp

---

## Goal

Sandbox SymPy template evaluation at three layers (container, SymPy whitelist, canary tests). Current sidecar has only millisecond timeout (`SymPySidecarClient.cs:103`) — insufficient for memory blow-up, infinite recursion, or sandbox-escape patterns. LLM-generated templates are untrusted input; CAS is correctness, sandbox is containment — both required.

### User decision 2026-04-20 — tightened DoD

- **Container layer**: dedicated sidecar with seccomp blocking `execve`/`fork`/`ptrace`/filesystem syscalls except stdin/stdout; cgroups 128MB mem + 1 CPU-sec hard limits; no network namespace
- **SymPy layer**: whitelist imports to `sympy.core`/`simplify`/`solvers`; deny `__import__`/`exec`/`eval`/`open`/`sympy.printing`/`__subclasses__`; `sympify(expr, strict=True)` with symbol whitelist, fail-closed
- **Canary suite**: `tests/fixtures/hostile-sympy/` with memory-bomb, infinite recursion, `__subclasses__` escape, SSRF via printing, filesystem probe — CI asserts each is contained
- **Defense in depth**: CAS output still passes `CasConformanceSuite` after sandbox; layered integration test

## Files

- `src/actors/Cena.Actors/Cas/SymPySidecarClient.cs` (resource-limit enforcement + whitelist)
- `src/actors/Cena.Actors/Cas/ProblemVariationEngine.cs` or content-authoring entry
- `deploy/docker/sympy-sidecar/Dockerfile` (seccomp + cgroups + no-network)
- `deploy/docker/sympy-sidecar/seccomp.json`
- `src/sympy-sidecar/main.py` (SymPy import + symbol whitelist)
- `tests/fixtures/hostile-sympy/` (5+ canary patterns)
- `tests/integration/SymPySandbox.HostileTemplate.Tests.cs`
- `tests/integration/SymPySandbox.ResourceLimit.Tests.cs`
- `docs/runbooks/sympy-sandbox-breach.md`

## Definition of Done

1. Sidecar rebuilt with seccomp + cgroups + no-network; `docker run` validates limits
2. SymPy import + symbol whitelist active; unknown symbol fails closed
3. All canary fixtures contained within limits; no host OOM/crash; assertions per canary
4. Layered integration test: sandbox + CasConformanceSuite both hold
5. Runbook for containment-failure response
6. **`docker ps` before build per `feedback_container_state_before_build`** — virtiofs + hot-reload crashed Docker Desktop twice
7. Full `Cena.Actors.sln` builds cleanly

## Blocked-by / Coordinate

- **prr-001** (EXIF stripping) — ingestion hardening; land together
- Loosely coupled with prr-007 (CAS router)

## Reporting

complete via: node .agentdb/kimi-queue.js complete <id> --worker kimi-coder --result "<branch>"

---

## Non-negotiable references
- #2: Misconception data session-scoped, 30-day max (ADR-0003)

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
- [Canonical task JSON](../../pre-release-review/reviews/tasks.jsonl) (id: prr-010)
