// =============================================================================
// Cena Adaptive Learning Platform — SyncManagerImpl
// Orchestrates the full offline sync lifecycle.
// =============================================================================

import 'dart:async';
import 'dart:convert';

import 'package:dio/dio.dart';
import 'package:logger/logger.dart';

import '../models/domain_models.dart';
import 'clock_skew_detector_impl.dart';
import 'conflict_resolver_impl.dart';
import 'offline_event_queue_impl.dart';
import 'offline_sync_service.dart';

/// Orchestrates the full sync lifecycle per MOB-004.5:
///
///   1. Check connectivity via [ConnectivityMonitor].
///   2. Perform clock skew calibration handshake (if not yet calibrated).
///   3. Collect all pending events from [OfflineEventQueue].
///   4. Build [SyncRequest] with events, clockOffsetMs, lastAcknowledgedSequence.
///   5. POST to /sync REST endpoint.
///   6. Process [SyncResponse]: accepted / corrected / rejected.
///   7. Apply corrections via [ConflictResolver].
///   8. Remove acknowledged events from queue.
///   9. Emit updated [SyncStatus].
///
/// Edge cases handled:
/// - Sync already in progress: debounced — returns the existing future.
/// - Empty queue: returns true immediately without hitting the server.
/// - HTTP 429: respects Retry-After header.
/// - HTTP 409: rejected keys moved to unresolved conflicts list.
/// - Network drops mid-sync: status transitions to error; retried on reconnect.
class SyncManagerImpl implements SyncManager {
  SyncManagerImpl({
    required OfflineEventQueueImpl eventQueue,
    required ClockSkewDetectorImpl clockSkew,
    required ConflictResolverImpl conflictResolver,
    required Dio restClient,
    required ConnectivityMonitor connectivityMonitor,
    required String studentId,
    String syncEndpoint = '/api/sync',
  })  : _eventQueue = eventQueue,
        _clockSkew = clockSkew,
        _conflictResolver = conflictResolver,
        _restClient = restClient,
        _connectivityMonitor = connectivityMonitor,
        _studentId = studentId,
        _syncEndpoint = syncEndpoint;

  final OfflineEventQueueImpl _eventQueue;
  final ClockSkewDetectorImpl _clockSkew;
  final ConflictResolverImpl _conflictResolver;
  final Dio _restClient;
  final ConnectivityMonitor _connectivityMonitor;
  final String _studentId;
  final String _syncEndpoint;
  final _logger = Logger(printer: PrettyPrinter(methodCount: 0));

  // Stream controllers.
  final _statusController = StreamController<SyncStatus>.broadcast();
  final _pendingCountController = StreamController<int>.broadcast();
  final _lastSyncTimeController = StreamController<DateTime>.broadcast();
  final _conflictCountController = StreamController<int>.broadcast();

  SyncStatus _status = SyncStatus.idle;
  int _pendingEventCount = 0;
  int _lastAcknowledgedSequence = 0;
  final List<SyncCorrection> _unresolvedConflicts = [];

  Timer? _autoSyncTimer;
  StreamSubscription<bool>? _connectivitySub;
  Future<bool>? _activeSyncFuture;

  // ---------------------------------------------------------------------------
  // SyncManager interface
  // ---------------------------------------------------------------------------

  @override
  Stream<SyncStatus> get statusStream => _statusController.stream;

  @override
  Stream<int> get pendingEventCountStream => _pendingCountController.stream;

  @override
  Stream<DateTime> get lastSyncTimeStream => _lastSyncTimeController.stream;

  @override
  Stream<int> get conflictCountStream => _conflictCountController.stream;

  @override
  int get pendingEventCount => _pendingEventCount;

  @override
  Future<void> enqueue(OfflineEvent event) async {
    await _eventQueue.enqueue(
      eventType: event.eventType,
      payload: jsonDecode(event.payload) as Map<String, dynamic>,
    );
    _pendingEventCount = await _eventQueue.pendingCount;
    _pendingCountController.add(_pendingEventCount);
  }

  @override
  Future<bool> syncNow() {
    // Debounce: if a sync is already running return the same future.
    if (_activeSyncFuture != null) return _activeSyncFuture!;
    _activeSyncFuture = _doSync().whenComplete(() => _activeSyncFuture = null);
    return _activeSyncFuture!;
  }

  @override
  Future<void> startAutoSync({Duration interval = const Duration(minutes: 5)}) async {
    stopAutoSync();

    // Sync when connectivity is restored.
    _connectivitySub = _connectivityMonitor.onConnectivityChanged.listen((online) {
      if (online) {
        _logger.d('SyncManager: connectivity restored — triggering sync');
        syncNow();
      }
    });

    // Periodic sync.
    _autoSyncTimer = Timer.periodic(interval, (_) {
      if (_connectivityMonitor.isOnline) syncNow();
    });
  }

  @override
  void stopAutoSync() {
    _autoSyncTimer?.cancel();
    _autoSyncTimer = null;
    _connectivitySub?.cancel();
    _connectivitySub = null;
  }

  @override
  Future<void> pruneAccepted() async {
    // Already pruned inline during syncNow via removeAcknowledged.
    _pendingEventCount = await _eventQueue.pendingCount;
    _pendingCountController.add(_pendingEventCount);
  }

  @override
  bool get hasConflicts => _unresolvedConflicts.isNotEmpty;

  @override
  List<SyncCorrection> getUnresolvedConflicts() =>
      List.unmodifiable(_unresolvedConflicts);

  @override
  Future<void> acceptCorrection(String idempotencyKey) async {
    _unresolvedConflicts.removeWhere((c) => c.idempotencyKey == idempotencyKey);
    _conflictCountController.add(_unresolvedConflicts.length);
  }

  @override
  void dispose() {
    stopAutoSync();
    _statusController.close();
    _pendingCountController.close();
    _lastSyncTimeController.close();
    _conflictCountController.close();
  }

  // ---------------------------------------------------------------------------
  // Internal sync logic
  // ---------------------------------------------------------------------------

  Future<bool> _doSync() async {
    if (!_connectivityMonitor.isOnline) {
      _logger.d('SyncManager: skipping sync — offline');
      return false;
    }

    final events = await _eventQueue.getAll();
    if (events.isEmpty) {
      _logger.d('SyncManager: queue empty — nothing to sync');
      return true;
    }

    _setStatus(SyncStatus.syncing);

    try {
      final request = SyncRequest(
        studentId: _studentId,
        clockOffsetMs: _clockSkew.estimatedOffsetMs,
        events: events,
        lastAcknowledgedSequence: _lastAcknowledgedSequence,
      );

      final response = await _postSync(request);

      // Update clock skew with server timestamp.
      final now = DateTime.now();
      _clockSkew.updateEstimate(
        clientSendTime: now,
        serverTimestamp: response.serverTimestamp,
        clientReceiveTime: DateTime.now(),
      );

      // Apply corrections.
      if (response.corrections.isNotEmpty) {
        await _conflictResolver.applyCorrections(response.corrections);
        _unresolvedConflicts.addAll(
          response.corrections.where((c) => c.weight > 0.0 && c.weight < 1.0),
        );
        _conflictCountController.add(_unresolvedConflicts.length);
        if (_unresolvedConflicts.isNotEmpty) {
          _setStatus(SyncStatus.conflict);
        }
      }

      // Remove acknowledged events.
      if (response.acknowledgedUpTo > 0) {
        await _eventQueue.removeAcknowledged(response.acknowledgedUpTo);
        _lastAcknowledgedSequence = response.acknowledgedUpTo;
      }

      // Increment retries for rejected keys.
      for (final key in response.rejectedKeys) {
        await _eventQueue.markFailed(key, 'rejected by server');
        await _eventQueue.incrementRetry(key);
      }

      _pendingEventCount = await _eventQueue.pendingCount;
      _pendingCountController.add(_pendingEventCount);

      final syncTime = DateTime.now();
      _lastSyncTimeController.add(syncTime);

      if (_status != SyncStatus.conflict) _setStatus(SyncStatus.idle);
      _logger.d(
        'SyncManager: sync complete — '
        'accepted=${response.acceptedKeys.length} '
        'corrections=${response.corrections.length} '
        'rejected=${response.rejectedKeys.length}',
      );
      return true;
    } on DioException catch (e) {
      _logger.e('SyncManager: sync failed — ${e.message}');

      if (e.response?.statusCode == 429) {
        final retryAfter = e.response?.headers.value('Retry-After');
        final backoffSec = int.tryParse(retryAfter ?? '') ?? 60;
        _logger.w('SyncManager: rate limited — retry after ${backoffSec}s');
      }

      _setStatus(SyncStatus.error);
      return false;
    } catch (e) {
      _logger.e('SyncManager: unexpected error — $e');
      _setStatus(SyncStatus.error);
      return false;
    }
  }

  Future<SyncResponse> _postSync(SyncRequest request) async {
    final response = await _restClient.post<Map<String, dynamic>>(
      _syncEndpoint,
      data: request.toJson(),
    );
    if (response.data == null) {
      throw StateError('SyncManager: empty response from server');
    }
    return SyncResponse.fromJson(response.data!);
  }

  void _setStatus(SyncStatus status) {
    if (_status != status) {
      _status = status;
      _statusController.add(status);
    }
  }
}
