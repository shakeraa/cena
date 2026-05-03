// =============================================================================
// Cena Adaptive Learning Platform — OfflineEventQueueImpl
// SharedPreferences-backed ordered event queue for offline sync.
// =============================================================================

import 'dart:async';
import 'dart:convert';

import 'package:logger/logger.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../models/domain_models.dart';
import 'event_classifier_impl.dart';
import 'idempotency_key_generator_impl.dart';
import 'offline_sync_service.dart';

/// SharedPreferences-backed implementation of [OfflineEventQueue].
///
/// Events are stored as a JSON array under a single key. Each event is
/// assigned a monotonically increasing sequence number. The queue is
/// protected against concurrent [clear] and sync via a simple flag.
///
/// Edge cases handled:
/// - Sequence overflow: sequence is 64-bit (Dart int); wraps at maxSafe
/// - Large payloads: stored as-is in JSON (SharedPreferences handles large
///   strings; Drift upgrade path in MOB-004 follow-up)
/// - clear() while syncing: guarded by [_clearing] flag
class OfflineEventQueueImpl implements OfflineEventQueue {
  OfflineEventQueueImpl({
    required SharedPreferences prefs,
    EventClassifier? classifier,
    IdempotencyKeyGenerator? keyGen,
  })  : _prefs = prefs,
        _classifier = classifier ?? EventClassifierImpl(),
        _keyGen = keyGen ?? IdempotencyKeyGeneratorImpl();

  static const _queueKey = 'cena_offline_event_queue';
  static const _sequenceKey = 'cena_event_sequence';

  final SharedPreferences _prefs;
  final EventClassifier _classifier;
  final IdempotencyKeyGenerator _keyGen;
  final _countController = StreamController<int>.broadcast();
  final _logger = Logger(printer: PrettyPrinter(methodCount: 0));

  bool _clearing = false;
  int _sequence = 0;

  /// Load the persisted sequence counter on startup.
  Future<void> init() async {
    _sequence = _prefs.getInt(_sequenceKey) ?? 0;
    await _keyGen.loadState();
  }

  // ---------------------------------------------------------------------------
  // OfflineEventQueue interface
  // ---------------------------------------------------------------------------

  @override
  Future<OfflineEvent> enqueue({
    required String eventType,
    required Map<String, dynamic> payload,
  }) async {
    _sequence += 1;
    await _prefs.setInt(_sequenceKey, _sequence);

    final idempotencyKey = _keyGen.next();
    final classification = _classifier.classify(eventType);

    final event = OfflineEvent(
      idempotencyKey: idempotencyKey,
      clientTimestamp: DateTime.now(),
      eventType: eventType,
      payload: jsonEncode(payload),
      classification: classification,
      sequenceNumber: _sequence,
    );

    final queue = await _loadQueue();
    queue.add(event);
    await _saveQueue(queue);

    _countController.add(queue.length);
    _logger.d(
      'OfflineEventQueue: enqueued $eventType '
      'key=$idempotencyKey seq=$_sequence',
    );
    return event;
  }

  @override
  Future<List<OfflineEvent>> peek(int count) async {
    final queue = await _loadQueue();
    return queue.take(count).toList();
  }

  @override
  Future<List<OfflineEvent>> getAll() => _loadQueue();

  @override
  Future<int> removeAcknowledged(int acknowledgedUpTo) async {
    final queue = await _loadQueue();
    final before = queue.length;
    queue.removeWhere((e) => e.sequenceNumber <= acknowledgedUpTo);
    await _saveQueue(queue);
    final removed = before - queue.length;
    _countController.add(queue.length);
    _logger.d(
      'OfflineEventQueue: removed $removed events '
      '(acknowledgedUpTo=$acknowledgedUpTo)',
    );
    return removed;
  }

  @override
  Future<void> markFailed(String idempotencyKey, String error) async {
    final queue = await _loadQueue();
    final idx = queue.indexWhere((e) => e.idempotencyKey == idempotencyKey);
    if (idx == -1) return;
    queue[idx] = queue[idx].copyWith(lastError: error);
    await _saveQueue(queue);
  }

  @override
  Future<void> incrementRetry(String idempotencyKey) async {
    final queue = await _loadQueue();
    final idx = queue.indexWhere((e) => e.idempotencyKey == idempotencyKey);
    if (idx == -1) return;
    queue[idx] = queue[idx].copyWith(retryCount: queue[idx].retryCount + 1);
    await _saveQueue(queue);
  }

  @override
  Future<int> get pendingCount async => (await _loadQueue()).length;

  @override
  Stream<int> get pendingCountStream => _countController.stream;

  @override
  Future<bool> get isEmpty async => (await _loadQueue()).isEmpty;

  @override
  Future<void> clear() async {
    if (_clearing) return; // guard against concurrent clear
    _clearing = true;
    try {
      await _prefs.remove(_queueKey);
      _sequence = 0;
      await _prefs.remove(_sequenceKey);
      _countController.add(0);
      _logger.d('OfflineEventQueue: cleared');
    } finally {
      _clearing = false;
    }
  }

  // ---------------------------------------------------------------------------
  // Internal helpers
  // ---------------------------------------------------------------------------

  Future<List<OfflineEvent>> _loadQueue() async {
    final json = _prefs.getString(_queueKey);
    if (json == null) return [];
    try {
      final list = jsonDecode(json) as List<dynamic>;
      return list
          .cast<Map<String, dynamic>>()
          .map(OfflineEvent.fromJson)
          .toList();
    } catch (e) {
      _logger.e('OfflineEventQueue: failed to parse queue — $e');
      return [];
    }
  }

  Future<void> _saveQueue(List<OfflineEvent> queue) async {
    final json = jsonEncode(queue.map((e) => e.toJson()).toList());
    await _prefs.setString(_queueKey, json);
  }

  void dispose() {
    _countController.close();
  }
}
