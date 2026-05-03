// =============================================================================
// Cena Adaptive Learning Platform — Session Flow Arc
// Divides a learning session into warm-up, core, and cool-down phases
// with difficulty targeting based on P(correct) probability ranges.
// =============================================================================

import 'package:flutter/material.dart';

// ---------------------------------------------------------------------------
// Session Phase Enum
// ---------------------------------------------------------------------------

/// The three phases of a learning session flow arc.
///
/// Each phase has a distinct pedagogical purpose:
///   - [warmUp]: Easy review to build confidence and recall context.
///   - [core]: Progressive difficulty toward the Zone of Proximal Development.
///   - [coolDown]: Wind down with easy items; always end on success.
enum SessionPhase {
  warmUp,
  core,
  coolDown,
}

// ---------------------------------------------------------------------------
// Phase Color Temperatures
// ---------------------------------------------------------------------------

/// Color tokens for each session phase.
///
/// These are used by [PhaseIndicator] to subtly shift the progress bar
/// color, signaling phase transitions without explicit labels.
abstract class PhaseColors {
  /// Warm-up: cool blue — calm, low-stakes review.
  static const Color warmUp = Color(0xFF42A5F5);

  /// Core: amber — focused, productive challenge.
  static const Color core = Color(0xFFFFB300);

  /// Cool-down: green — success, consolidation, positive ending.
  static const Color coolDown = Color(0xFF66BB6A);

  /// Returns the color for a given [phase].
  static Color forPhase(SessionPhase phase) {
    switch (phase) {
      case SessionPhase.warmUp:
        return warmUp;
      case SessionPhase.core:
        return core;
      case SessionPhase.coolDown:
        return coolDown;
    }
  }
}

// ---------------------------------------------------------------------------
// Session Phase Transition Event
// ---------------------------------------------------------------------------

/// Data class emitted when the session transitions between phases.
///
/// Used by analytics and the session notifier to track phase changes
/// without coupling the flow arc logic to the event bus.
class SessionPhaseTransition {
  const SessionPhaseTransition({
    required this.phase,
    required this.questionIndex,
    required this.focusLevel,
  });

  /// The phase being entered.
  final SessionPhase phase;

  /// The question index (0-based) at which this transition occurs.
  final int questionIndex;

  /// Focus level estimate [0.0, 1.0] at the time of transition.
  /// Derived from consecutive correct answers and response time trends.
  final double focusLevel;

  @override
  String toString() =>
      'SessionPhaseTransition(phase: $phase, questionIndex: $questionIndex, '
      'focusLevel: ${focusLevel.toStringAsFixed(2)})';

  @override
  bool operator ==(Object other) =>
      identical(this, other) ||
      other is SessionPhaseTransition &&
          phase == other.phase &&
          questionIndex == other.questionIndex &&
          focusLevel == other.focusLevel;

  @override
  int get hashCode => Object.hash(phase, questionIndex, focusLevel);
}

// ---------------------------------------------------------------------------
// Session Flow Arc
// ---------------------------------------------------------------------------

/// Manages the three-phase flow arc for a learning session.
///
/// Divides a session of [totalQuestions] into:
///   1. **Warm-up** (first [warmUpCount] questions):
///      Target P(correct) = 0.80-0.90. Easy review items to build confidence
///      and activate prior knowledge.
///
///   2. **Core** (middle questions):
///      Progressive difficulty from P(correct) 0.70 down to 0.60.
///      This is the Zone of Proximal Development where real learning happens.
///
///   3. **Cool-down** (last [coolDownCount] questions):
///      Easy review items. The session must always end on a success to maintain
///      motivation and self-efficacy.
///
/// The arc is invisible to the student — no "Phase 2" labels appear. Instead,
/// the [PhaseIndicator] widget subtly shifts the progress bar color.
class SessionFlowArc {
  SessionFlowArc({
    required this.totalQuestions,
    this.warmUpCount = 3,
    this.coolDownCount = 2,
  })  : assert(totalQuestions >= 5,
            'Session must have at least 5 questions for a meaningful flow arc'),
        assert(warmUpCount >= 2, 'Warm-up needs at least 2 questions'),
        assert(coolDownCount >= 2, 'Cool-down needs at least 2 questions');

  /// Total number of questions planned for this session.
  final int totalQuestions;

  /// Number of warm-up questions (default: 3).
  final int warmUpCount;

  /// Number of cool-down questions (default: 2).
  final int coolDownCount;

  /// Tracks the last wrong answer index for recovery question logic.
  int? _lastWrongAnswerIndex;

  /// Index where the core phase begins.
  int get _coreStart => warmUpCount;

  /// Index where the cool-down phase begins.
  int get _coolDownStart => totalQuestions - coolDownCount;

  /// Number of questions in the core phase.
  int get coreCount => _coolDownStart - _coreStart;

  // ---- Phase resolution ----

  /// Returns the [SessionPhase] for the given [questionIndex] (0-based).
  SessionPhase phaseAt(int questionIndex) {
    if (questionIndex < 0) return SessionPhase.warmUp;
    if (questionIndex < _coreStart) return SessionPhase.warmUp;
    if (questionIndex < _coolDownStart) return SessionPhase.core;
    return SessionPhase.coolDown;
  }

  /// Returns the color for the given question index based on its phase.
  Color colorAt(int questionIndex) {
    return PhaseColors.forPhase(phaseAt(questionIndex));
  }

  // ---- Difficulty targeting ----

  /// Returns the target P(correct) for the given [questionIndex].
  ///
  /// - Warm-up: 0.80 - 0.90 (easy review)
  /// - Core: linear ramp from 0.70 down to 0.60
  /// - Cool-down: 0.85 (easy, always end on success)
  double targetDifficultyAt(int questionIndex) {
    final phase = phaseAt(questionIndex);
    switch (phase) {
      case SessionPhase.warmUp:
        // Ramp from 0.90 (very easy) to 0.80 (still easy) across warm-up
        if (warmUpCount <= 1) return 0.85;
        final progress = questionIndex / (warmUpCount - 1);
        return 0.90 - (0.10 * progress); // 0.90 -> 0.80
      case SessionPhase.core:
        // Progressive difficulty from P(correct) 0.70 -> 0.60
        final coreIndex = questionIndex - _coreStart;
        if (coreCount <= 1) return 0.65;
        final progress = coreIndex / (coreCount - 1);
        return 0.70 - (0.10 * progress); // 0.70 -> 0.60
      case SessionPhase.coolDown:
        return 0.85; // Easy review — end on success
    }
  }

  // ---- Recovery question logic ----

  /// Call this when the student answers incorrectly.
  void recordWrongAnswer(int questionIndex) {
    _lastWrongAnswerIndex = questionIndex;
  }

  /// Call this when the student answers correctly.
  void recordCorrectAnswer() {
    _lastWrongAnswerIndex = null;
  }

  /// Whether a recovery question should be injected.
  ///
  /// In cool-down phase, if the student just got a question wrong,
  /// inject an easier recovery question so the session still ends
  /// on a positive note.
  bool get shouldAddRecoveryQuestion {
    if (_lastWrongAnswerIndex == null) return false;
    final phase = phaseAt(_lastWrongAnswerIndex!);
    return phase == SessionPhase.coolDown;
  }

  /// The target P(correct) for a recovery question (very easy).
  double get recoveryDifficulty => 0.92;

  // ---- Phase transition detection ----

  /// Returns a [SessionPhaseTransition] if the question at [questionIndex]
  /// enters a new phase compared to the previous question, or null otherwise.
  ///
  /// [focusLevel] is the current focus estimate (e.g. from consecutive correct
  /// streak divided by flow threshold).
  SessionPhaseTransition? checkTransition(
    int questionIndex, {
    double focusLevel = 0.5,
  }) {
    if (questionIndex <= 0) {
      // First question always starts in warm-up — emit initial transition.
      return SessionPhaseTransition(
        phase: SessionPhase.warmUp,
        questionIndex: 0,
        focusLevel: focusLevel,
      );
    }
    final currentPhase = phaseAt(questionIndex);
    final previousPhase = phaseAt(questionIndex - 1);
    if (currentPhase != previousPhase) {
      return SessionPhaseTransition(
        phase: currentPhase,
        questionIndex: questionIndex,
        focusLevel: focusLevel,
      );
    }
    return null;
  }

  /// Returns the session progress as a fraction [0.0, 1.0] at [questionIndex].
  double progressAt(int questionIndex) {
    if (totalQuestions <= 0) return 0.0;
    return ((questionIndex + 1) / totalQuestions).clamp(0.0, 1.0);
  }
}
