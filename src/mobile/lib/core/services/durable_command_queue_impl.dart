// =============================================================================
// Cena Adaptive Learning Platform — DurableCommandQueueImpl
// SharedPreferences-backed write-ahead command queue.
// SQLite/Drift upgrade is the MOB-004 follow-up task.
// =============================================================================

import 'dart:async';
import 'dart:convert';

import 'package:logger/logger.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'offline_sync_service.dart';
import 'websocket_service.dart';

/// SharedPreferences-backed [DurableCommandQueue].
///
/// Write-ahead pattern: the command is persisted to SharedPreferences BEFORE
/// the WebSocket send is attempted. If the send fails the command stays in
/// the queue with an incremented [retryCount] and the error in [lastError].
///
/// Commands that have been retried [OutgoingCommand.maxRetries] times are kept
/// in a separate "dead-letter" list so they can be inspected without blocking
/// normal retries.
///
/// Edge cases handled:
/// - App killed between persist and send: command survives, retried on launch.
/// - WebSocket ack received but markAcknowledged fails: server uses
///   idempotency key to deduplicate on next send.
/// - maxRetries exceeded: moved to dead-letter list, logged to analytics.
class DurableCommandQueueImpl implements DurableCommandQueue {
  DurableCommandQueueImpl({
    required SharedPreferences prefs,
    required WebSocketService webSocket,
  })  : _prefs = prefs,
        _webSocket = webSocket;

  static const _queueKey = 'cena_command_queue';
  static const _deadLetterKey = 'cena_command_dead_letter';

  final SharedPreferences _prefs;
  final WebSocketService _webSocket;
  final _logger = Logger(printer: PrettyPrinter(methodCount: 0));

  // ---------------------------------------------------------------------------
  // DurableCommandQueue interface
  // ---------------------------------------------------------------------------

  @override
  Future<String> enqueueCommand(OutgoingCommand cmd) async {
    // 1. Write-ahead: persist BEFORE sending.
    final queue = await _loadQueue(_queueKey);
    queue.add(cmd);
    await _saveQueue(_queueKey, queue);

    // 2. Attempt immediate send.
    try {
      await _send(cmd);
      // Successfully sent — remove from queue (server will ack separately,
      // but optimistically clear to avoid duplicate sends on reconnect when
      // the server uses idempotency keys for deduplication).
      await markAcknowledged(cmd.id);
    } catch (e) {
      // Leave in queue; retried by retryPending().
      _logger.w(
        'DurableCommandQueue: send failed for ${cmd.id} (${cmd.type}) — $e',
      );
      await _updateCommand(
        cmd.id,
        retryCount: cmd.retryCount + 1,
        lastError: e.toString(),
      );
    }

    return cmd.id;
  }

  @override
  Future<void> markAcknowledged(String commandId) async {
    final queue = await _loadQueue(_queueKey);
    queue.removeWhere((c) => c.id == commandId);
    await _saveQueue(_queueKey, queue);
    _logger.d('DurableCommandQueue: acknowledged $commandId');
  }

  @override
  Future<List<OutgoingCommand>> getPendingCommands() async {
    final queue = await _loadQueue(_queueKey);
    queue.sort((a, b) => a.enqueuedAt.compareTo(b.enqueuedAt));
    return queue;
  }

  @override
  Future<DurableRetryResult> retryPending() async {
    final queue = await getPendingCommands();
    int succeeded = 0;
    int failed = 0;

    for (final cmd in queue) {
      if (cmd.retryCount >= OutgoingCommand.maxRetries) {
        // Move to dead-letter.
        await _moveToDeadLetter(cmd);
        failed += 1;
        continue;
      }

      try {
        await _send(cmd);
        await markAcknowledged(cmd.id);
        succeeded += 1;
      } catch (e) {
        _logger.w(
          'DurableCommandQueue: retry failed for ${cmd.id} — $e',
        );
        await _updateCommand(
          cmd.id,
          retryCount: cmd.retryCount + 1,
          lastError: e.toString(),
        );
        failed += 1;
      }
    }

    final remaining = await pendingCount();
    return DurableRetryResult(
      succeeded: succeeded,
      failed: failed,
      remaining: remaining,
    );
  }

  @override
  Future<int> pendingCount() async {
    final queue = await _loadQueue(_queueKey);
    return queue.length;
  }

  // ---------------------------------------------------------------------------
  // Internal helpers
  // ---------------------------------------------------------------------------

  Future<void> _send(OutgoingCommand cmd) async {
    final p = cmd.payload;
    switch (cmd.type) {
      case 'StartSession':
        await _webSocket.startSession(StartSession(
          studentId: p['studentId'] as String? ?? '',
          durationMinutes: (p['durationMinutes'] as int?) ?? 25,
        ));
      case 'AttemptConcept':
        await _webSocket.attemptConcept(AttemptConcept(
          sessionId: p['sessionId'] as String,
          exerciseId: p['exerciseId'] as String,
          conceptId: p['conceptId'] as String,
          answer: p['answer'] as String,
          timeSpentMs: (p['timeSpentMs'] as int?) ?? 0,
          idempotencyKey: p['idempotencyKey'] as String? ?? cmd.id,
        ));
      case 'RequestHint':
        await _webSocket.requestHint(RequestHint(
          sessionId: p['sessionId'] as String,
          exerciseId: p['exerciseId'] as String,
          hintLevel: (p['hintLevel'] as int?) ?? 0,
        ));
      case 'SkipQuestion':
        await _webSocket.skipQuestion(SkipQuestion(
          sessionId: p['sessionId'] as String,
          exerciseId: p['exerciseId'] as String,
          reason: p['reason'] as String?,
        ));
      case 'SwitchApproach':
        await _webSocket.switchApproach(SwitchApproach(
          sessionId: p['sessionId'] as String,
          preferenceHint: p['preferenceHint'] as String,
        ));
      case 'EndSession':
        await _webSocket.endSession(EndSession(
          sessionId: p['sessionId'] as String,
          reason: p['reason'] as String? ?? 'manual',
        ));
      default:
        _logger.w(
          'DurableCommandQueue: unknown command type "${cmd.type}" — skipped',
        );
    }
  }

  Future<void> _updateCommand(
    String id, {
    required int retryCount,
    required String lastError,
  }) async {
    final queue = await _loadQueue(_queueKey);
    final idx = queue.indexWhere((c) => c.id == id);
    if (idx == -1) return;
    queue[idx] = queue[idx].copyWith(
      retryCount: retryCount,
      lastError: lastError,
    );
    await _saveQueue(_queueKey, queue);
  }

  Future<void> _moveToDeadLetter(OutgoingCommand cmd) async {
    // Remove from main queue.
    final queue = await _loadQueue(_queueKey);
    queue.removeWhere((c) => c.id == cmd.id);
    await _saveQueue(_queueKey, queue);

    // Append to dead-letter.
    final dead = await _loadQueue(_deadLetterKey);
    dead.add(cmd);
    await _saveQueue(_deadLetterKey, dead);
    _logger.e(
      'DurableCommandQueue: command ${cmd.id} (${cmd.type}) moved to '
      'dead-letter after ${cmd.retryCount} retries',
    );
  }

  Future<List<OutgoingCommand>> _loadQueue(String key) async {
    final json = _prefs.getString(key);
    if (json == null) return [];
    try {
      final list = jsonDecode(json) as List<dynamic>;
      return list
          .cast<Map<String, dynamic>>()
          .map(OutgoingCommand.fromJson)
          .toList();
    } catch (e) {
      _logger.e('DurableCommandQueue: failed to parse $key — $e');
      return [];
    }
  }

  Future<void> _saveQueue(String key, List<OutgoingCommand> queue) async {
    final json = jsonEncode(queue.map((c) => c.toJson()).toList());
    await _prefs.setString(key, json);
  }
}
