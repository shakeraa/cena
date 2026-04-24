// =============================================================================
// Cena Adaptive Learning Platform — WebSocket Service Contract
// SignalR-compatible WebSocket transport for real-time communication with
// the .NET 9 Proto.Actor backend cluster.
// =============================================================================

import 'dart:async';

import 'package:freezed_annotation/freezed_annotation.dart';
import 'package:json_annotation/json_annotation.dart';

import '../models/domain_models.dart';

part 'websocket_service.freezed.dart';
part 'websocket_service.g.dart';

// ---------------------------------------------------------------------------
// Connection State
// ---------------------------------------------------------------------------

/// WebSocket connection lifecycle states.
enum ConnectionState {
  /// Not connected and not attempting to connect.
  disconnected,

  /// Actively attempting to establish connection.
  connecting,

  /// Connection established, handshake in progress.
  handshaking,

  /// Fully connected and ready for message exchange.
  connected,

  /// Connection lost, will attempt automatic reconnection.
  reconnecting,

  /// Permanently failed after max retries exhausted.
  failed,
}

// ---------------------------------------------------------------------------
// Message Envelope — Discriminated Union
// ---------------------------------------------------------------------------

/// Wrapper for all WebSocket messages, matching the backend SignalR hub protocol.
///
/// The [target] field acts as the discriminator for message routing.
/// The [arguments] list contains the typed payload (SignalR convention).
@freezed
class MessageEnvelope with _$MessageEnvelope {
  const factory MessageEnvelope({
    /// SignalR method name / message type discriminator.
    required String target,

    /// Unique message ID for correlation and idempotency.
    required String invocationId,

    /// JSON-encoded arguments (typically a single-element list).
    required List<Map<String, dynamic>> arguments,

    /// ISO-8601 timestamp.
    required DateTime timestamp,
  }) = _MessageEnvelope;

  factory MessageEnvelope.fromJson(Map<String, dynamic> json) =>
      _$MessageEnvelopeFromJson(json);
}

// ---------------------------------------------------------------------------
// Client → Server Commands
// ---------------------------------------------------------------------------

/// Student submits an answer to the current exercise.
@freezed
class AttemptConcept with _$AttemptConcept {
  const factory AttemptConcept({
    required String sessionId,
    required String exerciseId,
    required String conceptId,

    /// The student's answer (text, selected option index, numeric value, etc.).
    required String answer,

    /// Time spent on this question in milliseconds.
    required int timeSpentMs,

    /// Idempotency key for offline replay safety.
    required String idempotencyKey,
  }) = _AttemptConcept;

  factory AttemptConcept.fromJson(Map<String, dynamic> json) =>
      _$AttemptConceptFromJson(json);
}

/// Request to start a new learning session.
@freezed
class StartSession with _$StartSession {
  const factory StartSession({
    required String studentId,

    /// Optional subject focus; null = adaptive selection.
    Subject? subject,

    /// Requested session duration in minutes.
    @Default(25) int durationMinutes,
  }) = _StartSession;

  factory StartSession.fromJson(Map<String, dynamic> json) =>
      _$StartSessionFromJson(json);
}

/// Signal that the student is ending the current session.
@freezed
class EndSession with _$EndSession {
  const factory EndSession({
    required String sessionId,

    /// Reason for ending: "completed", "fatigued", "manual", "timeout".
    @Default('manual') String reason,
  }) = _EndSession;

  factory EndSession.fromJson(Map<String, dynamic> json) =>
      _$EndSessionFromJson(json);
}

/// Student requests a hint for the current exercise.
@freezed
class RequestHint with _$RequestHint {
  const factory RequestHint({
    required String sessionId,
    required String exerciseId,

    /// Which hint level (0-based, progressive reveal).
    required int hintLevel,
  }) = _RequestHint;

  factory RequestHint.fromJson(Map<String, dynamic> json) =>
      _$RequestHintFromJson(json);
}

/// Student skips the current question.
@freezed
class SkipQuestion with _$SkipQuestion {
  const factory SkipQuestion({
    required String sessionId,
    required String exerciseId,

    /// Optional reason: "too_hard", "unclear", "boring", "other".
    String? reason,
  }) = _SkipQuestion;

  factory SkipQuestion.fromJson(Map<String, dynamic> json) =>
      _$SkipQuestionFromJson(json);
}

/// Student adds a personal annotation/note to a concept.
@freezed
class AddAnnotation with _$AddAnnotation {
  const factory AddAnnotation({
    required String conceptId,
    required String text,

    /// Optional image attachment (base64).
    String? imageData,
  }) = _AddAnnotation;

  factory AddAnnotation.fromJson(Map<String, dynamic> json) =>
      _$AddAnnotationFromJson(json);
}

/// Student requests a different pedagogical approach.
@freezed
class SwitchApproach with _$SwitchApproach {
  const factory SwitchApproach({
    required String sessionId,

    /// Student-facing reason: "explain_differently", "more_practice",
    /// "simpler_first", "challenge_me".
    required String preferenceHint,
  }) = _SwitchApproach;

  factory SwitchApproach.fromJson(Map<String, dynamic> json) =>
      _$SwitchApproachFromJson(json);
}

// ---------------------------------------------------------------------------
// Server → Client Events
// ---------------------------------------------------------------------------

/// Server presents a new question to the student.
@freezed
class QuestionPresented with _$QuestionPresented {
  const factory QuestionPresented({
    required Exercise exercise,
    required String sessionId,

    /// Position in the session (1-based).
    required int questionNumber,

    /// Estimated remaining questions in this session.
    required int estimatedRemaining,
  }) = _QuestionPresented;

  factory QuestionPresented.fromJson(Map<String, dynamic> json) =>
      _$QuestionPresentedFromJson(json);
}

/// Server returns evaluation of the student's answer.
@freezed
class AnswerEvaluated with _$AnswerEvaluated {
  const factory AnswerEvaluated({
    required String exerciseId,
    required AnswerResult result,
  }) = _AnswerEvaluated;

  factory AnswerEvaluated.fromJson(Map<String, dynamic> json) =>
      _$AnswerEvaluatedFromJson(json);
}

/// Server notifies of updated mastery for a concept.
@freezed
class MasteryUpdated with _$MasteryUpdated {
  const factory MasteryUpdated({
    required String conceptId,
    required MasteryState newState,

    /// True if this update crossed the mastery threshold.
    @Default(false) bool justMastered,
  }) = _MasteryUpdated;

  factory MasteryUpdated.fromJson(Map<String, dynamic> json) =>
      _$MasteryUpdatedFromJson(json);
}

/// Server switched the pedagogical methodology mid-session.
@freezed
class MethodologySwitched with _$MethodologySwitched {
  const factory MethodologySwitched({
    required String sessionId,
    required Methodology previous,
    required Methodology current,

    /// Student-facing explanation of what changed (Hebrew).
    required String explanation,
  }) = _MethodologySwitched;

  factory MethodologySwitched.fromJson(Map<String, dynamic> json) =>
      _$MethodologySwitchedFromJson(json);
}

/// End-of-session summary from the server.
@freezed
class SessionSummaryEvent with _$SessionSummaryEvent {
  const factory SessionSummaryEvent({
    required SessionSummary summary,
  }) = _SessionSummaryEvent;

  factory SessionSummaryEvent.fromJson(Map<String, dynamic> json) =>
      _$SessionSummaryEventFromJson(json);
}

/// XP awarded for an action.
@freezed
class XpAwarded with _$XpAwarded {
  const factory XpAwarded({
    required int amount,
    required String reason,
    required int totalXp,
    required int currentLevel,

    /// True if this XP caused a level-up.
    @Default(false) bool leveledUp,
  }) = _XpAwarded;

  factory XpAwarded.fromJson(Map<String, dynamic> json) =>
      _$XpAwardedFromJson(json);
}

/// Streak state updated.
@freezed
class StreakUpdated with _$StreakUpdated {
  const factory StreakUpdated({
    required int currentStreak,
    required int longestStreak,

    /// True if the streak was just broken and reset.
    @Default(false) bool streakBroken,
  }) = _StreakUpdated;

  factory StreakUpdated.fromJson(Map<String, dynamic> json) =>
      _$StreakUpdatedFromJson(json);
}

/// Full or partial knowledge graph update from the server.
@freezed
class KnowledgeGraphUpdated with _$KnowledgeGraphUpdated {
  const factory KnowledgeGraphUpdated({
    /// If true, [graph] is the complete graph. If false, it's a delta.
    @Default(false) bool isFullUpdate,
    required KnowledgeGraph graph,
  }) = _KnowledgeGraphUpdated;

  factory KnowledgeGraphUpdated.fromJson(Map<String, dynamic> json) =>
      _$KnowledgeGraphUpdatedFromJson(json);
}

/// Server warns that cognitive load is too high; suggest a break.
@freezed
class CognitiveLoadWarning with _$CognitiveLoadWarning {
  const factory CognitiveLoadWarning({
    required double fatigueScore,

    /// Suggested break duration in minutes.
    required int suggestedBreakMinutes,

    /// Student-facing message (Hebrew).
    required String message,
  }) = _CognitiveLoadWarning;

  factory CognitiveLoadWarning.fromJson(Map<String, dynamic> json) =>
      _$CognitiveLoadWarningFromJson(json);
}

// ---------------------------------------------------------------------------
// Reconnection Configuration
// ---------------------------------------------------------------------------

/// Configuration for exponential backoff reconnection strategy.
@freezed
class ReconnectionConfig with _$ReconnectionConfig {
  const factory ReconnectionConfig({
    /// Initial delay before first reconnection attempt.
    @Default(Duration(seconds: 1)) Duration initialDelay,

    /// Maximum delay between attempts.
    @Default(Duration(seconds: 30)) Duration maxDelay,

    /// Multiplier applied to delay after each failed attempt.
    @Default(2.0) double backoffMultiplier,

    /// Maximum number of reconnection attempts before giving up.
    @Default(10) int maxAttempts,

    /// Jitter factor [0.0, 1.0] to prevent thundering herd.
    @Default(0.2) double jitterFactor,
  }) = _ReconnectionConfig;
}

// ---------------------------------------------------------------------------
// WebSocket Service Contract
// ---------------------------------------------------------------------------

/// Abstract contract for the real-time WebSocket communication layer.
///
/// Implementations must handle:
/// - SignalR-compatible handshake and message framing
/// - Automatic reconnection with exponential backoff
/// - Heartbeat ping/pong to detect stale connections
/// - Message serialization/deserialization via [MessageEnvelope]
/// - Offline event queuing when disconnected
abstract class WebSocketService {
  // -- Lifecycle -----------------------------------------------------------

  /// Establish a WebSocket connection to the backend hub.
  ///
  /// [url] is the SignalR hub endpoint (wss://...).
  /// [authToken] is the JWT bearer token for authentication.
  /// Returns a future that completes when the connection is fully established.
  Future<void> connect({
    required String url,
    required String authToken,
    ReconnectionConfig reconnectionConfig = const ReconnectionConfig(),
  });

  /// Gracefully close the WebSocket connection.
  ///
  /// Sends a close frame and stops heartbeat/reconnection timers.
  Future<void> disconnect();

  /// Whether the service is currently connected and ready.
  bool get isConnected;

  // -- Messaging -----------------------------------------------------------

  /// Send a typed command to the server.
  ///
  /// Wraps [payload] in a [MessageEnvelope] with proper [target] routing.
  /// If disconnected, queues the message for send-on-reconnect.
  Future<void> send({
    required String target,
    required Map<String, dynamic> payload,
    String? invocationId,
  });

  /// Stream of all incoming server events as [MessageEnvelope].
  ///
  /// Consumers should switch on [MessageEnvelope.target] to route to
  /// the appropriate handler.
  Stream<MessageEnvelope> get onMessage;

  // -- Connection State ----------------------------------------------------

  /// Stream of connection state changes for UI binding.
  Stream<ConnectionState> get connectionState;

  /// Current connection state snapshot.
  ConnectionState get currentState;

  // -- Heartbeat -----------------------------------------------------------

  /// Interval between heartbeat pings.
  ///
  /// If a pong is not received within [heartbeatTimeout], the connection
  /// is considered dead and reconnection is triggered.
  Duration get heartbeatInterval;

  /// Maximum time to wait for a pong response.
  Duration get heartbeatTimeout;

  // -- Convenience typed senders -------------------------------------------

  /// Send a [StartSession] command.
  Future<void> startSession(StartSession command);

  /// Send an [AttemptConcept] command (student answer).
  Future<void> attemptConcept(AttemptConcept command);

  /// Send an [EndSession] command.
  Future<void> endSession(EndSession command);

  /// Send a [RequestHint] command.
  Future<void> requestHint(RequestHint command);

  /// Send a [SkipQuestion] command.
  Future<void> skipQuestion(SkipQuestion command);

  /// Send an [AddAnnotation] command.
  Future<void> addAnnotation(AddAnnotation command);

  /// Send a [SwitchApproach] command.
  Future<void> switchApproach(SwitchApproach command);

  // -- Cleanup -------------------------------------------------------------

  /// Release all resources (streams, timers, connections).
  void dispose();
}

// ---------------------------------------------------------------------------
// Message Router Helper
// ---------------------------------------------------------------------------

/// Maps SignalR target names to Dart event types for deserialization.
///
/// Usage:
/// ```dart
/// final router = MessageRouter();
/// router.on<QuestionPresented>('QuestionPresented', (event) { ... });
/// router.on<AnswerEvaluated>('AnswerEvaluated', (event) { ... });
/// webSocketService.onMessage.listen(router.dispatch);
/// ```
abstract class MessageRouter {
  /// Register a handler for a specific message target.
  void on<T>(
    String target,
    void Function(T event) handler,
    T Function(Map<String, dynamic> json) fromJson,
  );

  /// Dispatch a [MessageEnvelope] to the appropriate registered handler.
  void dispatch(MessageEnvelope envelope);

  /// Remove all registered handlers.
  void clear();
}

/// All client→server command target names, matching backend SignalR hub methods.
abstract class CommandTargets {
  static const String startSession = 'StartSession';
  static const String attemptConcept = 'AttemptConcept';
  static const String endSession = 'EndSession';
  static const String requestHint = 'RequestHint';
  static const String skipQuestion = 'SkipQuestion';
  static const String addAnnotation = 'AddAnnotation';
  static const String switchApproach = 'SwitchApproach';
}

/// All server→client event target names, matching backend SignalR hub events.
abstract class EventTargets {
  static const String questionPresented = 'QuestionPresented';
  static const String answerEvaluated = 'AnswerEvaluated';
  static const String masteryUpdated = 'MasteryUpdated';
  static const String methodologySwitched = 'MethodologySwitched';
  static const String sessionSummary = 'SessionSummary';
  static const String xpAwarded = 'XpAwarded';
  static const String streakUpdated = 'StreakUpdated';
  static const String knowledgeGraphUpdated = 'KnowledgeGraphUpdated';
  static const String cognitiveLoadWarning = 'CognitiveLoadWarning';
}
