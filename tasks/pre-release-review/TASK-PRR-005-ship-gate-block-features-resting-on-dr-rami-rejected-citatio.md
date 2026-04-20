# TASK-PRR-005: Ship-gate: block features resting on Dr. Rami REJECTED citations (FD-003/008/011)

**Priority**: P0 — ship-blocker (lens consensus: 5)
**Effort**: S — 1-2 days
**Lens consensus**: persona-redteam, persona-educator, persona-cogsci, persona-privacy, persona-sre
**Source docs**: `finding_assessment_dr_rami.md:L69`
**Assignee hint**: claude-subagent-shipgate
**Tags**: source=pre-release-review-2026-04-20, lens=redteam
**Status**: Not Started
**Source**: Synthesized from 10-persona pre-release review (2026-04-20) — see `/pre-release-review/reviews/SYNTHESIS.md`
**Tier**: mvp
**Epic**: EPIC-PRR-D — Ship-gate scanner v2 — banned vocabulary expansion

---

## Goal

Extend existing ship-gate scanner (`scripts/shipgate/lexicon-lock-gate.mjs`) with a rule pack that rejects Dr. Rami's REJECTED citations and related citation-abuse patterns. Requires positive-test fixture + retroactive scan + narrow whitelist.

### User decision 2026-04-20 — tightened DoD

- Catch exact strings AND reasonable variants for: FD-003 "95% misconception resolution", FD-008 "Yu et al. 2026", FD-011 "d=1.16", Hattie "d=1.44" near planning/self-reported context, interleaving "d=0.5-0.8"
- Positive-test fixture `shipgate/fixtures/banned-citation-sample.md` contains every pattern; CI asserts scanner catches each
- Narrow exemption whitelist: `retired.md`, `finding_assessment_dr_rami.md`, `cena_dr_nadia_pedagogical_review_20_findings.md`, this task file
- Retroactive scan on landing: report to `pre-release-review/reviews/banned-citation-historical-scan.md`; historical hits quarantined with follow-up tasks, not blocked

## Files

- `scripts/shipgate/banned-citations.yml` (new)
- `scripts/shipgate/lexicon-lock-gate.mjs` (teach scanner to load YAML rule pack if not generic)
- `shipgate/fixtures/banned-citation-sample.md` (positive-test)
- `tests/shipgate/banned-citations.spec.ts`
- `scripts/shipgate/banned-citations-whitelist.yml` (with one-line reason per entry)
- `.github/workflows/shipgate.yml` (wire new rule)

## Definition of Done

1. Rule pack detects all patterns in the fixture; CI fails PRs introducing banned patterns outside whitelist
2. Positive + negative tests in `tests/shipgate/`; positive fails scanner, negative passes
3. Retroactive scan report committed; historical hits whitelisted with per-hit follow-up tasks
4. Narrow whitelist — each entry has one-line reason
5. Rolls up into EPIC-PRR-D — coordinate with prr-013 euphemism ban, prr-019/040 banned-mechanics, prr-156 emoji inflation for single scanner invocation

## Reporting

complete via: node .agentdb/kimi-queue.js complete <id> --worker claude-subagent-shipgate --result "<branch>"

---

## Non-negotiable references
- #3: No dark-pattern engagement (streaks, loss-aversion, variable-ratio banned)
- evidence-integrity

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
- [Canonical task JSON](../../pre-release-review/reviews/tasks.jsonl) (id: prr-005)
