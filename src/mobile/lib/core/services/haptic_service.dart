// =============================================================================
// Cena Adaptive Learning Platform — Haptic Feedback Service (MOB-056)
// =============================================================================
//
// Centralized haptic feedback with named patterns mapped to Flutter's
// HapticFeedback API. Platform-adaptive: iOS uses Taptic Engine, Android
// uses standard vibration API. Respects system accessibility settings and
// the user's enabled toggle.
// =============================================================================

import 'dart:io';

import 'package:flutter/foundation.dart';
import 'package:flutter/services.dart';

/// Named haptic patterns used throughout the app.
enum HapticPattern {
  /// Correct answer — strong confirmation.
  correctAnswer,

  /// Wrong answer — gentle nudge.
  wrongAnswer,

  /// Any button press — subtle click.
  buttonPress,

  /// Swipe/drag gesture completed.
  swipeComplete,

  /// Badge unlocked — celebratory triple pulse.
  badgeUnlock,

  /// Level up — extended heavy pattern.
  levelUp,

  /// Tab/screen navigation — light click.
  navigation,
}

/// Centralized haptic feedback service.
///
/// Maps [HapticPattern]s to platform-appropriate vibration calls.
/// All methods are no-ops when haptics are disabled or on unsupported
/// platforms (web).
class CenaHaptics {
  CenaHaptics({this.enabled = true});

  /// Whether haptic feedback is enabled. Controlled by user preferences.
  bool enabled;

  /// The last pattern that was played, for testing verification.
  HapticPattern? lastPlayed;

  /// Play a haptic feedback [pattern].
  ///
  /// Does nothing when [enabled] is false or running on web.
  Future<void> play(HapticPattern pattern) async {
    if (!enabled || kIsWeb) return;

    lastPlayed = pattern;

    switch (pattern) {
      case HapticPattern.correctAnswer:
        await HapticFeedback.heavyImpact();

      case HapticPattern.wrongAnswer:
        await HapticFeedback.lightImpact();

      case HapticPattern.buttonPress:
        await HapticFeedback.selectionClick();

      case HapticPattern.swipeComplete:
        await HapticFeedback.mediumImpact();

      case HapticPattern.badgeUnlock:
        await _badgeUnlockPattern();

      case HapticPattern.levelUp:
        await _levelUpPattern();

      case HapticPattern.navigation:
        await HapticFeedback.selectionClick();
    }
  }

  /// Badge unlock: heavy-pause-heavy-pause-heavy triple pulse.
  ///
  /// Creates a celebratory rhythmic pattern. The 100ms pauses are
  /// short enough to feel connected but distinct.
  Future<void> _badgeUnlockPattern() async {
    await HapticFeedback.heavyImpact();
    await Future<void>.delayed(const Duration(milliseconds: 100));
    await HapticFeedback.heavyImpact();
    await Future<void>.delayed(const Duration(milliseconds: 100));
    await HapticFeedback.heavyImpact();
  }

  /// Level up: extended heavy pattern with escalating pauses.
  ///
  /// Four pulses with decreasing intervals for an accelerating feel:
  /// heavy(120ms)heavy(80ms)heavy(50ms)heavy
  Future<void> _levelUpPattern() async {
    await HapticFeedback.heavyImpact();
    await Future<void>.delayed(const Duration(milliseconds: 120));
    await HapticFeedback.heavyImpact();
    await Future<void>.delayed(const Duration(milliseconds: 80));
    await HapticFeedback.heavyImpact();
    await Future<void>.delayed(const Duration(milliseconds: 50));
    await HapticFeedback.heavyImpact();
  }

  /// Whether the current platform supports haptic feedback.
  ///
  /// Returns true for iOS and Android, false for web and desktop.
  static bool get platformSupportsHaptics {
    if (kIsWeb) return false;
    return Platform.isIOS || Platform.isAndroid;
  }
}
