# EPIC-PRR-N: Bagrut reference library + student-side variant generation

**Priority**: P1 (launch-adjacent; default-off feature flag at Launch per ADR-0059 §Q3)
**Effort**: XL (epic-level: 4-6 weeks aggregate across child tasks)
**Lens consensus**: pending — 6-persona review tracked under PRR-248
**Source docs**: [ADR-0059](../../docs/adr/0059-bagrut-reference-browse-and-variant-generation.md) (Proposed), user directive 2026-04-28
**Assignee hint**: claude-code (coordinator) for ADR sign-off + persona reviews; backend/frontend implementation eligible for kimi-coder + claude sub-agents under main session
**Tags**: source=user-directive-2026-04-28, type=epic, epic=epic-prr-n, priority=p1, content, ui, backend, adr-extension
**Status**: Not Started — blocked on ADR-0059 acceptance (PRR-248 + PRR-249)
**Tier**: launch-adjacent

---

## Epic goal

Activate the past-Bagrut corpus (PRR-242, done) on the student surface as a reference-browse library that hands students into CAS-verified variant practice. Closes the leak where students go to the Ministry website for past papers anyway, and amortizes the corpus investment across the student surface (not just the LLM authoring backend).

## Architectural substrate

- New `Reference<T>` wrapper sibling to `Deliverable<T>`. Permitted only for `MinistryBagrut`-provenanced reads with consent-token + audit. ADR-0043's strict ban remains in force everywhere else.
- Two-tier variant generation: parametric (deterministic + SymPy-verified, free) and structural (Tier-3 LLM via `GenerateSimilarHandler`, paid + rate-limited).
- Filter scope = `ExamTarget.QuestionPaperCodes` (per ADR-0050 + PRR-243). Single source of truth for what reference items a student sees.
- Variant practice flows through normal session pipeline; no special "reference-practice mode" in BKT — skill-keyed mastery feedback per ADR-0050 Item 4.

## Launch posture

Default-off behind feature flag `reference_library_enabled`. Two-week soak post-Launch, then ramp pending finops + cogsci telemetry.

## Sub-task map

| ID | Title | Priority | Role |
|---|---|---|---|
| [PRR-245](TASK-PRR-245-reference-library-variant-generation.md) | Reference library + student-side variant generation | P1 | implementation (blocked) |
| [PRR-248](TASK-PRR-248-adr-0059-six-persona-review.md) | 6-persona review for ADR-0059 | P0 | unblocks PRR-245 |
| [PRR-249](TASK-PRR-249-bagrut-corpus-display-delta-legal-memo.md) | Bagrut corpus in-app display delta — legal memo | P0 | unblocks PRR-245 |
| [PRR-250](TASK-PRR-250-pre-implementation-verification-sweep.md) | Pre-implementation verification sweep (PRR-244 API, BagrutCorpusItemDocument shape, feature flag) | P1 | unblocks PRR-245 |

## Definition of Done

All four child tasks closed, ADR-0059 moved from Proposed to Accepted, feature flag wired, default-off rollout config in place.

## Related epics

- [EPIC-PRR-F](EPIC-PRR-F-multi-target-onboarding-plan.md) — provides `ExamTarget.QuestionPaperCodes` filter source (PRR-243 done).
- [EPIC-PRR-G](EPIC-PRR-G-sat-pet-content-engineering.md) — provides corpus (PRR-242 done).

---

## History

- 2026-04-28: epic created by coordinator after user directive; reference library is the third surface for the corpus, distinct from LLM authoring backend (EPIC-PRR-G) and exam-target picker (EPIC-PRR-F).
