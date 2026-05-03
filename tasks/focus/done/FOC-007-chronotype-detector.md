# FOC-007: Chronotype Detector & Circadian Adjuster

**Priority:** P2 — reduces false focus-degradation alerts from time-of-day effects
**Blocked by:** FOC-002 (sensor layer for battery/time), DATA-004 (read models for session history)
**Estimated effort:** 3-5 days
**Contract:** Extends `TimeOfDayContext` and `RecommendBreak()`

---

> **NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md`.

## Context

Multiple studies (bioRxiv 2025, Nature 2025): Time-of-day significantly affects performance. Students with moderate daily rhythmicity achieve highest performance. Chronotype (lark/owl/finch) determines peak cognitive hours. Current Cena model uses a binary `IsLateEvening` flag — far too crude.

## Subtasks

### FOC-007.1: Session Time Pattern Collector
**Files:**
- `src/Cena.Actors/Services/ChronotypeDetector.cs` — NEW

**Acceptance:**
- [ ] Collects (sessionStartTime, averageAccuracy, averageFocusScore) per session over last 30 days
- [ ] Groups sessions into 3-hour blocks: 06-09, 09-12, 12-15, 15-18, 18-21, 21-00
- [ ] Identifies peak block (highest average accuracy) and trough block (lowest)
- [ ] After 10+ sessions across 3+ different time blocks, classifies chronotype: `Lark` (peak before 12:00), `Finch` (peak 12-18), `Owl` (peak after 18:00), `Unknown`

### FOC-007.2: Circadian Baseline Adjuster
**Files:**
- `src/Cena.Actors/Services/CircadianAdjuster.cs` — NEW

**Acceptance:**
- [ ] `AdjustBaseline(FocusInput, chronotype, currentTimeBlock) → AdjustedInput`
- [ ] During student's trough block: lower the "expected accuracy" baseline by 10-15%, so that naturally lower performance isn't misclassified as focus degradation
- [ ] During student's peak block: raise expectations — focus degradation detected sooner
- [ ] `TimeOfDayContext` expanded: `ChronotypeCategory`, `IsInPeakBlock`, `IsInTroughBlock`, `BlockPerformanceHistory`
- [ ] Adjustment is multiplicative on the vigilance score component (not other signals)

### FOC-007.3: Optimal Study Time Recommendation
**Files:**
- `lib/screens/insights/study_time_card.dart` — NEW (mobile widget)

**Acceptance:**
- [ ] After 10+ sessions, show student their "Best Study Time" insight card
- [ ] Visualization: bar chart of performance by time block (Hebrew/Arabic/English)
- [ ] "You perform 18% better when studying between 15:00-18:00" style message
- [ ] Optional push notification: "Your peak study time is starting!" (configurable, off by default)

## Research References
- bioRxiv (2025): chronotype distinctness predicts academic performance
- Nature (2025): morning wake-time influences test scores
- Focus Degradation Research doc, Section 2.8
