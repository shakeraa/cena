// =============================================================================
// Cena Adaptive Learning Platform — SignalR WebSocket Implementation (MOB-003)
// Concrete SignalR JSON protocol client with reconnection and offline queuing.
// Implements [WebSocketService] for use via dependency injection.
// =============================================================================

import 'dart:async';
import 'dart:convert';
import 'dart:math';
import 'package:web_socket_channel/web_socket_channel.dart';

import 'websocket_service.dart';

// ---------------------------------------------------------------------------
// Extended connection state (includes handshaking / failed)
// ---------------------------------------------------------------------------

enum SignalRConnectionState {
  disconnected,
  connecting,
  handshaking,
  connected,
  reconnecting,
  failed,
}

// ---------------------------------------------------------------------------
// Concrete SignalR service
// ---------------------------------------------------------------------------

/// Production implementation of [WebSocketService] using the SignalR JSON
/// sub-protocol over a raw WebSocket connection.
///
/// Features:
/// - SignalR v1 JSON handshake
/// - Exponential back-off reconnection (up to [_maxReconnectAttempts])
/// - Offline queue: invocations sent while disconnected are replayed on reconnect
/// - 15-second client-side heartbeat ping (type 6)
class SignalRWebSocketService implements WebSocketService {
  final String hubUrl;
  final Future<String> Function() tokenProvider;

  WebSocketChannel? _channel;

  final _messageController = StreamController<MessageEnvelope>.broadcast();
  final _stateController =
      StreamController<ConnectionState>.broadcast();
  final _signalRStateController =
      StreamController<SignalRConnectionState>.broadcast();
  final _offlineQueue = <String>[];

  ConnectionState _connectionState = ConnectionState.disconnected;
  SignalRConnectionState _signalRState = SignalRConnectionState.disconnected;

  int _reconnectAttempts = 0;
  int _invocationId = 0;
  Timer? _heartbeatTimer;
  Timer? _reconnectTimer;

  static const _separator = '\x1e'; // Record separator 0x1E
  static const _maxReconnectAttempts = 10;
  static const _heartbeatInterval = Duration(seconds: 15);

  /// Fine-grained state stream including [SignalRConnectionState.handshaking]
  /// and [SignalRConnectionState.failed].
  Stream<SignalRConnectionState> get signalRStateStream =>
      _signalRStateController.stream;

  SignalRConnectionState get signalRState => _signalRState;

  SignalRWebSocketService({
    required this.hubUrl,
    required this.tokenProvider,
  });

  // -------------------------------------------------------------------------
  // WebSocketService — connection
  // -------------------------------------------------------------------------

  @override
  Stream<MessageEnvelope> get messageStream => _messageController.stream;

  @override
  Stream<ConnectionState> get connectionState => _stateController.stream;

  @override
  ConnectionState get currentConnectionState => _connectionState;

  @override
  Future<void> connect(String authToken) => _connect(token: authToken);

  /// Internal connect — reuses stored token on reconnect if [token] is null.
  Future<void> _connect({String? token}) async {
    _setSignalRState(SignalRConnectionState.connecting);
    _setConnectionState(ConnectionState.connecting);
    try {
      final resolvedToken = token ?? await tokenProvider();
      final uri = Uri.parse('$hubUrl?access_token=$resolvedToken');
      _channel = WebSocketChannel.connect(uri);

      _setSignalRState(SignalRConnectionState.handshaking);
      _channel!.sink.add('{"protocol":"json","version":1}$_separator');

      _channel!.stream.listen(
        _onMessage,
        onError: _onError,
        onDone: _onDisconnected,
      );
    } catch (e) {
      _setSignalRState(SignalRConnectionState.failed);
      _setConnectionState(ConnectionState.disconnected);
      _scheduleReconnect();
    }
  }

  @override
  Future<void> disconnect() async {
    _heartbeatTimer?.cancel();
    _reconnectTimer?.cancel();
    _channel?.sink.close();
    _channel = null;
    _setSignalRState(SignalRConnectionState.disconnected);
    _setConnectionState(ConnectionState.disconnected);
  }

  // -------------------------------------------------------------------------
  // Message handling
  // -------------------------------------------------------------------------

  void _onMessage(dynamic data) {
    final parts = data
        .toString()
        .split(_separator)
        .where((s) => s.isNotEmpty);
    for (final raw in parts) {
      try {
        final json = jsonDecode(raw) as Map<String, dynamic>;
        final type = json['type'] as int?;

        switch (type) {
          case null:
            // Handshake response — success has no "error" key.
            if (json.containsKey('error')) {
              _setSignalRState(SignalRConnectionState.failed);
              _setConnectionState(ConnectionState.disconnected);
              return;
            }
            _setSignalRState(SignalRConnectionState.connected);
            _setConnectionState(ConnectionState.connected);
            _reconnectAttempts = 0;
            _startHeartbeat();
            _flushOfflineQueue();
            break;
          case 1:
            // Server-push invocation — map to [MessageEnvelope].
            final target = json['target'] as String? ?? '';
            final args = json['arguments'] as List<dynamic>? ?? [];
            final payload = args.isNotEmpty && args.first is Map<String, dynamic>
                ? args.first as Map<String, dynamic>
                : <String, dynamic>{};
            _messageController.add(MessageEnvelope(
              type: target,
              payload: payload,
              receivedAt: DateTime.now(),
            ));
            break;
          case 6:
            // Ping — pong immediately.
            _channel?.sink.add('{"type":6}$_separator');
            break;
          case 7:
            // Server-initiated close.
            disconnect();
            break;
        }
      } catch (_) {
        // Malformed frame; skip silently.
      }
    }
  }

  void _startHeartbeat() {
    _heartbeatTimer?.cancel();
    _heartbeatTimer = Timer.periodic(_heartbeatInterval, (_) {
      _channel?.sink.add('{"type":6}$_separator');
    });
  }

  void _flushOfflineQueue() {
    for (final msg in _offlineQueue) {
      _channel?.sink.add(msg);
    }
    _offlineQueue.clear();
  }

  // -------------------------------------------------------------------------
  // Generic invocation helper
  // -------------------------------------------------------------------------

  void _sendOrQueue(String method, Map<String, dynamic> args) {
    final id = '${++_invocationId}';
    final frame =
        jsonEncode({
          'type': 1,
          'target': method,
          'invocationId': id,
          'arguments': [args],
        }) +
        _separator;

    if (_connectionState == ConnectionState.connected) {
      _channel?.sink.add(frame);
    } else {
      _offlineQueue.add(frame);
    }
  }

  // -------------------------------------------------------------------------
  // WebSocketService — outbound commands
  // -------------------------------------------------------------------------

  @override
  Future<void> startSession(StartSession command) async =>
      _sendOrQueue('StartSession', command.toJson());

  @override
  Future<void> attemptConcept(AttemptConcept command) async =>
      _sendOrQueue('SubmitAnswer', command.toJson());

  @override
  Future<void> requestHint(RequestHint command) async =>
      _sendOrQueue('RequestHint', command.toJson());

  @override
  Future<void> skipQuestion(SkipQuestion command) async =>
      _sendOrQueue('SkipQuestion', command.toJson());

  @override
  Future<void> switchApproach(SwitchApproach command) async =>
      _sendOrQueue('SwitchApproach', command.toJson());

  @override
  Future<void> endSession(EndSession command) async =>
      _sendOrQueue('EndSession', command.toJson());

  // -------------------------------------------------------------------------
  // Error and reconnection
  // -------------------------------------------------------------------------

  void _onError(dynamic error) {
    _setSignalRState(SignalRConnectionState.reconnecting);
    _setConnectionState(ConnectionState.reconnecting);
    _scheduleReconnect();
  }

  void _onDisconnected() {
    if (_connectionState != ConnectionState.disconnected) {
      _setSignalRState(SignalRConnectionState.reconnecting);
      _setConnectionState(ConnectionState.reconnecting);
      _scheduleReconnect();
    }
  }

  void _scheduleReconnect() {
    if (_reconnectAttempts >= _maxReconnectAttempts) {
      _setSignalRState(SignalRConnectionState.failed);
      _setConnectionState(ConnectionState.disconnected);
      return;
    }
    final delayMs =
        (1000 * pow(2, _reconnectAttempts).toInt()).clamp(1000, 30000) +
        Random().nextInt(1000);
    _reconnectAttempts++;
    _reconnectTimer?.cancel();
    _reconnectTimer =
        Timer(Duration(milliseconds: delayMs), () => _connect());
  }

  void _setConnectionState(ConnectionState s) {
    _connectionState = s;
    _stateController.add(s);
  }

  void _setSignalRState(SignalRConnectionState s) {
    _signalRState = s;
    _signalRStateController.add(s);
  }

  /// Release all resources.  Call from [Provider.onDispose].
  void dispose() {
    disconnect();
    _messageController.close();
    _stateController.close();
    _signalRStateController.close();
  }
}
