# EPIC-PRR-D: Ship-gate scanner v2 — banned vocabulary expansion

**Priority**: P0
**Effort**: XL (epic-level: 3-8 weeks aggregate across 31 sub-tasks)
**Lens consensus**: persona-a11y, persona-cogsci, persona-educator, persona-ethics, persona-finops, persona-ministry, persona-privacy, persona-redteam, persona-sre
**Source docs**: AXIS_4_Parent_Engagement_Cena_Research.md:L129, AXIS_6_Assessment_Feedback_Research.md:L135, axis1_pedagogy_mechanics_cena.md:L155, axis1_pedagogy_mechanics_cena.md:L157, axis2_motivation_self_regulation_findings.md:L60, axis2_motivation_self_regulation_findings.md:L81, axis2_motivation_self_regulation_findings.md:L~, axis9_data_privacy_trust_mechanics.md:L132, axis9_data_privacy_trust_mechanics.md:L253, axis_4_parent_engagement_cena_research.md:L204 ...
**Assignee hint**: human-architect (epic-level planning) + named subagents per sub-task
**Tags**: source=pre-release-review-2026-04-20, type=epic, epic=epic-prr-d
**Status**: Not Started
**Source**: Synthesized from the 10-persona pre-release review 2026-04-20; bundles the absorbed tasks listed below because they share a single architectural substrate and must ship in lock-step.

---

## Epic goal
Extend the existing ship-gate scanner (locale banned-terms + dark-pattern copy checker) into a v2 that also blocks: rejected citations (Dr. Rami REJECTs), effect-size marketing without meta-analytic means, PII in LLM prompts, crisis/countdown copy, reward-inflation emoji, therapeutic-claim phrasing, Bagrut-score-prediction copy, cheating-alert framing, language-proficiency inference copy, and a catalog of narrow copy/UI rules flagged across the review. Consolidating these rules under one scanner keeps the rule set auditable and the CI cost low.

## Architectural substrate
One scanner, one banned-vocabulary registry, one ADR (citation-integrity) backing it. The scanner must cover all three locales (he/ar/en) and both surfaces (student-web + admin/full-version). Each absorbed sub-task either adds a new rule to the registry or refactors a site to be scannable.

## Absorbed tasks (31)
| ID | Title | Priority | Role in epic |
|---|---|---|---|
| prr-005 | Ship-gate: block features resting on Dr. Rami REJECTED citations (FD-003/008/011) | P0 | feature |
| prr-006 | Rename 'Crisis Mode' + replace Bagrut countdown with positive progress framing | P0 | feature |
| prr-019 | Ship-gate ban on Bagrut-score prediction + countdown copy (scanner expansion) | P1 | test |
| prr-022 | Ban PII in LLM prompts — lint rule + ADR | P1 | foundation |
| prr-027 | Correct FD-003 misconception-resolution figure, remove 95% claim | P1 | feature |
| prr-028 | Replace 'Yu et al. 2026' citations in FD-008 — or retire partial-credit grading | P1 | feature |
| prr-040 | Ship-gate banned-terms scanner: all three locales + admin/full-version | P1 | test |
| prr-042 | ADR: Citation-integrity / effect-size communication | P1 | foundation |
| prr-073 | Assistant messaging: no therapeutic claims | P2 | feature |
| prr-091 | Student-visible progress framing (no scores-as-identity) | P2 | feature |
| prr-121 | Retire FD-011 (d=1.16 fabricated claim) | P2 | feature |
| prr-142 | Education-friendly error messages (no blame) | P2 | feature |
| prr-144 | Retire 'cheating alert' family of features | P2 | feature |
| prr-153 | Ban reward-inflation emoji (🔥, ⚡) in learning UI | P1 | feature |
| prr-156 | Feature-spec citation verifiability rule | P1 | feature |
| prr-163 | Cohort-context copy lockdown: positive-frame only | P2 | feature |
| prr-164 | F2 breathing opener: confirm-before-route on mood tap | P2 | feature |
| prr-165 | F2 watermark: session-id not student-id | P2 | feature |
| prr-166 | F3 rubric review: 'how this scored' framing, not 'lost points' | P2 | feature |
| prr-167 | F6 explicit ban on language-proficiency inference | P2 | feature |
| prr-168 | Focus-ritual mood-adjustment copy: 'familiar pattern' not 'easier' | P2 | feature |
| prr-169 | Formalize 'intrinsic motivation risk map' as design-review tool | P2 | feature |
| prr-170 | Honest ES in product copy (Interleaving d=0.34, not 0.5-0.8) | P2 | feature |
| prr-171 | Journey path: no animation, no sound, pause-friendly copy | P2 | feature |
| prr-172 | Misconception tag UI: diagnostic-offer framing, dismissible | P2 | feature |
| prr-173 | Pre-problem retrieval prompts (F3) as session-local low-stakes recall | P2 | feature |
| prr-177 | Retire idle-pulse animation in Stuck? Ask button | P2 | feature |
| prr-178 | Session type menu: rename 'Challenge Round' | P2 | feature |
| prr-179 | Student-facing 'go gentler today' control for difficulty adjuster | P2 | feature |
| prr-182 | Top-10 Shortlist: carry-forward critique annotation | P2 | feature |
| prr-185 | Transparency report: add banned-mechanic compliance section | P2 | doc |

The absorbed task files remain in place as the executable unit-of-work; this epic file provides the coordination frame, dependency order, and DoD for the whole bundle.

## Suggested execution order
1. prr-040 — Extend scanner coverage: all three locales + admin/full-version
2. prr-005 — Block features resting on Dr. Rami REJECTED citations (FD-003/008/011)
3. prr-042 — ADR: Citation-integrity / effect-size communication
4. prr-019 — Ship-gate ban on Bagrut-score prediction + countdown copy
5. prr-022 — Ban PII in LLM prompts — lint rule + ADR
6. prr-006 — Rename Crisis Mode + replace Bagrut countdown with progress framing
7. prr-027, prr-028, prr-121, prr-144 — Remediate the specific rejected claims (FD-003 95%, FD-008 Yu-2026, FD-011 d=1.16, cheating-alert family)
8. prr-153, prr-073, prr-091, prr-142, prr-156 — Narrow rules (reward-inflation emoji, therapeutic claims, scores-as-identity, no-blame errors, feature-spec citation verifiability)
9. prr-163..prr-182, prr-185 — Copy-lockdown rules (cohort-context, breathing opener, session-type rename, journey path, misconception-tag UI, how-this-scored framing, transparency report banned-mechanic section, etc.)

## Epic-level DoD (in addition to per-task DoDs)
- Single ADR covers all absorbed scope (or the ADR pack is internally consistent).
- Integration test demonstrating all sub-task contributions work together in one end-to-end scenario.
- SYNTHESIS.md epic section reflects completion.
- No absorbed task is marked done until all peers in the epic are merged.

## Epic triage decisions 2026-04-20 (user)

**Adopted**: scope.

**Tightenings — 31 tasks requires internal sub-clustering**:

Split the 31 absorbed tasks into 5 coherent clusters, each shipping as a bounded PR:

| Cluster | Theme | Tasks | Size | Notes |
|---|---|---|---|---|
| **D1 — Citations** | Dr. Rami REJECTs, effect-size policy | prr-005, prr-027, prr-028, prr-042, prr-121, prr-156, prr-170 | 7 | prr-042 (citation-integrity ADR) ships first |
| **D2 — Mechanics** | Crisis/countdown/confetti/emoji/animation | prr-006, prr-019, prr-153, prr-164, prr-171, prr-177, prr-178 | 7 | — |
| **D3 — Copy semantics** | Therapeutic/cheating/blame/scores-as-identity/inference | prr-073, prr-091, prr-142, prr-144, prr-163, prr-166, prr-167, prr-168, prr-169, prr-172, prr-173 | 11 | Sequential — copy items need stakeholder review each |
| **D4 — Locale coverage** | he/ar/en across student + admin bundles | prr-040, prr-165 | 2 | — |
| **D5 — PII-in-prompts lint** | Roslyn analyzer for LLM prompt assembly | prr-022 | 1 | Structurally different from lexicon-lock-gate; separate analyzer |

**Effort correction**: XL (3-8 weeks) is optimistic at 31 tasks without clusters. Realistic: D1+D2+D4 parallel (1-2 weeks), D3 sequential (2-3 weeks), D5 standalone (1 week). **Parallel path: 4-5 weeks**; sequential: 6-8 weeks.

**Sequencing constraint**: prr-042 (citation-integrity ADR) lands before D1; prr-022 (PII lint) is independent and can parallelize; D2 landing before D3 since mechanic bans are clearer than copy-semantic ones.

**Tags**: user-decision=2026-04-20-epic-triaged, sub-clusters-D1-D5

---

## Implementation Protocol — Senior Architect

Implementation of this epic must be driven by a senior-architect mindset, not a checklist. Before writing any code, the implementer (human or agent) must answer both sets of questions in writing — either in a task-comment, the PR description, or a `docs/decisions/` note:

### Ask why
- **Why does this epic exist?** Read the source-doc lines cited in the absorbed sub-tasks and the persona reviews in `/pre-release-review/reviews/persona-*/` that raised them. If you cannot restate the motivation in one sentence, do not start coding.
- **Why this priority?** Read the lens-consensus list. Understand which persona lens raised it and what evidence they cited.
- **Why these files?** Trace the data flow end-to-end. Verify the files listed are the right seams. A bad seam invalidates the whole epic.
- **Why are the non-negotiables above relevant?** Show understanding of how each constrains the solution, not just that they exist.

### Ask how
- **How does this interact with existing aggregates and bounded contexts?** Name them.
- **How does it respect tenant isolation (ADR-0001), event sourcing, the CAS oracle (ADR-0002), and session-scoped misconception data (ADR-0003)?**
- **How will it fail?** What is the runbook at 03:00 on a Bagrut exam morning? If you cannot describe the failure mode, the design is incomplete.
- **How will it be verified end-to-end, with real data?** Not mocks. Query the DB, hit the APIs, compare field names and tenant scoping.
- **How does it honor the <500 LOC per file rule, the no-stubs-in-prod rule, and the full `Cena.Actors.sln` build gate?**

### Before committing
- Full `Cena.Actors.sln` must build cleanly (branch-only builds miss cross-project errors — learned 2026-04-13).
- Tests cover golden path **and** edge cases surfaced in the persona reviews.
- No cosmetic patches over root causes. No "Phase 1 stub → Phase 1b real" pattern (banned 2026-04-11).
- No dark-pattern copy (ship-gate scanner must pass).
- If the epic as-scoped is wrong in light of what you find, **push back** and propose the correction — do not silently expand scope, shrink scope, or ship a stub.

### If blocked
- Fail loudly. Do not silently reduce scope. Do not skip a non-negotiable.

### Definition of done is higher than the checklist above
- Labels match data (UI label = API key = DB column intent).
- Root cause fixed, not masked.
- Observability added (metrics, structured logs with tenant/session IDs, runbook entry).
- Related personas cross-lens handoffs addressed or explicitly deferred with a new task ID.


---

## Related
- Absorbed sub-tasks: prr-005, prr-006, prr-019, prr-022, prr-027, prr-028, prr-040, prr-042, prr-073, prr-091, prr-121, prr-142, prr-144, prr-153, prr-156, prr-163, prr-164, prr-165, prr-166, prr-167, prr-168, prr-169, prr-170, prr-171, prr-172, prr-173, prr-177, prr-178, prr-179, prr-182, prr-185
- [Full synthesis](../../pre-release-review/reviews/SYNTHESIS.md)
- [Retired proposals](../../pre-release-review/reviews/retired.md)
- [Conflicts](../../pre-release-review/reviews/conflicts.md)
- [Canonical task JSON](../../pre-release-review/reviews/tasks.jsonl) (epic id: epic-prr-d)
