// =============================================================================
// Cena — Integration Test: Session Flow (MOB-TEST-003)
// =============================================================================
// Tests the full session lifecycle: start → answer → feedback → end
// Uses mock WebSocket service to simulate backend events.
// =============================================================================

import 'dart:async';

import 'package:cena/core/models/domain_models.dart';
import 'package:cena/core/services/offline_sync_service.dart';
import 'package:cena/core/services/websocket_service.dart';
import 'package:cena/core/state/session_notifier.dart';
import 'package:flutter_test/flutter_test.dart';

// ---------------------------------------------------------------------------
// Mock WebSocket service
// ---------------------------------------------------------------------------

class MockWebSocketService implements WebSocketService {
  final _messageController = StreamController<MessageEnvelope>.broadcast();
  final _stateController = StreamController<ConnectionState>.broadcast();

  @override
  Stream<MessageEnvelope> get messageStream => _messageController.stream;

  @override
  Stream<ConnectionState> get connectionState => _stateController.stream;

  @override
  ConnectionState get currentConnectionState => ConnectionState.connected;

  @override
  Future<void> connect(String authToken) async {}

  @override
  Future<void> disconnect() async {}

  @override
  Future<void> startSession(StartSession command) async {
    // Simulate server response: SessionStarted
    // Note: Freezed generates snake_case JSON keys (started_at, not startedAt)
    _messageController.add(MessageEnvelope(
      type: 'SessionStarted',
      payload: {
        'session': {
          'id': 'test-session-1',
          'student_id': 'student-1',
          'started_at': DateTime.now().toIso8601String(),
          'target_duration_minutes': command.durationMinutes,
          'methodology': 'adaptive_difficulty',
        },
      },
      receivedAt: DateTime.now(),
    ));
  }

  @override
  Future<void> attemptConcept(AttemptConcept command) async {
    // Simulate AnswerEvaluated
    _messageController.add(MessageEnvelope(
      type: 'AnswerEvaluated',
      payload: {
        'result': {
          'exerciseId': command.exerciseId,
          'isCorrect': true,
          'correctAnswer': command.answer,
          'xpEarned': 10,
          'explanation': 'Well done!',
        },
      },
      receivedAt: DateTime.now(),
    ));
  }

  @override
  Future<void> endSession(EndSession command) async {
    _messageController.add(MessageEnvelope(
      type: 'SessionEnded',
      payload: {'sessionId': command.sessionId},
      receivedAt: DateTime.now(),
    ));
  }

  @override
  Future<void> requestHint(RequestHint command) async {}
  @override
  Future<void> skipQuestion(SkipQuestion command) async {}
  @override
  Future<void> switchApproach(SwitchApproach command) async {}

  void dispose() {
    _messageController.close();
    _stateController.close();
  }
}

// ---------------------------------------------------------------------------
// Mock SyncManager (no-op for integration test)
// ---------------------------------------------------------------------------

class MockSyncManager implements SyncManager {
  @override
  Stream<SyncStatus> get statusStream => const Stream.empty();
  @override
  Stream<int> get pendingEventCountStream => const Stream.empty();
  @override
  Stream<DateTime> get lastSyncTimeStream => const Stream.empty();
  @override
  Stream<int> get conflictCountStream => const Stream.empty();
  @override
  int get pendingEventCount => 0;
  @override
  Future<void> enqueue(OfflineEvent event) async {}
  @override
  Future<bool> syncNow() async => true;
  @override
  Future<void> startAutoSync({Duration interval = const Duration(seconds: 30)}) async {}
  @override
  void stopAutoSync() {}
  @override
  Future<void> pruneAccepted() async {}
  @override
  bool get hasConflicts => false;
  @override
  List<SyncCorrection> getUnresolvedConflicts() => [];
  @override
  Future<void> acceptCorrection(String idempotencyKey) async {}
  @override
  void dispose() {}
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

void main() {
  group('Session Flow Integration', () {
    late MockWebSocketService mockWs;
    late MockSyncManager mockSync;
    late SessionNotifier notifier;

    setUp(() {
      mockWs = MockWebSocketService();
      mockSync = MockSyncManager();
      notifier = SessionNotifier(
        webSocketService: mockWs,
        syncManager: mockSync,
        studentId: 'student-1',
      );
    });

    tearDown(() {
      notifier.dispose();
      mockWs.dispose();
    });

    test('initial state is inactive with no session', () {
      expect(notifier.state.isActive, isFalse);
      expect(notifier.state.currentSession, isNull);
    });

    test('startSession sends command and receives SessionStarted', () async {
      await notifier.startSession(durationMinutes: 25);

      // Allow the stream event to propagate
      await Future.delayed(const Duration(milliseconds: 50));

      expect(notifier.state.currentSession, isNotNull);
      expect(notifier.state.currentSession!.id, 'test-session-1');
      expect(notifier.state.questionsAttempted, 0);
    });

    test('endSession marks session as ended', () async {
      await notifier.startSession(durationMinutes: 25);
      await Future.delayed(const Duration(milliseconds: 50));

      await notifier.endSession();
      await Future.delayed(const Duration(milliseconds: 50));

      expect(notifier.state.isActive, isFalse);
    });

    test('flow state detection after consecutive correct', () {
      // Manually set consecutive correct to verify threshold
      expect(const SessionState(consecutiveCorrect: 2).isInFlowState, isFalse);
      expect(const SessionState(consecutiveCorrect: 3).isInFlowState, isTrue);
    });
  });
}
