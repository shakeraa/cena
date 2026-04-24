// =============================================================================
// Cena — Accessibility Service (MOB-A11Y-001 + MOB-A11Y-002)
// =============================================================================
// Blueprint §11: Accessibility Accommodations
// - Dyslexia: OpenDyslexic font, increased line spacing, cream bg
// - ADHD: Timer opt-in, reduced animation, focus mode
// - Color-blind: Blue/orange palette (not red/green)
// - Motor: Enlarged touch targets (48dp min), swipe alternatives
//
// MOB-A11Y-002: Tablet & adaptive layout breakpoints
// =============================================================================

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:shared_preferences/shared_preferences.dart';

// ---------------------------------------------------------------------------
// Accessibility preferences state
// ---------------------------------------------------------------------------

class AccessibilityPrefs {
  const AccessibilityPrefs({
    this.useDyslexicFont = false,
    this.reducedMotion = false,
    this.enlargedTouchTargets = false,
    this.highContrast = false,
    this.colorBlindMode = false,
    this.lineSpacingMultiplier = 1.0,
    this.useCreamBackground = false,
  });

  final bool useDyslexicFont;
  final bool reducedMotion;
  final bool enlargedTouchTargets;
  final bool highContrast;
  final bool colorBlindMode;
  final double lineSpacingMultiplier;
  final bool useCreamBackground;

  /// Minimum touch target size (Material guideline: 48dp, enlarged: 56dp).
  double get minTouchTarget => enlargedTouchTargets ? 56.0 : 48.0;

  /// Animation duration multiplier (0 = no animation, 1 = normal).
  double get animationScale => reducedMotion ? 0.0 : 1.0;

  /// Font family override for dyslexia support.
  String? get fontFamilyOverride =>
      useDyslexicFont ? 'OpenDyslexic' : null;

  AccessibilityPrefs copyWith({
    bool? useDyslexicFont,
    bool? reducedMotion,
    bool? enlargedTouchTargets,
    bool? highContrast,
    bool? colorBlindMode,
    double? lineSpacingMultiplier,
    bool? useCreamBackground,
  }) {
    return AccessibilityPrefs(
      useDyslexicFont: useDyslexicFont ?? this.useDyslexicFont,
      reducedMotion: reducedMotion ?? this.reducedMotion,
      enlargedTouchTargets:
          enlargedTouchTargets ?? this.enlargedTouchTargets,
      highContrast: highContrast ?? this.highContrast,
      colorBlindMode: colorBlindMode ?? this.colorBlindMode,
      lineSpacingMultiplier:
          lineSpacingMultiplier ?? this.lineSpacingMultiplier,
      useCreamBackground: useCreamBackground ?? this.useCreamBackground,
    );
  }
}

// ---------------------------------------------------------------------------
// Notifier — persists to SharedPreferences
// ---------------------------------------------------------------------------

class AccessibilityNotifier extends StateNotifier<AccessibilityPrefs> {
  AccessibilityNotifier() : super(const AccessibilityPrefs()) {
    _load();
  }

  static const _prefix = 'a11y_';

  Future<void> _load() async {
    final prefs = await SharedPreferences.getInstance();
    state = AccessibilityPrefs(
      useDyslexicFont: prefs.getBool('${_prefix}dyslexic') ?? false,
      reducedMotion: prefs.getBool('${_prefix}reduced_motion') ?? false,
      enlargedTouchTargets:
          prefs.getBool('${_prefix}large_touch') ?? false,
      highContrast: prefs.getBool('${_prefix}high_contrast') ?? false,
      colorBlindMode: prefs.getBool('${_prefix}color_blind') ?? false,
      lineSpacingMultiplier:
          prefs.getDouble('${_prefix}line_spacing') ?? 1.0,
      useCreamBackground: prefs.getBool('${_prefix}cream_bg') ?? false,
    );
  }

  Future<void> _save() async {
    final prefs = await SharedPreferences.getInstance();
    await prefs.setBool('${_prefix}dyslexic', state.useDyslexicFont);
    await prefs.setBool('${_prefix}reduced_motion', state.reducedMotion);
    await prefs.setBool('${_prefix}large_touch', state.enlargedTouchTargets);
    await prefs.setBool('${_prefix}high_contrast', state.highContrast);
    await prefs.setBool('${_prefix}color_blind', state.colorBlindMode);
    await prefs.setDouble('${_prefix}line_spacing', state.lineSpacingMultiplier);
    await prefs.setBool('${_prefix}cream_bg', state.useCreamBackground);
  }

  void setDyslexicFont(bool value) {
    state = state.copyWith(useDyslexicFont: value);
    _save();
  }

  void setReducedMotion(bool value) {
    state = state.copyWith(reducedMotion: value);
    _save();
  }

  void setEnlargedTouchTargets(bool value) {
    state = state.copyWith(enlargedTouchTargets: value);
    _save();
  }

  void setHighContrast(bool value) {
    state = state.copyWith(highContrast: value);
    _save();
  }

  void setColorBlindMode(bool value) {
    state = state.copyWith(colorBlindMode: value);
    _save();
  }

  void setLineSpacing(double value) {
    state = state.copyWith(lineSpacingMultiplier: value);
    _save();
  }

  void setCreamBackground(bool value) {
    state = state.copyWith(useCreamBackground: value);
    _save();
  }
}

final accessibilityProvider =
    StateNotifierProvider<AccessibilityNotifier, AccessibilityPrefs>(
  (ref) => AccessibilityNotifier(),
);

// ---------------------------------------------------------------------------
// Adaptive layout breakpoints (MOB-A11Y-002)
// ---------------------------------------------------------------------------

/// Responsive breakpoints for phone / tablet / desktop.
enum DeviceType { phone, tablet, desktop }

/// Determine device type from screen width.
DeviceType deviceTypeFromWidth(double width) {
  if (width >= 1200) return DeviceType.desktop;
  if (width >= 600) return DeviceType.tablet;
  return DeviceType.phone;
}

/// Adaptive grid column count based on device type.
int adaptiveGridColumns(DeviceType device) {
  switch (device) {
    case DeviceType.phone:
      return 2;
    case DeviceType.tablet:
      return 3;
    case DeviceType.desktop:
      return 4;
  }
}

/// Widget that provides responsive layout based on screen width.
class AdaptiveLayout extends StatelessWidget {
  const AdaptiveLayout({
    super.key,
    required this.phone,
    this.tablet,
    this.desktop,
  });

  final Widget phone;
  final Widget? tablet;
  final Widget? desktop;

  @override
  Widget build(BuildContext context) {
    return LayoutBuilder(
      builder: (context, constraints) {
        final device = deviceTypeFromWidth(constraints.maxWidth);
        switch (device) {
          case DeviceType.desktop:
            return desktop ?? tablet ?? phone;
          case DeviceType.tablet:
            return tablet ?? phone;
          case DeviceType.phone:
            return phone;
        }
      },
    );
  }
}
