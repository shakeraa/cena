# TASK-PRR-006: Rename 'Crisis Mode' + replace Bagrut countdown with positive progress framing

**Priority**: P0 — ship-blocker (lens consensus: 4)
**Effort**: M — 1-2 weeks
**Lens consensus**: persona-ethics, persona-educator, persona-cogsci, persona-ministry
**Source docs**: `AXIS_6_Assessment_Feedback_Research.md:L135`, `AXIS_4_Parent_Engagement_Cena_Research.md:L129`, `cena_competitive_analysis.md:L126`
**Assignee hint**: human-architect
**Tags**: source=pre-release-review-2026-04-20, lens=ethics
**Status**: Not Started
**Source**: Synthesized from 10-persona pre-release review (2026-04-20) — see `/pre-release-review/reviews/SYNTHESIS.md`
**Tier**: mvp
**Epic**: EPIC-PRR-D — Ship-gate scanner v2 — banned vocabulary expansion

---

## Goal

Kill "Crisis Mode" and "Bagrut Countdown" naming + mechanic patterns before they ship. This is **preventive** work — 2026-04-20 grep confirms no code implements these patterns today; the task is to keep it that way via scanner + design-intent ADR + positive-framing reference copy.

### Relationship to prr-013 "honest + supportive + legal" stance (user decision 2026-04-20)

The R-28 decision said "honest numbers, not soft euphemism" for student performance data. This task is orthogonal:
- **Honest language, session-scoped, in-surface**: YES (prr-013)
- **Loss-aversion *mechanics* (countdown timers, Crisis Mode label, red-at-risk tier, urgency copy)**: NO (this task)

A perfectly honest countdown ("42 days until exam, you're behind trajectory") is still a coercive *mechanic* banned by ship-gate GD-004. The mechanic is the problem, not the number's honesty.

### User decision 2026-04-20 — tightened DoD

**Scope A — ship-gate vocabulary expansion** (rolls up under EPIC-PRR-D):
- Banned terms in en/he/ar locale files: "Crisis", "crisis-mode", "countdown", "days remaining", "days until", "days left", "run out of time", "last chance", "almost out", "time is running out", and Hebrew/Arabic variants (checked with a native speaker or locale team)
- Scanner fails CI on any match in student-facing i18n bundles (`src/student/full-version/src/locales/`, `src/admin/full-version/src/locales/`)
- Exemption whitelist narrow: only this task file, `retired.md`, and `shipgate/fixtures/` may mention the patterns

**Scope B — design-intent ADR**:
- `docs/adr/NNNN-exam-prep-time-framing.md`: codify principle — **time-awareness is OK, time-pressure mechanics are not**. "2 of 8 units mastered" OK; "62 days remaining with red badge" not.
- ADR governs future feature proposals without requiring another swarm review
- Cross-link from CLAUDE.md non-negotiable #3 so future authors hit it

**Scope C — positive-framing reference copy**:
- `docs/design/exam-prep-positive-framing.md`: authoritative examples for every surface that would naively show countdown/crisis framing. Each example pairs banned-framing-anti-pattern with positive-framing target, with rationale.
- Designers reference this when building new exam-prep surfaces

**Scope D — per-family opt-in for time-awareness (optional, not default)**:
- If a family wants plain days-until-exam as informational (not coercive), it ships behind a parent-settings opt-in
- Never default-on, never student-surface default, never styled with red/alert/urgent semantics
- Note: this ties to EPIC-PRR-C Parent Aggregate — defer the opt-in settings UI to that epic; this task locks the *policy*

## Files

- `scripts/shipgate/banned-mechanics.yml` (new — distinct from banned-citations in prr-005; both feed scanner)
- `scripts/shipgate/lexicon-lock-gate.mjs` (teach scanner to load new rule)
- `docs/adr/NNNN-exam-prep-time-framing.md` (new)
- `docs/design/exam-prep-positive-framing.md` (new)
- `shipgate/fixtures/banned-mechanics-sample.md` (positive-test)
- `tests/shipgate/banned-mechanics.spec.ts`
- Locale pre-scan: `src/student/full-version/src/locales/**/*.json` — retroactive; quarantine any existing hit
- Update CLAUDE.md non-negotiable #3 to link the ADR

## Definition of Done

1. Banned-mechanics rule pack active; scanner catches every pattern in the fixture across en/he/ar
2. ADR accepted with time-awareness vs time-pressure distinction clearly articulated
3. Positive-framing reference copy committed with ≥5 concrete anti-pattern → target examples
4. Locale pre-scan report: zero hits in current student-facing i18n bundles (or, if hits found, each is fixed in this PR)
5. CLAUDE.md non-negotiable #3 cross-links the new ADR
6. Parent opt-in UI *deferred to EPIC-PRR-C* — document the handoff in that epic's task file
7. Rolls up into EPIC-PRR-D; coordinate with prr-005 (banned citations), prr-013 (banned euphemism), prr-156 (banned emoji), prr-019/040 for single scanner invocation
8. Full `Cena.Actors.sln` unaffected (this is scanner + docs + locale changes)

## Reporting

complete via: node .agentdb/kimi-queue.js complete <id> --worker human-architect --result "<branch>"

---

## Non-negotiable references
- #3: No dark-pattern engagement (streaks, loss-aversion, variable-ratio banned)
- #4: Bagrut content reference-only (AI-authored CAS-gated recreations)

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
- [Canonical task JSON](../../pre-release-review/reviews/tasks.jsonl) (id: prr-006)
