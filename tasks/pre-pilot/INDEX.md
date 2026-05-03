# Pre-Pilot Task Index

> Tasks identified by the expert panel codebase review (2026-04-13).  
> All tasks are written at **architect level** — full context, file paths, C# code samples, research citations, acceptance criteria.  
> Source: [EXPERT-PANEL-REVIEW.md](EXPERT-PANEL-REVIEW.md)

---

## Critical Blockers (must resolve before any pilot)

| ID | Task | Domain | Identified By |
|----|------|--------|---------------|
| [PP-001](TASK-PP-001-csam-detection-wire.md) | Wire CSAM hash detection to PhotoDNA + AI safety classification | Assessment Security | Ran (Security) |
| [PP-002](TASK-PP-002-compliance-docs-legal.md) | Complete compliance docs with Israeli privacy lawyer | Compliance | Dr. Rami (Adversarial) |
| [PP-003](TASK-PP-003-cross-track-mastery-adr.md) | Decide cross-track mastery sharing model (ADR decision) | Tenancy / Mastery | Dr. Rami (Adversarial) |

## High Priority (should resolve before pilot)

| ID | Task | Domain | Identified By |
|----|------|--------|---------------|
| [PP-004](TASK-PP-004-cas-math-extraction.md) | Harden CAS LLM output math claim extraction + fix IsAnswerLeak | CAS Engine | Dr. Rami (Adversarial) |
| [PP-005](TASK-PP-005-per-student-rate-limit.md) | Add per-student API rate limit (Tier 3) | Rate Limiting | Ran (Security) |
| [PP-006](TASK-PP-006-autofilled-credit-reduction.md) | Reduce AutoFilled assistance credit from 0.25 to 0.05 | BKT+ Mastery | Dr. Nadia (Pedagogy) |
| [PP-007](TASK-PP-007-db-migration-safety-net.md) | Create DB-03/04/07 migration safety net (AutoCreate.None + CI gate + deploy sequence) | DB Infrastructure | Oren (Architect) |

## Medium Priority (improve before or during pilot)

| ID | Task | Domain | Identified By |
|----|------|--------|---------------|
| [PP-008](TASK-PP-008-cas-error-detection-enum.md) | Replace CAS error string convention with typed enum | CAS Engine | Dina (Architect) |
| [PP-009](TASK-PP-009-step-hint-answer-mask.md) | Route step solver hints through answer-mask filter | CAS Engine | Oren (Architect) |
| [PP-010](TASK-PP-010-bkt-halflife-per-category.md) | Skill-category-specific half-life for BKT+ forgetting curve | BKT+ Mastery | Dr. Yael (Psychometrics) |
| [PP-011](TASK-PP-011-irt-honest-labeling.md) | Honest labeling of IRT calibration (Rasch not 2PL) + minimum N thresholds | IRT & CAT | Dr. Yael (Psychometrics) |
| [PP-012](TASK-PP-012-figure-quality-gate-ai-revalidation.md) | Re-run figure quality gate after AI figure generation | Figures | Dr. Nadia (Pedagogy) |
| [PP-013](TASK-PP-013-rtl-step-instructions.md) | Add RTL direction handling on step solver instructions | Step Solver UI | Tamar (RTL/a11y) |
| [PP-014](TASK-PP-014-arabic-physics-variables.md) | Expand Arabic math normalizer with physics variables | Localization | Prof. Amjad (Bagrut) |
| [PP-015](TASK-PP-015-arabic-bidi-normalization.md) | Bidi-aware Arabic math normalization (prevent visual corruption) | Localization | Tamar (RTL/a11y) |
| [PP-016](TASK-PP-016-invite-redeem-rate-limit.md) | Wire rate limiting on invite code redemption | Tenancy | Oren (Architect) |

## Low Priority (enhancement, not blocking)

| ID | Task | Domain | Identified By |
|----|------|--------|---------------|
| [PP-017](TASK-PP-017-productive-failure-cas-wiring.md) | Wire Exploratory scaffolding level into CAS response messaging | Pedagogy | Dr. Nadia (Pedagogy) |

---

## Totals

| Priority | Count |
|----------|-------|
| Critical | 3 |
| High | 4 |
| Medium | 9 |
| Low | 1 |
| **Total** | **17** |
