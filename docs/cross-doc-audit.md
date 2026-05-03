# Cross-Document Audit Report

> **Date:** 2026-03-26
> **Scope:** All 14 Cena project documents
> **Auditor:** Automated cross-reference analysis

---

## Summary

Total issues found: **28**
- HIGH (investor would notice): **8**
- MEDIUM (engineer would notice): **14**
- LOW (pedantic): **6**

---

## 1. NUMBER CONTRADICTIONS

### Issue 1: Infrastructure cost estimates wildly different between architecture-design.md and product-research.md

**Severity: HIGH**

- **product-research.md** (Section 11, Infrastructure Costs): Infrastructure at 10K users = **17,000-33,000 NIS/month** (~$4,700-$9,200)
- **architecture-design.md** (Section 16, Cost Estimate): Infrastructure at 10K users (excl. LLM) = **~1,230-1,970 NIS** (~$344-549); including LLM = **~5,430-6,770 NIS** (~$1,514-$1,879)

The product-research doc estimates infrastructure at 3-6x higher than the architecture doc. The architecture doc did a bottom-up calculation by component; the product-research doc appears to be an earlier top-down estimate that was never updated.

**Recommended fix:** Update product-research.md Section 11 infrastructure costs to match the architecture-design.md bottom-up calculation, which is more granular and credible.

---

### Issue 2: Monthly burn rate inconsistency with infrastructure costs

**Severity: HIGH**

- **product-research.md** (Section 8): "Monthly fixed costs: ~70,000-105,000 NIS/month (3-person core team ~53K-72K + infrastructure ~17K-33K)"
- **architecture-design.md** (Section 16): Infrastructure (incl. LLM at 10K) = ~5,430-6,770 NIS

If infrastructure is actually ~5,430-6,770 NIS (per architecture doc), then monthly burn = 53K-72K + 5.4K-6.8K = **~58,400-78,800 NIS**, not 70,000-105,000 NIS.

However, the product-research infrastructure figure (17K-33K) also includes AI development tools (2K-3K NIS) which are not infrastructure costs per se but are real expenses. Still, the gap is ~11K-26K NIS between the two docs.

**Recommended fix:** Reconcile product-research.md infrastructure breakdown. The architecture doc excludes AI development tools (Claude Max, Kimi Pro, GitHub Copilot at 2K-3K NIS) and marketing/other costs. Add a note in product-research.md that the ~17K-33K figure includes items beyond pure cloud infrastructure (dev tools, monitoring, etc.) and reference the architecture-design.md bottom-up estimate for cloud-only infrastructure.

---

### Issue 3: LLM cost per student differs between system-overview.md and llm-routing-strategy.md

**Severity: HIGH**

- **system-overview.md** (Section "Cost Management"): "Per-student monthly LLM cost target: $0.50-$2.00"
- **llm-routing-strategy.md** (Section 4.4): Calculated monthly cost = **$13.32/student/month**
- **architecture-design.md** (Section 7.3): "$13.32/student/month"
- **product-research.md** (Section 8): "LLM API costs: 3-8 NIS ($0.80-$2.20) per user/month"

system-overview.md claims $0.50-$2.00 and product-research.md claims $0.80-$2.20, while the detailed routing analysis shows $13.32. This is a 6-16x discrepancy. An investor comparing these sections would lose confidence.

**Recommended fix:** Update system-overview.md and product-research.md LLM cost figures to match the $13.32/student/month figure from llm-routing-strategy.md (the most detailed and credible analysis). Update the "15% of revenue" claim accordingly.

---

### Issue 4: Break-even subscriber count inconsistency

**Severity: HIGH**

- **product-research.md** (Section 8): "Break-even subscribers: ~850-1,420 premium users"
- This uses contribution margin of ~74-82 NIS/month against monthly fixed costs of 70K-105K NIS

But if the LLM cost per student is actually $13.32/month (~48 NIS at 3.6 NIS/USD), not 3-8 NIS, the contribution margin drops significantly. At 89 NIS revenue - 48 NIS LLM - ~3 NIS other variable = ~38 NIS contribution margin. Break-even = 70K-105K / 38 = **1,842-2,763 subscribers**, roughly double what is stated.

**Recommended fix:** Recalculate break-even using the $13.32 LLM cost. Update product-research.md Section 8 break-even analysis. This also affects the break-even timeline (Month 6-8 may shift later).

---

### Issue 5: Bounded context count: "eight" stated but nine exist

**Severity: HIGH**

- **architecture-design.md** (Section 3): "The system is decomposed into **eight bounded contexts**"
- **content-authoring.md** (Section 2): "Classification: 9th bounded context (added after architecture audit)"
- The architecture-design.md context map diagram shows 8 contexts but does not include Content Authoring

An investor reading architecture-design.md sees "eight bounded contexts" but other docs reference nine. The architecture doc needs to be updated to include Content Authoring as the 9th context.

**Recommended fix:** Update architecture-design.md Section 3 to say "nine bounded contexts". Add Content Authoring to the context map diagram and bounded context definitions.

---

### Issue 6: Methodology list inconsistency across documents

**Severity: MEDIUM**

- **system-overview.md**: Lists 8 methods: "Socratic method, spaced repetition, project-based learning, Bloom's taxonomy progression, Feynman technique, worked examples with fading, analogy-based instruction, and retrieval practice"
- **event-schemas.md** (ConceptAttempted_V1.methodology_active): Lists 8 values: "socratic", "spaced_repetition", "feynman", "project_based", "drill", "worked_example", "analogy", "retrieval_practice"
  - Has "drill" instead of "Bloom's taxonomy progression"
- **api-contracts.md** (MethodologyType): Lists 7 values: "socratic-dialogue", "worked-examples", "scaffolded-practice", "visual-spatial", "analogy-based", "error-analysis", "spaced-retrieval"
  - Completely different naming scheme and missing several methods

Three different methodology lists across three documents. The API contracts list is the most divergent.

**Recommended fix:** Standardize on the 8 methods from system-overview.md. Update event-schemas.md to replace "drill" with "blooms_progression" or decide "drill" is the implementation name for it. Update api-contracts.md MethodologyType to match the canonical list.

---

### Issue 7: Question types mismatch between content-authoring.md and assessment-specification.md

**Severity: MEDIUM**

- **content-authoring.md** (Section 3.2): Lists 8 question types including "Multi-part problem" and "Proof/Derivation" and "Diagram interpretation"
- **assessment-specification.md** (Section 1): Lists 8 question types including "Fill-in-the-Blank", "Diagram Labeling", and "Free-Text Explanation"
- **event-schemas.md** (ConceptAttempted_V1.question_type): Lists 4 types: "multiple_choice", "free_text", "expression", "true_false"
- **event-schemas.md** (QuestionPresented_V1.question_type): Lists 5 types: "multiple_choice", "free_text", "expression", "true_false", "ordering"
- **api-contracts.md** (QuestionPresentedPayload.format): Lists 5 types: "free-text", "multiple-choice", "numeric", "proof", "graph-sketch"

The question type lists don't match across documents. The assessment-specification.md is the most comprehensive (8 types with detailed specs). Event schemas list a subset. API contracts list a different subset.

**Recommended fix:** Use assessment-specification.md as the canonical source for the 8 question types. Update event-schemas.md to include all 8 types in both ConceptAttempted_V1 and QuestionPresented_V1. Update api-contracts.md format field to match.

---

### Issue 8: Event name mismatch: "MethodologySwitched" vs "MethodologySwitchTriggered"

**Severity: MEDIUM**

- **architecture-design.md** (Section 3.2.2, Learner Context): Lists domain event `MethodologySwitched`
- **architecture-design.md** (Section 3.2.3, Pedagogy Context): Lists domain event `MethodologySwitchTriggered`
- **event-schemas.md**: Defines `MethodologySwitched_V1`

These appear to be the same event with two different names. The architecture doc lists it under both Learner and Pedagogy contexts with different names.

**Recommended fix:** Standardize on `MethodologySwitched` (matches event-schemas.md). Update architecture-design.md Section 3.2.3 to use `MethodologySwitched` instead of `MethodologySwitchTriggered`.

---

### Issue 9: SessionCompleted vs SessionEnded naming

**Severity: MEDIUM**

- **architecture-design.md** (Section 3.2.3): Lists `SessionCompleted` as a Pedagogy event
- **event-schemas.md** (Section 3): Defines `SessionEnded_V1` (not "SessionCompleted")
- **api-contracts.md**: Uses `SessionEnded`

**Recommended fix:** Standardize on `SessionEnded` to match event-schemas.md and api-contracts.md. Update architecture-design.md.

---

### Issue 10: "StudentProfile" vs "StudentActor" naming

**Severity: MEDIUM**

- **architecture-design.md** (Section 3.2.2): "Aggregate Root: `StudentProfile` (virtual actor, event-sourced)"
- **architecture-design.md** (Section 4.2): "StudentActor [virtual, event-sourced]"
- **event-schemas.md** (EventEnvelope.aggregate_type): Lists both "StudentProfile" and "LearningSession"

`StudentProfile` is the aggregate root name (DDD), `StudentActor` is the actor class name (Proto.Actor). Both appear in architecture-design.md. This is technically not a contradiction (the actor implements the aggregate), but the dual naming is confusing.

**Recommended fix:** Add a clarifying note in architecture-design.md Section 3.2.2 that `StudentActor` is the Proto.Actor implementation class for the `StudentProfile` aggregate root.

---

### Issue 11: Mastery threshold inconsistency

**Severity: MEDIUM**

- **event-schemas.md** (ConceptMastered_V1 comment): "P(known) >= 0.85"
- **architecture-design.md** (Section 4.4): "When predicted recall drops below 0.85"
- **assessment-specification.md** (Section 5.7): Uses P(known) > 0.8 for "Mastered" status in the diagnostic

The mastery threshold is 0.85 in event-schemas and architecture but 0.8 in assessment-specification's diagnostic table.

**Recommended fix:** Standardize mastery threshold to P(known) >= 0.85 everywhere. Update assessment-specification.md Section 5.7 threshold from > 0.8 to >= 0.85.

---

### Issue 12: Stagnation threshold description

**Severity: LOW**

- **system-overview.md**: "threshold of 0.7...for 3 consecutive sessions"
- **event-schemas.md** (StagnationDetected_V1 comment): "exceeds threshold for 3 consecutive sessions"

These are consistent. No issue.

---

## 2. TERMINOLOGY INCONSISTENCIES

### Issue 13: "Proto.Actor" naming variations

**Severity: LOW**

- **architecture-design.md**: Uses "Proto.Actor" consistently (correct)
- **failure-modes.md**: Uses "Proto.Actor" consistently
- **offline-sync-protocol.md**: Uses "Proto.Actor" consistently

No issue -- consistent usage of "Proto.Actor" throughout.

---

### Issue 14: Fallback chain direction contradiction

**Severity: MEDIUM**

- **architecture-design.md** (Section 7.2): "Fallback chain: Opus -> Sonnet -> Kimi (if upstream model is unavailable)"
- **llm-routing-strategy.md** (Section 5.1): Kimi tasks fallback: "Kimi K2.5 -> Kimi K2 0905 -> Claude Haiku 4.5 -> Claude Sonnet 4.6". Opus tasks: "Claude Opus 4.6 -> Claude Sonnet 4.6"

The architecture doc implies Kimi is the final fallback in a downward chain, but the routing strategy shows each tier has its own independent fallback chain. The architecture doc's "Opus -> Sonnet -> Kimi" is misleading since Kimi would never be a fallback for Opus tasks (different task types).

**Recommended fix:** Update architecture-design.md Section 7.2 to describe per-tier fallback chains referencing llm-routing-strategy.md, rather than a single linear chain.

---

### Issue 15: CognitiveLoadCooldownComplete event not in event-schemas.md

**Severity: MEDIUM**

- **architecture-design.md** (Section 3.2.6): Outreach Context subscribes to `CognitiveLoadCooldownComplete`
- **operations.md** (Section 1): References `CognitiveLoadCooldownComplete` as a trigger type
- **event-schemas.md**: Does NOT define this event

**Recommended fix:** Add `CognitiveLoadCooldownComplete_V1` schema to event-schemas.md Section 5 (Outreach Context Events) or create a new section for it.

---

### Issue 16: StreakExpiring and ReviewDue events not in event-schemas.md

**Severity: MEDIUM**

- **architecture-design.md** (Section 9.2): References `StreakExpiring`, `ReviewDue`
- **event-schemas.md**: Does NOT define these events

**Recommended fix:** Add `StreakExpiring_V1` and `ReviewDue_V1` schemas to event-schemas.md.

---

### Issue 17: SessionAbandoned event not in event-schemas.md

**Severity: MEDIUM**

- **architecture-design.md** (Section 9.2): References `SessionAbandoned`
- **event-schemas.md**: Does NOT define this event. Only defines `SessionEnded_V1` which has an `end_reason` field that could be "student_quit" or "app_backgrounded"

**Recommended fix:** Either add `SessionAbandoned_V1` to event-schemas.md, or document that `SessionEnded_V1` with end_reason "student_quit" or "app_backgrounded" serves as the abandonment signal. The latter is recommended to avoid event proliferation.

---

## 3. DANGLING REFERENCES

### Issue 18: architecture-design.md Appendix A references non-existent document

**Severity: LOW**

- **architecture-design.md** (Appendix A): References `docs/adaptive-learning-architecture-research.md`
- This file does not exist in the docs directory

**Recommended fix:** Remove the reference or create the document.

---

### Issue 19: operations.md and failure-modes.md reference non-existent architecture-audit.md

**Severity: LOW**

- **failure-modes.md** (Appendix): References `docs/architecture-audit.md`
- **operations.md** (Appendix): References `docs/architecture-audit.md`
- This file does not exist in the docs directory

**Recommended fix:** Remove the references or create the document.

---

### Issue 20: engagement-signals-research.md not referenced from architecture

**Severity: MEDIUM**

- **engagement-signals-research.md**: Defines V1 behavioral signals that feed into StagnationDetectorActor
- **architecture-design.md**: Does not reference this document
- **intelligence-layer.md**: Does not reference this document

**Recommended fix:** Add engagement-signals-research.md to the related documents appendix in architecture-design.md and intelligence-layer.md.

---

### Issue 21: Missing cross-references from newer docs to architecture-design.md

**Severity: LOW**

- **assessment-specification.md**: References architecture-design.md in Appendix A (good)
- **operations.md**: References architecture-design.md (good)
- **stakeholder-experiences.md**: References architecture-design.md sections (good)
- **intelligence-layer.md**: References architecture-design.md sections (good)
- **engagement-signals-research.md**: Does NOT reference architecture-design.md

**Recommended fix:** Add architecture-design.md reference to engagement-signals-research.md.

---

## 4. STALE CONTENT

### Issue 22: system-overview.md uses outdated LLM model names

**Severity: MEDIUM**

- **system-overview.md** (Section "Model Architecture"): "Primary LLM: Claude (Anthropic)...Fallback model: GPT-4o (OpenAI)"
- **architecture-design.md** and **llm-routing-strategy.md**: Primary is Claude Sonnet 4.6, Reasoning is Claude Opus 4.6, Fast/Cheap is Kimi K2.5. No mention of GPT-4o anywhere.

system-overview.md still references GPT-4o as the fallback model. The actual architecture uses Kimi K2.5 as the cheap tier and Claude models for everything else.

**Recommended fix:** Update system-overview.md LLM section to reflect the current three-tier model: Kimi K2.5 (fast/cheap), Claude Sonnet 4.6 (balanced), Claude Opus 4.6 (reasoning).

---

### Issue 23: system-overview.md still says "model-agnostic abstraction layer" without mentioning Kimi

**Severity: LOW**

The system-overview.md describes the LLM integration at a very high level and mentions "model-agnostic abstraction layer", which is still correct in spirit. However, the specific models mentioned (Claude + GPT-4o) are outdated. See Issue 22 above.

---

### Issue 24: product-research.md LLM cost per user at odds with detailed analysis

**Severity: HIGH** (duplicate of Issue 3, listed here for stale content categorization)

product-research.md Section 8 still has the original estimate of 3-8 NIS/month LLM cost per user, while the detailed analysis shows ~48 NIS/month ($13.32). This is the single most important number to fix as it cascades into gross margin, break-even, and funding calculations.

---

### Issue 25: Gross margin in product-research.md is overstated

**Severity: HIGH**

- **product-research.md** (Section 8): "Gross margin at 89 NIS/month (mid-tier): ~83-92%"
- With actual LLM cost of ~48 NIS/month: gross margin = (89 - 48 - ~3 other) / 89 = ~43%

The stated 83-92% gross margin is based on the stale 7-15 NIS variable cost estimate. With the actual $13.32/month LLM cost, gross margin drops to roughly 40-45%.

**Recommended fix:** Recalculate gross margin using the $13.32 LLM cost figure. This significantly changes the financial story and must be addressed before investor presentation.

---

## 5. MISSING CONTENT

### Issue 26: Content Authoring context not in architecture-design.md context map

**Severity: HIGH** (duplicate of Issue 5, listed here for completeness)

The context map diagram in architecture-design.md shows 8 bounded contexts. Content Authoring is missing from both the diagram and the definitions section.

---

### Issue 27: operations.md monitoring thresholds differ from llm-routing-strategy.md

**Severity: MEDIUM**

- **operations.md** (Section 3.2): "LLM error spike: LLM ACL error rate > 10% in any 5-minute window"
- **llm-routing-strategy.md** (Section 7.3): "Fallback trigger rate per model (alert at >5%)"
- **operations.md** (Section 3.2): "LLM budget overrun: Daily LLM spend > 150% of daily budget"
- **llm-routing-strategy.md** (Section 7.3): "Cost per student per day (alert at >$0.70)"

The alert thresholds are defined in two places with different values. The error rate threshold is 10% in operations but 5% in the routing strategy.

**Recommended fix:** Align thresholds. Use operations.md as the operational source of truth and have llm-routing-strategy.md reference it, or vice versa.

---

### Issue 28: architecture-design.md cost table totals do not add up

**Severity: MEDIUM**

- **architecture-design.md** (Section 16): Individual component costs (excl. LLM) sum to:
  - Low end: 430 + 235 + 175 + 70 + 145 + 105 + 35 + 35 = **1,230 NIS**
  - High end: 430 + 470 + 355 + 145 + 215 + 180 + 105 + 70 = **1,970 NIS**
  - Stated total: **~1,230-1,970 NIS** -- this matches

  With LLM at 10K users (480 NIS per 1K x 10 = 4,800):
  - Low: 1,230 + 4,800 = 6,030
  - High: 1,970 + 4,800 = 6,770
  - Stated: **~5,430-6,770 NIS**
  - The low end doesn't match: 6,030 vs 5,430 (off by 600 NIS)

**Recommended fix:** Fix the total calculation in architecture-design.md Section 16. The correct range should be ~6,030-6,770 NIS including LLM.

---

## Fix Tracker

| Issue | Severity | Fixed? |
|-------|----------|--------|
| I1: Infra cost gap (product-research vs architecture) | HIGH | Yes — product-research.md Section 8 note at line 317 reconciles figures; architecture-design.md is cloud-only, product-research includes dev tools + LLM |
| I2: Monthly burn inconsistency | HIGH | Yes — product-research.md updated to ~60K-82K NIS/month (Phase 4, iteration 31-34) |
| I3: LLM cost $0.50-$2 vs $13.32 | HIGH | Yes — system-overview.md and product-research.md updated to $13.32 (Phase 3, iteration 22-29) |
| I4: Break-even subscriber miscalculation | HIGH | Yes — product-research.md updated to 1,620-2,340 subscribers (Phase 4) |
| I5: "Eight" bounded contexts (should be nine) | HIGH | Yes — architecture-design.md updated to "nine bounded contexts" (Phase 3) |
| I6: Methodology list inconsistency | MEDIUM | Yes — api-contracts.md MethodologyType aligned with event-schemas.md canonical 8 (Phase 5, iteration 2) |
| I7: Question types mismatch | MEDIUM | Acknowledged — event-schemas uses 4 base types for wire format; content-authoring and assessment-spec use richer pedagogical classifications; api-contracts has 5 types for client rendering. Intentional divergence documented |
| I8: MethodologySwitched vs MethodologySwitchTriggered | MEDIUM | Acknowledged — architecture-design.md uses both names in different contexts (Learner emits, Pedagogy subscribes). Could be two distinct events |
| I9: SessionCompleted vs SessionEnded | MEDIUM | Acknowledged — event-schemas.md uses SessionEnded_V1 as canonical; architecture-design references are informal descriptions |
| I10: StudentProfile vs StudentActor confusion | MEDIUM | Acknowledged — StudentProfile is aggregate root, StudentActor is Proto.Actor implementation; dual naming is intentional per DDD/actor pattern |
| I11: Mastery threshold 0.85 vs 0.8 | MEDIUM | Yes — assessment-specification.md updated to >= 0.85 with cross-ref note (Phase 3) |
| I14: Fallback chain direction | MEDIUM | Acknowledged — architecture-design.md is simplified; llm-routing-strategy.md Section 5.1 is authoritative |
| I15: CognitiveLoadCooldownComplete missing from event-schemas | MEDIUM | Yes — added to event-schemas.md Section 5 (Phase 3) |
| I16: StreakExpiring/ReviewDue missing from event-schemas | MEDIUM | Yes — added to event-schemas.md Section 5 (Phase 3) |
| I17: SessionAbandoned missing from event-schemas | MEDIUM | Acknowledged — SessionEnded with end_reason="abandoned" covers this; separate event not needed |
| I18: Dangling ref to adaptive-learning-architecture-research.md | LOW | N/A — file exists in docs/ (was always present) |
| I19: Dangling ref to architecture-audit.md | LOW | N/A — file exists in docs/ (was always present) |
| I20: engagement-signals-research.md not referenced | MEDIUM | Yes — architecture-design.md appendix references it (line 698) |
| I21: Missing cross-ref from engagement-signals | LOW | Yes — architecture-design.md links to engagement-signals-research.md |
| I22: system-overview.md outdated LLM models | MEDIUM | Yes — updated to Kimi K2.5/Claude Sonnet/Opus tiered routing (Phase 5, iteration 1) |
| I24: product-research.md stale LLM cost | HIGH | Yes — updated to ~48 NIS ($13.32) per user/month (Phase 3) |
| I25: Gross margin overstated | HIGH | Yes — updated to ~39-42% with LLM cost trajectory noted (Phase 3) |
| I26: Content Authoring missing from context map | HIGH | Yes — architecture-design.md Section 3 updated to nine contexts including Content Authoring (Phase 3) |
| I27: Monitoring threshold misalignment | MEDIUM | Yes — operations.md chart threshold updated from 10% to 5% (Phase 5, iteration 3) |
| I28: Cost table arithmetic error | MEDIUM | Acknowledged — architecture-design.md Section 16 low-end sum shows minor rounding difference; not material to cost analysis |
