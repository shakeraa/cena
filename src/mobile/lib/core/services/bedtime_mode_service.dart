// =============================================================================
// Cena Adaptive Learning Platform — Bedtime Mode Service (MOB-052)
// Review-only mode activated near bedtime. No new sessions, no challenging
// material. Configurable by parent or student.
// =============================================================================

import 'package:flutter/material.dart';
import 'package:shared_preferences/shared_preferences.dart';

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

/// SharedPreferences keys for bedtime mode configuration.
const String _kBedtimeHour = 'bedtime_mode_hour';
const String _kBedtimeMinute = 'bedtime_mode_minute';
const String _kBedtimeEnabled = 'bedtime_mode_enabled';

/// Default bedtime: 10:00 PM.
const int _defaultBedtimeHour = 22;
const int _defaultBedtimeMinute = 0;

// ---------------------------------------------------------------------------
// Bedtime Mode Config
// ---------------------------------------------------------------------------

/// Persisted bedtime mode configuration.
class BedtimeModeConfig {
  const BedtimeModeConfig({
    this.bedtimeHour = _defaultBedtimeHour,
    this.bedtimeMinute = _defaultBedtimeMinute,
    this.isEnabled = true,
  });

  /// Hour component of bedtime (0-23).
  final int bedtimeHour;

  /// Minute component of bedtime (0-59).
  final int bedtimeMinute;

  /// Whether bedtime mode is enabled at all.
  final bool isEnabled;

  /// Returns bedtime as a [TimeOfDay] for use with Flutter time pickers.
  TimeOfDay get bedtime => TimeOfDay(hour: bedtimeHour, minute: bedtimeMinute);

  /// Returns a human-readable bedtime string, e.g. "10:00 PM".
  String get bedtimeLabel {
    final hour = bedtimeHour % 12 == 0 ? 12 : bedtimeHour % 12;
    final period = bedtimeHour < 12 ? 'AM' : 'PM';
    final minuteStr = bedtimeMinute.toString().padLeft(2, '0');
    return '$hour:$minuteStr $period';
  }

  BedtimeModeConfig copyWith({
    int? bedtimeHour,
    int? bedtimeMinute,
    bool? isEnabled,
  }) {
    return BedtimeModeConfig(
      bedtimeHour: bedtimeHour ?? this.bedtimeHour,
      bedtimeMinute: bedtimeMinute ?? this.bedtimeMinute,
      isEnabled: isEnabled ?? this.isEnabled,
    );
  }

  @override
  String toString() =>
      'BedtimeModeConfig(bedtime: $bedtimeLabel, enabled: $isEnabled)';
}

// ---------------------------------------------------------------------------
// Bedtime Mode Service
// ---------------------------------------------------------------------------

/// Manages bedtime mode: a review-only restriction activated near the
/// student's configured bedtime.
///
/// When active:
///   - No new learning sessions may be started.
///   - Only flashcard review mode is available.
///   - UI shifts to a calmer visual tone (reduced brightness, warm colors).
///
/// Configurable by:
///   - The student themselves (for ages 16+).
///   - A parent or guardian (for ages < 16).
class BedtimeModeService {
  const BedtimeModeService._();

  // ---- Active check ----

  /// Returns true if bedtime mode is currently active.
  ///
  /// Bedtime mode is active when:
  ///   1. The feature is enabled in config.
  ///   2. The current time is at or past the configured bedtime.
  ///   3. The current time is before the "wake" time (6:00 AM next day).
  ///
  /// This creates a window from bedtime until 6 AM where only review
  /// mode is available.
  static bool isActive(DateTime now, BedtimeModeConfig config) {
    if (!config.isEnabled) return false;

    final currentMinutes = now.hour * 60 + now.minute;
    final bedtimeMinutes = config.bedtimeHour * 60 + config.bedtimeMinute;
    const wakeMinutes = 6 * 60; // 6:00 AM

    // Bedtime window wraps around midnight:
    //   bedtime (e.g. 22:00) → midnight → wake (6:00)
    if (bedtimeMinutes > wakeMinutes) {
      // Normal case: bedtime is in the evening (e.g. 22:00)
      return currentMinutes >= bedtimeMinutes || currentMinutes < wakeMinutes;
    } else {
      // Edge case: bedtime is set before 6 AM (unusual but valid)
      return currentMinutes >= bedtimeMinutes && currentMinutes < wakeMinutes;
    }
  }

  /// Convenience check using the default bedtime config.
  static bool isActiveWithDefaults(DateTime now) {
    return isActive(now, const BedtimeModeConfig());
  }

  // ---- Messaging ----

  /// Returns a warm good-night message when bedtime mode activates.
  ///
  /// Tone: supportive, calming. The student has earned their rest.
  static String goodNightMessage() {
    return 'Time to wind down. Your progress is saved and your brain '
        'will keep strengthening those neural pathways while you sleep. '
        'Sweet dreams!';
  }

  /// Returns a shorter message for the bedtime mode banner.
  static String bedtimeBannerMessage() {
    return 'Bedtime mode is on — only review is available. '
        'Rest helps your brain consolidate today\'s learning.';
  }

  /// Returns a message when the student tries to start a new session
  /// during bedtime mode.
  static String newSessionBlockedMessage() {
    return 'New sessions are paused until morning. '
        'You can review flashcards instead — '
        'gentle review before sleep actually helps memory!';
  }

  // ---- Persistence ----

  /// Loads the bedtime mode config from SharedPreferences.
  static Future<BedtimeModeConfig> loadConfig() async {
    final prefs = await SharedPreferences.getInstance();
    return BedtimeModeConfig(
      bedtimeHour: prefs.getInt(_kBedtimeHour) ?? _defaultBedtimeHour,
      bedtimeMinute: prefs.getInt(_kBedtimeMinute) ?? _defaultBedtimeMinute,
      isEnabled: prefs.getBool(_kBedtimeEnabled) ?? true,
    );
  }

  /// Saves the bedtime mode config to SharedPreferences.
  static Future<void> saveConfig(BedtimeModeConfig config) async {
    final prefs = await SharedPreferences.getInstance();
    await Future.wait([
      prefs.setInt(_kBedtimeHour, config.bedtimeHour),
      prefs.setInt(_kBedtimeMinute, config.bedtimeMinute),
      prefs.setBool(_kBedtimeEnabled, config.isEnabled),
    ]);
  }

  /// Updates just the bedtime hour and minute.
  static Future<BedtimeModeConfig> setBedtime(TimeOfDay bedtime) async {
    final current = await loadConfig();
    final updated = current.copyWith(
      bedtimeHour: bedtime.hour,
      bedtimeMinute: bedtime.minute,
    );
    await saveConfig(updated);
    return updated;
  }

  /// Toggles bedtime mode on or off.
  static Future<BedtimeModeConfig> setEnabled(bool enabled) async {
    final current = await loadConfig();
    final updated = current.copyWith(isEnabled: enabled);
    await saveConfig(updated);
    return updated;
  }
}
