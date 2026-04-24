// =============================================================================
// Cena Adaptive Learning Platform — Quiet Hours & Wellbeing (MOB-ETHICS-001)
// =============================================================================
//
// Blueprint Principle 5: Ethical by Default — No Dark Patterns
// - 9 PM – 7 AM: ZERO notifications. No exceptions.
// - Study time limits: 90 / 120 / 180 minutes with break enforcement.
// - Wellbeing signals: detect zombie sessions, unusual hours, declining accuracy.
// =============================================================================

import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:shared_preferences/shared_preferences.dart';

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

/// Quiet hours: no notifications between these hours (local time).
const int _quietStartHour = 21; // 9 PM
const int _quietEndHour = 7; // 7 AM

/// Study time limit presets in minutes.
const List<int> studyTimeLimitOptions = [90, 120, 180];

/// Default daily study limit in minutes.
const int defaultStudyLimitMinutes = 120;

/// SharedPreferences keys
const String _kStudyLimitMinutes = 'wellbeing_study_limit_minutes';
const String _kTotalStudyTodayMs = 'wellbeing_total_study_today_ms';
const String _kStudyDateKey = 'wellbeing_study_date';

// ---------------------------------------------------------------------------
// Quiet Hours check
// ---------------------------------------------------------------------------

/// Returns true if the current local time is within quiet hours (9 PM – 7 AM).
/// During quiet hours, push notifications MUST be suppressed.
bool isQuietHours([DateTime? now]) {
  final time = now ?? DateTime.now();
  return time.hour >= _quietStartHour || time.hour < _quietEndHour;
}

/// Returns the next quiet hours boundary.
/// If currently in quiet hours, returns when quiet hours end.
/// If currently outside, returns when quiet hours start.
DateTime nextQuietHoursBoundary([DateTime? now]) {
  final time = now ?? DateTime.now();
  if (isQuietHours(time)) {
    // Currently in quiet hours — return 7 AM today or tomorrow
    if (time.hour >= _quietStartHour) {
      return DateTime(time.year, time.month, time.day + 1, _quietEndHour);
    } else {
      return DateTime(time.year, time.month, time.day, _quietEndHour);
    }
  } else {
    // Outside quiet hours — return 9 PM today
    return DateTime(time.year, time.month, time.day, _quietStartHour);
  }
}

// ---------------------------------------------------------------------------
// Wellbeing State
// ---------------------------------------------------------------------------

class WellbeingState {
  const WellbeingState({
    this.studyLimitMinutes = defaultStudyLimitMinutes,
    this.totalStudyTodayMs = 0,
    this.isLimitReached = false,
    this.isLimitWarning = false,
    this.isQuietHoursActive = false,
  });

  /// Configured daily study limit in minutes.
  final int studyLimitMinutes;

  /// Total study time today in milliseconds.
  final int totalStudyTodayMs;

  /// Whether the daily study limit has been reached.
  final bool isLimitReached;

  /// Warning state: approaching limit (within 10 minutes).
  final bool isLimitWarning;

  /// Whether quiet hours are currently active.
  final bool isQuietHoursActive;

  /// Total study time today as a Duration.
  Duration get totalStudyToday => Duration(milliseconds: totalStudyTodayMs);

  /// Remaining study time today.
  Duration get remainingStudyTime {
    final limitMs = studyLimitMinutes * 60 * 1000;
    final remaining = limitMs - totalStudyTodayMs;
    return Duration(milliseconds: remaining.clamp(0, limitMs));
  }

  /// Progress toward the daily limit [0.0, 1.0].
  double get limitProgress {
    final limitMs = studyLimitMinutes * 60 * 1000;
    if (limitMs <= 0) return 0.0;
    return (totalStudyTodayMs / limitMs).clamp(0.0, 1.0);
  }

  WellbeingState copyWith({
    int? studyLimitMinutes,
    int? totalStudyTodayMs,
    bool? isLimitReached,
    bool? isLimitWarning,
    bool? isQuietHoursActive,
  }) {
    return WellbeingState(
      studyLimitMinutes: studyLimitMinutes ?? this.studyLimitMinutes,
      totalStudyTodayMs: totalStudyTodayMs ?? this.totalStudyTodayMs,
      isLimitReached: isLimitReached ?? this.isLimitReached,
      isLimitWarning: isLimitWarning ?? this.isLimitWarning,
      isQuietHoursActive: isQuietHoursActive ?? this.isQuietHoursActive,
    );
  }
}

// ---------------------------------------------------------------------------
// Wellbeing Notifier
// ---------------------------------------------------------------------------

class WellbeingNotifier extends StateNotifier<WellbeingState> {
  WellbeingNotifier() : super(const WellbeingState()) {
    _loadPersistedState();
    _startQuietHoursChecker();
  }

  Timer? _quietHoursTimer;

  Future<void> _loadPersistedState() async {
    final prefs = await SharedPreferences.getInstance();
    final limit = prefs.getInt(_kStudyLimitMinutes) ?? defaultStudyLimitMinutes;
    final savedDate = prefs.getString(_kStudyDateKey) ?? '';
    final today = _todayKey();

    // Reset counter if it's a new day
    int totalMs = 0;
    if (savedDate == today) {
      totalMs = prefs.getInt(_kTotalStudyTodayMs) ?? 0;
    } else {
      await prefs.setString(_kStudyDateKey, today);
      await prefs.setInt(_kTotalStudyTodayMs, 0);
    }

    final limitMs = limit * 60 * 1000;
    state = state.copyWith(
      studyLimitMinutes: limit,
      totalStudyTodayMs: totalMs,
      isLimitReached: totalMs >= limitMs,
      isLimitWarning: totalMs >= (limitMs - 10 * 60 * 1000) && totalMs < limitMs,
      isQuietHoursActive: isQuietHours(),
    );
  }

  void _startQuietHoursChecker() {
    // Check every minute for quiet hours transitions
    _quietHoursTimer = Timer.periodic(const Duration(minutes: 1), (_) {
      final nowQuiet = isQuietHours();
      if (nowQuiet != state.isQuietHoursActive) {
        state = state.copyWith(isQuietHoursActive: nowQuiet);
      }
    });
  }

  /// Record study time from a completed session.
  Future<void> recordStudyTime(Duration sessionDuration) async {
    final newTotalMs = state.totalStudyTodayMs + sessionDuration.inMilliseconds;
    final limitMs = state.studyLimitMinutes * 60 * 1000;

    state = state.copyWith(
      totalStudyTodayMs: newTotalMs,
      isLimitReached: newTotalMs >= limitMs,
      isLimitWarning:
          newTotalMs >= (limitMs - 10 * 60 * 1000) && newTotalMs < limitMs,
    );

    final prefs = await SharedPreferences.getInstance();
    await prefs.setString(_kStudyDateKey, _todayKey());
    await prefs.setInt(_kTotalStudyTodayMs, newTotalMs);
  }

  /// Update the daily study limit.
  Future<void> setStudyLimit(int minutes) async {
    final limitMs = minutes * 60 * 1000;
    state = state.copyWith(
      studyLimitMinutes: minutes,
      isLimitReached: state.totalStudyTodayMs >= limitMs,
      isLimitWarning: state.totalStudyTodayMs >= (limitMs - 10 * 60 * 1000) &&
          state.totalStudyTodayMs < limitMs,
    );
    final prefs = await SharedPreferences.getInstance();
    await prefs.setInt(_kStudyLimitMinutes, minutes);
  }

  String _todayKey() {
    final now = DateTime.now();
    return '${now.year}-${now.month.toString().padLeft(2, '0')}-${now.day.toString().padLeft(2, '0')}';
  }

  @override
  void dispose() {
    _quietHoursTimer?.cancel();
    super.dispose();
  }
}

// ---------------------------------------------------------------------------
// Provider
// ---------------------------------------------------------------------------

final wellbeingProvider =
    StateNotifierProvider<WellbeingNotifier, WellbeingState>(
  (ref) => WellbeingNotifier(),
);
