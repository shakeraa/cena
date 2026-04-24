// =============================================================================
// Cena Adaptive Learning Platform — Derived Providers
// Read-only selectors derived from the five core notifiers.
// All providers are auto-disposed when no UI is listening.
// =============================================================================

import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../models/domain_models.dart';
import '../services/websocket_service.dart';
import 'knowledge_graph_notifier.dart';
import 'offline_notifier.dart';
import 'outreach_notifier.dart';
import 'session_notifier.dart';
import 'user_notifier.dart';

// ---------------------------------------------------------------------------
// WebSocket service provider (canonical — override this in ProviderScope)
// ---------------------------------------------------------------------------

/// The canonical WebSocket service provider.
/// Override in the root ProviderScope at app startup with the real implementation.
final webSocketServiceProvider = Provider<WebSocketService>((ref) {
  throw UnimplementedError(
    'webSocketServiceProvider must be overridden in ProviderScope',
  );
});

/// The canonical SyncManager provider.
/// Override in the root ProviderScope at app startup with the real implementation.
final syncManagerProvider = Provider<dynamic>((ref) {
  throw UnimplementedError(
    'syncManagerProvider must be overridden in ProviderScope',
  );
});

/// The canonical ConnectivityMonitor provider.
/// Override in the root ProviderScope at app startup with the real implementation.
final connectivityMonitorProvider = Provider<dynamic>((ref) {
  throw UnimplementedError(
    'connectivityMonitorProvider must be overridden in ProviderScope',
  );
});

// ---------------------------------------------------------------------------
// Session-derived providers
// ---------------------------------------------------------------------------

/// Whether the student is currently in an active learning session.
final isSessionActiveProvider = Provider.autoDispose<bool>((ref) {
  return ref.watch(sessionProvider.select((s) => s.isActive));
});

/// Current accuracy rate for the active session [0.0, 1.0].
final sessionAccuracyProvider = Provider.autoDispose<double>((ref) {
  return ref.watch(sessionProvider.select((s) => s.accuracy));
});

/// Whether a break suggestion is active due to cognitive load.
final isBreakSuggestedProvider = Provider.autoDispose<bool>((ref) {
  return ref.watch(sessionProvider.select((s) => s.isBreakSuggested));
});

// ---------------------------------------------------------------------------
// WebSocket connection state
// ---------------------------------------------------------------------------

/// Live stream of the WebSocket connection state.
final connectionStateProvider =
    StreamProvider.autoDispose<ConnectionState>((ref) {
  return ref.watch(webSocketServiceProvider).connectionState;
});

// ---------------------------------------------------------------------------
// Offline-derived providers
// ---------------------------------------------------------------------------

/// Whether there are any events waiting to be synced.
final hasPendingEventsProvider = Provider.autoDispose<bool>((ref) {
  return ref.watch(offlineProvider.select((s) => s.hasPendingEvents));
});

/// Whether the device currently has network connectivity.
final isOnlineProvider = Provider.autoDispose<bool>((ref) {
  return ref.watch(offlineProvider.select((s) => s.isOnline));
});

/// Whether a sync is currently in progress.
final isSyncingProvider = Provider.autoDispose<bool>((ref) {
  return ref.watch(offlineProvider.select((s) => s.isSyncing));
});

// ---------------------------------------------------------------------------
// User-derived providers
// ---------------------------------------------------------------------------

/// Remaining "study energy" (LLM interaction budget) for today.
/// Server enforces the 50-cap; this is the client-side optimistic count.
final remainingStudyEnergyProvider = Provider.autoDispose<int>((ref) {
  return ref.watch(userProvider.select((s) => s.remainingStudyEnergy));
});

/// Whether the student has any study energy remaining.
final hasStudyEnergyProvider = Provider.autoDispose<bool>((ref) {
  return ref.watch(userProvider.select((s) => s.hasStudyEnergy));
});

/// Whether the student is authenticated.
final isAuthenticatedProvider = Provider.autoDispose<bool>((ref) {
  return ref.watch(userProvider.select((s) => s.isAuthenticated));
});

/// The current authenticated student, or null.
final currentStudentStateProvider = Provider.autoDispose<Student?>((ref) {
  return ref.watch(userProvider.select((s) => s.student));
});

/// Daily progress as a fraction [0.0, 1.0].
final dailyProgressProvider = Provider.autoDispose<double>((ref) {
  return ref.watch(userProvider.select((s) => s.dailyProgress));
});

// ---------------------------------------------------------------------------
// Knowledge graph-derived providers
// ---------------------------------------------------------------------------

/// Filtered and searched concept nodes ready for rendering.
final filteredGraphNodesProvider =
    Provider.autoDispose<List<ConceptNode>>((ref) {
  return ref.watch(
    knowledgeGraphProvider.select((s) => s.filteredNodes),
  );
});

/// The currently selected concept node, or null.
final selectedConceptProvider = Provider.autoDispose<ConceptNode?>((ref) {
  return ref.watch(knowledgeGraphProvider.select((s) => s.selectedNode));
});

/// Concept IDs along the highlighted prerequisite path.
final highlightedPathProvider = Provider.autoDispose<List<String>>((ref) {
  return ref.watch(knowledgeGraphProvider.select((s) => s.highlightedPath));
});

/// Current zoom level of the knowledge graph.
final graphZoomLevelProvider = Provider.autoDispose<double>((ref) {
  return ref.watch(knowledgeGraphProvider.select((s) => s.zoomLevel));
});

// ---------------------------------------------------------------------------
// Outreach-derived providers
// ---------------------------------------------------------------------------

/// Count of unread in-app notifications.
final unreadNotificationCountProvider = Provider.autoDispose<int>((ref) {
  return ref.watch(outreachProvider.select((s) => s.unreadCount));
});

/// Whether the streak expiry warning banner should be shown.
final isStreakWarningActiveProvider = Provider.autoDispose<bool>((ref) {
  return ref.watch(outreachProvider.select((s) => s.isStreakWarningActive));
});

/// All pending in-app notifications.
final pendingNotificationsProvider =
    Provider.autoDispose<List<AppNotification>>((ref) {
  return ref.watch(
    outreachProvider.select((s) => s.pendingNotifications),
  );
});
