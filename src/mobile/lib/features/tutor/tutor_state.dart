// =============================================================================
// Cena Adaptive Learning Platform — AI Tutor State (MOB-AI-001)
// =============================================================================

import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/services/websocket_service.dart';
import '../../core/state/derived_providers.dart'
    show webSocketServiceProvider;

// ---------------------------------------------------------------------------
// Models
// ---------------------------------------------------------------------------

enum MessageRole { student, tutor, system }

class TutorMessage {
  const TutorMessage({
    required this.id,
    required this.role,
    required this.text,
    required this.timestamp,
    this.isStreaming = false,
  });

  final String id;
  final MessageRole role;
  final String text;
  final DateTime timestamp;
  final bool isStreaming;

  TutorMessage copyWith({String? text, bool? isStreaming}) => TutorMessage(
        id: id,
        role: role,
        text: text ?? this.text,
        timestamp: timestamp,
        isStreaming: isStreaming ?? this.isStreaming,
      );
}

// ---------------------------------------------------------------------------
// State
// ---------------------------------------------------------------------------

class TutorChatState {
  const TutorChatState({
    this.messages = const [],
    this.isConnected = false,
    this.isTutorTyping = false,
    this.sessionId,
    this.error,
  });

  final List<TutorMessage> messages;
  final bool isConnected;
  final bool isTutorTyping;
  final String? sessionId;
  final String? error;

  TutorChatState copyWith({
    List<TutorMessage>? messages,
    bool? isConnected,
    bool? isTutorTyping,
    String? sessionId,
    String? error,
    bool clearError = false,
  }) {
    return TutorChatState(
      messages: messages ?? this.messages,
      isConnected: isConnected ?? this.isConnected,
      isTutorTyping: isTutorTyping ?? this.isTutorTyping,
      sessionId: sessionId ?? this.sessionId,
      error: clearError ? null : (error ?? this.error),
    );
  }
}

// ---------------------------------------------------------------------------
// Notifier
// ---------------------------------------------------------------------------

class TutorChatNotifier extends StateNotifier<TutorChatState> {
  TutorChatNotifier({required this.webSocketService})
      : super(const TutorChatState()) {
    _subscribe();
  }

  final WebSocketService webSocketService;
  final List<StreamSubscription<dynamic>> _subs = [];
  int _messageCounter = 0;

  void _subscribe() {
    _subs.add(
      webSocketService.messageStream.listen(_handleMessage),
    );
    _subs.add(
      webSocketService.connectionState.listen((cs) {
        state = state.copyWith(
          isConnected: cs == ConnectionState.connected,
        );
      }),
    );
  }

  void _handleMessage(MessageEnvelope envelope) {
    switch (envelope.type) {
      case 'TutoringStarted':
        final sid = envelope.payload['sessionId'] as String?;
        final greeting = envelope.payload['greeting'] as String?;
        state = state.copyWith(
          sessionId: sid,
          isTutorTyping: false,
          messages: [
            ...state.messages,
            if (greeting != null)
              TutorMessage(
                id: 'tutor-${++_messageCounter}',
                role: MessageRole.tutor,
                text: greeting,
                timestamp: DateTime.now(),
              ),
          ],
        );
      case 'TutorMessage':
        final text = envelope.payload['text'] as String? ?? '';
        final isComplete =
            envelope.payload['isComplete'] as bool? ?? true;

        if (isComplete) {
          // Final message — replace streaming placeholder if any, or add new
          final msgs = [...state.messages];
          final streamIdx =
              msgs.lastIndexWhere((m) => m.isStreaming);
          if (streamIdx >= 0) {
            msgs[streamIdx] =
                msgs[streamIdx].copyWith(text: text, isStreaming: false);
          } else {
            msgs.add(TutorMessage(
              id: 'tutor-${++_messageCounter}',
              role: MessageRole.tutor,
              text: text,
              timestamp: DateTime.now(),
            ));
          }
          state = state.copyWith(messages: msgs, isTutorTyping: false);
        } else {
          // Streaming chunk — append or create streaming message
          final msgs = [...state.messages];
          final streamIdx =
              msgs.lastIndexWhere((m) => m.isStreaming);
          if (streamIdx >= 0) {
            msgs[streamIdx] = msgs[streamIdx].copyWith(
              text: msgs[streamIdx].text + text,
            );
          } else {
            msgs.add(TutorMessage(
              id: 'tutor-${++_messageCounter}',
              role: MessageRole.tutor,
              text: text,
              timestamp: DateTime.now(),
              isStreaming: true,
            ));
          }
          state = state.copyWith(messages: msgs, isTutorTyping: true);
        }
      case 'TutoringEnded':
        state = state.copyWith(
          isTutorTyping: false,
          messages: [
            ...state.messages,
            TutorMessage(
              id: 'sys-${++_messageCounter}',
              role: MessageRole.system,
              text: 'Tutoring session ended.',
              timestamp: DateTime.now(),
            ),
          ],
        );
    }
  }

  /// Send a student message to the tutor.
  void sendMessage(String text) {
    if (text.trim().isEmpty) return;

    state = state.copyWith(
      messages: [
        ...state.messages,
        TutorMessage(
          id: 'student-${++_messageCounter}',
          role: MessageRole.student,
          text: text.trim(),
          timestamp: DateTime.now(),
        ),
      ],
      isTutorTyping: true,
    );

    // Send via WebSocket — the hub routes to the TutorActor
    webSocketService.switchApproach(SwitchApproach(
      sessionId: state.sessionId ?? '',
      preferenceHint: 'tutor:$text',
    ));
  }

  /// Send a quick-reply chip action.
  void sendQuickReply(String action) => sendMessage(action);

  /// Clear the chat history.
  void clearChat() {
    state = const TutorChatState();
    _messageCounter = 0;
  }

  @override
  void dispose() {
    for (final sub in _subs) {
      sub.cancel();
    }
    super.dispose();
  }
}

// ---------------------------------------------------------------------------
// Provider
// ---------------------------------------------------------------------------

final tutorChatProvider =
    StateNotifierProvider.autoDispose<TutorChatNotifier, TutorChatState>(
  (ref) => TutorChatNotifier(
    webSocketService: ref.watch(webSocketServiceProvider),
  ),
);
