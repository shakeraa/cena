# RDY-007: DIF Analysis Pipeline (Hebrew vs. Arabic)

- **Priority**: Critical — equity requirement for bilingual population
- **Complexity**: Senior engineer + psychometrician — statistical analysis
- **Source**: Expert panel audit — Yael (Psychometrics), Amjad (Curriculum), Ran (Compliance)
- **Tier**: 1
- **Effort**: 2-3 weeks

## Problem

Zero Differential Item Functioning (DIF) analysis exists. The `ItemBankHealthService` mentions DIF as a goal (header comment) and accepts a `trackId` parameter, but never performs language-stratified item parameter comparison.

**Panel concern**: Arabic-speaking students face terminology barriers. Items easy in Hebrew may be hard in Arabic due to language, not math. Without DIF, the platform cannot distinguish math difficulty from language difficulty — leading to unfair assessments and incorrect mastery estimates.

## Scope

### 1. DIF detection algorithm

Implement Mantel-Haenszel DIF detection:
- Stratify item responses by student locale (`he` vs. `ar`)
- For each item, compute MH chi-square statistic
- Flag items where |MH D-DIF| > 1.0 (moderate DIF) or > 1.5 (large DIF)
- Categorize: A (negligible), B (moderate), C (large)

### 2. Store DIF results per item

Add to `IrtItemParameters` or a separate `DifAnalysisResult`:
- `DifCategory` (A, B, C)
- `DifStatistic` (MH chi-square)
- `ReferenceGroup` ("he")
- `FocalGroup` ("ar")
- `LastAnalyzed` (timestamp)
- `ResponseCountReference` / `ResponseCountFocal`

### 3. Integrate with item bank health dashboard

- Add DIF section to health report: items by DIF category, flagged items list
- Items with Category C should be flagged for review (possible bias)
- Show DIF distribution chart (how many A/B/C items per concept)

### 4. CAT integration

- CAT algorithm should deprioritize Category C items when student locale is the focal group
- Log when a DIF-flagged item is administered to a focal-group student

### 5. Minimum data requirements

DIF analysis requires minimum N=100 per group per item. Until this threshold is met, items should be marked "DIF-pending" (not "DIF-clear").

## Files to Modify

- New: `src/actors/Cena.Actors/Services/DifAnalysisService.cs`
- `src/api/Cena.Admin.Api/ItemBankHealthService.cs` — integrate DIF results
- `src/actors/Cena.Actors/Assessment/ConstrainedCatAlgorithm.cs` — deprioritize C items
- `src/actors/Cena.Actors/Services/IrtCalibrationPipeline.cs` — store DIF results

## Acceptance Criteria

- [ ] MH DIF computed for all items with N>=100 per language group
- [ ] DIF categories (A/B/C) stored per item
- [ ] Health dashboard shows DIF distribution and flagged items
- [ ] Category C items deprioritized in CAT for focal-group students
- [ ] Items below N threshold marked "DIF-pending"
- [ ] Structured log when DIF-flagged item is administered
