// =============================================================================
// Cena Adaptive Learning Platform — Offline Sync Service Interfaces
// Abstracts the event queue, sync lifecycle, connectivity, and conflict
// resolution. SharedPreferences-backed; Drift upgrade is a future task.
// =============================================================================

import 'dart:async';

import '../models/domain_models.dart';

// ---------------------------------------------------------------------------
// OutgoingCommand — write-ahead command record
// ---------------------------------------------------------------------------

/// A command persisted before it is sent over WebSocket.
/// Survives app restarts; retried up to [maxRetries] times.
class OutgoingCommand {
  OutgoingCommand({
    required this.id,
    required this.type,
    required this.payload,
    required this.enqueuedAt,
    this.retryCount = 0,
    this.lastError,
  });

  final String id;
  final String type;
  final Map<String, dynamic> payload;
  final DateTime enqueuedAt;
  final int retryCount;
  final String? lastError;

  static const int maxRetries = 5;

  OutgoingCommand copyWith({int? retryCount, String? lastError}) {
    return OutgoingCommand(
      id: id,
      type: type,
      payload: payload,
      enqueuedAt: enqueuedAt,
      retryCount: retryCount ?? this.retryCount,
      lastError: lastError ?? this.lastError,
    );
  }

  Map<String, dynamic> toJson() => {
        'id': id,
        'type': type,
        'payload': payload,
        'enqueuedAt': enqueuedAt.toIso8601String(),
        'retryCount': retryCount,
        if (lastError != null) 'lastError': lastError,
      };

  factory OutgoingCommand.fromJson(Map<String, dynamic> json) =>
      OutgoingCommand(
        id: json['id'] as String,
        type: json['type'] as String,
        payload: json['payload'] as Map<String, dynamic>,
        enqueuedAt: DateTime.parse(json['enqueuedAt'] as String),
        retryCount: (json['retryCount'] as int?) ?? 0,
        lastError: json['lastError'] as String?,
      );
}

/// Result returned by [DurableCommandQueue.retryPending].
class DurableRetryResult {
  const DurableRetryResult({
    required this.succeeded,
    required this.failed,
    required this.remaining,
  });

  final int succeeded;
  final int failed;
  final int remaining;
}

// ---------------------------------------------------------------------------
// DurableCommandQueue — crash-safe write-ahead queue
// ---------------------------------------------------------------------------

/// Persists outgoing commands to durable storage BEFORE sending over
/// WebSocket so that no student action is lost on crash.
abstract class DurableCommandQueue {
  /// Persist the command then attempt immediate WebSocket send.
  /// Returns the command [id].
  Future<String> enqueueCommand(OutgoingCommand cmd);

  /// Remove an acknowledged command from the queue.
  Future<void> markAcknowledged(String commandId);

  /// All unacknowledged commands ordered by [enqueuedAt].
  Future<List<OutgoingCommand>> getPendingCommands();

  /// Retry all pending commands in order.
  Future<DurableRetryResult> retryPending();

  /// Count of unacknowledged commands.
  Future<int> pendingCount();
}

// ---------------------------------------------------------------------------
// OfflineEventQueue — ordered event queue for sync
// ---------------------------------------------------------------------------

/// Stores student events offline, ordered by monotonic sequence number.
abstract class OfflineEventQueue {
  /// Enqueue an event. Returns the persisted [OfflineEvent] with
  /// assigned [idempotencyKey] and [sequenceNumber].
  Future<OfflineEvent> enqueue({
    required String eventType,
    required Map<String, dynamic> payload,
  });

  /// Return the next [count] events without removing them.
  Future<List<OfflineEvent>> peek(int count);

  /// Return all pending events ordered by sequence number.
  Future<List<OfflineEvent>> getAll();

  /// Delete all events where sequenceNumber <= [acknowledgedUpTo].
  /// Returns the count of events removed.
  Future<int> removeAcknowledged(int acknowledgedUpTo);

  /// Set the [lastError] on a specific event.
  Future<void> markFailed(String idempotencyKey, String error);

  /// Increment the [retryCount] on a specific event.
  Future<void> incrementRetry(String idempotencyKey);

  /// Number of pending events (synchronous snapshot).
  Future<int> get pendingCount;

  /// Stream of pending event counts; emits after every mutation.
  Stream<int> get pendingCountStream;

  /// True when no events are pending.
  Future<bool> get isEmpty;

  /// Remove all events (on student logout).
  Future<void> clear();
}

// ---------------------------------------------------------------------------
// EventClassifier — classifies events into sync tiers
// ---------------------------------------------------------------------------

/// Classifies event types into one of three sync tiers.
abstract class EventClassifier {
  /// Return the [EventClassification] for [eventType].
  EventClassification classify(String eventType);

  /// Return the merge weight for a given classification.
  /// 1.0 = unconditional, 0.75 = conditional, 0.0 = server-authoritative.
  double weightFor(EventClassification classification);
}

// ---------------------------------------------------------------------------
// IdempotencyKeyGenerator — UUID:sequence key generation
// ---------------------------------------------------------------------------

/// Generates idempotency keys of the form `{uuid}:{sequence}`.
/// Sequence is monotonically increasing and persists across restarts.
abstract class IdempotencyKeyGenerator {
  /// Return the next `{uuid}:{sequence}` key.
  String next();

  /// Current sequence counter value.
  int get currentSequence;

  /// Load persisted sequence from storage (call on app start).
  Future<void> loadState();

  /// Reset the counter to [sequence] (called after server ack).
  Future<void> resetTo(int sequence);
}

// ---------------------------------------------------------------------------
// ClockSkewDetector — NTP-style client/server clock calibration
// ---------------------------------------------------------------------------

/// Estimates the offset between client and server clocks using NTP formula:
///   offset = ((T_server - T_clientSend) + (T_server - T_clientReceive)) / 2
abstract class ClockSkewDetector {
  /// Record a calibration sample.
  void updateEstimate({
    required DateTime clientSendTime,
    required DateTime serverTimestamp,
    required DateTime clientReceiveTime,
  });

  /// Running average offset in milliseconds (positive = client behind server).
  int get estimatedOffsetMs;

  /// Apply the estimated offset to [clientTime].
  DateTime adjustToServerTime(DateTime clientTime);

  /// Number of calibration samples collected so far.
  int get sampleCount;

  /// Confidence in the estimate [0.0, 1.0]; reaches 1.0 after 10 consistent
  /// samples. Decreases with high variance.
  double get confidence;
}

// ---------------------------------------------------------------------------
// ConflictResolver — weighted merge of server corrections
// ---------------------------------------------------------------------------

/// Resolves field conflicts between client and server values.
abstract class ConflictResolver {
  static const double weightFull = 1.0;
  static const double weightReduced = 0.75;
  static const double weightHistorical = 0.0;

  /// Resolve a single [SyncCorrection] and return the merged value.
  dynamic resolve(SyncCorrection correction);

  /// Apply a batch of corrections in order, calling [onApply] for each.
  Future<void> applyCorrections(
    List<SyncCorrection> corrections, {
    void Function(String idempotencyKey, dynamic value)? onApply,
  });
}

// ---------------------------------------------------------------------------
// SyncManager — full sync lifecycle orchestrator
// ---------------------------------------------------------------------------

/// Manages the offline event queue and synchronisation with the server.
///
/// Concrete implementation registered via ProviderScope override at app
/// startup.
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

  /// Start auto-sync: syncs when connectivity is restored and periodically.
  Future<void> startAutoSync({Duration interval});

  /// Stop auto-sync monitoring.
  void stopAutoSync();

  /// Clear all resolved/accepted events from the queue.
  Future<void> pruneAccepted();

  /// Whether there are unresolved server corrections.
  bool get hasConflicts;

  /// Return unresolved corrections.
  List<SyncCorrection> getUnresolvedConflicts();

  /// Mark a specific conflict as resolved.
  Future<void> acceptCorrection(String idempotencyKey);

  /// Release resources.
  void dispose();
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
