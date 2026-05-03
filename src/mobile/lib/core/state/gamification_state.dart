// =============================================================================
// Cena Adaptive Learning Platform — Gamification State Providers
// =============================================================================

import 'dart:math';

import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../models/domain_models.dart';
import '../services/streak_quality_gate.dart';
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
final dailyXpProvider = StateProvider<int>((ref) => 0);

/// Whether the first-session bonus (2x XP for first 5 questions) is active.
final firstSessionBonusActiveProvider = StateProvider<bool>((ref) => false);

/// How many bonus questions remain (0-5).
final bonusQuestionsRemainingProvider = StateProvider<int>((ref) => 0);

// ---------------------------------------------------------------------------
// Streak
// ---------------------------------------------------------------------------

/// Current consecutive day streak.
final streakProvider = StateProvider<int>((ref) {
  final student = ref.watch(currentStudentProvider);
  return student?.streak ?? 0;
});

/// Longest streak ever recorded for this student.
final longestStreakProvider = StateProvider<int>((ref) {
  final current = ref.watch(streakProvider);
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

/// Last 7 calendar days as [DayActivity] records for the calendar strip.
final last7DaysActivityProvider = Provider<List<DayActivity>>((ref) {
  final now = DateTime.now();
  final streakCount = ref.watch(streakProvider);
  final isSafeToday = ref.watch(streakSafeProvider);
  return List.generate(7, (i) {
    final date = now.subtract(Duration(days: i));
    final isToday = i == 0;
    final isActive = isToday ? isSafeToday : (i < streakCount);
    return DayActivity(date: date, isActive: isActive, isToday: isToday);
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
// Quality-Gated Streak Providers
// ---------------------------------------------------------------------------

/// The active streak visualization mode.
final streakModeProvider =
    StateProvider<StreakMode>((ref) => StreakMode.classic);

/// Last 30 days of activity (true = qualifying session that day).
/// Index 0 = today, index 29 = 29 days ago.
final last30DaysActivityProvider = StateProvider<List<bool>>((ref) {
  final streak = ref.watch(streakProvider);
  final isSafe = ref.watch(streakSafeProvider);
  return List.generate(30, (i) {
    if (i == 0) return isSafe;
    return i < streak;
  });
});

/// Consistency score: % of target days studied in rolling 30-day window.
final consistencyScoreProvider = Provider<int>((ref) {
  final days = ref.watch(last30DaysActivityProvider);
  return StreakQualityGate.computeConsistencyScore(days);
});

/// Momentum meter: rolling 7-day weighted average (recency bias).
final momentumMeterProvider = Provider<int>((ref) {
  final days = ref.watch(last30DaysActivityProvider);
  final last7 = days.length >= 7 ? days.sublist(0, 7) : days;
  return StreakQualityGate.computeMomentumMeter(last7);
});

/// Streak freezes earned: 1 per 7-day streak, max 3.
final streakFreezeCountProvider = Provider<int>((ref) {
  final streak = ref.watch(streakProvider);
  return min(streak ~/ 7, 3);
});

/// Habit stage based on consecutive streak days.
final habitStageProvider = Provider<HabitStage>((ref) {
  final streak = ref.watch(streakProvider);
  return StreakQualityGate.getHabitStage(streak);
});

// ---------------------------------------------------------------------------
// Badge Types
// ---------------------------------------------------------------------------

/// Badge category for grouping in the UI.
enum BadgeCategory { streak, mastery, engagement, special }

/// Rarity tier for badges, affecting visual treatment and discovery.
enum BadgeRarity { common, uncommon, rare, epic, secret }

/// Static definition of a badge (not tied to a specific student).
class BadgeDefinition {
  const BadgeDefinition({
    required this.id,
    required this.name,
    required this.nameHe,
    this.nameAr,
    required this.description,
    required this.icon,
    required this.category,
    required this.requiredValue,
    this.rarity = BadgeRarity.common,
  });

  final String id;
  final String name;
  final String nameHe;
  final String? nameAr;
  final String description;
  final String icon;
  final BadgeCategory category;
  final int requiredValue;
  final BadgeRarity rarity;
}

// ---------------------------------------------------------------------------
// Badges
// ---------------------------------------------------------------------------

/// All badges the student has earned.
final badgesProvider = StateProvider<List<Badge>>((ref) => []);

/// 35 badges across 4 categories. Each line: one badge definition.
final badgeCatalogueProvider = Provider<List<BadgeDefinition>>((ref) {
  return const [
    // -- Learning Behavior (streak) -- 8 badges
    BadgeDefinition(id: 'streak_3', name: '3-Day Streak', nameHe: 'רצף 3 ימים', nameAr: 'سلسلة 3 أيام', description: 'Study 3 days in a row', icon: 'local_fire_department', category: BadgeCategory.streak, requiredValue: 3, rarity: BadgeRarity.common),
    BadgeDefinition(id: 'streak_7', name: '7-Day Streak', nameHe: 'רצף שבוע', nameAr: 'سلسلة أسبوع', description: 'Study 7 days in a row — you earned a streak freeze!', icon: 'whatshot', category: BadgeCategory.streak, requiredValue: 7, rarity: BadgeRarity.common),
    BadgeDefinition(id: 'streak_14', name: 'Two-Week Warrior', nameHe: 'לוחם שבועיים', nameAr: 'محارب الأسبوعين', description: 'Study 14 days in a row', icon: 'whatshot', category: BadgeCategory.streak, requiredValue: 14, rarity: BadgeRarity.uncommon),
    BadgeDefinition(id: 'streak_30', name: '30-Day Streak', nameHe: 'רצף חודש', nameAr: 'سلسلة شهر', description: 'Study every day for a full month', icon: 'star', category: BadgeCategory.streak, requiredValue: 30, rarity: BadgeRarity.rare),
    BadgeDefinition(id: 'streak_60', name: 'Iron Will', nameHe: 'רצון ברזל', nameAr: 'إرادة حديدية', description: 'Maintain a 60-day streak', icon: 'diamond', category: BadgeCategory.streak, requiredValue: 60, rarity: BadgeRarity.epic),
    BadgeDefinition(id: 'streak_100', name: 'Centurion', nameHe: 'קנטוריון', nameAr: 'المئوي', description: 'Achieve a 100-day streak', icon: 'military_tech', category: BadgeCategory.streak, requiredValue: 100, rarity: BadgeRarity.epic),
    BadgeDefinition(id: 'review_consistency', name: 'Review Champion', nameHe: 'אלוף החזרות', nameAr: 'بطل المراجعة', description: 'Complete all due reviews for 7 consecutive days', icon: 'verified', category: BadgeCategory.streak, requiredValue: 7, rarity: BadgeRarity.uncommon),
    BadgeDefinition(id: 'methodology_explorer', name: 'Methodology Explorer', nameHe: 'חוקר שיטות', nameAr: 'مستكشف المنهجيات', description: 'Try all 5 pedagogical methodologies', icon: 'explore', category: BadgeCategory.streak, requiredValue: 5, rarity: BadgeRarity.uncommon),
    // -- Subject Mastery -- 8 badges
    BadgeDefinition(id: 'first_mastery', name: 'First Mastery', nameHe: 'שליטה ראשונה', nameAr: 'أول إتقان', description: 'Master your first concept', icon: 'school', category: BadgeCategory.mastery, requiredValue: 1, rarity: BadgeRarity.common),
    BadgeDefinition(id: 'mastery_5', name: 'Concept Explorer', nameHe: 'חוקר מושגים', nameAr: 'مستكشف المفاهيم', description: 'Master 5 concepts', icon: 'explore', category: BadgeCategory.mastery, requiredValue: 5, rarity: BadgeRarity.common),
    BadgeDefinition(id: 'mastery_20', name: 'Deep Thinker', nameHe: 'חושב עמוק', nameAr: 'المفكر العميق', description: 'Master 20 concepts', icon: 'psychology', category: BadgeCategory.mastery, requiredValue: 20, rarity: BadgeRarity.uncommon),
    BadgeDefinition(id: 'math_25', name: 'Math Apprentice', nameHe: 'שוליית מתמטיקה', nameAr: 'متدرب الرياضيات', description: 'Master 25% of Math concepts', icon: 'calculate', category: BadgeCategory.mastery, requiredValue: 25, rarity: BadgeRarity.common),
    BadgeDefinition(id: 'math_50', name: 'Math Scholar', nameHe: 'חוקר מתמטיקה', nameAr: 'عالم الرياضيات', description: 'Master 50% of Math concepts', icon: 'calculate', category: BadgeCategory.mastery, requiredValue: 50, rarity: BadgeRarity.uncommon),
    BadgeDefinition(id: 'physics_50', name: 'Physics Prodigy', nameHe: 'גאון פיזיקה', nameAr: 'عبقري الفيزياء', description: 'Master 50% of Physics concepts', icon: 'bolt', category: BadgeCategory.mastery, requiredValue: 50, rarity: BadgeRarity.uncommon),
    BadgeDefinition(id: 'chemistry_50', name: 'Lab Expert', nameHe: 'מומחה מעבדה', nameAr: 'خبير المختبر', description: 'Master 50% of Chemistry concepts', icon: 'science', category: BadgeCategory.mastery, requiredValue: 50, rarity: BadgeRarity.uncommon),
    BadgeDefinition(id: 'biology_50', name: 'Life Scientist', nameHe: 'מדען חיים', nameAr: 'عالم الأحياء', description: 'Master 50% of Biology concepts', icon: 'biotech', category: BadgeCategory.mastery, requiredValue: 50, rarity: BadgeRarity.uncommon),
    // -- Social (engagement) -- 8 badges
    BadgeDefinition(id: 'first_session', name: 'First Step', nameHe: 'הצעד הראשון', nameAr: 'الخطوة الأولى', description: 'Complete your first learning session', icon: 'play_circle', category: BadgeCategory.engagement, requiredValue: 1, rarity: BadgeRarity.common),
    BadgeDefinition(id: 'sessions_10', name: 'Dedicated Learner', nameHe: 'לומד מסור', nameAr: 'متعلم مخلص', description: 'Complete 10 learning sessions', icon: 'emoji_events', category: BadgeCategory.engagement, requiredValue: 10, rarity: BadgeRarity.common),
    BadgeDefinition(id: 'helper', name: 'Helpful Hand', nameHe: 'יד עוזרת', nameAr: 'يد المساعدة', description: 'Help a classmate with a concept explanation', icon: 'volunteer_activism', category: BadgeCategory.engagement, requiredValue: 1, rarity: BadgeRarity.uncommon),
    BadgeDefinition(id: 'team_player', name: 'Team Player', nameHe: 'שחקן קבוצתי', nameAr: 'لاعب الفريق', description: 'Participate in 5 group study sessions', icon: 'groups', category: BadgeCategory.engagement, requiredValue: 5, rarity: BadgeRarity.uncommon),
    BadgeDefinition(id: 'peer_tutor', name: 'Peer Tutor', nameHe: 'מורה עמיתים', nameAr: 'مرشد الأقران', description: 'Tutor 3 different classmates successfully', icon: 'support_agent', category: BadgeCategory.engagement, requiredValue: 3, rarity: BadgeRarity.rare),
    BadgeDefinition(id: 'class_contributor', name: 'Class Contributor', nameHe: 'תורם לכיתה', nameAr: 'المساهم في الصف', description: 'Share 10 helpful resources with your class', icon: 'forum', category: BadgeCategory.engagement, requiredValue: 10, rarity: BadgeRarity.rare),
    BadgeDefinition(id: 'encourager', name: 'Encourager', nameHe: 'מעודד', nameAr: 'المشجع', description: 'Send 20 encouraging messages to classmates', icon: 'favorite', category: BadgeCategory.engagement, requiredValue: 20, rarity: BadgeRarity.uncommon),
    BadgeDefinition(id: 'mentor', name: 'Mentor', nameHe: 'מנטור', nameAr: 'المرشد', description: 'Help 10 classmates master a concept they were stuck on', icon: 'self_improvement', category: BadgeCategory.engagement, requiredValue: 10, rarity: BadgeRarity.epic),
    // -- Meta / Hidden (special) -- 11 badges
    BadgeDefinition(id: 'all_subjects', name: 'Renaissance', nameHe: 'רנסנס', nameAr: 'عصر النهضة', description: 'Practice in all 5 subjects in one week', icon: 'hub', category: BadgeCategory.special, requiredValue: 5, rarity: BadgeRarity.rare),
    BadgeDefinition(id: 'level_10', name: 'Level 10', nameHe: 'רמה 10', nameAr: 'المستوى 10', description: 'Reach level 10', icon: 'military_tech', category: BadgeCategory.special, requiredValue: 10, rarity: BadgeRarity.uncommon),
    BadgeDefinition(id: 'midnight_scholar', name: 'Midnight Scholar', nameHe: 'חוקר חצות', nameAr: 'باحث منتصف الليل', description: 'Complete a session between midnight and 4am', icon: 'nightlight', category: BadgeCategory.special, requiredValue: 1, rarity: BadgeRarity.secret),
    BadgeDefinition(id: 'perfect_day', name: 'Perfect Day', nameHe: 'יום מושלם', nameAr: 'يوم مثالي', description: 'Achieve 100% accuracy across all sessions in one day', icon: 'percent', category: BadgeCategory.special, requiredValue: 1, rarity: BadgeRarity.secret),
    BadgeDefinition(id: 'all_subjects_one_day', name: 'Polymath', nameHe: 'פולימת', nameAr: 'المتعدد المعارف', description: 'Study all 5 subjects in a single day', icon: 'workspaces', category: BadgeCategory.special, requiredValue: 5, rarity: BadgeRarity.secret),
    BadgeDefinition(id: 'speed_demon', name: 'Speed Demon', nameHe: 'שד מהירות', nameAr: 'شيطان السرعة', description: 'Answer 10 consecutive questions correctly in under 15s each', icon: 'speed', category: BadgeCategory.special, requiredValue: 10, rarity: BadgeRarity.secret),
    BadgeDefinition(id: 'comeback_kid', name: 'Comeback Kid', nameHe: 'חזרה מנצחת', nameAr: 'عودة المنتصر', description: 'Break a streak, then build a longer one', icon: 'trending_up', category: BadgeCategory.special, requiredValue: 1, rarity: BadgeRarity.secret),
    BadgeDefinition(id: 'boss_slayer', name: 'Boss Slayer', nameHe: 'מכה בוס', nameAr: 'قاهر الزعيم', description: 'Win your first boss battle with no lives lost', icon: 'shield', category: BadgeCategory.special, requiredValue: 1, rarity: BadgeRarity.secret),
    BadgeDefinition(id: 'early_bird', name: 'Early Bird', nameHe: 'ציפור מוקדמת', nameAr: 'الطائر المبكر', description: 'Complete a session before 7am', icon: 'bolt', category: BadgeCategory.special, requiredValue: 1, rarity: BadgeRarity.secret),
    BadgeDefinition(id: 'marathon_session', name: 'Marathon Mind', nameHe: 'מוח מרתון', nameAr: 'عقل الماراثون', description: 'Complete a 30-min session with over 90% accuracy', icon: 'timer', category: BadgeCategory.special, requiredValue: 1, rarity: BadgeRarity.secret),
    BadgeDefinition(id: 'quest_master', name: 'Quest Master', nameHe: 'אדון המשימות', nameAr: 'سيد المهام', description: 'Complete 50 quests', icon: 'rocket_launch', category: BadgeCategory.special, requiredValue: 50, rarity: BadgeRarity.epic),
  ];
});

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
