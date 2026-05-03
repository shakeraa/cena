// =============================================================================
// Cena Adaptive Learning Platform — Interaction Feedback Service
// =============================================================================
//
// Centralized haptic + sound mapping.
// Sounds are OFF by default. Haptics are ON by default.
// =============================================================================

import 'dart:async';

import 'package:flutter/services.dart';
import 'package:shared_preferences/shared_preferences.dart';

const String _kSoundsEnabled = 'interaction_feedback_sounds_enabled';
const String _kHapticsEnabled = 'interaction_feedback_haptics_enabled';

class InteractionFeedbackService {
  const InteractionFeedbackService._();

  static bool? _cachedSoundsEnabled;
  static bool? _cachedHapticsEnabled;

  static Future<void> preload() async {
    final prefs = await SharedPreferences.getInstance();
    _cachedSoundsEnabled = prefs.getBool(_kSoundsEnabled) ?? false;
    _cachedHapticsEnabled = prefs.getBool(_kHapticsEnabled) ?? true;
  }

  static Future<bool> soundsEnabled() async {
    if (_cachedSoundsEnabled != null) return _cachedSoundsEnabled!;
    final prefs = await SharedPreferences.getInstance();
    _cachedSoundsEnabled = prefs.getBool(_kSoundsEnabled) ?? false;
    return _cachedSoundsEnabled!;
  }

  static Future<bool> hapticsEnabled() async {
    if (_cachedHapticsEnabled != null) return _cachedHapticsEnabled!;
    final prefs = await SharedPreferences.getInstance();
    _cachedHapticsEnabled = prefs.getBool(_kHapticsEnabled) ?? true;
    return _cachedHapticsEnabled!;
  }

  static Future<void> setSoundsEnabled(bool enabled) async {
    final prefs = await SharedPreferences.getInstance();
    _cachedSoundsEnabled = enabled;
    await prefs.setBool(_kSoundsEnabled, enabled);
  }

  static Future<void> setHapticsEnabled(bool enabled) async {
    final prefs = await SharedPreferences.getInstance();
    _cachedHapticsEnabled = enabled;
    await prefs.setBool(_kHapticsEnabled, enabled);
  }

  /// MCQ option tap -> selectionClick().
  static Future<void> selectionClick() async {
    if (await hapticsEnabled()) {
      await HapticFeedback.selectionClick();
    }
    if (await soundsEnabled()) {
      await SystemSound.play(SystemSoundType.click);
    }
  }

  /// Submit action -> mediumImpact().
  static Future<void> submitTap() async {
    if (await hapticsEnabled()) {
      await HapticFeedback.mediumImpact();
    }
    if (await soundsEnabled()) {
      await SystemSound.play(SystemSoundType.click);
    }
  }

  /// Correct answer -> heavyImpact().
  static Future<void> correctAnswer() async {
    if (await hapticsEnabled()) {
      await HapticFeedback.heavyImpact();
    }
    if (await soundsEnabled()) {
      await SystemSound.play(SystemSoundType.alert);
    }
  }

  /// Incorrect answer -> light impact.
  static Future<void> incorrectAnswer() async {
    if (await hapticsEnabled()) {
      await HapticFeedback.lightImpact();
    }
    if (await soundsEnabled()) {
      await SystemSound.play(SystemSoundType.click);
    }
  }

  /// Level-up -> heavyImpact() x2 with 200ms gap.
  static Future<void> levelUp() async {
    if (await hapticsEnabled()) {
      await HapticFeedback.heavyImpact();
      await Future<void>.delayed(const Duration(milliseconds: 200));
      await HapticFeedback.heavyImpact();
    }
    if (await soundsEnabled()) {
      await SystemSound.play(SystemSoundType.alert);
    }
  }
}
