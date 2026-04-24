// =============================================================================
// Cena Adaptive Learning Platform — IdempotencyKeyGeneratorImpl
// Generates `{uuidv4}:{sequence}` keys. Sequence persists across restarts.
// =============================================================================

import 'package:shared_preferences/shared_preferences.dart';
import 'package:uuid/uuid.dart';

import 'offline_sync_service.dart';

/// SharedPreferences-backed idempotency key generator.
///
/// Keys are formatted as `{uuid-v4}:{sequence}` where the sequence is a
/// monotonically increasing 64-bit integer. The UUID component changes each
/// time [next] is called; the sequence always increments by 1.
///
/// The sequence is persisted after every [next] call so that it survives
/// app restarts without replaying old values.
class IdempotencyKeyGeneratorImpl implements IdempotencyKeyGenerator {
  IdempotencyKeyGeneratorImpl({SharedPreferences? storage})
      : _storage = storage;

  static const _sequenceKey = 'cena_idempotency_sequence';

  SharedPreferences? _storage;
  int _sequence = 0;
  final _uuid = const Uuid();

  /// Must be called once at app startup before [next] is used.
  @override
  Future<void> loadState() async {
    _storage ??= await SharedPreferences.getInstance();
    _sequence = _storage!.getInt(_sequenceKey) ?? 0;
  }

  @override
  String next() {
    _sequence += 1;
    _persistSequence();
    return '${_uuid.v4()}:$_sequence';
  }

  @override
  int get currentSequence => _sequence;

  @override
  Future<void> resetTo(int sequence) async {
    _sequence = sequence;
    await _persistSequence();
  }

  Future<void> _persistSequence() async {
    _storage ??= await SharedPreferences.getInstance();
    await _storage!.setInt(_sequenceKey, _sequence);
  }
}
