// =============================================================================
// Cena Adaptive Learning Platform — Home Screen Widget Config (MOB-041)
// Data models for Flutter home_widget package integration. Provides
// streak and quick-review widget data for the OS home screen.
// =============================================================================

// ---------------------------------------------------------------------------
// Widget Type
// ---------------------------------------------------------------------------

/// Types of home screen widgets the app exposes.
enum HomeWidgetType {
  /// Shows current streak count and today's progress indicator.
  streak,

  /// Shows number of concepts due for review with a one-tap start button.
  quickReview,

  /// Shows daily study time and wellbeing progress.
  dailyProgress,
}

// ---------------------------------------------------------------------------
// Widget Data
// ---------------------------------------------------------------------------

/// Data payload for a single home screen widget.
///
/// Serialized to SharedPreferences by the home_widget package for
/// native widget rendering on Android and iOS.
class WidgetData {
  const WidgetData({
    required this.type,
    required this.title,
    required this.value,
    required this.action,
    this.subtitle,
    this.progressFraction,
  });

  /// The type of widget (determines native layout template).
  final HomeWidgetType type;

  /// Primary display title, e.g. "Study Streak" or "Due for Review".
  final String title;

  /// Primary value shown prominently, e.g. "7 days" or "5 concepts".
  final String value;

  /// Deep-link action URI triggered when the widget is tapped.
  /// Examples: "cena://session/start", "cena://session/review".
  final String action;

  /// Optional secondary text below the value.
  final String? subtitle;

  /// Optional progress indicator [0.0, 1.0] for visual representation.
  final double? progressFraction;

  /// Converts to a flat `Map<String, dynamic>` for SharedPreferences storage.
  Map<String, dynamic> toMap() {
    return {
      'type': type.name,
      'title': title,
      'value': value,
      'action': action,
      if (subtitle != null) 'subtitle': subtitle,
      if (progressFraction != null) 'progressFraction': progressFraction,
    };
  }

  /// Reconstructs from a SharedPreferences map. Returns null if data is
  /// malformed.
  static WidgetData? fromMap(Map<String, dynamic> map) {
    final typeName = map['type'] as String?;
    final title = map['title'] as String?;
    final value = map['value'] as String?;
    final action = map['action'] as String?;

    if (typeName == null || title == null || value == null || action == null) {
      return null;
    }

    final type = HomeWidgetType.values.firstWhere(
      (t) => t.name == typeName,
      orElse: () => HomeWidgetType.streak,
    );

    return WidgetData(
      type: type,
      title: title,
      value: value,
      action: action,
      subtitle: map['subtitle'] as String?,
      progressFraction: (map['progressFraction'] as num?)?.toDouble(),
    );
  }

  @override
  String toString() => 'WidgetData(type: $type, title: $title, value: $value)';
}

// ---------------------------------------------------------------------------
// Widget Config Builder
// ---------------------------------------------------------------------------

/// Builds [WidgetData] payloads from current app state.
///
/// Called whenever gamification state changes so the native home screen
/// widgets stay in sync via the home_widget package.
class HomeScreenWidgetConfig {
  const HomeScreenWidgetConfig._();

  /// SharedPreferences key prefix for home_widget data.
  static const String _keyPrefix = 'home_widget_';

  /// Key for the streak widget data.
  static const String streakKey = '${_keyPrefix}streak';

  /// Key for the quick review widget data.
  static const String quickReviewKey = '${_keyPrefix}quick_review';

  /// Key for the daily progress widget data.
  static const String dailyProgressKey = '${_keyPrefix}daily_progress';

  // ---- Streak Widget ----

  /// Builds streak widget data.
  ///
  /// [currentStreak]: consecutive day count.
  /// [todayCompleted]: whether today's session qualifies for streak credit.
  static WidgetData buildStreakWidget({
    required int currentStreak,
    required bool todayCompleted,
  }) {
    final statusLabel = todayCompleted ? 'Today done' : 'Practice today';

    return WidgetData(
      type: HomeWidgetType.streak,
      title: 'Study Streak',
      value: '$currentStreak ${currentStreak == 1 ? 'day' : 'days'}',
      action: 'cena://session/start',
      subtitle: statusLabel,
      progressFraction: todayCompleted ? 1.0 : 0.0,
    );
  }

  // ---- Quick Review Widget ----

  /// Builds quick review widget data.
  ///
  /// [conceptsDue]: number of SRS items currently due for review.
  static WidgetData buildQuickReviewWidget({
    required int conceptsDue,
  }) {
    final valueText = conceptsDue > 0
        ? '$conceptsDue ${conceptsDue == 1 ? 'concept' : 'concepts'} due'
        : 'All caught up';
    final subtitleText =
        conceptsDue > 0 ? 'Tap to start review' : 'Great job staying current';

    return WidgetData(
      type: HomeWidgetType.quickReview,
      title: 'Quick Review',
      value: valueText,
      action: conceptsDue > 0
          ? 'cena://session/review'
          : 'cena://home',
      subtitle: subtitleText,
    );
  }

  // ---- Daily Progress Widget ----

  /// Builds daily progress widget data.
  ///
  /// [studyMinutesToday]: minutes studied today.
  /// [dailyGoalMinutes]: target minutes per day.
  static WidgetData buildDailyProgressWidget({
    required int studyMinutesToday,
    required int dailyGoalMinutes,
  }) {
    final progress = dailyGoalMinutes > 0
        ? (studyMinutesToday / dailyGoalMinutes).clamp(0.0, 1.0)
        : 0.0;
    final valueText = '${studyMinutesToday}m / ${dailyGoalMinutes}m';
    final subtitleText = progress >= 1.0
        ? 'Daily goal reached'
        : '${dailyGoalMinutes - studyMinutesToday}m remaining';

    return WidgetData(
      type: HomeWidgetType.dailyProgress,
      title: 'Today\'s Study',
      value: valueText,
      action: 'cena://home',
      subtitle: subtitleText,
      progressFraction: progress,
    );
  }
}
