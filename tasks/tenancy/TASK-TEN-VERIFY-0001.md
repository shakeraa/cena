# TASK-TEN-VERIFY-0001: Transfer-of-Learning Literature Review

**Phase**: Research
**Priority**: high
**Effort**: 3--5d
**Depends on**: none
**Blocks**: TEN-P2a, TEN-P2b, TEN-P2c, TEN-P2d, TEN-P2e, TEN-P2f (all of Phase 2)
**Queue ID**: `t_785163249bae`
**Assignee**: unassigned
**Status**: ready

---

## Goal

Produce a peer-reviewed literature review and a formal ADR-0002 that locks Decision 2 (mastery-state sharing model: fully shared, fully isolated, or seeded-but-divergent). The output determines how every student's BKT/Elo/HLR state is keyed across enrollments -- the single most impactful data-model decision in the tenancy bundle.

## Background

ADR-0001 Decision 2 asks: when a student holds two enrollments that share a concept (e.g. `linear-equations` in both `MATH-BAGRUT-5UNIT` and `MATH-SAT-700`), should the mastery state be shared, isolated, or seeded-then-divergent? The user's null hypothesis is Model C (seeded-but-divergent). This task verifies or refutes that hypothesis with citable evidence.

## Specification

### Required citations (minimum -- more encouraged)

1. **Thorndike, E. L. & Woodworth, R. S. (1901)**. "The influence of improvement in one mental function upon the efficiency of other functions." *Psychological Review*, 8(3), 247--261. DOI: [10.1037/h0074898](https://doi.org/10.1037/h0074898).
2. **Perkins, D. N. & Salomon, G. (1992)**. "Transfer of Learning." In *International Encyclopedia of Education* (2nd ed.).
3. **Barnett, S. M. & Ceci, S. J. (2002)**. "When and where do we apply what we learn? A taxonomy for far transfer." *Psychological Bulletin*, 128(4), 612--637. DOI: [10.1037/0033-2909.128.4.612](https://doi.org/10.1037/0033-2909.128.4.612).
4. **Singley, M. K. & Anderson, J. R. (1989)**. *The Transfer of Cognitive Skill.* Harvard University Press. ISBN: 978-0674903401.
5. **Schwartz, D. L., Bransford, J. D. & Sears, D. (2005)**. "Efficiency and Innovation in Transfer." In *Transfer of Learning from a Modern Multidisciplinary Perspective.* IAP.

### Deliverables

| File | Format | Content |
|---|---|---|
| `docs/research/transfer-of-learning-review.md` | Markdown | 1500--3000 word review covering near/far transfer, identical-elements theory, ACT-R production-rule transfer, preparation-for-future-learning. Each claim must cite author+year+DOI. |
| `docs/adr/0002-mastery-sharing-model.md` | ADR (same format as ADR-0001) | Locks Decision 2 with one of: Model A, B, or C. Must include concrete keying design, seed function (if C), and refutation criteria. |
| `docs/references.md` | Append-only | Add all new references used in the review. |

### ADR-0002 must answer

- How is mastery state keyed? `(conceptId)`, `(enrollmentId, conceptId)`, or hybrid?
- If Model C: what is the seed function? Example: `newPMastery = weight * existingPMastery + (1 - weight) * prior`.
- If Model C: where does the transfer weight come from? Literature default? Track-designer authored? Learned from data?
- What evidence would refute the chosen model once real user data exists?

## Implementation notes

- This is a research task, not a code task. No code changes.
- Follow the citation standard established in `docs/references.md` and `QuestionDocument.cs` (which cites Elo 1978 and Wilson et al. 2019 inline).
- The 2026-04-11 review standard applies: "Research shows..." without a name+year+DOI is not acceptable evidence (see ADR-0001 section "What verification requires").
- Cross-reference FIND-pedagogy-008 (learning objectives) and FIND-pedagogy-009 (Elo enrichment) to show how existing pedagogy infrastructure maps onto the transfer framework.

## Quality requirements

No stubs, no canned data, no placeholder implementations. Every claim must trace to a real publication with a verifiable DOI or ISBN. No unsourced pedagogy claims. The review must address counter-arguments for all three models, not just the preferred one.

## Tests required

No code tests. The deliverable is prose. Quality gate:

- `TransferReviewCompleteness_Test`: grep `docs/research/transfer-of-learning-review.md` for all 5 required citation last names (Thorndike, Perkins, Barnett, Singley, Schwartz) -- all must appear.
- `ADR0002HasDecision_Test`: grep `docs/adr/0002-mastery-sharing-model.md` for the string `Status: Locked` or `Status: Decision locked`.
- `ReferencesUpdated_Test`: grep `docs/references.md` for at least 3 of the 5 required DOIs.

## Definition of Done

- [ ] `docs/research/transfer-of-learning-review.md` exists and is 1500--3000 words
- [ ] All 5 required authors cited with DOI/ISBN
- [ ] `docs/adr/0002-mastery-sharing-model.md` exists with locked decision
- [ ] ADR-0002 specifies exact keying design for mastery state
- [ ] ADR-0002 includes refutation criteria for the chosen model
- [ ] `docs/references.md` updated with all new citations
- [ ] No unsourced claims (grep for "research shows" without a citation within 2 lines)
- [ ] Reviewed by project owner before Phase 2 tasks unblock

## Files to read first

1. `docs/adr/0001-multi-institute-enrollment.md` -- Decision 2 context
2. `docs/references.md` -- existing citation format
3. `docs/reviews/agent-4-pedagogy-findings.md` -- pedagogy review standards
4. `src/actors/Cena.Actors/Events/StudentProfileSnapshot.cs` -- current mastery state shape
5. `src/actors/Cena.Actors/Services/EloDifficultyService.cs` -- current Elo implementation

## Files to create / modify

| File path | Action | What changes |
|---|---|---|
| `docs/research/transfer-of-learning-review.md` | create | Full literature review |
| `docs/adr/0002-mastery-sharing-model.md` | create | ADR locking Decision 2 |
| `docs/references.md` | modify | Append 5+ new references |
