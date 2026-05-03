# MOB-008: Streaks, XP, Badges, Streak Freeze, Vacation Mode

**Priority:** P1 — blocks engagement
**Blocked by:** MOB-005 (State)
**Estimated effort:** 3 days
**Contract:** `contracts/mobile/lib/features/gamification/streak_widget.dart`

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule. `throw UnimplementedError`, `// TODO: implement`, empty bodies, and mock returns are FORBIDDEN in source code. If you cannot implement it fully, file a blocking dependency instead.

## Context

Gamification drives engagement: daily streaks with flame animation, XP progress bar with level-ups, achievement badges, streak freeze (save streak on missed day), and vacation mode (pause streak tracking). XP formula: `xpForLevel(n) = 100 * n * (1 + 0.1 * n)`.

## Subtasks

### MOB-008.1: StreakCounter Widget
- [ ] Animated flame icon (flicker when active, gray when 0)
- [ ] Current streak count, longest streak, "New record!" badge
- [ ] Compact mode for app bar

### MOB-008.2: XpBar Widget
- [ ] Level badge, animated progress bar, XP text
- [ ] Level-up celebration animation (confetti)
- [ ] Daily goal progress indicator

### MOB-008.3: BadgeGrid Widget
- [ ] Grid of earned badges with lock icon for unearned
- [ ] Badge categories: mastery, streak, engagement, special
- [ ] Tap to see badge description and earn date

### MOB-008.4: Streak Freeze + Vacation Mode
- [ ] Streak freeze: auto-use on missed day, max 2 freezes stored
- [ ] Vacation mode: pause streak for 1-7 days, UI toggle in settings
- [ ] Freeze purchase: earned via 7-day streak completion

### MOB-008.5: XP Calculation Engine
- [ ] `xpForLevel(n) = 100 * n * (1 + 0.1 * n)` — gentle exponential
- [ ] XP awards: correct answer = 10 XP, streak day = 50 XP, concept mastered = 200 XP
- [ ] Bonus: first session of day = 2x XP for first 5 questions

**Test:**
```dart
test('XP formula produces expected values', () {
  expect(xpForLevel(1), equals(110));
  expect(xpForLevel(10), equals(2000));
});
```

---

## Definition of Done
- [ ] All 5 subtasks implemented
- [ ] Animations smooth at 60fps
- [ ] PR reviewed by architect
