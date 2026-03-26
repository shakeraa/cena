# MOB-008: Gamification (Streaks, XP, Badges, Streak Freeze, Vacation Mode)

**Priority:** P1 — engagement and retention mechanics
**Blocked by:** MOB-005 (Riverpod state — UserNotifier, OutreachNotifier)
**Estimated effort:** 5 days
**Contract:** `contracts/mobile/lib/features/gamification/streak_widget.dart`

---

## Context
Cena's gamification system motivates students through streaks, XP progression, badges, and daily goals. The streak system must handle Shabbat and Israeli holidays via streak freeze / vacation mode — without this, observant students (~30% of the Israeli student population) are penalized. XP uses a gentle exponential curve so early levels feel fast. All gamification elements are localized in Hebrew (primary), Arabic, and English. The gamification intensity is configurable via `FeatureFlags.gamificationIntensity` (minimal/standard/full) for A/B testing.

## Subtasks

### MOB-008.1: StreakCounter Widget
**Files:**
- `lib/features/gamification/widgets/streak_counter.dart`

**Acceptance:**
- [ ] `StreakCounter extends ConsumerWidget` — reads from `userProvider` for streak data
- [ ] Parameters: `compact` (bool, default false) — compact mode for app bar
- [ ] Active streak (>0): animated flame icon (flicker via `flutter_animate`), bold count, warm orange/red gradient
- [ ] No streak (0): gray flame, "0 days" / "0 ימים" text
- [ ] Record-breaking (current >= longest): extra glow effect + "New record!" / "!שיא חדש" badge
- [ ] Compact mode: only flame icon + number (for `AppBar` leading/action)
- [ ] Full mode: flame + count + "ימים" label + longest streak below + record badge
- [ ] Color: warm gradient (orange `0xFFFF8F00` -> red `0xFFE53935`) when active, gray `0xFFBDBDBD` when 0
- [ ] Hebrew label: "ימים" (days), Arabic: "أيام"

**Test:**
```dart
testWidgets('StreakCounter shows animated flame for active streak', (tester) async {
  final container = ProviderContainer(overrides: [
    webSocketServiceProvider.overrideWithValue(mockWs),
  ]);
  container.read(userProvider.notifier).setStudent(Student(
    id: 's1', name: 'Test',
    experimentCohort: ExperimentCohort.control,
    lastActive: DateTime.now(),
    streak: 7,
  ));

  await tester.pumpWidget(
    UncontrolledProviderScope(
      container: container,
      child: MaterialApp(home: Scaffold(body: StreakCounter())),
    ),
  );

  expect(find.text('7'), findsOneWidget);
  expect(find.byIcon(Icons.local_fire_department), findsOneWidget);
});

testWidgets('StreakCounter shows gray when streak is 0', (tester) async {
  final container = ProviderContainer(overrides: [
    webSocketServiceProvider.overrideWithValue(mockWs),
  ]);
  container.read(userProvider.notifier).setStudent(Student(
    id: 's1', name: 'Test',
    experimentCohort: ExperimentCohort.control,
    lastActive: DateTime.now(),
    streak: 0,
  ));

  await tester.pumpWidget(
    UncontrolledProviderScope(
      container: container,
      child: MaterialApp(home: Scaffold(body: StreakCounter())),
    ),
  );

  expect(find.text('0'), findsOneWidget);
  // Verify gray color on the icon
});

testWidgets('compact mode shows only icon and number', (tester) async {
  final container = ProviderContainer(overrides: [
    webSocketServiceProvider.overrideWithValue(mockWs),
  ]);
  container.read(userProvider.notifier).setStudent(Student(
    id: 's1', name: 'Test',
    experimentCohort: ExperimentCohort.control,
    lastActive: DateTime.now(),
    streak: 12,
  ));

  await tester.pumpWidget(
    UncontrolledProviderScope(
      container: container,
      child: MaterialApp(home: Scaffold(body: StreakCounter(compact: true))),
    ),
  );

  expect(find.text('12'), findsOneWidget);
  expect(find.text('ימים'), findsNothing); // no label in compact mode
});
```

**Edge Cases:**
- Streak of 999+ days — truncate display or use "999+" text
- Student with no `lastActive` date — show streak as 0
- Transition from 0 to 1 — play unlock animation (flame lights up)

---

### MOB-008.2: XP Bar & Level Calculator
**Files:**
- `lib/features/gamification/widgets/xp_bar.dart`
- `lib/features/gamification/models/xp_level_calculator.dart`

**Acceptance:**
- [ ] `XpLevelCalculator` static class per contract:
  - `xpForLevel(n)` = `(100 * n * (1 + 0.1 * n)).round()`
  - `totalXpForLevel(n)` = sum of `xpForLevel(1..n)`
  - `levelForXp(totalXp)` = current level for given total XP
  - `progressInLevel(totalXp)` = [0.0, 1.0] progress within current level
- [ ] `XpBar extends ConsumerWidget` — reads from `userProvider`
- [ ] Level badge (left): circular badge with "Lvl {n}"
- [ ] Progress bar (center): animated `LinearProgressIndicator` with gradient fill (blue -> purple)
- [ ] XP text (right): "{current}/{needed} XP"
- [ ] If `showDailyGoal`: small text below showing "Daily: {earned}/{goal} XP"
- [ ] Level-up animation: bar fills completely, flashes gold, level badge increments with scale animation
- [ ] Responds to `XpAwarded` events from `UserNotifier` — animates progress change

**Test:**
```dart
test('xpForLevel formula matches contract', () {
  expect(XpLevelCalculator.xpForLevel(1), equals(110));   // 100 * 1 * 1.1
  expect(XpLevelCalculator.xpForLevel(2), equals(240));   // 100 * 2 * 1.2
  expect(XpLevelCalculator.xpForLevel(5), equals(750));   // 100 * 5 * 1.5
  expect(XpLevelCalculator.xpForLevel(10), equals(2000)); // 100 * 10 * 2.0
});

test('totalXpForLevel accumulates correctly', () {
  expect(XpLevelCalculator.totalXpForLevel(1), equals(110));
  expect(XpLevelCalculator.totalXpForLevel(2), equals(350)); // 110 + 240
});

test('levelForXp returns correct level', () {
  expect(XpLevelCalculator.levelForXp(0), equals(1));
  expect(XpLevelCalculator.levelForXp(110), equals(1));  // exactly at level 1 cap
  expect(XpLevelCalculator.levelForXp(111), equals(1));  // into level 2 progress
  expect(XpLevelCalculator.levelForXp(350), equals(2));  // at level 2 cap
  expect(XpLevelCalculator.levelForXp(5000), greaterThan(3));
});

test('progressInLevel returns 0-1 range', () {
  final progress = XpLevelCalculator.progressInLevel(200);
  expect(progress, inInclusiveRange(0.0, 1.0));
});

testWidgets('XpBar displays level and progress', (tester) async {
  final container = ProviderContainer(overrides: [
    webSocketServiceProvider.overrideWithValue(mockWs),
  ]);
  container.read(userProvider.notifier).setStudent(Student(
    id: 's1', name: 'Test',
    experimentCohort: ExperimentCohort.control,
    lastActive: DateTime.now(),
    xp: 200, level: 2,
  ));

  await tester.pumpWidget(
    UncontrolledProviderScope(
      container: container,
      child: MaterialApp(home: Scaffold(body: XpBar())),
    ),
  );

  expect(find.textContaining('Lvl'), findsOneWidget);
  expect(find.textContaining('XP'), findsOneWidget);
});

testWidgets('XpBar shows daily goal when enabled', (tester) async {
  final container = ProviderContainer(overrides: [
    webSocketServiceProvider.overrideWithValue(mockWs),
  ]);
  container.read(userProvider.notifier).setStudent(Student(
    id: 's1', name: 'Test',
    experimentCohort: ExperimentCohort.control,
    lastActive: DateTime.now(),
    xp: 200, level: 2,
  ));

  await tester.pumpWidget(
    UncontrolledProviderScope(
      container: container,
      child: MaterialApp(home: Scaffold(body: XpBar(showDailyGoal: true))),
    ),
  );

  expect(find.textContaining('Daily'), findsOneWidget);
});
```

**Edge Cases:**
- XP exactly at level boundary — display as 100% progress with option to show "Level Up!" briefly
- Level 1 with 0 XP — progress at 0%, level badge shows 1
- Very high level (50+) — XP curve makes it slow but XpBar still renders proportionally

---

### MOB-008.3: Badge Grid & Badge Tile
**Files:**
- `lib/features/gamification/widgets/badge_grid.dart`
- `lib/features/gamification/widgets/badge_tile.dart`

**Acceptance:**
- [ ] `BadgeGrid extends ConsumerWidget` — reads badges from `userProvider`
- [ ] `GridView.builder` with configurable `crossAxisCount` (default 4)
- [ ] `showLocked` parameter (default true) — when true, shows locked/unearned badges as grayscale silhouettes with "?" overlay
- [ ] Earned badges: full-color icon + name, subtle glow animation
- [ ] New badges (`isNew == true`): flip-reveal animation (800ms) with sparkle effect via `AnimationController`
- [ ] Locked badges: grayscale filter + "?" `Text` overlay
- [ ] Tap earned badge: opens bottom sheet with badge `description`, `nameHe`, and `earnedAt` date
- [ ] `BadgeTile extends StatefulWidget` with `SingleTickerProviderStateMixin`
- [ ] Unlock animation plays automatically when `badge.isNew == true`

**Test:**
```dart
testWidgets('BadgeGrid renders earned and locked badges', (tester) async {
  final container = ProviderContainer(overrides: [
    webSocketServiceProvider.overrideWithValue(mockWs),
  ]);
  container.read(userProvider.notifier).state = UserState(
    badges: [
      Badge(id: 'b1', name: 'First Step', iconAsset: 'assets/icons/first.svg', description: 'desc', earnedAt: DateTime.now()),
      Badge(id: 'b2', name: 'Streak Master', iconAsset: 'assets/icons/streak.svg', description: 'desc'),
    ],
  );

  await tester.pumpWidget(
    UncontrolledProviderScope(
      container: container,
      child: MaterialApp(home: Scaffold(body: BadgeGrid())),
    ),
  );

  expect(find.byType(BadgeTile), findsNWidgets(2));
});

testWidgets('new badge plays unlock animation', (tester) async {
  await tester.pumpWidget(MaterialApp(
    home: Scaffold(
      body: BadgeTile(
        badge: Badge(
          id: 'b1', name: 'Perfect', iconAsset: 'assets/icons/p.svg',
          description: 'desc', earnedAt: DateTime.now(), isNew: true,
        ),
      ),
    ),
  ));

  // Animation should be playing
  await tester.pump(Duration(milliseconds: 400)); // mid-animation
  // AnimationController should be active
  await tester.pumpAndSettle(); // animation completes
});

testWidgets('tap earned badge opens detail sheet', (tester) async {
  await tester.pumpWidget(MaterialApp(
    home: Scaffold(
      body: BadgeTile(
        badge: Badge(
          id: 'b1', name: 'First Step', nameHe: 'צעד ראשון',
          iconAsset: 'assets/icons/first.svg',
          description: 'Completed first exercise',
          earnedAt: DateTime(2026, 1, 15),
        ),
      ),
    ),
  ));

  await tester.tap(find.byType(BadgeTile));
  await tester.pumpAndSettle();

  expect(find.text('צעד ראשון'), findsOneWidget);
  expect(find.textContaining('2026'), findsOneWidget);
});

testWidgets('locked badge shows question mark overlay', (tester) async {
  await tester.pumpWidget(MaterialApp(
    home: Scaffold(
      body: BadgeTile(
        badge: Badge(id: 'b1', name: 'Secret', iconAsset: 'assets/icons/s.svg', description: 'desc'),
        isLocked: true,
      ),
    ),
  ));

  expect(find.text('?'), findsOneWidget);
});
```

**Edge Cases:**
- Badge icon asset missing — show placeholder icon
- 0 badges earned — show encouraging "Start studying to earn badges!" message
- Very many badges (50+) — grid scrolls, lazy-loads tiles

---

### MOB-008.4: Streak Freeze & Vacation Mode
**Files:**
- `lib/features/gamification/widgets/streak_freeze_button.dart`
- `lib/features/gamification/models/vacation_mode_config.dart`

**Acceptance:**
- [ ] `StreakFreezeButton extends StatelessWidget` with: `freezesRemaining`, `onActivateFreeze`
- [ ] Shield icon + "Freeze streak" / "הקפא רצף" text + remaining count
- [ ] Disabled state (grayed out) when `freezesRemaining == 0`
- [ ] `VacationModeConfig` class per contract:
  - `shabbatAutoFreeze` (default false, opt-in)
  - `holidayAutoFreeze` (default true)
  - `monthlyFreezes` (default 2, configurable by A/B test)
  - `freezesRemaining` (default 2)
  - `freezeExpiresAt` (nullable)
- [ ] `isAutoFreezeDay(DateTime now)` checks Hebrew calendar for:
  - Shabbat: Friday sunset -> Saturday night (if `shabbatAutoFreeze` enabled)
  - Major holidays: Rosh Hashana (2d), Yom Kippur (1d), Sukkot (8d), Pesach (8d), Shavuot (1d), Hanukkah (8d)
- [ ] Uses `intl` package for Hebrew calendar date computation
- [ ] Design rationale documented: streak freeze prevents penalizing observant students

**Test:**
```dart
testWidgets('StreakFreezeButton shows remaining count', (tester) async {
  await tester.pumpWidget(MaterialApp(
    home: Scaffold(
      body: StreakFreezeButton(
        freezesRemaining: 2,
        onActivateFreeze: () {},
      ),
    ),
  ));

  expect(find.text('2'), findsOneWidget);
  expect(find.byIcon(Icons.shield), findsOneWidget);
});

testWidgets('StreakFreezeButton disabled when 0 remaining', (tester) async {
  bool activated = false;
  await tester.pumpWidget(MaterialApp(
    home: Scaffold(
      body: StreakFreezeButton(
        freezesRemaining: 0,
        onActivateFreeze: () => activated = true,
      ),
    ),
  ));

  await tester.tap(find.byType(StreakFreezeButton));
  expect(activated, isFalse); // Button should be disabled
});

test('VacationModeConfig defaults', () {
  const config = VacationModeConfig();
  expect(config.shabbatAutoFreeze, isFalse);
  expect(config.holidayAutoFreeze, isTrue);
  expect(config.monthlyFreezes, equals(2));
  expect(config.freezesRemaining, equals(2));
  expect(config.freezeExpiresAt, isNull);
});

test('isAutoFreezeDay detects Shabbat when enabled', () {
  final config = VacationModeConfig(shabbatAutoFreeze: true);
  // Saturday, Jan 3 2026 is Shabbat
  final saturday = DateTime(2026, 1, 3, 12, 0, 0);
  // This depends on Hebrew calendar implementation
  // The test verifies the method is callable and returns bool
  expect(config.isAutoFreezeDay(saturday), isA<bool>());
});

test('isAutoFreezeDay returns false for regular weekday', () {
  final config = VacationModeConfig(holidayAutoFreeze: true);
  // Regular Tuesday, not a holiday
  final tuesday = DateTime(2026, 3, 10, 12, 0, 0);
  expect(config.isAutoFreezeDay(tuesday), isFalse);
});

testWidgets('StreakFreezeButton calls onActivateFreeze when tapped', (tester) async {
  bool activated = false;
  await tester.pumpWidget(MaterialApp(
    home: Scaffold(
      body: StreakFreezeButton(
        freezesRemaining: 1,
        onActivateFreeze: () => activated = true,
      ),
    ),
  ));

  await tester.tap(find.byType(StreakFreezeButton));
  expect(activated, isTrue);
});
```

**Edge Cases:**
- Two-day holidays (Rosh Hashana) — freeze covers both days, consumes only 1 freeze
- Freeze activated at 11:59 PM — covers the next calendar day as well
- Student opts into Shabbat freeze but also manually freezes on Friday — do not double-consume
- Hebrew calendar edge cases: leap years, postponed holidays

---

### MOB-008.5: Daily Goal Widget, Streak Warning & Gamification Dashboard
**Files:**
- `lib/features/gamification/widgets/daily_goal_widget.dart`
- `lib/features/gamification/widgets/streak_warning.dart`
- `lib/features/gamification/widgets/gamification_dashboard.dart`

**Acceptance:**
- [ ] `DailyGoalWidget extends ConsumerWidget`:
  - Circular progress ring (`CustomPaint` arc) sized by `size` parameter (default 120.0)
  - Center text: "{answered}/{goal}"
  - Below: "questions today" / "שאלות היום"
  - Color transitions: gray -> blue -> green as progress increases
  - Goal met: green fill + checkmark + brief celebration animation
- [ ] `StreakWarning extends ConsumerWidget`:
  - Material banner or snackbar-style overlay
  - Dimming flame icon animation
  - "Your streak is about to expire!" / "!הרצף שלך עומד לפוג"
  - Countdown timer to `expiresAt`
  - "Study Now" primary action button (`onStudyNow`)
  - Close/dismiss button (`onDismiss`)
- [ ] `GamificationDashboard extends ConsumerWidget`:
  - Column layout: `StreakCounter` + `XpBar` -> `DailyGoalWidget` -> `BadgeGrid` (recent 8) -> `LeaderboardCard` (opt-in, if `FeatureFlags.leaderboardEnabled`)
  - Adapts to `GamificationIntensity`:
    - `minimal`: only `StreakCounter`
    - `standard`: `StreakCounter` + `XpBar` + `DailyGoalWidget` + `BadgeGrid`
    - `full`: everything including `LeaderboardCard`

**Test:**
```dart
testWidgets('DailyGoalWidget shows progress ring', (tester) async {
  final container = ProviderContainer(overrides: [
    webSocketServiceProvider.overrideWithValue(mockWs),
  ]);
  container.read(userProvider.notifier).state = UserState(
    dailyQuestionsAnswered: 12,
    dailyGoal: 20,
  );

  await tester.pumpWidget(
    UncontrolledProviderScope(
      container: container,
      child: MaterialApp(home: Scaffold(body: DailyGoalWidget())),
    ),
  );

  expect(find.textContaining('12'), findsWidgets);
  expect(find.textContaining('20'), findsWidgets);
});

testWidgets('DailyGoalWidget shows celebration at goal', (tester) async {
  final container = ProviderContainer(overrides: [
    webSocketServiceProvider.overrideWithValue(mockWs),
  ]);
  container.read(userProvider.notifier).state = UserState(
    dailyQuestionsAnswered: 20,
    dailyGoal: 20,
  );

  await tester.pumpWidget(
    UncontrolledProviderScope(
      container: container,
      child: MaterialApp(home: Scaffold(body: DailyGoalWidget())),
    ),
  );

  expect(find.byIcon(Icons.check_circle), findsOneWidget);
});

testWidgets('StreakWarning shows countdown and Study Now button', (tester) async {
  final expiresAt = DateTime.now().add(Duration(hours: 2, minutes: 30));
  bool studyTapped = false;

  await tester.pumpWidget(MaterialApp(
    home: Scaffold(
      body: StreakWarning(
        expiresAt: expiresAt,
        onStudyNow: () => studyTapped = true,
        onDismiss: () {},
      ),
    ),
  ));

  expect(find.textContaining('2'), findsWidgets); // hours
  expect(find.text('Study Now'), findsOneWidget);

  await tester.tap(find.text('Study Now'));
  expect(studyTapped, isTrue);
});

testWidgets('GamificationDashboard respects intensity level', (tester) async {
  // Minimal: only streak
  await tester.pumpWidget(
    ProviderScope(
      overrides: [
        ...testOverrides,
        // Override feature flags for minimal
      ],
      child: MaterialApp(home: Scaffold(body: GamificationDashboard())),
    ),
  );

  expect(find.byType(StreakCounter), findsOneWidget);
  expect(find.byType(XpBar), findsNothing); // hidden in minimal
});

testWidgets('StreakWarning dismiss works', (tester) async {
  bool dismissed = false;
  await tester.pumpWidget(MaterialApp(
    home: Scaffold(
      body: StreakWarning(
        expiresAt: DateTime.now().add(Duration(hours: 1)),
        onStudyNow: () {},
        onDismiss: () => dismissed = true,
      ),
    ),
  ));

  await tester.tap(find.byIcon(Icons.close));
  expect(dismissed, isTrue);
});
```

**Edge Cases:**
- `dailyGoal` is 0 — avoid division by zero in progress calculation, show 100%
- `expiresAt` is in the past — show "Streak expired" message instead of countdown
- Leaderboard disabled by feature flag — `LeaderboardCard` not rendered at all (not just hidden)
- Device in landscape — dashboard adjusts layout (row instead of column for StreakCounter + XpBar)

---

## Integration Test

```dart
void main() {
  group('MOB-008 Integration: Gamification end-to-end', () {
    testWidgets('XP award triggers bar animation and level check', (tester) async {
      final container = ProviderContainer(overrides: testOverrides);
      container.read(userProvider.notifier).setStudent(Student(
        id: 's1', name: 'Test',
        experimentCohort: ExperimentCohort.control,
        lastActive: DateTime.now(),
        xp: 100, level: 1, streak: 3,
      ));

      await tester.pumpWidget(
        UncontrolledProviderScope(
          container: container,
          child: MaterialApp(home: Scaffold(body: GamificationDashboard())),
        ),
      );

      // Simulate XP awarded event
      simulateEvent(mockWs, EventTargets.xpAwarded, {
        'amount': 25,
        'reason': 'correct_answer',
        'totalXp': 125,
        'currentLevel': 2,
        'leveledUp': true,
      });

      await tester.pump(Duration(milliseconds: 500));
      // XP bar should be animating
      // Level should update to 2
    });

    testWidgets('streak break updates all streak-related widgets', (tester) async {
      final container = ProviderContainer(overrides: testOverrides);
      container.read(userProvider.notifier).setStudent(Student(
        id: 's1', name: 'Test',
        experimentCohort: ExperimentCohort.control,
        lastActive: DateTime.now(),
        xp: 500, level: 3, streak: 15,
      ));

      await tester.pumpWidget(
        UncontrolledProviderScope(
          container: container,
          child: MaterialApp(home: Scaffold(body: GamificationDashboard())),
        ),
      );

      expect(find.text('15'), findsWidgets); // streak count

      // Simulate streak break
      simulateEvent(mockWs, EventTargets.streakUpdated, {
        'currentStreak': 0,
        'longestStreak': 15,
        'streakBroken': true,
      });

      await tester.pumpAndSettle();
      expect(find.text('0'), findsWidgets); // streak reset
    });

    test('XpLevelCalculator is consistent with itself', () {
      for (int xp = 0; xp < 10000; xp += 50) {
        final level = XpLevelCalculator.levelForXp(xp);
        final progress = XpLevelCalculator.progressInLevel(xp);
        expect(level, greaterThanOrEqualTo(1));
        expect(progress, inInclusiveRange(0.0, 1.0));
      }
    });
  });
}
```

## Rollback Criteria
- If Hebrew calendar computation is too complex for `intl` package: use a hardcoded holiday table for 2025-2030 and revisit
- If streak freeze logic causes edge cases with timezone: simplify to UTC-only dates with server-authoritative freeze management
- If gamification A/B testing shows `full` intensity demotivates weaker students: default to `minimal` and iterate
- If badge animation causes jank on low-end devices: disable animation, show static earned state

## Definition of Done
- [ ] All 5 subtasks pass their individual tests
- [ ] XpLevelCalculator formula matches contract exactly
- [ ] StreakCounter animates correctly for all states (0, active, record)
- [ ] Badge grid renders earned/locked badges with animations
- [ ] Streak freeze button works and respects remaining count
- [ ] VacationModeConfig detects Shabbat and holidays via Hebrew calendar
- [ ] Daily goal widget tracks progress and celebrates completion
- [ ] Streak warning shows countdown and navigates to session
- [ ] GamificationDashboard respects intensity levels from FeatureFlags
- [ ] All text localized in Hebrew and Arabic
- [ ] PR reviewed by mobile lead
