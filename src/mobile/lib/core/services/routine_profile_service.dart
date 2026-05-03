// =============================================================================
// Cena Adaptive Learning Platform — Routine Profile Service (MOB-041)
// Learns daily study rhythm from usage timestamps and identifies natural
// study windows by clustering session start times.
// =============================================================================

import 'dart:math';

// ---------------------------------------------------------------------------
// Study Window
// ---------------------------------------------------------------------------

/// Time-of-day windows derived from student usage patterns.
///
/// Hour ranges:
///   - morning:     06:00 – 08:59
///   - commute:     09:00 – 11:59
///   - afterSchool: 14:00 – 16:59
///   - evening:     17:00 – 20:59
///   - bedtime:     21:00 – 23:59
enum StudyWindow {
  morning,
  commute,
  afterSchool,
  evening,
  bedtime,
}

// ---------------------------------------------------------------------------
// Routine Profile
// ---------------------------------------------------------------------------

/// Describes a student's habitual study rhythm learned from 14+ days of
/// session timestamps.
class RoutineProfile {
  const RoutineProfile({
    required this.preferredTimes,
    required this.avgSessionLengthMinutes,
    required this.weekdayPattern,
    required this.suggestedNotificationTime,
  });

  /// Study windows ranked from most to least frequent.
  final List<StudyWindow> preferredTimes;

  /// Average observed session length in minutes.
  final int avgSessionLengthMinutes;

  /// Per-weekday activity: index 1 = Monday … 7 = Sunday (ISO 8601).
  /// Value is the count of sessions observed on that weekday.
  final Map<int, int> weekdayPattern;

  /// Best notification time computed as the median of the preferred window
  /// shifted by ±30 minutes to avoid interrupting an active session.
  final DateTime suggestedNotificationTime;

  /// The single most-preferred window, or null if no data.
  StudyWindow? get primaryWindow =>
      preferredTimes.isNotEmpty ? preferredTimes.first : null;

  /// Returns true if the student tends to study on [weekday] (1=Mon, 7=Sun).
  bool isActiveOnWeekday(int weekday) =>
      (weekdayPattern[weekday] ?? 0) > 0;

  @override
  String toString() =>
      'RoutineProfile(primary: $primaryWindow, '
      'avgSession: ${avgSessionLengthMinutes}min, '
      'notification: ${suggestedNotificationTime.hour}:'
      '${suggestedNotificationTime.minute.toString().padLeft(2, '0')})';
}

// ---------------------------------------------------------------------------
// Routine Profile Service
// ---------------------------------------------------------------------------

/// Analyses session timestamps to build a [RoutineProfile].
///
/// Designed to learn from at least 14 days of usage data. With fewer
/// samples the profile is valid but less reliable.
class RoutineProfileService {
  const RoutineProfileService._();

  // ---- Window classification ----

  /// Classifies a [DateTime] into its corresponding [StudyWindow].
  static StudyWindow windowFor(DateTime timestamp) {
    final hour = timestamp.hour;
    if (hour >= 6 && hour <= 8) return StudyWindow.morning;
    if (hour >= 9 && hour <= 11) return StudyWindow.commute;
    if (hour >= 14 && hour <= 16) return StudyWindow.afterSchool;
    if (hour >= 17 && hour <= 20) return StudyWindow.evening;
    if (hour >= 21 && hour <= 23) return StudyWindow.bedtime;
    // Hours 0-5 and 12-13 are edge cases — map to nearest window.
    if (hour <= 5) return StudyWindow.bedtime; // late night = bedtime carryover
    return StudyWindow.afterSchool; // 12-13 = early afternoon
  }

  // ---- Profile construction ----

  /// Builds a [RoutineProfile] from a list of session start timestamps.
  ///
  /// Timestamps should span at least 14 days for reliable results, but
  /// the algorithm works with any non-empty list.
  ///
  /// [sessionDurations] is an optional parallel list of session lengths
  /// in minutes. If omitted, average session length defaults to 15 min.
  static RoutineProfile fromTimestamps(
    List<DateTime> sessionTimestamps, {
    List<int>? sessionDurations,
  }) {
    if (sessionTimestamps.isEmpty) {
      return RoutineProfile(
        preferredTimes: const [],
        avgSessionLengthMinutes: 15,
        weekdayPattern: const {},
        suggestedNotificationTime: DateTime(
          DateTime.now().year,
          DateTime.now().month,
          DateTime.now().day,
          16, // 4 PM default when no data
        ),
      );
    }

    // 1. Cluster timestamps into study windows.
    final windowCounts = <StudyWindow, int>{};
    final windowHours = <StudyWindow, List<int>>{};
    final weekdayCounts = <int, int>{};

    for (final ts in sessionTimestamps) {
      final window = windowFor(ts);
      windowCounts[window] = (windowCounts[window] ?? 0) + 1;
      windowHours.putIfAbsent(window, () => []).add(ts.hour);

      final weekday = ts.weekday; // 1=Monday, 7=Sunday
      weekdayCounts[weekday] = (weekdayCounts[weekday] ?? 0) + 1;
    }

    // 2. Rank windows by frequency, descending.
    final ranked = windowCounts.entries.toList()
      ..sort((a, b) => b.value.compareTo(a.value));
    final preferredTimes = ranked.map((e) => e.key).toList();

    // 3. Average session length.
    int avgMinutes = 15;
    if (sessionDurations != null && sessionDurations.isNotEmpty) {
      final total = sessionDurations.fold<int>(0, (sum, d) => sum + d);
      avgMinutes = (total / sessionDurations.length).round().clamp(1, 180);
    }

    // 4. Suggested notification time:
    //    Median hour of the top window, then subtract 30 minutes to nudge
    //    the student before their natural study time.
    final topWindow = preferredTimes.first;
    final hoursInTopWindow = windowHours[topWindow]!..sort();
    final medianHour = hoursInTopWindow[hoursInTopWindow.length ~/ 2];
    const medianMinute = 0; // hour-level granularity from timestamps

    // Subtract 30 minutes to send notification before study time.
    var notifHour = medianHour;
    var notifMinute = medianMinute - 30;
    if (notifMinute < 0) {
      notifMinute += 60;
      notifHour = max(0, notifHour - 1);
    }

    final now = DateTime.now();
    final suggestedNotification = DateTime(
      now.year,
      now.month,
      now.day,
      notifHour,
      notifMinute,
    );

    return RoutineProfile(
      preferredTimes: preferredTimes,
      avgSessionLengthMinutes: avgMinutes,
      weekdayPattern: weekdayCounts,
      suggestedNotificationTime: suggestedNotification,
    );
  }

  /// Returns the [StudyWindow] most likely for the current time of day
  /// based on the student's profile, or the raw time-based window if no
  /// profile data is available.
  static StudyWindow currentWindow(RoutineProfile? profile, [DateTime? now]) {
    final timestamp = now ?? DateTime.now();
    if (profile != null && profile.preferredTimes.isNotEmpty) {
      // If the student is currently in one of their preferred windows, use it.
      final rawWindow = windowFor(timestamp);
      if (profile.preferredTimes.contains(rawWindow)) {
        return rawWindow;
      }
      // Otherwise, fall back to their top preferred window.
      return profile.preferredTimes.first;
    }
    return windowFor(timestamp);
  }
}
