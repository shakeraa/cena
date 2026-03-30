# FOC-011: Gamification Novelty Rotation

**Priority:** P2 — prevents engagement decay from stale game mechanics
**Blocked by:** MOB-008 (gamification components)
**Estimated effort:** 2 days
**Contract:** Extends gamification service

---

> **NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md`.

## Context

Meta-analysis (Zeng et al., 2024; Frontiers, 2023): gamification has large overall effect (g = 0.822) BUT interventions longer than 1 semester have **negligible to negative** effect sizes. The novelty wears off. Competition + collaboration elements are most durable.

Cena's `streakConsistency` factor (0.15 weight in ResilienceScore) relies on gamification. Without rotation, this signal degrades over months.

## Subtasks

### FOC-011.1: Game Element Rotation Engine
**Files:**
- `src/Cena.Actors/Services/GamificationRotationService.cs` — NEW

**Acceptance:**
- [ ] Tracks per-student: `gamificationTenure` (days since first exposure to each element)
- [ ] Elements in rotation pool: `DailyStreak`, `WeeklyChallenge`, `ConceptMasteryBadge`, `LeaderboardPosition`, `StudyGroupChallenge`, `TimeTrialMode`, `MysteryReward`
- [ ] After 30 days with same primary element, introduce a new one and reduce emphasis on the old
- [ ] After 90 days, fully rotate: swap primary and secondary elements
- [ ] Never remove an element entirely (some students may still value it) — just reduce prominence

### FOC-011.2: Engagement Decay Tracking
**Files:**
- `src/Cena.Actors/Services/GamificationRotationService.cs` — extend

**Acceptance:**
- [ ] Track per-element engagement rate over time (interactions with gamification UI / sessions)
- [ ] If engagement rate drops >30% over 2 weeks for an element → flag for rotation
- [ ] If engagement rate remains stable >60 days → this element is durable for this student (keep it)
- [ ] Dashboard metric: "gamification freshness score" per student cohort

### FOC-011.3: Resilience Score Adjustment
**Files:**
- `src/Cena.Actors/Services/FocusDegradationService.cs` — modify `ComputeResilience()`

**Acceptance:**
- [ ] `streakConsistency` weight adjusts by student tenure:
  - Month 1: 0.15 (current)
  - Month 3+: 0.10 (reduced)
  - Month 6+: 0.05 (minimal)
- [ ] Freed weight redistributed to `challengeSeeking` (most behaviorally robust factor)
- [ ] Logged as telemetry so we can validate the adjustment

## Research References
- Zeng et al. (2024): gamification meta-analysis, novelty wears off after 1 semester
- Frontiers (2023): g = 0.822 overall, competition + collaboration most effective
- Focus Degradation Research doc, Section 4.3
