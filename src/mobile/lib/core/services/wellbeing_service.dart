// =============================================================================
// Cena Adaptive Learning Platform — Wellbeing Service (MOB-052)
// Age-tiered study time limits with progressive enforcement:
// warning → soft stop → hard stop. Messaging is always positive and
// non-judgmental — celebrating effort, never guilt-tripping.
// =============================================================================

// ---------------------------------------------------------------------------
// Wellbeing Level
// ---------------------------------------------------------------------------

/// Progressive wellbeing enforcement levels.
///
/// The system escalates gently:
///   1. [normal]: Student is within healthy study limits.
///   2. [warning]: 80% of daily limit reached — encourage awareness.
///   3. [softStop]: 100% of limit — suggest a break, progress is saved.
///   4. [hardStop]: 120% of limit — session is paused automatically.
enum WellbeingLevel {
  /// Within healthy limits. No intervention needed.
  normal,

  /// 80% of daily limit reached. Show a gentle reminder.
  warning,

  /// 100% of daily limit. Suggest stopping; progress is saved.
  softStop,

  /// 120% of daily limit. Auto-pause the session.
  hardStop,
}

// ---------------------------------------------------------------------------
// Age Tier
// ---------------------------------------------------------------------------

/// Age-based tier for study time limits.
///
/// Limits follow child development research and screen-time guidelines:
///   - Under 13: 90 minutes max
///   - 13-15: 120 minutes max
///   - 16+: 180 minutes max
enum AgeTier {
  /// Age < 13 — 90 minute daily limit.
  child,

  /// Age 13-15 — 120 minute daily limit.
  teen,

  /// Age 16+ — 180 minute daily limit.
  youngAdult,
}

// ---------------------------------------------------------------------------
// Wellbeing Service
// ---------------------------------------------------------------------------

/// Manages daily study time limits and progressive wellbeing enforcement.
///
/// Tracks cumulative daily study time per student and enforces age-tiered
/// limits with gentle escalation from warning to hard stop.
///
/// All messaging is positive: we celebrate effort and encourage rest as
/// a learning strategy, never guilt-trip or use loss-aversion language.
class WellbeingService {
  const WellbeingService._();

  // ---- Age tier classification ----

  /// Classifies a student's age into the appropriate [AgeTier].
  static AgeTier ageTierFor(int ageYears) {
    if (ageYears < 13) return AgeTier.child;
    if (ageYears <= 15) return AgeTier.teen;
    return AgeTier.youngAdult;
  }

  // ---- Time limits ----

  /// Returns the daily study time limit in minutes for the given [AgeTier].
  static int limitMinutesFor(AgeTier tier) {
    switch (tier) {
      case AgeTier.child:
        return 90;
      case AgeTier.teen:
        return 120;
      case AgeTier.youngAdult:
        return 180;
    }
  }

  /// Returns the daily study time limit for a student of the given age.
  static int limitMinutesForAge(int ageYears) {
    return limitMinutesFor(ageTierFor(ageYears));
  }

  // ---- State evaluation ----

  /// Evaluates the current [WellbeingLevel] based on age and today's study time.
  ///
  /// Thresholds:
  ///   - normal: < 80% of limit
  ///   - warning: >= 80% and < 100%
  ///   - softStop: >= 100% and < 120%
  ///   - hardStop: >= 120%
  static WellbeingLevel getState(int ageYears, Duration todayStudyTime) {
    final limitMinutes = limitMinutesForAge(ageYears);
    final studiedMinutes = todayStudyTime.inMinutes;

    final warningThreshold = (limitMinutes * 0.80).round();
    final softStopThreshold = limitMinutes;
    final hardStopThreshold = (limitMinutes * 1.20).round();

    if (studiedMinutes >= hardStopThreshold) return WellbeingLevel.hardStop;
    if (studiedMinutes >= softStopThreshold) return WellbeingLevel.softStop;
    if (studiedMinutes >= warningThreshold) return WellbeingLevel.warning;
    return WellbeingLevel.normal;
  }

  /// Returns the percentage of the daily limit consumed [0.0, 1.0+].
  static double progressFraction(int ageYears, Duration todayStudyTime) {
    final limitMinutes = limitMinutesForAge(ageYears);
    if (limitMinutes <= 0) return 0.0;
    return todayStudyTime.inMinutes / limitMinutes;
  }

  /// Returns the remaining study time before the soft stop.
  static Duration remainingTime(int ageYears, Duration todayStudyTime) {
    final limitMinutes = limitMinutesForAge(ageYears);
    final remaining = limitMinutes - todayStudyTime.inMinutes;
    return Duration(minutes: remaining.clamp(0, limitMinutes));
  }

  // ---- Positive messaging ----

  /// Returns a gentle warning message at 80% of the daily limit.
  ///
  /// Tone: encouraging, acknowledging effort, suggesting awareness.
  static String warningMessage(Duration todayStudyTime) {
    final minutes = todayStudyTime.inMinutes;
    return "You've been studying for $minutes minutes — great work! "
        'Consider taking a short break to let your brain consolidate '
        'what you have learned.';
  }

  /// Returns a soft stop message at 100% of the daily limit.
  ///
  /// Tone: warm, affirming, no guilt. Progress is safe.
  static String softStopMessage() {
    return 'Time for a break. Your progress is saved and your brain will '
        'keep working on what you learned even while you rest. '
        'Come back refreshed tomorrow!';
  }

  /// Returns a hard stop message at 120% of the daily limit.
  ///
  /// Tone: caring, firm but kind. The session is paused, not punished.
  static String hardStopMessage() {
    return 'Your daily study session is complete. Rest is an important '
        'part of learning — your brain consolidates knowledge during '
        'downtime. See you tomorrow!';
  }

  /// Returns the appropriate message for the current [WellbeingLevel].
  static String? messageForState(
    WellbeingLevel level, {
    Duration? todayStudyTime,
  }) {
    switch (level) {
      case WellbeingLevel.normal:
        return null;
      case WellbeingLevel.warning:
        return warningMessage(todayStudyTime ?? Duration.zero);
      case WellbeingLevel.softStop:
        return softStopMessage();
      case WellbeingLevel.hardStop:
        return hardStopMessage();
    }
  }
}
