// =============================================================================
// Cena Adaptive Learning Platform — Domain Models
// All models are immutable, null-safe, and json_serializable-annotated.
// Run: dart run build_runner build --delete-conflicting-outputs
// =============================================================================

import 'package:freezed_annotation/freezed_annotation.dart';
import 'package:json_annotation/json_annotation.dart';

part 'domain_models.freezed.dart';
part 'domain_models.g.dart';

// ---------------------------------------------------------------------------
// Enums
// ---------------------------------------------------------------------------

/// Bloom's taxonomy levels used for concept difficulty classification.
@JsonEnum(valueField: 'level')
enum BloomLevel {
  remember(1),
  understand(2),
  apply(3),
  analyze(4),
  evaluate(5),
  create(6);

  const BloomLevel(this.level);
  final int level;
}

/// Bagrut STEM subjects supported by the platform.
enum Subject {
  @JsonValue('math')
  math,
  @JsonValue('physics')
  physics,
  @JsonValue('chemistry')
  chemistry,
  @JsonValue('biology')
  biology,
  @JsonValue('cs')
  cs,
}

/// Question presentation types.
enum QuestionType {
  @JsonValue('mcq')
  multipleChoice,
  @JsonValue('free_text')
  freeText,
  @JsonValue('numeric')
  numeric,
  @JsonValue('proof')
  proof,
  @JsonValue('diagram')
  diagram,
}

/// Pedagogical methodology variants for A/B experimentation.
enum Methodology {
  @JsonValue('spaced_repetition')
  spacedRepetition,
  @JsonValue('interleaved')
  interleaved,
  @JsonValue('blocked')
  blocked,
  @JsonValue('adaptive_difficulty')
  adaptiveDifficulty,
  @JsonValue('socratic')
  socratic,
}

/// Error classification returned by the LLM evaluator.
enum ErrorType {
  @JsonValue('conceptual')
  conceptual,
  @JsonValue('procedural')
  procedural,
  @JsonValue('careless')
  careless,
  @JsonValue('notation')
  notation,
  @JsonValue('incomplete')
  incomplete,
  @JsonValue('none')
  none,
}

/// A/B experiment cohort assignments.
enum ExperimentCohort {
  @JsonValue('control')
  control,
  @JsonValue('treatment_a')
  treatmentA,
  @JsonValue('treatment_b')
  treatmentB,
}

/// Offline event classification for sync conflict resolution.
enum EventClassification {
  /// Always accepted by server without revalidation.
  @JsonValue('unconditional')
  unconditional,

  /// Server validates context before accepting.
  @JsonValue('conditional')
  conditional,

  /// Server recalculates entirely; client value is advisory only.
  @JsonValue('server_authoritative')
  serverAuthoritative,
}

/// Current sync state of the offline queue.
enum SyncStatus {
  idle,
  syncing,
  error,
  conflict,
}

// ---------------------------------------------------------------------------
// Core Domain Models
// ---------------------------------------------------------------------------

/// Represents a student user.
@freezed
class Student with _$Student {
  const factory Student({
    required String id,
    required String name,
    required ExperimentCohort experimentCohort,
    @Default(0) int streak,
    @Default(0) int xp,
    required DateTime lastActive,

    /// ISO 639-1 locale code: "he" (Hebrew), "ar" (Arabic), "en" (English).
    /// Hebrew and Arabic are both RTL. Arabic enables Israeli Arab students
    /// (~30% of student population) and future MENA market expansion.
    @Default('he') String locale,

    /// Current student level derived from XP.
    @Default(1) int level,
  }) = _Student;

  factory Student.fromJson(Map<String, dynamic> json) =>
      _$StudentFromJson(json);
}

/// A single concept in the knowledge graph (curriculum node).
@freezed
class Concept with _$Concept {
  const factory Concept({
    required String id,
    required String name,

    /// Localized display name (Hebrew primary).
    String? nameHe,
    required Subject subject,

    /// 1-10 difficulty scale aligned with Bagrut levels.
    required int difficulty,
    required BloomLevel bloomLevel,

    /// IDs of prerequisite concepts that must be mastered first.
    @Default([]) List<String> prerequisiteIds,

    /// Optional Bagrut exam reference, e.g. "5-point Math, Topic 803".
    String? bagrutReference,
  }) = _Concept;

  factory Concept.fromJson(Map<String, dynamic> json) =>
      _$ConceptFromJson(json);
}

/// Bayesian Knowledge Tracing state for one concept per student.
@freezed
class MasteryState with _$MasteryState {
  const factory MasteryState({
    required String conceptId,

    /// P(Known) — probability the student has mastered this concept [0.0, 1.0].
    required double pKnown,

    /// True when pKnown exceeds the mastery threshold (default 0.85).
    @Default(false) bool isMastered,
    DateTime? lastAttempted,

    /// The methodology active when this mastery was last updated.
    Methodology? methodology,

    /// Number of attempts on this concept.
    @Default(0) int attemptCount,

    /// Consecutive correct answers (resets on wrong answer).
    @Default(0) int consecutiveCorrect,
  }) = _MasteryState;

  factory MasteryState.fromJson(Map<String, dynamic> json) =>
      _$MasteryStateFromJson(json);
}

/// An active learning session.
@freezed
class Session with _$Session {
  const factory Session({
    required String id,
    required DateTime startedAt,
    DateTime? endedAt,
    required Methodology methodology,

    /// Number of questions presented so far.
    @Default(0) int questionsAttempted,

    /// Cognitive load / fatigue score [0.0, 1.0]. Above 0.7 triggers break.
    @Default(0.0) double fatigueScore,

    /// Target session duration in minutes.
    @Default(25) int targetDurationMinutes,

    /// Subject focus for this session, if any.
    Subject? subject,
  }) = _Session;

  factory Session.fromJson(Map<String, dynamic> json) =>
      _$SessionFromJson(json);
}

/// A single exercise / question presented to the student.
@freezed
class Exercise with _$Exercise {
  const factory Exercise({
    required String id,
    required String conceptId,
    required QuestionType questionType,

    /// 1-10 difficulty matching concept scale.
    required int difficulty,

    /// The question text (may contain LaTeX via \$...\$).
    required String content,

    /// For MCQ: list of option strings. Null for other types.
    List<String>? options,

    /// Optional diagram/image URL or base64 data URI.
    String? diagram,

    /// Hints available (revealed progressively).
    @Default([]) List<String> hints,

    /// Maximum time in seconds (0 = no limit).
    @Default(0) int timeLimitSeconds,
  }) = _Exercise;

  factory Exercise.fromJson(Map<String, dynamic> json) =>
      _$ExerciseFromJson(json);
}

/// Result of evaluating a student's answer.
@freezed
class AnswerResult with _$AnswerResult {
  const factory AnswerResult({
    required bool isCorrect,
    required ErrorType errorType,

    /// P(Known) before this answer.
    required double priorMastery,

    /// P(Known) after Bayesian update.
    required double posteriorMastery,

    /// LLM-generated feedback text (Hebrew).
    required String feedback,

    /// Optional worked solution shown after wrong answer.
    String? workedSolution,

    /// XP earned for this answer.
    @Default(0) int xpEarned,
  }) = _AnswerResult;

  factory AnswerResult.fromJson(Map<String, dynamic> json) =>
      _$AnswerResultFromJson(json);
}

// ---------------------------------------------------------------------------
// Knowledge Graph Visualization Models
// ---------------------------------------------------------------------------

/// A node in the rendered knowledge graph.
@freezed
class ConceptNode with _$ConceptNode {
  const factory ConceptNode({
    required String conceptId,
    required String label,

    /// Hebrew label.
    String? labelHe,
    required Subject subject,

    /// Mastery level [0.0, 1.0] for color interpolation.
    required double mastery,
    required bool isMastered,

    /// 2D position in the graph layout.
    required double x,
    required double y,

    /// Visual radius (larger = more central/important).
    @Default(24.0) double radius,

    /// Whether this node is currently selected by the student.
    @Default(false) bool isSelected,

    /// Whether prerequisites are met (unlocked).
    @Default(true) bool isUnlocked,
  }) = _ConceptNode;

  factory ConceptNode.fromJson(Map<String, dynamic> json) =>
      _$ConceptNodeFromJson(json);
}

/// A directed edge in the knowledge graph (prerequisite relationship).
@freezed
class PrerequisiteEdge with _$PrerequisiteEdge {
  const factory PrerequisiteEdge({
    required String fromConceptId,
    required String toConceptId,

    /// Edge weight for layout [0.0, 1.0].
    @Default(1.0) double weight,

    /// True if the "from" concept is mastered (edge turns solid).
    @Default(false) bool isSatisfied,
  }) = _PrerequisiteEdge;

  factory PrerequisiteEdge.fromJson(Map<String, dynamic> json) =>
      _$PrerequisiteEdgeFromJson(json);
}

/// Full knowledge graph state for rendering.
@freezed
class KnowledgeGraph with _$KnowledgeGraph {
  const factory KnowledgeGraph({
    required List<ConceptNode> nodes,
    required List<PrerequisiteEdge> edges,

    /// Map of conceptId -> MasteryState for the overlay.
    required Map<String, MasteryState> masteryOverlay,

    /// Currently selected subject filter, if any.
    Subject? subjectFilter,
  }) = _KnowledgeGraph;

  factory KnowledgeGraph.fromJson(Map<String, dynamic> json) =>
      _$KnowledgeGraphFromJson(json);
}

// ---------------------------------------------------------------------------
// Offline Sync Models
// ---------------------------------------------------------------------------

/// An event queued for offline sync, stored in SQLite via drift.
@freezed
class OfflineEvent with _$OfflineEvent {
  const factory OfflineEvent({
    /// Client-generated unique key: UUID + sequence number.
    required String idempotencyKey,

    /// ISO-8601 timestamp from the client clock.
    required DateTime clientTimestamp,

    /// Event type name matching backend discriminator.
    required String eventType,

    /// JSON-encoded event payload.
    required String payload,

    /// Sync classification determining server handling.
    required EventClassification classification,

    /// Monotonically increasing client sequence number.
    required int sequenceNumber,

    /// Number of sync attempts so far.
    @Default(0) int retryCount,

    /// Last error message if sync failed.
    String? lastError,
  }) = _OfflineEvent;

  factory OfflineEvent.fromJson(Map<String, dynamic> json) =>
      _$OfflineEventFromJson(json);
}

/// Request sent to the server to sync offline events.
@freezed
class SyncRequest with _$SyncRequest {
  const factory SyncRequest({
    required String studentId,

    /// Estimated client clock offset in milliseconds.
    required int clockOffsetMs,

    /// Ordered list of events to sync.
    required List<OfflineEvent> events,

    /// Last server-acknowledged sequence number.
    required int lastAcknowledgedSequence,
  }) = _SyncRequest;

  factory SyncRequest.fromJson(Map<String, dynamic> json) =>
      _$SyncRequestFromJson(json);
}

/// Server response after processing sync request.
@freezed
class SyncResponse with _$SyncResponse {
  const factory SyncResponse({
    /// Highest sequence number now acknowledged.
    required int acknowledgedUpTo,

    /// Events that were accepted as-is.
    @Default([]) List<String> acceptedKeys,

    /// Events that required correction (server recalculated).
    @Default([]) List<SyncCorrection> corrections,

    /// Events that were rejected (stale or conflicting).
    @Default([]) List<String> rejectedKeys,

    /// Server timestamp for clock skew calibration.
    required DateTime serverTimestamp,
  }) = _SyncResponse;

  factory SyncResponse.fromJson(Map<String, dynamic> json) =>
      _$SyncResponseFromJson(json);
}

/// A correction applied by the server to a synced event.
@freezed
class SyncCorrection with _$SyncCorrection {
  const factory SyncCorrection({
    required String idempotencyKey,

    /// The field that was corrected.
    required String field,

    /// The client's original value (JSON-encoded).
    required String clientValue,

    /// The server's authoritative value (JSON-encoded).
    required String serverValue,

    /// Weight applied: 1.0 = full, 0.75 = reduced, 0.0 = discarded.
    required double weight,

    /// Human-readable reason for the correction.
    String? reason,
  }) = _SyncCorrection;

  factory SyncCorrection.fromJson(Map<String, dynamic> json) =>
      _$SyncCorrectionFromJson(json);
}

// ---------------------------------------------------------------------------
// Gamification Models
// ---------------------------------------------------------------------------

/// Badge earned by the student.
@freezed
class Badge with _$Badge {
  const factory Badge({
    required String id,
    required String name,
    String? nameHe,
    required String iconAsset,
    required String description,
    DateTime? earnedAt,
    @Default(false) bool isNew,
  }) = _Badge;

  factory Badge.fromJson(Map<String, dynamic> json) => _$BadgeFromJson(json);
}

/// Session summary shown at the end of a learning session.
@freezed
class SessionSummary with _$SessionSummary {
  const factory SessionSummary({
    required String sessionId,
    required int questionsAttempted,
    required int correctAnswers,
    required int xpEarned,
    required Duration duration,
    required List<String> conceptsMastered,
    required List<String> conceptsImproved,
    Badge? badgeEarned,
    required bool streakMaintained,
  }) = _SessionSummary;

  factory SessionSummary.fromJson(Map<String, dynamic> json) =>
      _$SessionSummaryFromJson(json);
}
