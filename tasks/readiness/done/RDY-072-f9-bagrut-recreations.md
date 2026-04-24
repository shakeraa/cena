# RDY-072 — F9: AI-recreated Bagrut practice with expert-review gate

- **Wave**: C
- **Priority**: HIGH
- **Effort**: 4 engineer-weeks + ongoing expert-review capacity
- **Dependencies**: RDY-068 Arabic lexicon (for Arabic-language items); existing AI + CAS pipeline (Phase 1A shipped)
- **Source**: [panel review](../../docs/research/cena-panel-review-user-personas-2026-04-17.md) Round 2.F9; [project_bagrut_reference_only memory](/Users/shaker/.claude/projects/-Users-shaker-edu-apps-cena/memory/project_bagrut_reference_only.md)

## Problem

GOOL and BagrutOnline's flagship product is past-Bagrut solutions. Cena cannot legally serve raw Ministry exam text (decision 2026-04-15, reference-only). But students desperately need Bagrut-shaped practice. The answer: AI-authored, CAS-verified recreations traceable to the Ministry item they mirror.

## Scope

**Recreation pipeline:**
- Input: Ministry Bagrut item (reference only, internal) + topic metadata + target difficulty
- AI generates 3-5 candidate recreations
- SymPy CAS verifies each candidate's symbolic correctness (ADR-0002)
- Expert reviewer (Prof. Amjad or qualified substitute) passes/rejects per item
- Approved items enter item bank with `source_ministry_ref` + `ai_recreation_v1` tags

**Difficulty calibration** (Dr. Yael's demand):
- Items must be IRT-calibrated to match Ministry target difficulty
- Initial calibration via expert judgment (b-parameter estimate) — replaced with empirical estimate after pilot data
- Items below calibration-confidence threshold labeled "practice" not "exam-calibrated"

**Legal framing** (Ran's demand):
- Every item's metadata explicitly says "AI-authored original, reference only, not Ministry-published"
- Public-facing phrasing: "Problems inspired by Bagrut 2024 summer"

## Files to Create / Modify

- `src/shared/Cena.Domain/Content/BagrutRecreationAggregate.cs`
- `src/shared/Cena.Domain/Content/RecreationReviewWorkflow.cs`
- `src/api/Cena.Admin.Api/Features/ContentAuthoring/RecreationPipeline.cs`
- `src/admin/full-version/src/views/content/RecreationReviewQueue.vue` — expert review UI
- `docs/content/bagrut-recreation-process.md` — Prof. Amjad sign-off procedure
- `docs/legal/recreation-disclosure-template.md`

## Acceptance Criteria

- [ ] 5 recent Bagrut session recreations (5-unit track), 10 items each, fully reviewed + approved
- [ ] Each approved item has `source_ministry_ref`, CAS verification, expert-reviewer audit
- [ ] Legal disclosure string attached to every item
- [ ] Expert-review queue UI allows Prof. Amjad to review ≥ 20 items/hour
- [ ] Public UI never shows raw Ministry text; only recreations
- [ ] Calibration confidence tier ("practice" vs "exam-calibrated") displayed honestly

## Success Metrics

- **Expert-review throughput**: target ≥ 50 items reviewed per week (5-unit first)
- **Student-reported Bagrut-likeness survey** (post-exam): target >60% say items felt "like" the real exam
- **Drift rate**: % of items re-flagged by expert on spot-check; target < 5%
- **Zero legal challenges** on Ministry text overlap

## ADR Alignment

- ADR-0002: SymPy CAS verifies every item
- Reference-only Bagrut decision (2026-04-15 memory)
- Anti-dark-pattern (no misleading "real Bagrut" claims)

## Out of Scope

- 4-unit and 3-unit recreations (5-unit first, expand later)
- Automated expert review (human in the loop is the point)
- Historical Ministry exam license acquisition (separate legal path)

## Assignee

Unassigned. Critical path: Prof. Amjad (no substitute) for review throughput. Content-authoring team for pipeline.
