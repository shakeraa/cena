# EPIC-PRR-G: SAT + PET content engineering (launch blocker)

**Priority**: P0
**Effort**: XL (content-engineering epic: 8-16 weeks, not code-weeks)
**Lens consensus**: persona-educator, persona-cogsci, persona-ministry, persona-privacy, persona-finops
**Source docs**: [docs/design/MULTI-TARGET-EXAM-PLAN-001-discussion.md §4](../../docs/design/MULTI-TARGET-EXAM-PLAN-001-discussion.md), persona-finops findings (~$10-15k one-shot budget estimate)
**Assignee hint**: content-engineering lead (human) + parallel subject-matter experts + CAS-gate reviewers
**Tags**: source=multi-target-exam-plan-001, type=epic, epic=epic-prr-g, content-engineering, launch-blocker
**Status**: Not Started — blocked on decision-holder budget approval
**Source**: User decision 2026-04-21: SAT + Psychometry ship fully-functional v1, not "coming soon" flags. Per memory "No stubs — production grade", this requires real item banks on day one.

---

## Epic goal

Stand up production-grade item banks, rubric DSL entries, coverage matrix rows, and per-language content for **SAT** (Math + Reading + Writing & Language) and **PET / Psychometry** (Quantitative + Verbal × {Hebrew, Arabic, Russian?} + English + combined scoring) so that a student who picks either target in multi-target onboarding (EPIC-PRR-F) gets real adaptive sessions on day one.

## Why this is a launch blocker parallel to EPIC-PRR-F

EPIC-PRR-F adds SAT + PET to the exam catalog and renders them as selectable onboarding targets. Without EPIC-PRR-G's content, a student who picks "SAT" or "Psychometry" gets a broken session — either an empty item bank or fallback Bagrut items that don't match the exam format. Per memory "No stubs — production grade" (2026-04-11), shipping placeholder items is banned. Therefore EPIC-F **cannot ship without EPIC-G**.

## Scope — what's in

### SAT
- **Math (no-calc + calc)**: US-curriculum; linear functions, data analysis, passport-to-advanced-math, problem-solving & data analysis, geometry + trig. Multiple-choice + grid-in. CAS-gated.
- **Reading**: passage + comprehension items. Factual, vocabulary-in-context, inference, rhetorical.
- **Writing & Language**: grammar + rhetoric in passage context. Copy-edit tasks.
- **Item formats**: MC (4 opts), grid-in (math only). English-primary per memory "Language Strategy".

### PET (Psychometric Entrance Test / NITE — not Ministry-of-Education)
- **Quantitative Reasoning**: number problems, word problems, geometry, data interpretation. Hebrew / Arabic / English native versions.
- **Verbal Reasoning (Hebrew)**: native Hebrew; vocabulary, analogies, sentence completion, logic, reading comprehension.
- **Verbal Reasoning (Arabic)**: native Arabic; parallel structure to Hebrew. **Not machine-translated** — language-native authoring.
- **Verbal Reasoning (Russian)** — decision-gated on brief §14.5 Q2 (olim population).
- **English section**: ESL-style, Israeli-PET-specific.
- **Combined quantitative+verbal scoring logic** — PET's composite score is a specific algorithm, not a simple sum.

## Scope — what's out (v1)

- SAT Essay (optional section; deprecated by CollegeBoard 2021 but some schools request).
- ACT, GCSE, IB — no target in catalog.
- PET Mekhina prep — not NITE-scored.

## Deliverables

| Artefact | SAT | PET |
|---|---|---|
| Item bank size (initial) | ≥250 math + ≥120 reading + ≥120 writing = 490 | ≥200 quant × {HE,AR} + ≥200 verbal HE + ≥200 verbal AR + ≥100 English = ~900 |
| Rubric DSL entries (PRR-033) | full section-to-item mapping | full section × language |
| Coverage matrix rows (PRR-072) | topic × difficulty × item-count | topic × language × difficulty × item-count |
| CAS-gate coverage | 100% of math items | 100% of quant items |
| Per-item provenance (author, source, CAS-verified-at) | yes | yes |
| Banned-text scan (copyright per ADR-0002 recreation rule) | yes | yes |

## Non-negotiables

- Per memory "Bagrut reference-only": no raw CollegeBoard or NITE item text. All items are AI-authored + human-reviewed recreations verifiable against public syllabus.
- ADR-0002 (SymPy CAS oracle): 100% of math / quantitative items CAS-gated. No exceptions.
- Memory "Honest not complimentary": item coverage claims are numeric + CI-bounded. If item-bank is thin on a sub-topic, surface it, don't hide it.
- Per memory "No stubs — production grade": no item bank below minimum size ships. If authoring slips, launch slips.
- Per PRR-022 (ban PII in LLM prompts): PET verbal items must not embed real student-generated content as training data.

## Dependencies

- Blocked on **decision-holder Q4 (brief §14.5)**: who owns the $10-15k content-engineering budget line? What's the approval path?
- Blocked on **decision-holder Q2 (brief §14.5)**: PET Russian-verbal in v1 scope or v2?
- Coordinates with [PRR-033 Bagrut rubric DSL](TASK-PRR-033-ministry-bagrut-rubric-dsl-version-pinning-per-track-sign-of.md) — rubric DSL is extended, not forked.
- Coordinates with [PRR-072 coverage matrix](post-launch/TASK-PRR-072-item-bank-coverage-matrix-vs-bagrut-syllabus.md) — same structure, extended with new exam families.

## Sub-tasks

Decomposition deferred to content-engineering lead after budget approval. Expected shape: one sub-task per (exam × section × language) = ~12-18 sub-tasks.

## Related

- [EPIC-PRR-F — Multi-target onboarding + plan aggregate](EPIC-PRR-F-multi-target-onboarding-plan.md)
- [MULTI-TARGET-EXAM-PLAN-001 discussion brief §4, §14.5 Q4](../../docs/design/MULTI-TARGET-EXAM-PLAN-001-discussion.md)
- [persona-finops findings](../../pre-release-review/reviews/persona-finops/multi-target-exam-plan-findings.md)
- [ADR-0002 — SymPy CAS oracle](../../docs/adr/0002-sympy-correctness-oracle.md)
