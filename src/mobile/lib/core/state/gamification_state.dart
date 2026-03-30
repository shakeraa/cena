// =============================================================================
// Cena Adaptive Learning Platform — Gamification State Providers
// =============================================================================

import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../models/domain_models.dart';
import 'app_state.dart';

// ---------------------------------------------------------------------------
// XP & Level
// ---------------------------------------------------------------------------

/// Total cumulative XP earned by the current student.
/// Seeded from [currentStudentProvider] and incremented locally on answer events.
final xpProvider = StateProvider<int>((ref) {
  final student = ref.watch(currentStudentProvider);
  return student?.xp ?? 0;
});

/// XP required to reach level [n].
/// Formula: xpForLevel(n) = 100 * n * (1 + 0.1 * n)
int xpForLevel(int n) => (100 * n * (1 + 0.1 * n)).round();

/// Derives the current level from total XP using the xpForLevel formula.
/// Level 1 requires 110 XP, level 10 requires 2000 XP, etc.
/// We search upward until the threshold would exceed total XP.
final levelProvider = Provider<int>((ref) {
  final totalXp = ref.watch(xpProvider);
  int level = 1;
  int accumulated = 0;
  while (true) {
    final needed = xpForLevel(level);
    if (accumulated + needed > totalXp) break;
    accumulated += needed;
    level++;
  }
  return level;
});

/// XP earned within the current level (resets at each level boundary).
final xpWithinLevelProvider = Provider<int>((ref) {
  final totalXp = ref.watch(xpProvider);
  final level = ref.watch(levelProvider);
  int accumulated = 0;
  for (int i = 1; i < level; i++) {
    accumulated += xpForLevel(i);
  }
  return totalXp - accumulated;
});

/// XP required to complete the current level.
final xpForCurrentLevelProvider = Provider<int>((ref) {
  final level = ref.watch(levelProvider);
  return xpForLevel(level);
});

/// Progress fraction [0.0, 1.0] toward the next level.
final xpProgressProvider = Provider<double>((ref) {
  final within = ref.watch(xpWithinLevelProvider);
  final needed = ref.watch(xpForCurrentLevelProvider);
  if (needed <= 0) return 0.0;
  return (within / needed).clamp(0.0, 1.0);
});

/// XP earned today (resets at midnight).
/// Updated by session notifiers after each answer event.
final dailyXpProvider = StateProvider<int>((ref) => 0);

/// Whether the first-session bonus (2x XP for first 5 questions) is active today.
final firstSessionBonusActiveProvider = StateProvider<bool>((ref) => false);

/// How many bonus questions remain (0–5). Decrements as questions are answered.
final bonusQuestionsRemainingProvider = StateProvider<int>((ref) => 0);

// ---------------------------------------------------------------------------
// Streak
// ---------------------------------------------------------------------------

/// Current consecutive day streak.
/// Seeded from [currentStudentProvider] and updated by session events.
final streakProvider = StateProvider<int>((ref) {
  final student = ref.watch(currentStudentProvider);
  return student?.streak ?? 0;
});

/// Longest streak ever recorded for this student.
final longestStreakProvider = StateProvider<int>((ref) {
  final current = ref.watch(streakProvider);
  // In production this comes from the server; initialise to current as floor.
  return current;
});

/// Whether today's practice session has been completed (streak is safe).
final streakSafeProvider = StateProvider<bool>((ref) => false);

/// Number of streak freezes the student has stored (max 2).
final streakFreezesProvider = StateProvider<int>((ref) => 0);

/// Whether vacation mode is active (streak paused).
final vacationModeProvider = StateProvider<bool>((ref) => false);

/// End date of the active vacation window, or null if not in vacation mode.
final vacationEndDateProvider = StateProvider<DateTime?>((ref) => null);

/// The last 7 calendar days as [_DayActivity] records for the calendar strip.
/// Day 0 = today, Day 6 = six days ago.
final last7DaysActivityProvider = Provider<List<DayActivity>>((ref) {
  final now = DateTime.now();
  final streakCount = ref.watch(streakProvider);
  final isSafeToday = ref.watch(streakSafeProvider);

  return List.generate(7, (i) {
    final date = now.subtract(Duration(days: i));
    // Days before the streak window are missed; days within it are active.
    // Today is active only if streakSafeProvider is true.
    final isToday = i == 0;
    final isActive = isToday ? isSafeToday : (i < streakCount);
    return DayActivity(
      date: date,
      isActive: isActive,
      isToday: isToday,
    );
  });
});

/// Simple value type used by the calendar strip in [StreakWidget].
class DayActivity {
  const DayActivity({
    required this.date,
    required this.isActive,
    required this.isToday,
  });

  final DateTime date;
  final bool isActive;
  final bool isToday;
}

// ---------------------------------------------------------------------------
// Badges
// ---------------------------------------------------------------------------

/// All badges the student has earned.
final badgesProvider = StateProvider<List<Badge>>((ref) => []);

/// Catalogue of all possible badges (earned and unearned).
/// In production this is fetched from the API; here we define the static set.
final badgeCatalogueProvider = Provider<List<BadgeDefinition>>((ref) {
  return const [
    // Streak badges
    BadgeDefinition(
      id: 'streak_3',
      name: '3-Day Streak',
      nameHe: 'רצף 3 ימים',
      description: 'Study 3 days in a row',
      icon: 'local_fire_department',
      category: BadgeCategory.streak,
      requiredValue: 3,
    ),
    BadgeDefinition(
      id: 'streak_7',
      name: '7-Day Streak',
      nameHe: 'רצף שבוע',
      description: 'Study 7 days in a row — you earned a streak freeze!',
      icon: 'whatshot',
      category: BadgeCategory.streak,
      requiredValue: 7,
    ),
    BadgeDefinition(
      id: 'streak_30',
      name: '30-Day Streak',
      nameHe: 'רצף חודש',
      description: 'Study every day for a full month',
      icon: 'star',
      category: BadgeCategory.streak,
      requiredValue: 30,
    ),
    // Mastery badges
    BadgeDefinition(
      id: 'first_mastery',
      name: 'First Mastery',
      nameHe: 'שליטה ראשונה',
      description: 'Master your first concept',
      icon: 'school',
      category: BadgeCategory.mastery,
      requiredValue: 1,
    ),
    BadgeDefinition(
      id: 'mastery_5',
      name: 'Concept Explorer',
      nameHe: 'חוקר מושגים',
      description: 'Master 5 concepts',
      icon: 'explore',
      category: BadgeCategory.mastery,
      requiredValue: 5,
    ),
    BadgeDefinition(
      id: 'mastery_20',
      name: 'Deep Thinker',
      nameHe: 'חושב עמוק',
      description: 'Master 20 concepts',
      icon: 'psychology',
      category: BadgeCategory.mastery,
      requiredValue: 20,
    ),
    // Engagement badges
    BadgeDefinition(
      id: 'first_session',
      name: 'First Step',
      nameHe: 'הצעד הראשון',
      description: 'Complete your first learning session',
      icon: 'play_circle',
      category: BadgeCategory.engagement,
      requiredValue: 1,
    ),
    BadgeDefinition(
      id: 'sessions_10',
      name: 'Dedicated Learner',
      nameHe: 'לומד מסור',
      description: 'Complete 10 learning sessions',
      icon: 'emoji_events',
      category: BadgeCategory.engagement,
      requiredValue: 10,
    ),
    // Special / methodology
    BadgeDefinition(
      id: 'all_subjects',
      name: 'Renaissance',
      nameHe: 'רנסנס',
      description: 'Practice in all 5 subjects in one week',
      icon: 'hub',
      category: BadgeCategory.special,
      requiredValue: 5,
    ),
    BadgeDefinition(
      id: 'level_10',
      name: 'Level 10',
      nameHe: 'רמה 10',
      description: 'Reach level 10',
      icon: 'military_tech',
      category: BadgeCategory.special,
      requiredValue: 10,
    ),
  ];
});

/// Badge category for grouping in the UI.
enum BadgeCategory {
  streak,
  mastery,
  engagement,
  special,
}

/// Static definition of a badge (not tied to a specific student).
class BadgeDefinition {
  const BadgeDefinition({
    required this.id,
    required this.name,
    required this.nameHe,
    required this.description,
    required this.icon,
    required this.category,
    required this.requiredValue,
  });

  final String id;
  final String name;
  final String nameHe;
  final String description;
  final String icon;
  final BadgeCategory category;

  /// Numeric threshold at which this badge is earned (streak days, mastery
  /// count, session count, or level, depending on category).
  final int requiredValue;
}

// ---------------------------------------------------------------------------
// Recent Achievement Events
// ---------------------------------------------------------------------------

/// An event record shown in the "Recent achievements" list.
class AchievementEvent {
  const AchievementEvent({
    required this.type,
    required this.label,
    required this.xpDelta,
    required this.timestamp,
    this.badgeId,
  });

  final AchievementEventType type;
  final String label;
  final int xpDelta;
  final DateTime timestamp;
  final String? badgeId;
}

enum AchievementEventType {
  correctAnswer,
  streakDay,
  conceptMastered,
  badgeEarned,
  levelUp,
}

/// Recent achievement events, newest first.
final recentAchievementsProvider = StateProvider<List<AchievementEvent>>(
  (ref) => [],
);
