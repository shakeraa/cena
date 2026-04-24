# MOB-041: Habit Stacking — Context-Aware Session Design

**Priority:** P2.3 — High
**Phase:** 2 — Engagement Layer (Months 3-5)
**Source:** habit-loops-hook-model-research.md Section 3
**Blocked by:** MOB-040 (Quality-Gated Streaks), MOB-014 (Push Notifications)
**Estimated effort:** L (3-6 weeks)

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** See `tasks/00-master-plan.md`.

## Subtasks

### MOB-041.1: Routine Profile Detection
- [ ] Learn student's daily rhythm from app usage timestamps over 14 days
- [ ] Identify natural study windows: morning, commute, after-school, evening, bedtime
- [ ] `RoutineProfile` in `StudentActor` state: `{PreferredTimes[], AvgSessionLength, WeekdayPattern}`

### MOB-041.2: Context-Specific Session Designs
- [ ] **Morning Review** (< 5 min): 5 SRS review items, quick wins
- [ ] **Commute Session** (10-15 min): audio-friendly content, larger touch targets
- [ ] **After-School Deep** (15-25 min): full session arc, new concepts
- [ ] **Evening Review** (5-10 min): day's concepts revisited, memory consolidation
- [ ] **Before-Bed** (3-5 min): gentle flashcard review, no challenging new material

### MOB-041.3: Smart Notification Timing
- [ ] Notifications aligned to detected routine windows (±30 min)
- [ ] 2 notifications/day hard budget
- [ ] Personalized trigger patterns: "Your morning review is ready" at learned time
- [ ] Shabbat/holiday suppression (configurable)

### MOB-041.4: Home Screen Widgets
- [ ] Streak widget: current streak count + today's progress
- [ ] Quick Review widget: "5 concepts due" with one-tap start
- [ ] Both using Flutter home_widget package

**Definition of Done:**
- App detects student's study routine over 14 days
- Session type adapts to time of day
- Notifications align with personal routine windows
- Home screen widgets for streak and quick review

**Test:**
```dart
test('Routine profile detects morning study habit', () {
  final timestamps = List.generate(14, (i) =>
    DateTime(2026, 4, i + 1, 7, 30) // 7:30 AM for 14 days
  );
  final profile = RoutineProfile.fromTimestamps(timestamps);
  expect(profile.preferredTimes, contains(StudyWindow.morning));
  expect(profile.suggestedNotificationTime.hour, closeTo(7, 1));
});
```
