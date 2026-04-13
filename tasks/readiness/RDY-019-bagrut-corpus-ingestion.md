# RDY-019: Bagrut Corpus Ingestion + Topic Taxonomy

- **Priority**: Medium — blocks content validity and calibration
- **Complexity**: Senior engineer + curriculum expert
- **Source**: Expert panel audit — Amjad (Curriculum), Yael (Psychometrics)
- **Tier**: 3
- **Effort**: 3-4 weeks

## Problem

### 1. No real Bagrut items
All 1,000 questions are programmatically generated. Zero items come from actual Bagrut exams. The Ministry exam archive (640 pages at `meyda.education.gov.il/sheeloney_bagrut/`) has not been scraped or ingested.

### 2. No formal topic taxonomy
Concepts are implicit string IDs (e.g., `math_5u_derivatives_chain_rule`). No formal hierarchy matches the official Ministry syllabus. Cannot verify: "Have we covered all topics on the 5-unit exam?"

### 3. Missing tracks
3-unit math: completely absent. 4-unit math: mentioned in research but no curriculum ontology.

## Scope

### 1. Scrape Ministry exam archive

- Download Bagrut exam PDFs from Ministry website
- Extract questions using OCR (Gemini/Mathpix pipeline exists)
- Structure as `QuestionDocument` events
- Target: 100+ real exam items across Math 5-unit topics

### 2. Formalize topic taxonomy

Create `scripts/bagrut-taxonomy.json`:
```json
{
  "math_5u": {
    "algebra": ["equations", "inequalities", "polynomials", "sequences"],
    "calculus": ["limits", "derivatives", "integrals", "applications"],
    "geometry": ["euclidean", "analytic", "trigonometry"],
    "probability": ["combinatorics", "probability", "statistics"]
  }
}
```

Map every existing question to this taxonomy. Identify coverage gaps.

### 3. Coverage tracking

Add to quality gate: per-topic coverage report showing question count, difficulty distribution, and coverage percentage against taxonomy.

### 4. 3-unit and 4-unit track stubs

Create taxonomy entries for 3-unit and 4-unit math. Populate with at least 10 seed questions each to validate the schema.

## Files to Modify

- New: `scripts/bagrut-taxonomy.json` — canonical taxonomy
- New: `scripts/bagrut-scraper.py` — Ministry exam scraper
- `src/api/Cena.Admin.Api/QuestionBankSeedData.cs` — add real Bagrut items
- `src/shared/Cena.Infrastructure/Content/QualityGateService.cs` — topic coverage report

## Acceptance Criteria

- [ ] 100+ real Bagrut exam items ingested and structured
- [ ] Topic taxonomy formalized as version-controlled JSON
- [ ] Every question mapped to taxonomy node
- [ ] Coverage report shows questions per topic with gap identification
- [ ] 3-unit and 4-unit tracks have taxonomy entries + 10 seed questions each
- [ ] Taxonomy reviewed by curriculum expert
