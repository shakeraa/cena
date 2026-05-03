// =============================================================================
// Cena Adaptive Learning Platform — Quest Generator
// =============================================================================
//
// Generates daily, weekly, and side quests based on the student's current
// learning state. Quests are designed to be achievable and motivating:
//   - Daily: 1-2 small goals, 25-50 XP, at least 1 achievable in single session
//   - Weekly: 2-3 larger goals, 100-200 XP
//   - Side: Optional exploration, clearly marked, no pressure
// =============================================================================

import 'dart:math';

import '../models/quest_models.dart';

// ---------------------------------------------------------------------------
// Student State (input to the generator)
// ---------------------------------------------------------------------------

/// Snapshot of the student's current state, used by the quest generator
/// to produce contextually relevant quests.
class StudentQuestState {
  const StudentQuestState({
    required this.totalMasteredConcepts,
    required this.totalConceptsAvailable,
    required this.dueReviewItems,
    required this.sessionsCompletedThisWeek,
    required this.weeklyAccuracy,
    required this.currentStreak,
    required this.subjectsStudied,
    required this.allSubjects,
    required this.methodologiesUsedRecently,
    required this.allMethodologies,
    required this.staleConcepts,
  });

  /// Total concepts the student has mastered (P(Known) >= 0.85).
  final int totalMasteredConcepts;

  /// Total concepts available in the student's curriculum.
  final int totalConceptsAvailable;

  /// Number of items due for spaced repetition review.
  final int dueReviewItems;

  /// Sessions completed so far this calendar week.
  final int sessionsCompletedThisWeek;

  /// Accuracy across all sessions this week [0.0, 1.0].
  final double weeklyAccuracy;

  /// Current consecutive-day streak.
  final int currentStreak;

  /// Subjects the student has studied in the last 7 days.
  final List<String> subjectsStudied;

  /// All subjects available in the curriculum.
  final List<String> allSubjects;

  /// Methodologies used in the last 14 days.
  final List<String> methodologiesUsedRecently;

  /// All available pedagogical methodologies.
  final List<String> allMethodologies;

  /// Concepts not attempted in 30+ days (id -> name).
  final Map<String, String> staleConcepts;
}

// ---------------------------------------------------------------------------
// Quest Generator
// ---------------------------------------------------------------------------

/// Generates contextually relevant quests for the student.
///
/// Quest generation follows these principles:
///   1. At least one daily quest must be achievable in a single session.
///   2. Weekly quests build on consistent daily effort.
///   3. Side quests encourage exploration without pressure.
///   4. XP rewards scale with difficulty and time investment.
class QuestGenerator {
  const QuestGenerator._();

  static final _rng = Random();

  /// Generates 1-2 daily quests based on the student's current state.
  ///
  /// Daily quests reset at midnight and expire at end of day.
  /// At least one quest is achievable in a single session.
  /// XP reward: 25-50 per quest.
  static List<Quest> generateDaily(StudentQuestState state) {
    final quests = <Quest>[];
    final now = DateTime.now();
    final endOfDay = DateTime(now.year, now.month, now.day, 23, 59, 59);
    final dayHash = now.day + now.month * 31;

    // Quest 1: Always achievable in a single session.
    if (state.dueReviewItems >= 5) {
      quests.add(Quest(
        id: 'daily_review_${now.toIso8601String().substring(0, 10)}',
        type: QuestType.daily,
        title: 'Review ${min(5, state.dueReviewItems)} due items',
        titleHe: 'חזור על ${min(5, state.dueReviewItems)} פריטים לחזרה',
        description:
            'Complete your spaced repetition reviews to strengthen memory.',
        criteria: ReviewItems(count: min(5, state.dueReviewItems)),
        target: min(5, state.dueReviewItems),
        xpReward: 30,
        status: QuestStatus.accepted,
        expiresAt: endOfDay,
      ));
    } else {
      quests.add(Quest(
        id: 'daily_master_${now.toIso8601String().substring(0, 10)}',
        type: QuestType.daily,
        title: 'Master 1 new concept',
        titleHe: 'שלוט במושג חדש אחד',
        description:
            'Reach mastery level on a concept you haven\'t mastered yet.',
        criteria: const MasterConcepts(count: 1),
        target: 1,
        xpReward: 40,
        status: QuestStatus.accepted,
        expiresAt: endOfDay,
      ));
    }

    // Quest 2: Variety quest, rotated based on the day.
    final variety = dayHash % 3;
    switch (variety) {
      case 0:
        if (state.totalConceptsAvailable > state.totalMasteredConcepts) {
          quests.add(Quest(
            id: 'daily_session_${now.toIso8601String().substring(0, 10)}',
            type: QuestType.daily,
            title: 'Complete a full session',
            titleHe: 'השלם שיעור מלא',
            description:
                'Complete a learning session that passes the quality gate.',
            criteria: const CompleteSessions(count: 1),
            target: 1,
            xpReward: 25,
            status: QuestStatus.accepted,
            expiresAt: endOfDay,
          ));
        }
      case 1:
        if (state.weeklyAccuracy < 0.80) {
          quests.add(Quest(
            id: 'daily_accuracy_${now.toIso8601String().substring(0, 10)}',
            type: QuestType.daily,
            title: 'Achieve 80% accuracy today',
            titleHe: 'השג דיוק של 80% היום',
            description:
                'Answer at least 80% of questions correctly in your sessions.',
            criteria: const AchieveAccuracy(targetAccuracy: 0.80),
            target: 80,
            xpReward: 35,
            status: QuestStatus.accepted,
            expiresAt: endOfDay,
          ));
        }
      case 2:
        quests.add(Quest(
          id: 'daily_concepts_${now.toIso8601String().substring(0, 10)}',
          type: QuestType.daily,
          title: 'Master 2 concepts',
          titleHe: 'שלוט ב-2 מושגים',
          description: 'Push toward mastery on two concepts today.',
          criteria: const MasterConcepts(count: 2),
          target: 2,
          xpReward: 50,
          status: QuestStatus.accepted,
          expiresAt: endOfDay,
        ));
    }

    return quests;
  }

  /// Generates 2-3 weekly quests based on the student's current state.
  ///
  /// Weekly quests reset on Sunday and expire at end of the week.
  /// XP reward: 100-200 per quest.
  static List<Quest> generateWeekly(StudentQuestState state) {
    final quests = <Quest>[];
    final now = DateTime.now();
    // End of week (Sunday 23:59:59).
    final daysUntilSunday = DateTime.sunday - now.weekday;
    final endOfWeek = DateTime(
      now.year,
      now.month,
      now.day + (daysUntilSunday <= 0 ? 7 : daysUntilSunday),
      23,
      59,
      59,
    );
    final weekId = '${now.year}_w${(now.day ~/ 7) + 1}_${now.month}';

    // Quest 1: Session completion goal.
    quests.add(Quest(
      id: 'weekly_sessions_$weekId',
      type: QuestType.weekly,
      title: 'Complete 5 sessions this week',
      titleHe: 'השלם 5 שיעורים השבוע',
      description: 'Build a consistent study habit by completing 5 sessions.',
      criteria: const CompleteSessions(count: 5),
      target: 5,
      progress: state.sessionsCompletedThisWeek,
      xpReward: 150,
      status: state.sessionsCompletedThisWeek >= 5
          ? QuestStatus.completed
          : QuestStatus.inProgress,
      expiresAt: endOfWeek,
    ));

    // Quest 2: Accuracy-based, using a subject the student is weaker in.
    final unstudied = state.allSubjects
        .where((s) => !state.subjectsStudied.contains(s))
        .toList();
    if (unstudied.isNotEmpty) {
      final subject = unstudied[_rng.nextInt(unstudied.length)];
      quests.add(Quest(
        id: 'weekly_accuracy_$weekId',
        type: QuestType.weekly,
        title: 'Achieve 80% accuracy in ${_capitalize(subject)}',
        titleHe: 'השג דיוק של 80% ב${_capitalize(subject)}',
        description:
            'Focus on $subject this week and reach 80% accuracy.',
        criteria: AchieveAccuracy(targetAccuracy: 0.80, subject: subject),
        target: 80,
        xpReward: 200,
        status: QuestStatus.accepted,
        expiresAt: endOfWeek,
      ));
    } else {
      quests.add(Quest(
        id: 'weekly_mastery_$weekId',
        type: QuestType.weekly,
        title: 'Master 5 new concepts',
        titleHe: 'שלוט ב-5 מושגים חדשים',
        description: 'Deepen your understanding across your subjects.',
        criteria: const MasterConcepts(count: 5),
        target: 5,
        xpReward: 175,
        status: QuestStatus.accepted,
        expiresAt: endOfWeek,
      ));
    }

    // Quest 3: Review consistency.
    if (state.dueReviewItems >= 10) {
      quests.add(Quest(
        id: 'weekly_review_$weekId',
        type: QuestType.weekly,
        title: 'Clear 20 review items',
        titleHe: 'השלם 20 פריטי חזרה',
        description:
            'Stay on top of your spaced repetition schedule this week.',
        criteria: const ReviewItems(count: 20),
        target: 20,
        xpReward: 100,
        status: QuestStatus.accepted,
        expiresAt: endOfWeek,
      ));
    }

    return quests;
  }

  /// Generates optional side quests that encourage exploration.
  ///
  /// Side quests are clearly marked as optional and carry no deadline.
  /// They nudge the student to try new things without pressure.
  static List<Quest> generateSideQuests(StudentQuestState state) {
    final quests = <Quest>[];
    final now = DateTime.now();

    // Side quest: Try a methodology the student hasn't used recently.
    final unusedMethodologies = state.allMethodologies
        .where((m) => !state.methodologiesUsedRecently.contains(m))
        .toList();
    if (unusedMethodologies.isNotEmpty) {
      final methodology =
          unusedMethodologies[_rng.nextInt(unusedMethodologies.length)];
      quests.add(Quest(
        id: 'side_methodology_${methodology}_${now.millisecondsSinceEpoch}',
        type: QuestType.side,
        title: 'Try ${_formatMethodology(methodology)} approach',
        titleHe: 'נסה גישת ${_formatMethodology(methodology)}',
        description:
            'Explore a different learning methodology. '
            'You might discover a new favorite!',
        criteria: TryMethodology(methodology: methodology),
        target: 1,
        xpReward: 50,
        status: QuestStatus.available,
      ));
    }

    // Side quest: Revisit a stale concept.
    if (state.staleConcepts.isNotEmpty) {
      final entry = state.staleConcepts.entries.first;
      quests.add(Quest(
        id: 'side_revisit_${entry.key}_${now.millisecondsSinceEpoch}',
        type: QuestType.side,
        title: 'Revisit: ${entry.value}',
        titleHe: 'חזור ל: ${entry.value}',
        description:
            'You haven\'t practiced this concept in over 30 days. '
            'See how much you still remember!',
        criteria: const ExploreOldConcept(daysSinceLastAttempt: 30),
        target: 1,
        xpReward: 40,
        status: QuestStatus.available,
      ));
    }

    // Side quest: Study all subjects in one week.
    final subjectsMissing = state.allSubjects
        .where((s) => !state.subjectsStudied.contains(s))
        .length;
    if (subjectsMissing > 0 && subjectsMissing <= 3) {
      quests.add(Quest(
        id: 'side_all_subjects_${now.millisecondsSinceEpoch}',
        type: QuestType.side,
        title: 'Study all subjects this week',
        titleHe: 'למד את כל המקצועות השבוע',
        description:
            'You\'ve studied ${state.subjectsStudied.length} out of '
            '${state.allSubjects.length} subjects. '
            'Try the rest for a well-rounded week!',
        criteria: CompleteSessions(count: subjectsMissing),
        target: subjectsMissing,
        xpReward: 75,
        status: QuestStatus.available,
      ));
    }

    return quests;
  }

  static String _capitalize(String s) {
    if (s.isEmpty) return s;
    return '${s[0].toUpperCase()}${s.substring(1)}';
  }

  static String _formatMethodology(String m) {
    return m
        .replaceAll('_', ' ')
        .split(' ')
        .map(_capitalize)
        .join(' ');
  }
}
