# RDY-004a: Translation QA Pipeline Integration

- **Priority**: High — completes the non-translation infrastructure for RDY-004
- **Complexity**: Mid engineer
- **Source**: RDY-004 split (AI-doable portion)
- **Tier**: 1
- **Effort**: 1-2 days
- **Depends on**: RDY-027 (glossary, done)
- **Parent**: RDY-004 (split into 4a infrastructure + 4b human translation)

## Problem

RDY-004 requires 200 Arabic question translations done by human translators. But the QA/enforcement infrastructure around those translations is AI-doable and should ship first so the pipeline is ready the moment translations arrive.

Current state:

- ✅ `scripts/translation-qa.mjs` exists (term consistency, bidi check, gender agreement)
- ✅ `config/glossary.json` has 271 terms
- ✅ `contracts/llm/prompt-templates.py` Arabic glossary expanded to 150+ terms
- ❌ `translation-qa.mjs` is NOT wired into CI
- ❌ `QualityGateService` does NOT flag missing `ar` LanguageVersions
- ❌ No analytics event for "question served in fallback language because `ar` missing"

## Scope

### 1. Wire translation-qa.mjs into CI

- Add `.github/workflows/translation-qa.yml` running on PR + main
- Runs `node scripts/translation-qa.mjs --verbose`
- Fails CI if new Arabic translations violate glossary term consistency or have bidi issues
- Allows missing translations (warn-only — this ships before RDY-004b)

### 2. Quality gate translation completeness rule

In `src/api/Cena.Admin.Api/QualityGate/QualityGateService.cs`:

- Add `CheckTranslationCompleteness(QuestionDocument q)` rule
- Returns `Warning` (NOT blocker) when `LanguageVersions` is missing `ar` entry
- Warning message includes concept ID + recommended translator assignment
- Per RDY-004 spec: "Quality gate flags questions missing Arabic translation (warning, not blocker)"

### 3. Analytics: fallback-language events

When a student requests a question and the requested locale is missing:

- Emit `QuestionFallbackLanguage_V1` event (studentId, questionId, requestedLocale, servedLocale, timestamp)
- Register in `MartenConfiguration`
- Admin dashboard tile: "Questions served in fallback language — last 7 days"
- Surfaces translation-gap impact on real students

### 4. Regenerate translation gap report

- `scripts/translation-gap-report.mjs` (new) — per-concept count of questions with/without Arabic translation
- Output: `docs/content/translation-gap-YYYY-MM-DD.md` with priorities for the human translator queue (RDY-004b)

## Files to Modify

- New: `.github/workflows/translation-qa.yml`
- `src/api/Cena.Admin.Api/QualityGate/QualityGateService.cs` — add translation completeness rule
- `src/api/Cena.Admin.Api.Tests/QualityGate/QualityGateTests.cs` — test for translation check
- New: `src/actors/Cena.Actors/Events/QuestionFallbackLanguage_V1.cs`
- `src/actors/Cena.Actors/Configuration/MartenConfiguration.cs` — register event
- `src/api/Cena.Student.Api.Host/Endpoints/SessionEndpoints.cs` — emit event on fallback
- New: `scripts/translation-gap-report.mjs`
- `src/api/Cena.Admin.Api/AdminDashboardEndpoints.cs` — add fallback-language tile endpoint

## Acceptance Criteria

- [ ] CI job runs `translation-qa.mjs` on every PR
- [ ] Quality gate emits `Warning` for questions missing Arabic translation
- [ ] `QuestionFallbackLanguage_V1` event emitted when student is served a non-requested locale
- [ ] Admin dashboard shows fallback-language count (last 7 days)
- [ ] `translation-gap-report.mjs` produces per-concept priority list for translators
- [ ] Tests cover the new quality-gate rule
- [ ] `helm lint` + `dotnet build` + `npm run lint` all pass
