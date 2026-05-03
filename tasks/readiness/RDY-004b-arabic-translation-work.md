# RDY-004b: Arabic Translation Content (Top 200 Questions)

- **Priority**: SHIP-BLOCKER — Arabic is the primary user language (80% of target users)
- **Complexity**: Human translators (2 peer reviewers) + PM coordination
- **Source**: RDY-004 split (human-only portion)
- **Tier**: 0 (blocks deployment)
- **Effort**: 4-6 weeks (translator-dependent)
- **Depends on**: RDY-004a (QA pipeline must be in place first), RDY-027 (glossary, done)
- **Parent**: RDY-004 (split into 4a infrastructure + 4b translation work)

## Problem

All 1,000 seed questions exist only in Hebrew. Top 200 (by topic coverage breadth) need Arabic `LanguageVersions` populated. This work cannot be done by AI — per research doc (`docs/autoresearch/arabic-math-education-research.md`), machine translation introduces pronoun gender errors and is banned for math content.

## Scope

### 1. Translator sourcing

- Identify 2 translators fluent in Israeli Arabic math conventions
- Confirm budget (estimated $15-30K for 200 questions × 2 translators for peer review)
- Establish QA + rework cycle

### 2. Translation prioritization

- Use `scripts/translation-gap-report.mjs` (from RDY-004a) output to select 200 questions
- Prefer questions that maximize distinct concept coverage (not depth in one concept)
- Start with Math 5-unit (primary beachhead track)

### 3. Translation execution

Per-question requirements (from RDY-004 original):

- Stem text in MSA (Modern Standard Arabic)
- Math notation stays in LTR (Israeli Arab students use Western Arabic numerals 0-9)
- Answer options translated
- Distractors translated with equivalent error reasoning
- Term consistency against the 271-term glossary (enforced by `translation-qa.mjs`)
- Gender agreement correct (enforced by `translation-qa.mjs`)

### 4. Peer review cycle

Translator A translates → Translator B reviews → QA script runs → merge to `QuestionBankSeedData.cs`.

### 5. Rework budget

Plan for 30% rework rate on first pass — common issues are terminology inconsistency and bidi rendering errors caught by automated QA.

## Files to Modify

- `src/api/Cena.Admin.Api/QuestionBankSeedData.cs` — add Arabic LanguageVersions to 200 questions

## Acceptance Criteria

- [ ] 200 questions have Arabic `LanguageVersions` with stem + all options translated
- [ ] All translations pass `scripts/translation-qa.mjs` (0 errors)
- [ ] Arabic + math mixed-direction rendering verified in QuestionCard (visual QA)
- [ ] Peer-review record for each of the 200 questions (2-translator sign-off)
- [ ] Topic coverage report shows 200 questions span ≥80% of Math 5-unit concepts

## Notes

- DO NOT use MT (Google Translate, GPT, etc.) even for first drafts — research shows gender errors propagate even with post-editing
- DO use the glossary canonical terms consistently (`config/glossary.json`)
- Track translation progress in a shared spreadsheet or via the admin dashboard fallback-language tile (from RDY-004a)
