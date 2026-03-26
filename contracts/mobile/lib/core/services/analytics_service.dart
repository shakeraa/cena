// =============================================================================
// Cena Adaptive Learning Platform — Client-Side Analytics Service Contract
// Privacy-safe analytics with offline batching and performance tracking.
// =============================================================================

import 'dart:async';

import '../models/domain_models.dart';

// ---------------------------------------------------------------------------
// Analytics Event Types
// ---------------------------------------------------------------------------

/// Base class for all analytics events.
///
/// All events are timestamped and include a hashed student identifier.
/// NO plaintext PII is ever stored or transmitted in analytics.
abstract class AnalyticsEvent {
  /// Event type discriminator for the analytics backend.
  String get eventType;

  /// Client timestamp of the event.
  DateTime get timestamp;

  /// SHA-256 hashed student ID (never plaintext).
  String get hashedStudentId;

  /// Serialize to a JSON-safe map for batched upload.
  Map<String, dynamic> toJson();
}

// ---------------------------------------------------------------------------
// Session Events
// ---------------------------------------------------------------------------

/// Tracks when a learning session starts.
class SessionStartEvent implements AnalyticsEvent {
  const SessionStartEvent({
    required this.timestamp,
    required this.hashedStudentId,
    required this.sessionId,
    required this.subject,
    required this.targetDurationMinutes,
    required this.methodology,
    required this.experimentCohort,
  });

  @override
  String get eventType => 'session_start';
  @override
  final DateTime timestamp;
  @override
  final String hashedStudentId;

  final String sessionId;
  final Subject? subject;
  final int targetDurationMinutes;
  final Methodology methodology;
  final ExperimentCohort experimentCohort;

  @override
  Map<String, dynamic> toJson() => {
        'event_type': eventType,
        'timestamp': timestamp.toIso8601String(),
        'hashed_student_id': hashedStudentId,
        'session_id': sessionId,
        'subject': subject?.name,
        'target_duration_minutes': targetDurationMinutes,
        'methodology': methodology.name,
        'experiment_cohort': experimentCohort.name,
      };
}

/// Tracks when a learning session ends.
class SessionEndEvent implements AnalyticsEvent {
  const SessionEndEvent({
    required this.timestamp,
    required this.hashedStudentId,
    required this.sessionId,
    required this.durationSeconds,
    required this.questionsAttempted,
    required this.questionsCorrect,
    required this.endReason,
    required this.fatigueScore,
  });

  @override
  String get eventType => 'session_end';
  @override
  final DateTime timestamp;
  @override
  final String hashedStudentId;

  final String sessionId;
  final int durationSeconds;
  final int questionsAttempted;
  final int questionsCorrect;
  final String endReason;
  final double fatigueScore;

  @override
  Map<String, dynamic> toJson() => {
        'event_type': eventType,
        'timestamp': timestamp.toIso8601String(),
        'hashed_student_id': hashedStudentId,
        'session_id': sessionId,
        'duration_seconds': durationSeconds,
        'questions_attempted': questionsAttempted,
        'questions_correct': questionsCorrect,
        'end_reason': endReason,
        'fatigue_score': fatigueScore,
      };
}

/// Tracks each question attempt (answer submission).
class QuestionAttemptEvent implements AnalyticsEvent {
  const QuestionAttemptEvent({
    required this.timestamp,
    required this.hashedStudentId,
    required this.sessionId,
    required this.exerciseId,
    required this.conceptId,
    required this.questionType,
    required this.difficulty,
    required this.isCorrect,
    required this.errorType,
    required this.timeSpentMs,
    required this.hintsUsed,
    required this.priorMastery,
    required this.posteriorMastery,
  });

  @override
  String get eventType => 'question_attempt';
  @override
  final DateTime timestamp;
  @override
  final String hashedStudentId;

  final String sessionId;
  final String exerciseId;
  final String conceptId;
  final QuestionType questionType;
  final int difficulty;
  final bool isCorrect;
  final ErrorType errorType;
  final int timeSpentMs;
  final int hintsUsed;
  final double priorMastery;
  final double posteriorMastery;

  @override
  Map<String, dynamic> toJson() => {
        'event_type': eventType,
        'timestamp': timestamp.toIso8601String(),
        'hashed_student_id': hashedStudentId,
        'session_id': sessionId,
        'exercise_id': exerciseId,
        'concept_id': conceptId,
        'question_type': questionType.name,
        'difficulty': difficulty,
        'is_correct': isCorrect,
        'error_type': errorType.name,
        'time_spent_ms': timeSpentMs,
        'hints_used': hintsUsed,
        'prior_mastery': priorMastery,
        'posterior_mastery': posteriorMastery,
      };
}

/// Tracks when a concept is mastered.
class MasteryEvent implements AnalyticsEvent {
  const MasteryEvent({
    required this.timestamp,
    required this.hashedStudentId,
    required this.conceptId,
    required this.subject,
    required this.pKnown,
    required this.attemptCount,
    required this.methodology,
  });

  @override
  String get eventType => 'mastery_achieved';
  @override
  final DateTime timestamp;
  @override
  final String hashedStudentId;

  final String conceptId;
  final Subject subject;
  final double pKnown;
  final int attemptCount;
  final Methodology methodology;

  @override
  Map<String, dynamic> toJson() => {
        'event_type': eventType,
        'timestamp': timestamp.toIso8601String(),
        'hashed_student_id': hashedStudentId,
        'concept_id': conceptId,
        'subject': subject.name,
        'p_known': pKnown,
        'attempt_count': attemptCount,
        'methodology': methodology.name,
      };
}

// ---------------------------------------------------------------------------
// UI Interaction Events
// ---------------------------------------------------------------------------

/// Tracks when a student taps a concept node in the knowledge graph.
class GraphNodeTapEvent implements AnalyticsEvent {
  const GraphNodeTapEvent({
    required this.timestamp,
    required this.hashedStudentId,
    required this.conceptId,
    required this.subject,
    required this.mastery,
  });

  @override
  String get eventType => 'graph_node_tap';
  @override
  final DateTime timestamp;
  @override
  final String hashedStudentId;

  final String conceptId;
  final Subject subject;
  final double mastery;

  @override
  Map<String, dynamic> toJson() => {
        'event_type': eventType,
        'timestamp': timestamp.toIso8601String(),
        'hashed_student_id': hashedStudentId,
        'concept_id': conceptId,
        'subject': subject.name,
        'mastery': mastery,
      };
}

/// Tracks when a student requests a different approach.
class ApproachChangeEvent implements AnalyticsEvent {
  const ApproachChangeEvent({
    required this.timestamp,
    required this.hashedStudentId,
    required this.sessionId,
    required this.preferenceHint,
    required this.previousMethodology,
  });

  @override
  String get eventType => 'approach_change';
  @override
  final DateTime timestamp;
  @override
  final String hashedStudentId;

  final String sessionId;
  final String preferenceHint;
  final Methodology previousMethodology;

  @override
  Map<String, dynamic> toJson() => {
        'event_type': eventType,
        'timestamp': timestamp.toIso8601String(),
        'hashed_student_id': hashedStudentId,
        'session_id': sessionId,
        'preference_hint': preferenceHint,
        'previous_methodology': previousMethodology.name,
      };
}

/// Tracks hint requests.
class HintRequestEvent implements AnalyticsEvent {
  const HintRequestEvent({
    required this.timestamp,
    required this.hashedStudentId,
    required this.sessionId,
    required this.exerciseId,
    required this.hintLevel,
  });

  @override
  String get eventType => 'hint_request';
  @override
  final DateTime timestamp;
  @override
  final String hashedStudentId;

  final String sessionId;
  final String exerciseId;
  final int hintLevel;

  @override
  Map<String, dynamic> toJson() => {
        'event_type': eventType,
        'timestamp': timestamp.toIso8601String(),
        'hashed_student_id': hashedStudentId,
        'session_id': sessionId,
        'exercise_id': exerciseId,
        'hint_level': hintLevel,
      };
}

// ---------------------------------------------------------------------------
// Performance Events
// ---------------------------------------------------------------------------

/// Tracks app launch performance.
class AppLaunchEvent implements AnalyticsEvent {
  const AppLaunchEvent({
    required this.timestamp,
    required this.hashedStudentId,
    required this.coldStartMs,
    required this.timeToInteractiveMs,
    required this.isFirstLaunch,
  });

  @override
  String get eventType => 'app_launch';
  @override
  final DateTime timestamp;
  @override
  final String hashedStudentId;

  /// Time from process start to first frame in milliseconds.
  final int coldStartMs;

  /// Time from process start to interactive state.
  final int timeToInteractiveMs;

  /// Whether this is the very first launch after install.
  final bool isFirstLaunch;

  @override
  Map<String, dynamic> toJson() => {
        'event_type': eventType,
        'timestamp': timestamp.toIso8601String(),
        'hashed_student_id': hashedStudentId,
        'cold_start_ms': coldStartMs,
        'time_to_interactive_ms': timeToInteractiveMs,
        'is_first_launch': isFirstLaunch,
      };
}

/// Tracks rendering performance during knowledge graph interaction.
class GraphRenderPerformanceEvent implements AnalyticsEvent {
  const GraphRenderPerformanceEvent({
    required this.timestamp,
    required this.hashedStudentId,
    required this.averageFps,
    required this.droppedFrames,
    required this.nodeCount,
    required this.edgeCount,
    required this.renderDurationMs,
  });

  @override
  String get eventType => 'graph_render_performance';
  @override
  final DateTime timestamp;
  @override
  final String hashedStudentId;

  final double averageFps;
  final int droppedFrames;
  final int nodeCount;
  final int edgeCount;
  final int renderDurationMs;

  @override
  Map<String, dynamic> toJson() => {
        'event_type': eventType,
        'timestamp': timestamp.toIso8601String(),
        'hashed_student_id': hashedStudentId,
        'average_fps': averageFps,
        'dropped_frames': droppedFrames,
        'node_count': nodeCount,
        'edge_count': edgeCount,
        'render_duration_ms': renderDurationMs,
      };
}

/// Tracks offline sync performance.
class SyncPerformanceEvent implements AnalyticsEvent {
  const SyncPerformanceEvent({
    required this.timestamp,
    required this.hashedStudentId,
    required this.eventCount,
    required this.syncDurationMs,
    required this.acceptedCount,
    required this.correctedCount,
    required this.rejectedCount,
    required this.clockOffsetMs,
  });

  @override
  String get eventType => 'sync_performance';
  @override
  final DateTime timestamp;
  @override
  final String hashedStudentId;

  final int eventCount;
  final int syncDurationMs;
  final int acceptedCount;
  final int correctedCount;
  final int rejectedCount;
  final int clockOffsetMs;

  @override
  Map<String, dynamic> toJson() => {
        'event_type': eventType,
        'timestamp': timestamp.toIso8601String(),
        'hashed_student_id': hashedStudentId,
        'event_count': eventCount,
        'sync_duration_ms': syncDurationMs,
        'accepted_count': acceptedCount,
        'corrected_count': correctedCount,
        'rejected_count': rejectedCount,
        'clock_offset_ms': clockOffsetMs,
      };
}

// ---------------------------------------------------------------------------
// Analytics Service Contract
// ---------------------------------------------------------------------------

/// Abstract contract for the client-side analytics service.
///
/// Privacy guarantees:
/// - All student IDs are SHA-256 hashed before storage or transmission
/// - No plaintext student names, emails, or other PII in analytics
/// - Analytics data is stored locally in encrypted Hive boxes
/// - Batch upload only occurs on connectivity restore
/// - Students/parents can request full analytics deletion
abstract class AnalyticsService {
  // -- Event Tracking -------------------------------------------------------

  /// Track a single analytics event.
  ///
  /// The event is stored locally and batched for upload.
  Future<void> track(AnalyticsEvent event);

  /// Track a session start.
  Future<void> trackSessionStart({
    required String sessionId,
    required Subject? subject,
    required int targetDurationMinutes,
    required Methodology methodology,
    required ExperimentCohort cohort,
  });

  /// Track a session end.
  Future<void> trackSessionEnd({
    required String sessionId,
    required int durationSeconds,
    required int questionsAttempted,
    required int questionsCorrect,
    required String endReason,
    required double fatigueScore,
  });

  /// Track a question attempt.
  Future<void> trackQuestionAttempt({
    required String sessionId,
    required String exerciseId,
    required String conceptId,
    required QuestionType questionType,
    required int difficulty,
    required bool isCorrect,
    required ErrorType errorType,
    required int timeSpentMs,
    required int hintsUsed,
    required double priorMastery,
    required double posteriorMastery,
  });

  /// Track a mastery achievement.
  Future<void> trackMastery({
    required String conceptId,
    required Subject subject,
    required double pKnown,
    required int attemptCount,
    required Methodology methodology,
  });

  // -- UI Interaction Tracking -----------------------------------------------

  /// Track a knowledge graph node tap.
  Future<void> trackGraphNodeTap({
    required String conceptId,
    required Subject subject,
    required double mastery,
  });

  /// Track an approach change request.
  Future<void> trackApproachChange({
    required String sessionId,
    required String preferenceHint,
    required Methodology previousMethodology,
  });

  /// Track a hint request.
  Future<void> trackHintRequest({
    required String sessionId,
    required String exerciseId,
    required int hintLevel,
  });

  // -- Performance Tracking --------------------------------------------------

  /// Track app launch performance.
  Future<void> trackAppLaunch({
    required int coldStartMs,
    required int timeToInteractiveMs,
    required bool isFirstLaunch,
  });

  /// Track knowledge graph rendering performance.
  Future<void> trackGraphRenderPerformance({
    required double averageFps,
    required int droppedFrames,
    required int nodeCount,
    required int edgeCount,
    required int renderDurationMs,
  });

  /// Track sync performance metrics.
  Future<void> trackSyncPerformance({
    required int eventCount,
    required int syncDurationMs,
    required int acceptedCount,
    required int correctedCount,
    required int rejectedCount,
    required int clockOffsetMs,
  });

  // -- Batch Upload ----------------------------------------------------------

  /// Upload all buffered events to the analytics backend.
  ///
  /// Called automatically on connectivity restore and periodically.
  /// Events are removed from local storage only after confirmed upload.
  Future<void> flush();

  /// Number of events buffered locally waiting for upload.
  Future<int> get bufferedEventCount;

  // -- Privacy ---------------------------------------------------------------

  /// Hash a student ID using SHA-256 with a per-install salt.
  ///
  /// The salt is generated once on first launch and stored securely.
  /// This ensures the hash is consistent within one device but cannot
  /// be reversed or correlated across devices without server-side mapping.
  String hashStudentId(String studentId);

  /// Delete all locally stored analytics data.
  ///
  /// Called on logout or explicit privacy request.
  Future<void> purgeLocalData();

  // -- Lifecycle -------------------------------------------------------------

  /// Initialize the analytics service (open local storage, load salt).
  Future<void> initialize();

  /// Release all resources.
  void dispose();
}
