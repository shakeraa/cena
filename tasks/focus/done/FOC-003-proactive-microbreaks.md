# FOC-003: Proactive Microbreak Engine

**Priority:** P0 — single highest-impact change from research (Cohen's d = 1.784)
**Blocked by:** FOC-001 (focus pipeline for timing optimization)
**Estimated effort:** 3-5 days
**Contract:** Extends `FocusDegradationService.RecommendBreak()` (lines 337-391)

---

> **NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md`.

## Context

**This is the single most impactful finding from the 20-iteration autoresearch.**

Frontiers in Psychology (2025) study: 90-second micro-breaks every 10 minutes produced:
- **Cohen's d = 1.784** (massive effect)
- **65.13% vs 56.44%** average quiz performance (microbreak vs traditional)
- **20.6 percentage-point advantage** during critical middle period
- Vigilance decrement onset **delayed by 2 timepoints** (from timepoint 3 to timepoint 5)

The current Cena model is purely REACTIVE — it triggers breaks only AFTER focus has degraded. The research says PROACTIVE breaks BEFORE degradation are dramatically more effective.

## Subtasks

### FOC-003.1: Microbreak Scheduler
**Files:**
- `src/Cena.Actors/Services/MicrobreakScheduler.cs` — NEW

**Acceptance:**
- [ ] `IMicrobreakScheduler` interface: `ShouldTriggerMicrobreak(SessionState) → MicrobreakDecision`
- [ ] Triggers microbreak every `N` questions OR every `M` minutes, whichever comes first
- [ ] Default: every 8 questions OR every 10 minutes (configurable per student)
- [ ] Does NOT trigger during flow state (`FocusLevel.Flow` with score >= 0.85)
- [ ] Does NOT trigger if student just returned from a reactive break (cooldown: 5 min)
- [ ] Does NOT trigger mid-problem (waits for answer submission)
- [ ] Tracks microbreak count per session for analytics

**Test:**
```csharp
[Fact]
public void Microbreak_TriggersAfter8Questions()
{
    var scheduler = new MicrobreakScheduler(MicrobreakConfig.Default);
    for (int i = 0; i < 7; i++)
        Assert.False(scheduler.ShouldTrigger(questionsAnswered: i + 1, elapsedMinutes: 2 * (i + 1)));
    Assert.True(scheduler.ShouldTrigger(questionsAnswered: 8, elapsedMinutes: 16).ShouldBreak);
}

[Fact]
public void Microbreak_SkippedDuringFlow()
{
    var scheduler = new MicrobreakScheduler(MicrobreakConfig.Default);
    var decision = scheduler.ShouldTrigger(
        questionsAnswered: 10, elapsedMinutes: 20,
        currentFocusLevel: FocusLevel.Flow, focusScore: 0.9);
    Assert.False(decision.ShouldBreak);
}
```

### FOC-003.2: Microbreak Duration & Activity
**Files:**
- `src/Cena.Actors/Services/MicrobreakScheduler.cs` — extend

**Acceptance:**
- [ ] Microbreak duration: 60-90 seconds (not the 5-30 min reactive breaks)
- [ ] Activity suggestions rotate to prevent habituation:
  - `StretchBreak` — "Stand up and stretch for 60 seconds"
  - `BreathingExercise` — "Take 5 deep breaths"
  - `LookAway` — "Look at something far away for 30 seconds" (20-20-20 rule)
  - `WaterBreak` — "Grab some water"
  - `MiniWalk` — "Walk to the kitchen and back"
- [ ] Activity selection: round-robin with randomization (no same activity twice in a row)
- [ ] Countdown timer visible on screen (60s or 90s)
- [ ] Student can skip microbreak (but this is tracked as "microbreakSkipped" signal)

### FOC-003.3: Two-Tier Break System (Proactive + Reactive)
**Files:**
- `src/Cena.Actors/Services/FocusDegradationService.cs` — modify `RecommendBreak()`

**Acceptance:**
- [ ] `BreakRecommendation` gains `BreakType` enum: `Microbreak` (proactive, 60-90s) vs `RecoveryBreak` (reactive, 5-30 min)
- [ ] Proactive microbreaks triggered by `MicrobreakScheduler` BEFORE focus drops
- [ ] Reactive recovery breaks triggered by focus level (existing logic) AFTER degradation
- [ ] If microbreaks are active and working (focus stays above 0.6), reactive breaks trigger less often
- [ ] If student skips >3 microbreaks consecutively, fall back to reactive-only (they don't want microbreaks)
- [ ] Analytics event: `MicrobreakTaken` with `duration`, `activityType`, `preFocusScore`, `postFocusScore`

### FOC-003.4: Mobile Microbreak UI
**Files:**
- `lib/screens/session/widgets/microbreak_overlay.dart` — NEW
- `lib/screens/session/widgets/microbreak_timer.dart` — NEW

**Acceptance:**
- [ ] Overlay slides up from bottom with a gentle animation (not abrupt)
- [ ] Shows activity suggestion in Hebrew/Arabic/English
- [ ] Countdown timer (circular progress indicator)
- [ ] "Skip" button (small, not prominent — we want them to take the break)
- [ ] "I'm back!" button appears after timer completes
- [ ] Nature imagery or calming color background (Attention Restoration Theory)
- [ ] Haptic feedback (light tap) when microbreak starts

## Research References
- Frontiers in Psychology (2025): "Sustaining student concentration" — d = 1.784
- Kitayama et al. (2022): systematic microbreaks → positive performance effects
- Biwer et al. (2023): systematic breaks → efficiency + mood restoration
- Attention Restoration Theory: nature exposure restores attentional resources
