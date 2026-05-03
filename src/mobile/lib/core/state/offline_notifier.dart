// =============================================================================
// Cena Adaptive Learning Platform — Offline Notifier
// Monitors the offline event queue and sync lifecycle.
// =============================================================================

import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../models/domain_models.dart';
import '../services/offline_sync_service.dart';
import 'derived_providers.dart' show syncManagerProvider, connectivityMonitorProvider;

// ---------------------------------------------------------------------------
// State
// ---------------------------------------------------------------------------

/// Immutable snapshot of the offline sync state.
class OfflineState {
  const OfflineState({
    this.queuedEventCount = 0,
    this.syncStatus = SyncStatus.idle,
    this.lastSyncTime,
    this.isOnline = true,
    this.lastError,
    this.conflictCount = 0,
  });

  final int queuedEventCount;
  final SyncStatus syncStatus;
  final DateTime? lastSyncTime;
  final bool isOnline;
  final String? lastError;
  final int conflictCount;

  bool get hasPendingEvents => queuedEventCount > 0;
  bool get hasConflicts => conflictCount > 0;
  bool get isSyncing => syncStatus == SyncStatus.syncing;

  OfflineState copyWith({
    int? queuedEventCount,
    SyncStatus? syncStatus,
    DateTime? lastSyncTime,
    bool? isOnline,
    String? lastError,
    int? conflictCount,
    bool clearError = false,
  }) {
    return OfflineState(
      queuedEventCount: queuedEventCount ?? this.queuedEventCount,
      syncStatus: syncStatus ?? this.syncStatus,
      lastSyncTime: lastSyncTime ?? this.lastSyncTime,
      isOnline: isOnline ?? this.isOnline,
      lastError: clearError ? null : (lastError ?? this.lastError),
      conflictCount: conflictCount ?? this.conflictCount,
    );
  }
}

// ---------------------------------------------------------------------------
// Notifier
// ---------------------------------------------------------------------------

/// Monitors the offline event queue, sync lifecycle, and network connectivity.
///
/// Subscribes to four streams from [SyncManager] and [ConnectivityMonitor]:
/// - `statusStream`            → sync status changes
/// - `pendingEventCountStream` → queue depth changes
/// - `lastSyncTimeStream`      → timestamp of last successful sync
/// - `conflictCountStream`     → server-side conflict corrections
/// - `onConnectivityChanged`   → online/offline transitions
class OfflineNotifier extends StateNotifier<OfflineState> {
  OfflineNotifier({
    required this.syncManager,
    required this.connectivityMonitor,
  }) : super(OfflineState(isOnline: connectivityMonitor.isOnline)) {
    _startMonitoring();
  }

  final SyncManager syncManager;
  final ConnectivityMonitor connectivityMonitor;
  final List<StreamSubscription<dynamic>> _subscriptions = [];

  // ---- Public API ----

  /// Trigger an immediate sync attempt.
  /// Returns true when sync completed without errors.
  Future<bool> syncNow() => syncManager.syncNow();

  // ---- Internal monitoring ----

  void _startMonitoring() {
    _subscriptions.add(
      syncManager.statusStream.listen((status) {
        state = state.copyWith(syncStatus: status);
      }),
    );

    _subscriptions.add(
      syncManager.pendingEventCountStream.listen((count) {
        state = state.copyWith(queuedEventCount: count);
      }),
    );

    _subscriptions.add(
      syncManager.lastSyncTimeStream.listen((time) {
        state = state.copyWith(lastSyncTime: time);
      }),
    );

    _subscriptions.add(
      syncManager.conflictCountStream.listen((count) {
        state = state.copyWith(conflictCount: count);
      }),
    );

    _subscriptions.add(
      connectivityMonitor.onConnectivityChanged.listen((online) {
        state = state.copyWith(isOnline: online);
        // Trigger sync automatically when coming back online.
        if (online && state.hasPendingEvents) {
          syncManager.syncNow();
        }
      }),
    );
  }

  @override
  void dispose() {
    for (final sub in _subscriptions) {
      sub.cancel();
    }
    super.dispose();
  }
}

// ---------------------------------------------------------------------------
// Providers
// ---------------------------------------------------------------------------

/// Offline sync state provider — kept alive (sync is always relevant).
final offlineProvider = StateNotifierProvider<OfflineNotifier, OfflineState>(
  (ref) => OfflineNotifier(
    syncManager: ref.watch(syncManagerProvider) as SyncManager,
    connectivityMonitor:
        ref.watch(connectivityMonitorProvider) as ConnectivityMonitor,
  ),
);
