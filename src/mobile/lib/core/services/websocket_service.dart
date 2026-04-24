// =============================================================================
// Cena Adaptive Learning Platform — WebSocket Service Interface
// Abstracts SignalR hub communication for learning session events.
// =============================================================================

import 'dart:async';

import '../models/domain_models.dart';

// ---------------------------------------------------------------------------
// Connection State
// ---------------------------------------------------------------------------

/// Connection lifecycle state for the SignalR hub.
enum ConnectionState {
  disconnected,
  connecting,
  connected,
  reconnecting,
}

// ---------------------------------------------------------------------------
// Outbound Command Models
// ---------------------------------------------------------------------------

/// Command to start a new learning session.
class StartSession {
  const StartSession({
    required this.studentId,
    this.subject,
    this.durationMinutes = 25,
  });

  final String studentId;
  final Subject? subject;
  final int durationMinutes;

  Map<String, dynamic> toJson() => {
        'studentId': studentId,
        if (subject != null) 'subject': subject!.name,
        'durationMinutes': durationMinutes,
      };
}

/// Command to submit an attempt on a concept exercise.
class AttemptConcept {
  const AttemptConcept({
    required this.sessionId,
    required this.exerciseId,
    required this.conceptId,
    required this.answer,
    required this.timeSpentMs,
    required this.idempotencyKey,
  });

  final String sessionId;
  final String exerciseId;
  final String conceptId;
  final String answer;
  final int timeSpentMs;
  final String idempotencyKey;

  Map<String, dynamic> toJson() => {
        'sessionId': sessionId,
        'exerciseId': exerciseId,
        'conceptId': conceptId,
        'answer': answer,
        'timeSpentMs': timeSpentMs,
        'idempotencyKey': idempotencyKey,
      };
}

/// Command to request a hint for the current exercise.
class RequestHint {
  const RequestHint({
    required this.sessionId,
    required this.exerciseId,
    required this.hintLevel,
  });

  final String sessionId;
  final String exerciseId;
  final int hintLevel;

  Map<String, dynamic> toJson() => {
        'sessionId': sessionId,
        'exerciseId': exerciseId,
        'hintLevel': hintLevel,
      };
}

/// Command to skip the current question.
class SkipQuestion {
  const SkipQuestion({
    required this.sessionId,
    required this.exerciseId,
    this.reason,
  });

  final String sessionId;
  final String exerciseId;
  final String? reason;

  Map<String, dynamic> toJson() => {
        'sessionId': sessionId,
        'exerciseId': exerciseId,
        if (reason != null) 'reason': reason,
      };
}

/// Command to request a different pedagogical approach.
class SwitchApproach {
  const SwitchApproach({
    required this.sessionId,
    required this.preferenceHint,
  });

  final String sessionId;
  final String preferenceHint;

  Map<String, dynamic> toJson() => {
        'sessionId': sessionId,
        'preferenceHint': preferenceHint,
      };
}

/// Command to end the current session.
class EndSession {
  const EndSession({
    required this.sessionId,
    this.reason = 'manual',
  });

  final String sessionId;
  final String reason;

  Map<String, dynamic> toJson() => {
        'sessionId': sessionId,
        'reason': reason,
      };
}

// ---------------------------------------------------------------------------
// Inbound Event Envelope
// ---------------------------------------------------------------------------

/// Discriminated union envelope for all inbound SignalR messages.
class MessageEnvelope {
  const MessageEnvelope({
    required this.type,
    required this.payload,
    required this.receivedAt,
  });

  /// Event type discriminator (e.g. "QuestionPresented", "AnswerEvaluated").
  final String type;

  /// Raw JSON payload — deserialised by the handler for each event type.
  final Map<String, dynamic> payload;

  final DateTime receivedAt;
}

// ---------------------------------------------------------------------------
// WebSocket Service Interface
// ---------------------------------------------------------------------------

/// Abstract interface for the SignalR learning hub connection.
///
/// Concrete implementation lives in `websocket_service_impl.dart` and is
/// registered via dependency injection at app startup (ProviderScope override).
///
/// All outbound methods are fire-and-forget from the notifier perspective —
/// the server responds via the inbound [messageStream].
abstract class WebSocketService {
  /// Broadcast stream of all inbound messages from the hub.
  Stream<MessageEnvelope> get messageStream;

  /// Stream of connection state changes.
  Stream<ConnectionState> get connectionState;

  /// Current connection state (synchronous snapshot).
  ConnectionState get currentConnectionState;

  /// Connect to the hub with the given auth token.
  Future<void> connect(String authToken);

  /// Gracefully disconnect from the hub.
  Future<void> disconnect();

  // ---- Outbound commands ----

  Future<void> startSession(StartSession command);
  Future<void> attemptConcept(AttemptConcept command);
  Future<void> requestHint(RequestHint command);
  Future<void> skipQuestion(SkipQuestion command);
  Future<void> switchApproach(SwitchApproach command);
  Future<void> endSession(EndSession command);
}
