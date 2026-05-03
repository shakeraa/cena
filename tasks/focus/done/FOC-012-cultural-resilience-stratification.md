# FOC-012: Cultural Resilience Stratification

**Priority:** P3 — ensures fair resilience scoring across Israeli student populations
**Blocked by:** FOC-001 (focus pipeline)
**Estimated effort:** 2 days
**Contract:** Extends `ComputeResilience()` and onboarding

---

> **NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md`.

## Context

Research (PMC 2022, 2024): Israeli-Jewish students report higher academic self-efficacy and resilience than Israeli non-Jewish students. Palestinian Arab students build resilience through COLLECTIVE support (peer groups, mutual assistance). Cultural background is the most salient predictor of student stress.

If Cena calibrates a single "expected resilience" across all users, it will systematically underrate Arab students' resilience (which manifests differently — through collective behavior rather than individual metrics).

## Subtasks

### FOC-012.1: Cultural Context Signal
**Files:**
- `src/Cena.Actors/Services/CulturalContextService.cs` — NEW

**Acceptance:**
- [ ] Student's language preference (Hebrew/Arabic) serves as a proxy for cultural group
- [ ] NO explicit ethnicity collection (privacy + sensitivity)
- [ ] `CulturalContext`: `HebrewDominant`, `ArabicDominant`, `Bilingual`, `Unknown`
- [ ] Detected from: onboarding language choice, interface language setting, typing language

### FOC-012.2: Resilience Weight Adjustment
**Files:**
- `src/Cena.Actors/Services/FocusDegradationService.cs` — modify `ComputeResilience()`

**Acceptance:**
- [ ] For `ArabicDominant` students, adjust resilience weights:
  - `Persistence`: 0.30 (slightly lower — collective support may mean more breaks shared with peers)
  - `Recovery`: 0.30 (higher — returning after difficulty is a strong resilience signal in collectivist cultures)
  - `ChallengeSeeking`: 0.25 (same)
  - `StreakConsistency`: 0.15 (same)
- [ ] For `HebrewDominant` students: keep existing weights (calibrated for individualist pattern)
- [ ] For `Bilingual`/`Unknown`: use baseline weights
- [ ] All adjustments logged for transparency and validation
- [ ] A/B test (FOC-010) should validate whether adjusted weights better predict Bagrut outcomes per group

### FOC-012.3: Social Resilience Signal (Future)
**Files:**
- `src/Cena.Actors/Services/SocialResilienceSignal.cs` — NEW (skeleton for future implementation)

**Acceptance:**
- [ ] Defines interface for future social resilience signals:
  - Study group participation rate
  - Peer help-giving/receiving frequency
  - Shared session completions
- [ ] Not implemented yet — requires study group features (future sprint)
- [ ] `ISocialResilienceSignal.ComputeSocialScore() → double?` — returns null until social features exist
- [ ] When social features launch, this signal replaces `streakConsistency` for collectivist students

## Research References
- PMC (2022): Israeli nursing students — Jewish vs non-Jewish self-efficacy differences
- Palestinian Arab student resilience through collective support (Tandfonline, 2024)
- Focus Degradation Research doc, Section 4.6
