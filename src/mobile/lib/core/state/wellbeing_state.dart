// =============================================================================
// Cena Adaptive Learning Platform — Wellbeing State Providers (MOB-052)
// Riverpod providers for daily study time tracking, wellbeing enforcement,
// bedtime mode, weekly summaries, and break compliance.
// =============================================================================

import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../services/bedtime_mode_service.dart';
import '../services/wellbeing_service.dart';

// ---------------------------------------------------------------------------
// Student Age Provider (dependency)
// ---------------------------------------------------------------------------

/// The current student's age in years. Must be overridden by the auth layer
/// once the student's profile is loaded.
final studentAgeProvider = StateProvider<int>((ref) => 15);

// ---------------------------------------------------------------------------
// Daily Study Time
// ---------------------------------------------------------------------------

/// Cumulative study time today. Updated by session notifiers after each
/// session completes.
final dailyStudyTimeProvider = StateProvider<Duration>(
  (ref) => Duration.zero,
);

// ---------------------------------------------------------------------------
// Wellbeing Level (computed)
// ---------------------------------------------------------------------------

/// Current [WellbeingLevel] derived from the student's age and today's
/// cumulative study time. Drives UI enforcement (warning banner, soft stop
/// dialog, hard stop auto-pause).
final wellbeingLevelProvider = Provider<WellbeingLevel>((ref) {
  final age = ref.watch(studentAgeProvider);
  final studyTime = ref.watch(dailyStudyTimeProvider);
  return WellbeingService.getState(age, studyTime);
});

/// Progress fraction [0.0, 1.0+] toward the daily study time limit.
final wellbeingProgressProvider = Provider<double>((ref) {
  final age = ref.watch(studentAgeProvider);
  final studyTime = ref.watch(dailyStudyTimeProvider);
  return WellbeingService.progressFraction(age, studyTime);
});

/// Remaining study time before the soft stop is triggered.
final remainingStudyTimeProvider = Provider<Duration>((ref) {
  final age = ref.watch(studentAgeProvider);
  final studyTime = ref.watch(dailyStudyTimeProvider);
  return WellbeingService.remainingTime(age, studyTime);
});

/// The appropriate wellbeing message for the current level, or null
/// when the student is in the [WellbeingLevel.normal] range.
final wellbeingMessageProvider = Provider<String?>((ref) {
  final level = ref.watch(wellbeingLevelProvider);
  final studyTime = ref.watch(dailyStudyTimeProvider);
  return WellbeingService.messageForState(level, todayStudyTime: studyTime);
});

/// Daily study time limit in minutes for the current student's age.
final dailyLimitMinutesProvider = Provider<int>((ref) {
  final age = ref.watch(studentAgeProvider);
  return WellbeingService.limitMinutesForAge(age);
});

// ---------------------------------------------------------------------------
// Bedtime Mode
// ---------------------------------------------------------------------------

/// The persisted bedtime mode configuration.
final bedtimeModeConfigProvider = StateProvider<BedtimeModeConfig>(
  (ref) => const BedtimeModeConfig(),
);

/// Whether bedtime mode is currently active, computed from the config
/// and the current time.
///
/// This provider uses a simple current-time check. In production, a
/// periodic timer or stream should refresh this every minute (similar
/// to the quiet hours checker in quiet_hours_service.dart).
final bedtimeModeActiveProvider = Provider<bool>((ref) {
  final config = ref.watch(bedtimeModeConfigProvider);
  return BedtimeModeService.isActive(DateTime.now(), config);
});

/// The bedtime mode message to show, or null if bedtime mode is inactive.
final bedtimeModeMessageProvider = Provider<String?>((ref) {
  final isActive = ref.watch(bedtimeModeActiveProvider);
  if (!isActive) return null;
  return BedtimeModeService.bedtimeBannerMessage();
});

// ---------------------------------------------------------------------------
// Weekly Study Summary
// ---------------------------------------------------------------------------

/// Daily study durations for the last 7 days, ordered from today (index 0)
/// to 6 days ago (index 6).
///
/// Updated by the session persistence layer at app startup and after each
/// session completes. Defaults to all zeros.
final weeklyStudySummaryProvider = StateProvider<List<Duration>>(
  (ref) => List.filled(7, Duration.zero),
);

/// Total study time across the last 7 days.
final weeklyTotalStudyTimeProvider = Provider<Duration>((ref) {
  final weekly = ref.watch(weeklyStudySummaryProvider);
  return weekly.fold<Duration>(Duration.zero, (sum, d) => sum + d);
});

/// Average daily study time over the last 7 days.
final weeklyAverageDailyProvider = Provider<Duration>((ref) {
  final total = ref.watch(weeklyTotalStudyTimeProvider);
  return Duration(minutes: total.inMinutes ~/ 7);
});

/// Number of days studied in the last 7 days (days with > 0 study time).
final weeklyActiveDaysProvider = Provider<int>((ref) {
  final weekly = ref.watch(weeklyStudySummaryProvider);
  return weekly.where((d) => d > Duration.zero).length;
});

// ---------------------------------------------------------------------------
// Break Compliance
// ---------------------------------------------------------------------------

/// Number of break suggestions shown to the student today.
final breakSuggestionsShownProvider = StateProvider<int>((ref) => 0);

/// Number of break suggestions the student accepted (took the break).
final breaksTakenProvider = StateProvider<int>((ref) => 0);

/// Break compliance rate: percentage of suggested breaks that were taken.
/// Returns 1.0 (100%) when no breaks have been suggested.
final breakComplianceRateProvider = Provider<double>((ref) {
  final suggested = ref.watch(breakSuggestionsShownProvider);
  final taken = ref.watch(breaksTakenProvider);
  if (suggested <= 0) return 1.0;
  return (taken / suggested).clamp(0.0, 1.0);
});

/// Human-readable break compliance label, e.g. "85%".
final breakComplianceLabelProvider = Provider<String>((ref) {
  final rate = ref.watch(breakComplianceRateProvider);
  return '${(rate * 100).round()}%';
});
