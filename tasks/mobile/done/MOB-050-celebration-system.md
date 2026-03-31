# MOB-050: Five-Tier Celebration System

**Priority:** P5.1 — Medium
**Phase:** 5 — Polish & Delight (Months 12-15)
**Source:** microinteractions-emotional-design.md Sections 3, 10
**Blocked by:** MOB-008 (Gamification)
**Estimated effort:** L (3-6 weeks)

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** See `tasks/00-master-plan.md`.

## Context

Current celebrations are one-size-fits-all. Research shows celebration intensity should scale with achievement importance (peak-end rule, Kahneman). A 5-tier system ensures daily correct answers feel good while major milestones feel EPIC.

## Subtasks

### MOB-050.1: Tier 1 — Micro (Correct Answer)
- [ ] Subtle green glow on option + haptic `selectionClick`
- [ ] Scale animation: 1.0 → 1.05 → 1.0 (150ms)
- [ ] No sound (too frequent)

### MOB-050.2: Tier 2 — Small (Streak Day, Quest Complete)
- [ ] Color burst animation + haptic `mediumImpact`
- [ ] Small confetti (20 particles, 600ms)
- [ ] Chime sound (optional)

### MOB-050.3: Tier 3 — Medium (Badge Earned, Level Up)
- [ ] Full confetti burst (100 particles, 1000ms)
- [ ] Badge/level reveal animation with glow
- [ ] Haptic `heavyImpact`
- [ ] Fanfare sound

### MOB-050.4: Tier 4 — Large (Concept Mastered, Weekly Mission)
- [ ] Full-screen overlay with particle effects
- [ ] Knowledge graph node lights up (if visible)
- [ ] Extended haptic pattern
- [ ] 3-second celebration sequence

### MOB-050.5: Tier 5 — Epic (Course Complete, Semester Goal)
- [ ] Rive/Lottie full-screen animation (3-5 seconds)
- [ ] Certificate generation with shareable image
- [ ] Special sound effect
- [ ] Particle shower + glow effects
- [ ] Auto-share prompt (optional)

### MOB-050.6: Performance Constraints
- [ ] All animations 60fps on Samsung A14 (Mali-G57)
- [ ] Max 200 particles at any time
- [ ] `CustomPainter` for particles (not individual widgets)
- [ ] Max 8 `AnimationController` instances per screen
- [ ] Tier 1-2: no `AnimationController` (use `TweenAnimationBuilder`)

**Definition of Done:**
- 5 celebration tiers scaling from micro to epic
- Performance: 60fps on budget devices, max 200 particles
- Haptic + sound + visual coordinated per tier
- No celebration interrupts during questions (queued for between-question transitions)

**Test:**
```dart
test('Celebration tier matches achievement importance', () {
  expect(CelebrationTier.forEvent(CorrectAnswer()), CelebrationTier.micro);
  expect(CelebrationTier.forEvent(StreakDay(count: 7)), CelebrationTier.small);
  expect(CelebrationTier.forEvent(BadgeEarned(rarity: BadgeRarity.rare)), CelebrationTier.medium);
  expect(CelebrationTier.forEvent(ConceptMastered()), CelebrationTier.large);
  expect(CelebrationTier.forEvent(CourseCompleted()), CelebrationTier.epic);
});
```
