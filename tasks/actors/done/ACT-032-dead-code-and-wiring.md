# ACT-032: Dead Code Cleanup and DI Wiring Fixes

**Priority:** P2 — MEDIUM (code hygiene)
**Blocked by:** None
**Estimated effort:** 0.5 days
**Source:** Architect review 2026-03-27, Issues #8, #10, misc

---

## Problem

Several code hygiene issues discovered during review:

1. **`MethodologySwitchService` is dead code** — `Program.cs` registers `DefaultMethodologySwitchService`, not the full 5-step `MethodologySwitchService`. The full implementation is unused.
2. **`DrillAndPractice` missing from `MethodologySwitchService.AllMethodologies`** — enum has 9 values, array has 8
3. **Outreach throttle uses UTC date, not Israel timezone** — `ResetDailyThrottleIfNeeded` and quiet hours use different timezone bases
4. **`UnitTest1.cs` scaffold** — leftover in test project
5. **Domain services not wired** — `CognitiveLoadService`, `FocusDegradationService`, `PrerequisiteEnforcementService`, `DecayPropagationService` are implemented but not registered in DI

## Subtasks

### ACT-032.1: Consolidate methodology switch implementations
- [ ] Either delete `DefaultMethodologySwitchService` from `Program.cs` and register the full `MethodologySwitchService`, OR merge their logic
- [ ] Ensure DI registers whichever implementation is kept
- [ ] Add `DrillAndPractice` to `AllMethodologies` array if keeping the full service

### ACT-032.2: Fix outreach throttle timezone
**File:** `src/actors/Cena.Actors/Outreach/OutreachSchedulerActor.cs`
- [ ] `ResetDailyThrottleIfNeeded` should use Israel timezone for date comparison, matching quiet hours
- [ ] Replace `DateTimeOffset.UtcNow.Date` with `TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, IsraelTz).Date`

### ACT-032.3: Register domain services in DI
**File:** `src/actors/Cena.Actors.Host/Program.cs`
- [ ] Register `IBktService` → `BktService`
- [ ] Register `IHlrService` → `HlrService`
- [ ] Register `ICognitiveLoadService` → `CognitiveLoadService`
- [ ] Register `IFocusDegradationService` → `FocusDegradationService`
- [ ] Register `IPrerequisiteEnforcementService` → `PrerequisiteEnforcementService`
- [ ] Register `IDecayPropagationService` → `DecayPropagationService`
- [ ] Register `OfflineSyncHandler`

### ACT-032.4: Delete UnitTest1.cs
- [ ] Remove `src/actors/Cena.Actors.Tests/UnitTest1.cs`

## Acceptance Criteria

- [ ] No dead methodology switch implementation
- [ ] Outreach throttle resets at midnight Israel time
- [ ] All domain services registered in DI
- [ ] Build and tests pass
