// =============================================================================
// Cena Adaptive Learning Platform — Notification Intelligence (MOB-057)
// Smart notification suppression with daily budget, priority ranking,
// and ethical rules: no spam, no guilt, respect quiet hours and holidays.
// =============================================================================

import 'package:shared_preferences/shared_preferences.dart';

import 'quiet_hours_service.dart' show isQuietHours;

// ---------------------------------------------------------------------------
// Notification Priority
// ---------------------------------------------------------------------------

/// Priority levels for push notifications, highest to lowest.
///
/// The daily budget of 2 notifications picks from the top of this list.
/// Lower-priority items are suppressed when the budget is exhausted.
enum NotificationPriority {
  /// Streak about to expire at midnight — highest urgency.
  streakAtRisk,

  /// SRS items are due for review — time-sensitive for retention.
  reviewDue,

  /// A quest or challenge is available — engagement driver.
  questAvailable,

  /// Social: classmate activity, study group updates.
  social,

  /// Marketing / promotional content — lowest priority.
  marketing,
}

// ---------------------------------------------------------------------------
// Queued Notification
// ---------------------------------------------------------------------------

/// A notification waiting to be delivered.
class QueuedNotification {
  const QueuedNotification({
    required this.type,
    required this.priority,
    required this.title,
    required this.body,
    this.actionRoute,
    this.enqueuedAt,
  });

  /// Notification type identifier (e.g. "streak_expiry", "review_due").
  final String type;

  /// Delivery priority.
  final NotificationPriority priority;

  /// Notification title.
  final String title;

  /// Notification body text.
  final String body;

  /// Optional deep-link route.
  final String? actionRoute;

  /// When this notification was enqueued.
  final DateTime? enqueuedAt;

  @override
  String toString() =>
      'QueuedNotification(type: $type, priority: ${priority.name})';
}

// ---------------------------------------------------------------------------
// Student Notification State
// ---------------------------------------------------------------------------

/// Snapshot of student state relevant to notification decisions.
class StudentNotificationState {
  const StudentNotificationState({
    required this.practicedToday,
    required this.consecutiveDismissals,
    required this.inactiveDays,
    required this.isShabbatOrHoliday,
    required this.notificationsSentToday,
  });

  /// Whether the student has already completed a qualifying session today.
  final bool practicedToday;

  /// Count of consecutively dismissed notifications (across days).
  final int consecutiveDismissals;

  /// Days since last session (0 = active today).
  final int inactiveDays;

  /// Whether it is currently Shabbat or a configured holiday.
  final bool isShabbatOrHoliday;

  /// Number of notifications already sent today.
  final int notificationsSentToday;
}

// ---------------------------------------------------------------------------
// Notification Intelligence Service
// ---------------------------------------------------------------------------

/// Smart notification delivery with ethical suppression rules.
///
/// Core principles:
///   1. Budget: maximum 2 notifications per student per day.
///   2. Relevance: highest-priority notifications win the budget.
///   3. Respect: suppress during quiet hours, holidays, and after dismissals.
///   4. Dignity: stop all outreach after 60 days of inactivity.
///   5. No dark patterns: never guilt-trip or use loss-aversion language.
class NotificationIntelligenceService {
  NotificationIntelligenceService();

  /// Maximum notifications per student per day.
  static const int dailyBudget = 2;

  /// After this many consecutive dismissals, reduce frequency by 50%.
  static const int dismissalThresholdForReduction = 3;

  /// Duration of the reduced-frequency period after dismissal threshold.
  static const int reducedFrequencyDays = 7;

  /// After this many days of inactivity, stop all notifications.
  static const int silenceAfterInactiveDays = 60;

  // ---- Internal queue ----

  final List<QueuedNotification> _queue = [];

  /// Adds a notification to the queue for potential delivery.
  void enqueue(QueuedNotification notification) {
    _queue.add(QueuedNotification(
      type: notification.type,
      priority: notification.priority,
      title: notification.title,
      body: notification.body,
      actionRoute: notification.actionRoute,
      enqueuedAt: DateTime.now(),
    ));
  }

  /// Clears the queue.
  void clearQueue() => _queue.clear();

  /// Returns the number of items in the queue.
  int get queueLength => _queue.length;

  /// Returns the top-priority notifications within the daily budget.
  ///
  /// Sorted by priority (lowest enum index = highest priority), then by
  /// enqueue time (earliest first for same priority).
  List<QueuedNotification> getScheduled() {
    if (_queue.isEmpty) return const [];

    final sorted = List<QueuedNotification>.from(_queue)
      ..sort((a, b) {
        final priorityCompare = a.priority.index.compareTo(b.priority.index);
        if (priorityCompare != 0) return priorityCompare;
        final aTime = a.enqueuedAt ?? DateTime.now();
        final bTime = b.enqueuedAt ?? DateTime.now();
        return aTime.compareTo(bTime);
      });

    return sorted.take(dailyBudget).toList();
  }

  // ---- Suppression rules ----

  /// Evaluates all suppression rules and returns true if a notification
  /// of the given [priority] should be sent to this student.
  ///
  /// Rules (in evaluation order):
  ///   1. Stop all after 60 days of inactivity.
  ///   2. Suppress during Shabbat/holidays (configurable).
  ///   3. Suppress during quiet hours (9 PM – 7 AM).
  ///   4. Suppress if student already practiced today.
  ///   5. Budget: suppress if daily limit already reached.
  ///   6. Reduce frequency after 3 consecutive dismissals.
  bool shouldSend(
    NotificationPriority priority,
    StudentNotificationState state, [
    DateTime? now,
  ]) {
    // Rule 5: Stop all after 60 days of inactivity — respect the departure.
    if (state.inactiveDays >= silenceAfterInactiveDays) {
      return false;
    }

    // Rule 3: Suppress during Shabbat/holidays.
    if (state.isShabbatOrHoliday) {
      return false;
    }

    // Rule 2: Suppress during quiet hours.
    if (isQuietHours(now)) {
      return false;
    }

    // Rule 1: Suppress if student already practiced today.
    // Exception: streak-at-risk still goes through (they may need a reminder
    // to complete a qualifying session).
    if (state.practicedToday && priority != NotificationPriority.streakAtRisk) {
      return false;
    }

    // Rule 5 (budget): Suppress if daily limit reached.
    if (state.notificationsSentToday >= dailyBudget) {
      return false;
    }

    // Rule 4: After 3 consecutive dismissals, 50% frequency reduction
    // for 7 days. Implemented as: skip if dismissal count is odd
    // (effectively halving sends over the reduction window).
    if (state.consecutiveDismissals >= dismissalThresholdForReduction) {
      // Simple 50% reduction: suppress on odd-numbered attempts.
      if (state.notificationsSentToday % 2 != 0) {
        return false;
      }
    }

    return true;
  }

  // ---- Dismissal tracking persistence ----

  /// SharedPreferences key for consecutive dismissal count.
  static const String _kDismissals = 'notif_consecutive_dismissals';

  /// SharedPreferences key for today's send count.
  static const String _kSentToday = 'notif_sent_today';

  /// SharedPreferences key for the date of the last send count reset.
  static const String _kSentDate = 'notif_sent_date';

  /// Records a notification dismissal.
  static Future<int> recordDismissal() async {
    final prefs = await SharedPreferences.getInstance();
    final current = prefs.getInt(_kDismissals) ?? 0;
    final updated = current + 1;
    await prefs.setInt(_kDismissals, updated);
    return updated;
  }

  /// Resets the consecutive dismissal counter (called when a notification
  /// is acted upon rather than dismissed).
  static Future<void> resetDismissals() async {
    final prefs = await SharedPreferences.getInstance();
    await prefs.setInt(_kDismissals, 0);
  }

  /// Gets the current consecutive dismissal count.
  static Future<int> getDismissalCount() async {
    final prefs = await SharedPreferences.getInstance();
    return prefs.getInt(_kDismissals) ?? 0;
  }

  /// Increments today's sent count. Resets automatically on a new day.
  static Future<int> recordSent() async {
    final prefs = await SharedPreferences.getInstance();
    final today = _todayKey();
    final savedDate = prefs.getString(_kSentDate) ?? '';

    int count;
    if (savedDate == today) {
      count = (prefs.getInt(_kSentToday) ?? 0) + 1;
    } else {
      count = 1;
      await prefs.setString(_kSentDate, today);
    }

    await prefs.setInt(_kSentToday, count);
    return count;
  }

  /// Gets today's sent count.
  static Future<int> getSentToday() async {
    final prefs = await SharedPreferences.getInstance();
    final savedDate = prefs.getString(_kSentDate) ?? '';
    if (savedDate != _todayKey()) return 0;
    return prefs.getInt(_kSentToday) ?? 0;
  }

  static String _todayKey() {
    final now = DateTime.now();
    return '${now.year}-${now.month.toString().padLeft(2, '0')}-'
        '${now.day.toString().padLeft(2, '0')}';
  }
}
