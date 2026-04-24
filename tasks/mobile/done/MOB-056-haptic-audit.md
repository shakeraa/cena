# MOB-056: Haptic Feedback Audit

**Priority:** P5.3 — Medium
**Phase:** 5 — Polish & Delight (Months 12-15)
**Source:** microinteractions-emotional-design.md Section 8
**Blocked by:** MOB-050 (Celebration System)
**Estimated effort:** S (< 1 week)

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** See `tasks/00-master-plan.md`.

## Subtasks

### MOB-056.1: Haptic Mapping
- [ ] Correct answer: `HapticFeedback.heavyImpact` (already implemented)
- [ ] Wrong answer: `HapticFeedback.lightImpact` (already implemented)
- [ ] Button press: `HapticFeedback.selectionClick`
- [ ] Swipe complete (flashcard): `HapticFeedback.mediumImpact`
- [ ] Badge unlock: custom pattern (heavy-pause-heavy-pause-heavy)
- [ ] Level up: extended heavy pattern
- [ ] Navigation: `HapticFeedback.selectionClick` (subtle)

### MOB-056.2: Haptic Settings
- [ ] Toggle: haptics on/off in settings
- [ ] Respect system haptic settings
- [ ] Reduce haptics for accessibility profile

### MOB-056.3: Android Pattern Support
- [ ] Map iOS Taptic Engine patterns to Android vibration patterns
- [ ] Platform-adaptive haptic service

**Definition of Done:**
- Every interaction type has assigned haptic pattern
- Respects system settings and user preferences
- Platform-adaptive (iOS Taptic + Android vibration)

**Test:**
```dart
test('Haptic service respects disabled setting', () {
  final haptics = CenaHaptics(enabled: false);
  haptics.play(HapticPattern.correctAnswer);
  // Verify no haptic was triggered
  expect(haptics.lastPlayed, isNull);
});
```
