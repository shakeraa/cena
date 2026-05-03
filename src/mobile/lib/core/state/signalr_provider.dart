// =============================================================================
// Cena Adaptive Learning Platform — SignalR Riverpod Providers (MOB-003)
// =============================================================================

import 'package:firebase_auth/firebase_auth.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import '../services/websocket_service.dart';
import '../services/websocket_service_impl.dart';
import 'app_state.dart';

/// Provides a lazily-initialised [SignalRWebSocketService] scoped to the
/// current [ProviderScope].  The hub URL is derived from [appConfigProvider].
final signalRProvider = Provider<SignalRWebSocketService>((ref) {
  final config = ref.watch(appConfigProvider);
  final service = SignalRWebSocketService(
    hubUrl: config.endpoints.webSocketUrl,
    tokenProvider: () async {
      final user = FirebaseAuth.instance.currentUser;
      return await user?.getIdToken() ?? '';
    },
  );
  ref.onDispose(() => service.dispose());
  return service;
});

/// Exposes the live [SignalRConnectionState] stream from [signalRProvider].
final signalRConnectionStateProvider =
    StreamProvider<SignalRConnectionState>((ref) {
  return ref.watch(signalRProvider).signalRStateStream;
});

/// Exposes the coarse [ConnectionState] stream from [signalRProvider],
/// compatible with the existing [WebSocketService] contract.
final connectionStateProvider = StreamProvider<ConnectionState>((ref) {
  return ref.watch(signalRProvider).connectionState;
});

/// Exposes the raw server-push [MessageEnvelope] stream from [signalRProvider].
/// Listeners should filter by [MessageEnvelope.type] to handle specific events.
final serverEventsProvider = StreamProvider<MessageEnvelope>((ref) {
  return ref.watch(signalRProvider).messageStream;
});
