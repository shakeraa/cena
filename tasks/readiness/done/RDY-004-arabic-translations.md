# RDY-004: Arabic Translations (Top 200 Questions)

- **Priority**: SHIP-BLOCKER — primary user language has zero content (80% of target users)
- **Complexity**: Human translators + engineer for pipeline
- **Source**: Expert panel audit — Amjad (Curriculum)
- **Tier**: 0 (blocks deployment — Arabic is the primary user language)
- **Effort**: 4-6 weeks (translator-dependent, includes QA and rework)

> **Cross-review (Amjad)**: Effort upgraded from 2-3 weeks to 4-6 weeks. Per-question translation takes 22-32 min (stem + 4 options + distractors + bidi check + gender agreement + terminology consistency). 200 questions = 75-100 hours minimum + 30% rework. Need 2 translators for peer review.

## Problem

All 1,000 seed questions exist only in Hebrew. The `LanguageVersions` dictionary on `QuestionDocument` supports `he`, `ar`, `en` variants, and the `AddLanguageVersionAsync` endpoint exists — but zero translations have been created.

The platform targets Arabic-speaking students in northern Israel. Launching without Arabic content negates the product's core value proposition.

## Scope

### 1. Prioritize top 200 questions by coverage breadth

Select 200 questions that maximize topic coverage across Math 5-unit (primary beachhead). Prefer questions that cover the most distinct concepts rather than depth in one concept.

### 2. Arabic translation (human, not MT)

Per research doc (`docs/autoresearch/arabic-math-education-research.md`): machine translation "introduces pronoun gender errors" and is not recommended. Use human translators familiar with Israeli Arabic math conventions.

Translation requirements:
- Stem text in MSA (Modern Standard Arabic)
- Math notation stays in LTR (Israeli Arab students use Western Arabic numerals 0-9)
- Answer options translated
- Distractors translated with equivalent error reasoning
- Term consistency checked against the 30-term Arabic glossary

### 3. Expand Arabic glossary

Current: 30 terms. Target: 100+ terms covering all concepts in the top 200 questions. Add to the existing glossary in `contracts/llm/prompt-templates.py`.

### 4. Translation QA gate

Add a validation step: every Arabic translation must pass:
- Term consistency check against glossary
- Bidi rendering check (no visual corruption in mixed Arabic + math)
- Gender agreement check (Arabic math terms are gendered)

## Files to Modify

- `src/api/Cena.Admin.Api/QuestionBankSeedData.cs` — add Arabic LanguageVersions
- `contracts/llm/prompt-templates.py` — expand glossary to 100+ terms
- New: `scripts/translation-qa.ts` — automated QA checks for translations
- `src/shared/Cena.Infrastructure/Content/QualityGateService.cs` — add translation completeness check

## Acceptance Criteria

- [ ] 200 questions have Arabic `LanguageVersions` with stem + all options translated
- [ ] Arabic glossary expanded to 100+ terms
- [ ] All Arabic translations pass term consistency check
- [ ] Arabic + math mixed-direction rendering verified in QuestionCard
- [ ] Quality gate flags questions missing Arabic translation (warning, not blocker)
- [ ] Translation QA script runs as part of content pipeline
