// =============================================================================
// Cena Adaptive Learning Platform — Offline Sync Service Contract
// Durable offline-first event queue with three-tier classification,
// clock skew detection, and conflict resolution.
// =============================================================================

import 'dart:async';

import '../models/domain_models.dart';

// ---------------------------------------------------------------------------
// Idempotency Key Generation
// ---------------------------------------------------------------------------

/// Generates globally unique, deterministic idempotency keys for offline events.
///
/// Format: `{UUID v4}:{clientSequenceNumber}`
/// The sequence number provides ordering guarantees within a single client,
/// while the UUID ensures uniqueness across devices.
abstract class IdempotencyKeyGenerator {
  /// Generate the next idempotency key, incrementing the sequence counter.
  String next();

  /// Current sequence number (persisted across app restarts).
  int get currentSequence;

  /// Reset the sequence counter (only after server acknowledges all events).
  Future<void> resetTo(int sequence);
}

// ---------------------------------------------------------------------------
// Clock Skew Detection
// ---------------------------------------------------------------------------

/// Estimates and compensates for client-server clock offset.
///
/// Uses NTP-style round-trip estimation from server response timestamps.
/// The offset is applied to all outbound event timestamps so the server
/// can reconstruct accurate ordering.
abstract class ClockSkewDetector {
  /// Estimated offset: `serverTime - clientTime` in milliseconds.
  ///
  /// Positive = client clock is behind server.
  /// Negative = client clock is ahead of server.
  int get estimatedOffsetMs;

  /// Update the offset estimate from a server response.
  ///
  /// [clientSendTime] — when the client sent the request.
  /// [serverTimestamp] — the server's timestamp in the response.
  /// [clientReceiveTime] — when the client received the response.
  void updateEstimate({
    required DateTime clientSendTime,
    required DateTime serverTimestamp,
    required DateTime clientReceiveTime,
  });

  /// Adjust a client timestamp to approximate server time.
  DateTime adjustToServerTime(DateTime clientTime);

  /// Number of samples collected for the estimate.
  int get sampleCount;

  /// Confidence level of the current estimate [0.0, 1.0].
  /// Increases with more samples, decreases with high variance.
  double get confidence;
}

// ---------------------------------------------------------------------------
// Offline Event Queue (Drift/SQLite-backed)
// ---------------------------------------------------------------------------

/// Durable, ordered queue of events generated while offline.
///
/// Backed by drift (SQLite) to survive app restarts, crashes, and OS kills.
/// Events are stored in insertion order with monotonic sequence numbers.
abstract class OfflineEventQueue {
  /// Enqueue a new event for later sync.
  ///
  /// Assigns the next idempotency key and sequence number.
  /// Classifies the event based on [eventType].
  Future<OfflineEvent> enqueue({
    required String eventType,
    required Map<String, dynamic> payload,
  });

  /// Peek at the next [count] events without removing them.
  Future<List<OfflineEvent>> peek(int count);

  /// Retrieve all pending events in order.
  Future<List<OfflineEvent>> getAll();

  /// Remove events that have been acknowledged by the server.
  ///
  /// [acknowledgedUpTo] is the highest sequence number confirmed.
  Future<int> removeAcknowledged(int acknowledgedUpTo);

  /// Mark a specific event as failed with an error message.
  Future<void> markFailed(String idempotencyKey, String error);

  /// Increment the retry count for a specific event.
  Future<void> incrementRetry(String idempotencyKey);

  /// Total number of pending (unsynced) events.
  Future<int> get pendingCount;

  /// Stream that emits the current pending count whenever it changes.
  Stream<int> get pendingCountStream;

  /// Remove all events (e.g., on logout).
  Future<void> clear();

  /// Whether the queue is empty.
  Future<bool> get isEmpty;
}

// ---------------------------------------------------------------------------
// Event Classifier
// ---------------------------------------------------------------------------

/// Classifies offline events into the three-tier sync categories.
///
/// Classification determines how the server handles each event during sync:
///
/// - **Unconditional** (weight=1.0): Always accepted. Examples: annotations,
///   UI preferences, skip events.
///
/// - **Conditional** (weight=0.75): Server validates that the context is still
///   valid. Examples: answer attempts (exercise may have been reassigned),
///   hint requests (session may have expired).
///
/// - **ServerAuthoritative** (weight=0.0 to 1.0): Server recalculates
///   entirely. Client value is advisory. Examples: mastery updates, XP
///   calculations, streak changes.
abstract class EventClassifier {
  /// Classify an event by its type name.
  EventClassification classify(String eventType);

  /// Get the weight applied to an event of this classification.
  ///
  /// - Unconditional: 1.0 (full weight, always accepted)
  /// - Conditional: 0.75 (reduced weight, context-validated)
  /// - ServerAuthoritative: 0.0 (server recalculates from scratch)
  double weightFor(EventClassification classification);

  /// Well-known classification mappings.
  static const Map<String, EventClassification> defaultClassifications = {
    // Unconditional — always accepted
    'AddAnnotation': EventClassification.unconditional,
    'SkipQuestion': EventClassification.unconditional,
    'SwitchApproach': EventClassification.unconditional,

    // Conditional — server validates context
    'AttemptConcept': EventClassification.conditional,
    'RequestHint': EventClassification.conditional,
    'StartSession': EventClassification.conditional,
    'EndSession': EventClassification.conditional,

    // Server-authoritative — server recalculates
    'MasteryUpdate': EventClassification.serverAuthoritative,
    'XpCalculation': EventClassification.serverAuthoritative,
    'StreakCalculation': EventClassification.serverAuthoritative,
  };
}

// ---------------------------------------------------------------------------
// Conflict Resolver
// ---------------------------------------------------------------------------

/// Resolves conflicts between offline client events and server state.
///
/// Uses a weighted approach based on event classification:
/// - full (1.0): client value accepted as-is
/// - reduced (0.75): client value accepted with reduced confidence
/// - historical (0.0): client value discarded, server value used
abstract class ConflictResolver {
  /// Resolve a single sync correction.
  ///
  /// Returns the merged value to apply locally.
  dynamic resolve(SyncCorrection correction);

  /// Apply a batch of corrections to local state.
  ///
  /// Corrections are applied in order. For ServerAuthoritative events,
  /// the server value completely replaces the client value.
  /// For Conditional events, a weighted merge is performed.
  Future<void> applyCorrections(List<SyncCorrection> corrections);

  /// Weight constants for conflict resolution.
  static const double weightFull = 1.0;
  static const double weightReduced = 0.75;
  static const double weightHistorical = 0.0;
}

// ---------------------------------------------------------------------------
// Sync Manager — Orchestrator
// ---------------------------------------------------------------------------

/// Orchestrates the offline sync lifecycle.
///
/// Workflow:
/// 1. Detect connectivity restoration
/// 2. Perform clock skew handshake with server
/// 3. Classify all pending events
/// 4. Submit batch sync request
/// 5. Process server response (accepted, corrected, rejected)
/// 6. Apply corrections via [ConflictResolver]
/// 7. Purge acknowledged events from queue
/// 8. Emit updated [SyncStatus]
abstract class SyncManager {
  /// Current sync status.
  SyncStatus get status;

  /// Stream of sync status changes for UI binding.
  Stream<SyncStatus> get statusStream;

  /// Timestamp of the last successful sync.
  DateTime? get lastSyncTime;

  /// Stream of last sync time updates.
  Stream<DateTime?> get lastSyncTimeStream;

  /// Trigger an immediate sync attempt.
  ///
  /// Returns true if sync completed successfully, false if there were
  /// errors or conflicts that need user attention.
  Future<bool> syncNow();

  /// Start automatic sync monitoring.
  ///
  /// Listens for connectivity changes and triggers sync when online.
  /// Also performs periodic sync at [interval] when connected.
  Future<void> startAutoSync({
    Duration interval = const Duration(minutes: 5),
  });

  /// Stop automatic sync monitoring.
  Future<void> stopAutoSync();

  /// Perform the initial clock skew handshake with the server.
  ///
  /// Should be called once on app startup when connectivity is available.
  Future<void> calibrateClock();

  /// Number of events pending sync.
  Future<int> get pendingEventCount;

  /// Stream of pending event count for UI badges.
  Stream<int> get pendingEventCountStream;

  /// Whether there are unresolved conflicts requiring user attention.
  bool get hasConflicts;

  /// Get all unresolved conflicts for display.
  Future<List<SyncCorrection>> getUnresolvedConflicts();

  /// Accept a server correction for a specific conflict.
  Future<void> acceptCorrection(String idempotencyKey);

  /// Release all resources.
  void dispose();
}

// ---------------------------------------------------------------------------
// Connectivity Monitor
// ---------------------------------------------------------------------------

/// Monitors network connectivity state for sync triggering.
abstract class ConnectivityMonitor {
  /// Whether the device currently has internet access.
  bool get isOnline;

  /// Stream that emits on connectivity changes.
  Stream<bool> get onConnectivityChanged;

  /// Start monitoring connectivity.
  Future<void> start();

  /// Stop monitoring.
  Future<void> stop();
}
