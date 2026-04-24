// =============================================================================
// Cena Adaptive Learning Platform — Progressive Disclosure Service (MOB-034)
// =============================================================================
//
// Four disclosure levels controlling what UI elements are visible:
//   Level 1 (Core)     : question text + answer options only (max 4-6 elements)
//   Level 2 (Context)  : hints, approach selector, methodology badge
//   Level 3 (Deep Dive): full solution, theory, related concepts — bottom sheet
//   Level 4 (Meta)     : analytics, progress, settings — separate screen
//
// DisclosureBudget enforces max 8 visible interactive elements per screen.
// =============================================================================

import 'package:flutter_riverpod/flutter_riverpod.dart';

// ---------------------------------------------------------------------------
// Disclosure Level
// ---------------------------------------------------------------------------

/// Progressive disclosure levels controlling information density.
enum DisclosureLevel {
  /// Core: question text + answer options. Max 4-6 interactive elements.
  core(1, 'Core', maxElements: 6),

  /// Context: hints, approach selector, methodology badge. On-demand reveal.
  context(2, 'Context', maxElements: 8),

  /// Deep Dive: full solution, theory, related concepts. Bottom sheet.
  deepDive(3, 'Deep Dive', maxElements: 12),

  /// Meta: analytics, progress, settings. Separate screen.
  meta(4, 'Meta', maxElements: 20);

  const DisclosureLevel(this.level, this.label, {required this.maxElements});

  final int level;
  final String label;
  final int maxElements;

  /// Whether this level includes the given [other] level.
  bool includes(DisclosureLevel other) => level >= other.level;
}

// ---------------------------------------------------------------------------
// Disclosure Budget
// ---------------------------------------------------------------------------

/// Enforces a maximum number of visible interactive elements per screen.
///
/// The budget tracks how many elements are currently shown and prevents
/// new elements from being added when the budget is exhausted.
class DisclosureBudget {
  DisclosureBudget({
    this.maxVisibleElements = 8,
  });

  /// Maximum number of visible interactive elements on screen.
  final int maxVisibleElements;

  /// Current count of visible interactive elements.
  int _currentCount = 0;

  /// Number of elements currently consuming budget.
  int get currentCount => _currentCount;

  /// Remaining budget.
  int get remaining => maxVisibleElements - _currentCount;

  /// Whether the budget is exhausted.
  bool get isExhausted => _currentCount >= maxVisibleElements;

  /// Attempt to allocate [count] elements. Returns true if successful.
  bool tryAllocate(int count) {
    if (_currentCount + count > maxVisibleElements) return false;
    _currentCount += count;
    return true;
  }

  /// Release [count] elements back into the budget.
  void release(int count) {
    _currentCount = (_currentCount - count).clamp(0, maxVisibleElements);
  }

  /// Reset the budget to zero allocated elements.
  void reset() {
    _currentCount = 0;
  }

  /// Create a budget for a specific disclosure level.
  factory DisclosureBudget.forLevel(DisclosureLevel level) {
    return DisclosureBudget(maxVisibleElements: level.maxElements);
  }
}

// ---------------------------------------------------------------------------
// Disclosure Level Config
// ---------------------------------------------------------------------------

/// Configuration that defines which UI elements are visible at each level.
class DisclosureLevelConfig {
  const DisclosureLevelConfig({
    required this.level,
    required this.showQuestionText,
    required this.showAnswerOptions,
    required this.showHintChip,
    required this.showApproachSelector,
    required this.showMethodologyBadge,
    required this.showFullSolution,
    required this.showRelatedConcepts,
    required this.showTheoryExplanation,
    required this.showAnalytics,
    required this.showProgressDetails,
    required this.showSettings,
  });

  final DisclosureLevel level;
  final bool showQuestionText;
  final bool showAnswerOptions;
  final bool showHintChip;
  final bool showApproachSelector;
  final bool showMethodologyBadge;
  final bool showFullSolution;
  final bool showRelatedConcepts;
  final bool showTheoryExplanation;
  final bool showAnalytics;
  final bool showProgressDetails;
  final bool showSettings;

  /// Core level: question text + answer options only.
  static const core = DisclosureLevelConfig(
    level: DisclosureLevel.core,
    showQuestionText: true,
    showAnswerOptions: true,
    showHintChip: false,
    showApproachSelector: false,
    showMethodologyBadge: false,
    showFullSolution: false,
    showRelatedConcepts: false,
    showTheoryExplanation: false,
    showAnalytics: false,
    showProgressDetails: false,
    showSettings: false,
  );

  /// Context level: adds hints, approach selector, methodology badge.
  static const context = DisclosureLevelConfig(
    level: DisclosureLevel.context,
    showQuestionText: true,
    showAnswerOptions: true,
    showHintChip: true,
    showApproachSelector: true,
    showMethodologyBadge: true,
    showFullSolution: false,
    showRelatedConcepts: false,
    showTheoryExplanation: false,
    showAnalytics: false,
    showProgressDetails: false,
    showSettings: false,
  );

  /// Deep Dive level: adds full solution, related concepts, theory.
  static const deepDive = DisclosureLevelConfig(
    level: DisclosureLevel.deepDive,
    showQuestionText: true,
    showAnswerOptions: true,
    showHintChip: true,
    showApproachSelector: true,
    showMethodologyBadge: true,
    showFullSolution: true,
    showRelatedConcepts: true,
    showTheoryExplanation: true,
    showAnalytics: false,
    showProgressDetails: false,
    showSettings: false,
  );

  /// Meta level: everything including analytics and settings.
  static const meta = DisclosureLevelConfig(
    level: DisclosureLevel.meta,
    showQuestionText: true,
    showAnswerOptions: true,
    showHintChip: true,
    showApproachSelector: true,
    showMethodologyBadge: true,
    showFullSolution: true,
    showRelatedConcepts: true,
    showTheoryExplanation: true,
    showAnalytics: true,
    showProgressDetails: true,
    showSettings: true,
  );

  /// Get the config for a given level.
  static DisclosureLevelConfig forLevel(DisclosureLevel level) {
    switch (level) {
      case DisclosureLevel.core:
        return core;
      case DisclosureLevel.context:
        return context;
      case DisclosureLevel.deepDive:
        return deepDive;
      case DisclosureLevel.meta:
        return meta;
    }
  }
}

// ---------------------------------------------------------------------------
// Disclosure Level Notifier
// ---------------------------------------------------------------------------

/// Manages the current disclosure level for the session screen.
class DisclosureLevelNotifier extends StateNotifier<DisclosureLevel> {
  DisclosureLevelNotifier() : super(DisclosureLevel.core);

  /// Elevate to the next disclosure level.
  void elevate() {
    final nextIndex = state.index + 1;
    if (nextIndex < DisclosureLevel.values.length) {
      state = DisclosureLevel.values[nextIndex];
    }
  }

  /// Set a specific disclosure level.
  void setLevel(DisclosureLevel level) {
    state = level;
  }

  /// Reset to core level.
  void reset() {
    state = DisclosureLevel.core;
  }
}

// ---------------------------------------------------------------------------
// Providers
// ---------------------------------------------------------------------------

/// The current disclosure level for the active session.
final disclosureLevelProvider =
    StateNotifierProvider<DisclosureLevelNotifier, DisclosureLevel>(
  (ref) => DisclosureLevelNotifier(),
);

/// The disclosure config derived from the current level.
final disclosureConfigProvider = Provider<DisclosureLevelConfig>((ref) {
  final level = ref.watch(disclosureLevelProvider);
  return DisclosureLevelConfig.forLevel(level);
});

/// The disclosure budget for the current level.
final disclosureBudgetProvider = Provider<DisclosureBudget>((ref) {
  final level = ref.watch(disclosureLevelProvider);
  return DisclosureBudget.forLevel(level);
});
