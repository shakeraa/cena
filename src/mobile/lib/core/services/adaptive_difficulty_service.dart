// =============================================================================
// Cena — Adaptive Difficulty / ZPD Targeting (MOB-CORE-005)
// + FSRS Spaced Repetition Client (MOB-AI-002)
// =============================================================================
// Blueprint Principle 2: Target P(correct) = 0.55-0.75
// Blueprint Principle 7: FSRS-based review scheduling
// =============================================================================

import 'dart:math';

import 'package:flutter_riverpod/flutter_riverpod.dart';

// ---------------------------------------------------------------------------
// Adaptive Difficulty — ZPD tracker
// ---------------------------------------------------------------------------

/// Tracks rolling accuracy and emits difficulty adjustment signals.
class AdaptiveDifficultyTracker {
  AdaptiveDifficultyTracker({
    this.targetAccuracyLow = 0.55,
    this.targetAccuracyHigh = 0.75,
    this.windowSize = 10,
  });

  final double targetAccuracyLow;
  final double targetAccuracyHigh;
  final int windowSize;
  final List<bool> _recentResults = [];

  /// Record an answer result.
  void recordAnswer(bool correct) {
    _recentResults.add(correct);
    if (_recentResults.length > windowSize) {
      _recentResults.removeAt(0);
    }
  }

  /// Rolling accuracy over the last [windowSize] answers.
  double get rollingAccuracy {
    if (_recentResults.isEmpty) return 0.5;
    final correct = _recentResults.where((r) => r).length;
    return correct / _recentResults.length;
  }

  /// Suggested difficulty adjustment: -1 (easier), 0 (stay), +1 (harder).
  int get difficultyAdjustment {
    if (_recentResults.length < 3) return 0; // Not enough data
    final acc = rollingAccuracy;
    if (acc > targetAccuracyHigh) return 1;  // Too easy → harder
    if (acc < targetAccuracyLow) return -1;  // Too hard → easier
    return 0; // In the zone
  }

  /// Whether the student is currently in the flow channel.
  bool get isInFlowZone {
    final acc = rollingAccuracy;
    return acc >= targetAccuracyLow && acc <= targetAccuracyHigh;
  }

  /// Human-readable difficulty label.
  String get difficultyLabel {
    switch (difficultyAdjustment) {
      case -1:
        return 'Easier questions recommended';
      case 1:
        return 'Harder questions recommended';
      default:
        return 'Difficulty well-matched';
    }
  }

  void reset() => _recentResults.clear();
}

final adaptiveDifficultyProvider = Provider<AdaptiveDifficultyTracker>(
  (ref) => AdaptiveDifficultyTracker(),
);

// ---------------------------------------------------------------------------
// FSRS — Free Spaced Repetition Scheduler (shadow mode)
// ---------------------------------------------------------------------------

/// Simplified FSRS card state. Full FSRS-5 implementation would run
/// server-side; this client tracks review timing locally for shadow-mode
/// comparison and offline scheduling hints.
class FsrsCard {
  FsrsCard({
    required this.conceptId,
    this.stability = 1.0,
    this.difficulty = 0.3,
    this.elapsedDays = 0,
    this.scheduledDays = 1,
    this.lastReview,
    this.reps = 0,
  });

  final String conceptId;
  double stability;
  double difficulty;
  int elapsedDays;
  int scheduledDays;
  DateTime? lastReview;
  int reps;

  /// Whether this card is due for review.
  bool get isDue {
    if (lastReview == null) return true;
    final daysSince = DateTime.now().difference(lastReview!).inDays;
    return daysSince >= scheduledDays;
  }

  /// Estimated retrievability [0, 1] — probability of recall.
  double get retrievability {
    if (lastReview == null) return 0.0;
    final daysSince = DateTime.now().difference(lastReview!).inDays;
    return pow(1 + daysSince / (9 * stability), -1).toDouble();
  }

  /// Update after a review. Rating: 1=Again, 2=Hard, 3=Good, 4=Easy.
  void review(int rating) {
    reps++;
    lastReview = DateTime.now();

    // Simplified FSRS-5 update equations
    final ratingFactor = [0.0, 0.0, 0.6, 1.0, 1.5][rating.clamp(0, 4)];
    difficulty = (difficulty + (0.1 - ratingFactor * 0.05)).clamp(0.0, 1.0);
    stability = stability * (1 + ratingFactor * (1.5 - difficulty));
    scheduledDays = max(1, (stability * 0.9 * (1 + ratingFactor)).round());
    elapsedDays = 0;
  }
}

/// Shadow-mode FSRS tracker — runs locally alongside the backend's SRS engine.
/// Compares client-predicted intervals with server-assigned intervals to
/// calibrate the model before going live.
class FsrsTracker {
  final Map<String, FsrsCard> _cards = {};

  /// Get or create a card for a concept.
  FsrsCard getCard(String conceptId) {
    return _cards.putIfAbsent(conceptId, () => FsrsCard(conceptId: conceptId));
  }

  /// Record a review for a concept.
  void recordReview(String conceptId, int rating) {
    getCard(conceptId).review(rating);
  }

  /// Get all cards due for review, sorted by lowest retrievability first.
  List<FsrsCard> getDueCards() {
    return _cards.values.where((c) => c.isDue).toList()
      ..sort((a, b) => a.retrievability.compareTo(b.retrievability));
  }

  /// Number of cards due today.
  int get dueCount => _cards.values.where((c) => c.isDue).length;
}

final fsrsTrackerProvider = Provider<FsrsTracker>(
  (ref) => FsrsTracker(),
);
