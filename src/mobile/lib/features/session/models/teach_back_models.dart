// =============================================================================
// Cena Adaptive Learning Platform — Teach-Back Models (MOB-048)
// =============================================================================
// Data models for the teach-back feature where students explain mastered
// concepts in their own words for deeper learning and XP bonus.
// =============================================================================

// ---------------------------------------------------------------------------
// Configuration
// ---------------------------------------------------------------------------

/// Configuration constants for the teach-back feature.
class TeachBackConfig {
  const TeachBackConfig({
    this.maxPerSession = 2,
    this.minMasteryThreshold = 0.85,
    this.xpMultiplier = 2.5,
    this.minWordCount = 10,
    this.maxCharacters = 500,
  });

  /// Maximum teach-back prompts per session (to avoid fatigue).
  final int maxPerSession;

  /// Minimum P(known) required before prompting teach-back.
  final double minMasteryThreshold;

  /// XP multiplier awarded for completing a teach-back.
  final double xpMultiplier;

  /// Minimum word count for a valid explanation.
  final int minWordCount;

  /// Maximum characters allowed in the explanation.
  final int maxCharacters;

  /// Default singleton configuration.
  static const defaultConfig = TeachBackConfig();
}

// ---------------------------------------------------------------------------
// Submission
// ---------------------------------------------------------------------------

/// A student's teach-back explanation submitted to the backend.
class TeachBackSubmission {
  const TeachBackSubmission({
    required this.studentId,
    required this.conceptId,
    required this.explanationText,
    required this.wordCount,
    required this.timestamp,
    this.sessionId,
    this.usedVoiceInput = false,
  });

  final String studentId;
  final String conceptId;
  final String explanationText;
  final int wordCount;
  final DateTime timestamp;
  final String? sessionId;

  /// Whether the student used voice-to-text input.
  final bool usedVoiceInput;

  factory TeachBackSubmission.fromJson(Map<String, dynamic> json) {
    return TeachBackSubmission(
      studentId: json['studentId'] as String? ?? '',
      conceptId: json['conceptId'] as String? ?? '',
      explanationText: json['explanationText'] as String? ?? '',
      wordCount: (json['wordCount'] as num?)?.toInt() ?? 0,
      timestamp: json['timestamp'] != null
          ? DateTime.parse(json['timestamp'] as String)
          : DateTime.now(),
      sessionId: json['sessionId'] as String?,
      usedVoiceInput: json['usedVoiceInput'] as bool? ?? false,
    );
  }

  Map<String, dynamic> toJson() => {
        'studentId': studentId,
        'conceptId': conceptId,
        'explanationText': explanationText,
        'wordCount': wordCount,
        'timestamp': timestamp.toIso8601String(),
        if (sessionId != null) 'sessionId': sessionId,
        'usedVoiceInput': usedVoiceInput,
      };
}

// ---------------------------------------------------------------------------
// Evaluation
// ---------------------------------------------------------------------------

/// Server-side evaluation result for a teach-back submission.
class TeachBackEvaluation {
  const TeachBackEvaluation({
    required this.completenessScore,
    required this.accuracyScore,
    required this.clarityScore,
    required this.feedback,
    required this.xpAwarded,
  });

  /// How completely the concept was covered [0.0, 1.0].
  final double completenessScore;

  /// Factual accuracy of the explanation [0.0, 1.0].
  final double accuracyScore;

  /// Clarity and readability of the explanation [0.0, 1.0].
  final double clarityScore;

  /// Human-readable feedback from the evaluator.
  final String feedback;

  /// Total XP awarded for this teach-back.
  final int xpAwarded;

  /// Overall quality score (average of all dimensions).
  double get overallScore =>
      (completenessScore + accuracyScore + clarityScore) / 3.0;

  factory TeachBackEvaluation.fromJson(Map<String, dynamic> json) {
    return TeachBackEvaluation(
      completenessScore:
          (json['completenessScore'] as num?)?.toDouble() ?? 0.0,
      accuracyScore: (json['accuracyScore'] as num?)?.toDouble() ?? 0.0,
      clarityScore: (json['clarityScore'] as num?)?.toDouble() ?? 0.0,
      feedback: json['feedback'] as String? ?? '',
      xpAwarded: (json['xpAwarded'] as num?)?.toInt() ?? 0,
    );
  }

  Map<String, dynamic> toJson() => {
        'completenessScore': completenessScore,
        'accuracyScore': accuracyScore,
        'clarityScore': clarityScore,
        'feedback': feedback,
        'xpAwarded': xpAwarded,
      };
}

// ---------------------------------------------------------------------------
// State tracking
// ---------------------------------------------------------------------------

/// Tracks the in-progress state of a teach-back prompt within a session.
class TeachBackState {
  const TeachBackState({
    required this.conceptId,
    this.explanation = '',
    this.wordCount = 0,
    this.isSubmitted = false,
    this.isSkipped = false,
    this.evaluation,
  });

  final String conceptId;
  final String explanation;
  final int wordCount;
  final bool isSubmitted;
  final bool isSkipped;
  final TeachBackEvaluation? evaluation;

  /// Whether the teach-back is complete (submitted or skipped).
  bool get isComplete => isSubmitted || isSkipped;

  TeachBackState copyWith({
    String? explanation,
    int? wordCount,
    bool? isSubmitted,
    bool? isSkipped,
    TeachBackEvaluation? evaluation,
  }) {
    return TeachBackState(
      conceptId: conceptId,
      explanation: explanation ?? this.explanation,
      wordCount: wordCount ?? this.wordCount,
      isSubmitted: isSubmitted ?? this.isSubmitted,
      isSkipped: isSkipped ?? this.isSkipped,
      evaluation: evaluation ?? this.evaluation,
    );
  }
}
