// =============================================================================
// Cena Adaptive Learning Platform — Confidence Calibration Tracker (MOB-047)
// =============================================================================
//
// Tracks the relationship between student's self-reported confidence and
// actual correctness to detect overconfidence or underconfidence patterns.
//
// Overconfident students answer incorrectly despite high confidence — they
// need metacognitive scaffolding ("try explaining your reasoning").
//
// Underconfident students answer correctly despite low confidence — they
// need encouragement and growth-mindset messaging.
//
// Rolling window of 50 calibration points ensures the signal is recent
// and responsive to changes in the student's self-awareness.
// =============================================================================

import 'package:flutter_riverpod/flutter_riverpod.dart';

// ---------------------------------------------------------------------------
// CalibrationPoint
// ---------------------------------------------------------------------------

/// A single data point relating self-reported confidence to actual outcome.
class CalibrationPoint {
  const CalibrationPoint({
    required this.confidence,
    required this.correct,
    required this.timestamp,
  });

  /// Self-reported confidence level.
  /// For younger students: 1-3 (Guess/Think so/Sure).
  /// For older students: 1-5 slider value.
  /// Normalized to [0.0, 1.0] internally by the tracker.
  final int confidence;

  /// Whether the student actually answered correctly.
  final bool correct;

  /// When this calibration was recorded.
  final DateTime timestamp;
}

// ---------------------------------------------------------------------------
// CalibrationBucket
// ---------------------------------------------------------------------------

/// Aggregated accuracy data for a single confidence level.
/// Used for graphing calibration curves.
class CalibrationBucket {
  const CalibrationBucket({
    required this.confidenceLevel,
    required this.actualAccuracy,
    required this.sampleCount,
  });

  /// The confidence level this bucket represents (1-5 normalized).
  final int confidenceLevel;

  /// Actual accuracy rate [0.0, 1.0] for questions at this confidence level.
  final double actualAccuracy;

  /// Number of data points in this bucket.
  final int sampleCount;
}

// ---------------------------------------------------------------------------
// CalibrationTracker
// ---------------------------------------------------------------------------

/// Compares student self-reported confidence against actual correctness.
///
/// Maintains a rolling window of the last [windowSize] calibration points
/// and computes overconfidence/underconfidence scores.
class CalibrationTracker {
  CalibrationTracker({this.windowSize = 50});

  /// Maximum number of recent calibration points to retain.
  final int windowSize;

  /// The rolling window of calibration data.
  final List<CalibrationPoint> _points = [];

  /// The maximum confidence scale value.
  /// Set to 5 for older students, 3 for younger students.
  /// Defaults to 5; callers should set this based on the student's age tier.
  int maxScale = 5;

  /// All current calibration points (read-only).
  List<CalibrationPoint> get points => List.unmodifiable(_points);

  /// Total number of calibration points recorded.
  int get totalPoints => _points.length;

  // ---- Recording ----

  /// Record a new calibration data point.
  ///
  /// [confidence]: the student's self-reported confidence (1-maxScale).
  /// [correct]: whether they actually answered correctly.
  void record({required int confidence, required bool correct}) {
    _points.add(CalibrationPoint(
      confidence: confidence.clamp(1, maxScale),
      correct: correct,
      timestamp: DateTime.now(),
    ));

    // Trim the window if it exceeds the limit.
    while (_points.length > windowSize) {
      _points.removeAt(0);
    }
  }

  // ---- Overconfidence Score ----

  /// Average normalized confidence when the student was WRONG.
  ///
  /// High values (> 0.6) indicate the student is confident in wrong answers
  /// and needs metacognitive support ("explain your reasoning", "try again
  /// without hints").
  ///
  /// Range: [0.0, 1.0]. Returns 0.0 if no wrong answers in the window.
  double get overconfidenceScore {
    final wrongPoints = _points.where((p) => !p.correct).toList();
    if (wrongPoints.isEmpty) return 0.0;

    // Average of normalized confidence values for wrong answers.
    final totalNormalized = wrongPoints.fold<double>(
      0.0,
      (sum, p) => sum + _normalize(p.confidence),
    );
    return totalNormalized / wrongPoints.length;
  }

  // ---- Underconfidence Score ----

  /// Average normalized LACK of confidence when the student was RIGHT.
  ///
  /// Computed as: 1 - (average confidence when correct).
  /// High values (> 0.5) indicate the student doesn't trust their own
  /// knowledge and needs encouragement/growth-mindset messaging.
  ///
  /// Range: [0.0, 1.0]. Returns 0.0 if no correct answers in the window.
  double get underconfidenceScore {
    final correctPoints = _points.where((p) => p.correct).toList();
    if (correctPoints.isEmpty) return 0.0;

    final avgConfidence = correctPoints.fold<double>(
      0.0,
      (sum, p) => sum + _normalize(p.confidence),
    ) / correctPoints.length;

    // Underconfidence = how far below max confidence they rate themselves
    // when they actually know the answer.
    return 1.0 - avgConfidence;
  }

  // ---- Alert Thresholds ----

  /// Whether the student shows a problematic overconfidence pattern.
  ///
  /// Triggers when: overconfidence score > 0.6 AND at least 5 wrong answers
  /// in the window (to avoid false positives with small samples).
  bool get isOverconfident {
    final wrongCount = _points.where((p) => !p.correct).length;
    return wrongCount >= 5 && overconfidenceScore > 0.6;
  }

  /// Whether the student shows a problematic underconfidence pattern.
  ///
  /// Triggers when: underconfidence score > 0.5 AND at least 5 correct
  /// answers in the window.
  bool get isUnderconfident {
    final correctCount = _points.where((p) => p.correct).length;
    return correctCount >= 5 && underconfidenceScore > 0.5;
  }

  // ---- Calibration Curve Data ----

  /// Returns (confidenceLevel, actualAccuracy) pairs for graphing.
  ///
  /// Groups all calibration points by their confidence level and computes
  /// the actual accuracy rate for each level. A perfectly calibrated student
  /// would have points along the diagonal (confidence == accuracy).
  List<CalibrationBucket> getCalibrationData() {
    if (_points.isEmpty) return [];

    // Group points by confidence level.
    final Map<int, List<CalibrationPoint>> buckets = {};
    for (final point in _points) {
      buckets.putIfAbsent(point.confidence, () => []).add(point);
    }

    // Compute accuracy for each confidence level.
    final result = <CalibrationBucket>[];
    for (int level = 1; level <= maxScale; level++) {
      final bucket = buckets[level];
      if (bucket == null || bucket.isEmpty) continue;

      final correctCount = bucket.where((p) => p.correct).length;
      final accuracy = correctCount / bucket.length;

      result.add(CalibrationBucket(
        confidenceLevel: level,
        actualAccuracy: accuracy,
        sampleCount: bucket.length,
      ));
    }

    return result;
  }

  /// The overall accuracy across all calibration points.
  double get overallAccuracy {
    if (_points.isEmpty) return 0.0;
    final correct = _points.where((p) => p.correct).length;
    return correct / _points.length;
  }

  /// The overall average confidence (normalized to [0.0, 1.0]).
  double get averageConfidence {
    if (_points.isEmpty) return 0.0;
    final total = _points.fold<double>(
      0.0,
      (sum, p) => sum + _normalize(p.confidence),
    );
    return total / _points.length;
  }

  /// The calibration error: absolute difference between average confidence
  /// and actual accuracy. Lower is better. 0.0 = perfectly calibrated.
  double get calibrationError {
    return (averageConfidence - overallAccuracy).abs();
  }

  /// Reset all tracked data.
  void reset() => _points.clear();

  // ---- Private helpers ----

  /// Normalize a confidence level to [0.0, 1.0].
  double _normalize(int confidence) {
    if (maxScale <= 1) return 1.0;
    return (confidence - 1) / (maxScale - 1);
  }
}

// ---------------------------------------------------------------------------
// Riverpod Provider
// ---------------------------------------------------------------------------

/// Canonical calibration tracker instance.
///
/// Persists for the lifetime of the app. In production, this would be
/// hydrated from local storage / server sync on session restore.
final calibrationTrackerProvider = Provider<CalibrationTracker>((ref) {
  return CalibrationTracker();
});

/// Whether the student is currently showing overconfidence.
final isOverconfidentProvider = Provider<bool>((ref) {
  final tracker = ref.watch(calibrationTrackerProvider);
  return tracker.isOverconfident;
});

/// Whether the student is currently showing underconfidence.
final isUnderconfidentProvider = Provider<bool>((ref) {
  final tracker = ref.watch(calibrationTrackerProvider);
  return tracker.isUnderconfident;
});
