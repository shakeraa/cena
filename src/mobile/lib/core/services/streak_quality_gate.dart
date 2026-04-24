// =============================================================================
// Cena Adaptive Learning Platform — Streak Quality Gate
// =============================================================================
//
// Prevents "zombie streaks" where students tap through sessions mindlessly.
// Validates that a session involved genuine learning before counting toward
// the streak. Also provides alternative streak modes (consistency score,
// momentum meter) and habit-stage classification.
// =============================================================================

import 'dart:math';

// ---------------------------------------------------------------------------
// Session Result
// ---------------------------------------------------------------------------

/// Summary of a completed session used for quality gate evaluation.
class SessionResult {
  const SessionResult({
    required this.questionsAnswered,
    required this.avgResponseTimeMs,
    required this.newConceptsAttempted,
  });

  /// Number of questions the student answered in the session.
  final int questionsAnswered;

  /// Average response time across all questions, in milliseconds.
  final int avgResponseTimeMs;

  /// Number of concepts the student attempted for the first time.
  final int newConceptsAttempted;
}

// ---------------------------------------------------------------------------
// Streak Mode
// ---------------------------------------------------------------------------

/// Alternative streak visualization modes.
///
/// - [classic]: Traditional consecutive day count (default).
/// - [consistencyScore]: Percentage of target days in a rolling 30-day window.
/// - [momentumMeter]: Weighted 7-day rolling average (recent days weighted more).
enum StreakMode {
  /// Traditional consecutive-day streak counter.
  classic,

  /// Percentage of target days met in a rolling 30-day window.
  consistencyScore,

  /// Rolling 7-day weighted average with recency bias.
  momentumMeter,
}

// ---------------------------------------------------------------------------
// Habit Stage
// ---------------------------------------------------------------------------

/// Habit formation stages based on behavioral psychology research.
///
/// Based on Phillippa Lally's research (European Journal of Social Psychology):
/// habits take 18-254 days to form, with 66 days as the median.
enum HabitStage {
  /// < 7 consecutive days. Fragile, needs strong external motivation.
  novice,

  /// 7-21 consecutive days. Building routine, occasional lapses expected.
  developing,

  /// 21-66 consecutive days. Routine is forming, internal motivation growing.
  established,

  /// 66+ consecutive days. Automatic behavior, high resilience to disruption.
  habitual,
}

// ---------------------------------------------------------------------------
// Streak Quality Gate
// ---------------------------------------------------------------------------

/// Validates whether a completed session qualifies for streak credit.
///
/// Quality gate thresholds:
///   - At least 3 questions answered
///   - Average response time > 5000ms (proves engagement, not random tapping)
///   - At least 1 new concept attempted
///
/// Also provides alternative streak modes and habit-stage classification.
class StreakQualityGate {
  const StreakQualityGate._();

  /// Minimum questions per session to qualify for streak credit.
  static const int minQuestions = 3;

  /// Minimum average response time in ms (5 seconds).
  static const int minAvgResponseTimeMs = 5000;

  /// Minimum new concepts attempted per session.
  static const int minNewConcepts = 1;

  /// Threshold below which a session is considered "zombie" (ms).
  static const int zombieThresholdMs = 5000;

  // ---- Quality Gate ----

  /// Returns true if [result] meets the quality bar for streak credit.
  ///
  /// All three conditions must be met:
  ///   1. >= 3 questions answered
  ///   2. Average response time > 5s (proves thoughtful engagement)
  ///   3. >= 1 new concept attempted
  static bool qualifiesForStreak(SessionResult result) {
    return result.questionsAnswered >= minQuestions &&
        result.avgResponseTimeMs > minAvgResponseTimeMs &&
        result.newConceptsAttempted >= minNewConcepts;
  }

  /// Returns true if the session exhibits "zombie" behavior.
  ///
  /// A zombie session is one where the average response time is under 5
  /// seconds, indicating the student is rapidly tapping without thinking.
  static bool detectZombieSession(SessionResult result) {
    return result.avgResponseTimeMs < zombieThresholdMs;
  }

  // ---- Habit Stage ----

  /// Classifies the student's habit stage based on consecutive streak days.
  static HabitStage getHabitStage(int consecutiveDays) {
    if (consecutiveDays >= 66) return HabitStage.habitual;
    if (consecutiveDays >= 21) return HabitStage.established;
    if (consecutiveDays >= 7) return HabitStage.developing;
    return HabitStage.novice;
  }

  // ---- No-Shame Messaging ----

  /// Returns a positive, encouraging message when a streak breaks.
  ///
  /// No-shame messaging is critical: punishing streak breaks causes anxiety
  /// and dropout. Instead, we celebrate what was accomplished and invite
  /// the student to start fresh.
  static String noShameMessage() {
    final messages = [
      'Every expert was once a beginner. Your knowledge stays with you!',
      'Rest is part of learning. Your brain consolidates while you recharge.',
      'Streaks measure habit, not talent. You can start a new one today!',
      'Missing a day is human. What matters is coming back.',
      'Your mastery progress is permanent. Only the streak counter resets.',
      'The best learners know when to rest. Welcome back!',
      'A streak is just a number. Your understanding is what counts.',
    ];
    return messages[DateTime.now().millisecond % messages.length];
  }

  // ---- Consistency Score ----

  /// Computes the consistency score: percentage of target days studied
  /// in a rolling 30-day window.
  ///
  /// [activeDays] is the list of the last 30 days, where `true` means
  /// the student had a qualifying session on that day.
  /// [targetDaysPerWeek] is how many days per week the student aims for
  /// (default: 5, i.e. weekdays).
  ///
  /// Returns a percentage [0, 100].
  static int computeConsistencyScore(
    List<bool> activeDays, {
    int targetDaysPerWeek = 5,
  }) {
    if (activeDays.isEmpty) return 0;

    // Clamp to 30 days maximum.
    final window = activeDays.length > 30
        ? activeDays.sublist(activeDays.length - 30)
        : activeDays;

    final totalActive = window.where((d) => d).length;
    final totalWeeks = window.length / 7.0;
    final targetDays = (totalWeeks * targetDaysPerWeek).ceil();

    if (targetDays <= 0) return 0;
    return ((totalActive / targetDays) * 100).round().clamp(0, 100);
  }

  // ---- Momentum Meter ----

  /// Computes the momentum meter: a rolling 7-day weighted average where
  /// recent days are weighted more heavily.
  ///
  /// [last7Days] is ordered from most recent (index 0) to oldest (index 6).
  /// Each entry is `true` if the student studied that day.
  ///
  /// Weights: today=7, yesterday=6, ... , 6 days ago=1.
  /// Returns a percentage [0, 100].
  static int computeMomentumMeter(List<bool> last7Days) {
    if (last7Days.isEmpty) return 0;

    final days = last7Days.length > 7 ? last7Days.sublist(0, 7) : last7Days;

    double weightedSum = 0;
    double totalWeight = 0;

    for (int i = 0; i < days.length; i++) {
      final weight = (days.length - i).toDouble(); // 7, 6, 5, 4, 3, 2, 1
      totalWeight += weight;
      if (days[i]) {
        weightedSum += weight;
      }
    }

    if (totalWeight <= 0) return 0;
    return ((weightedSum / totalWeight) * 100).round().clamp(0, 100);
  }

  // ---- Streak Freeze Allocation ----

  /// Computes the number of streak freezes earned.
  ///
  /// Rule: 1 freeze per 7-day streak, maximum 3.
  static int computeStreakFreezes(int currentStreak) {
    return min(currentStreak ~/ 7, 3);
  }
}
