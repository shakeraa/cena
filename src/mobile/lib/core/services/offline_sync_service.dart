// =============================================================================
// Cena Adaptive Learning Platform — Offline Sync Service Interfaces
// Abstracts the SQLite event queue, sync lifecycle, and connectivity.
// =============================================================================

import 'dart:async';

import '../models/domain_models.dart';

// ---------------------------------------------------------------------------
// SyncManager — event queue + sync lifecycle
// ---------------------------------------------------------------------------

/// Manages the offline event queue and synchronisation with the server.
///
/// Concrete implementation backed by Drift (SQLite) registered via
/// ProviderScope override at app startup.
abstract class SyncManager {
  /// Emits the current [SyncStatus] whenever it changes.
  Stream<SyncStatus> get statusStream;

  /// Emits the number of events in the pending queue whenever it changes.
  Stream<int> get pendingEventCountStream;

  /// Emits the timestamp of the most recent successful sync.
  Stream<DateTime> get lastSyncTimeStream;

  /// Emits conflict count whenever the server returns corrections.
  Stream<int> get conflictCountStream;

  /// Number of events currently waiting to be synced.
  int get pendingEventCount;

  /// Enqueue an event for later sync.
  Future<void> enqueue(OfflineEvent event);

  /// Trigger an immediate sync attempt.
  /// Returns true if the sync completed without errors.
  Future<bool> syncNow();

  /// Clear all resolved/accepted events from the queue.
  Future<void> pruneAccepted();
}

// ---------------------------------------------------------------------------
// ConnectivityMonitor — network reachability
// ---------------------------------------------------------------------------

/// Monitors network connectivity and exposes reachability changes.
///
/// Concrete implementation uses `connectivity_plus` package and is registered
/// via ProviderScope override at app startup.
abstract class ConnectivityMonitor {
  /// Emits true when the device has network access, false when offline.
  Stream<bool> get onConnectivityChanged;

  /// Synchronous snapshot of the current reachability state.
  bool get isOnline;
}
