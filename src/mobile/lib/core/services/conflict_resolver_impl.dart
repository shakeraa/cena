// =============================================================================
// Cena Adaptive Learning Platform — ConflictResolverImpl
// Weighted merge of server corrections per the three-tier weight scheme.
// =============================================================================

import 'package:logger/logger.dart';

import '../models/domain_models.dart';
import 'offline_sync_service.dart';

/// Resolves field conflicts between client and server values.
///
/// Three tiers (from [ConflictResolver] constants):
///   1.0  → client value wins unconditionally
///   0.75 → weighted merge for numerics; server wins for strings
///   0.0  → server value replaces client entirely
///
/// Every applied correction is logged for audit trail.
/// Unresolvable corrections (weight 0.75 on non-numeric) default to
/// server-wins and are logged as warnings.
class ConflictResolverImpl implements ConflictResolver {
  final _logger = Logger(printer: PrettyPrinter(methodCount: 0));

  @override
  dynamic resolve(SyncCorrection correction) {
    // Clamp weight to [0.0, 1.0].
    final weight = correction.weight.clamp(0.0, 1.0);

    if (weight == ConflictResolver.weightFull) {
      // Unconditional: client wins.
      _logger.d(
        'ConflictResolver: key=${correction.idempotencyKey} '
        'field=${correction.field} — client wins (weight=1.0)',
      );
      return correction.clientValue;
    }

    if (weight == ConflictResolver.weightHistorical) {
      // Server-authoritative: server wins.
      _logger.d(
        'ConflictResolver: key=${correction.idempotencyKey} '
        'field=${correction.field} — server wins (weight=0.0)',
      );
      return correction.serverValue;
    }

    // weight == 0.75: weighted merge for numerics, server wins for strings.
    final clientNum = double.tryParse(correction.clientValue);
    final serverNum = double.tryParse(correction.serverValue);

    if (clientNum != null && serverNum != null) {
      final merged = clientNum * weight + serverNum * (1.0 - weight);
      _logger.d(
        'ConflictResolver: key=${correction.idempotencyKey} '
        'field=${correction.field} — weighted merge '
        '($clientNum * $weight + $serverNum * ${1.0 - weight}) = $merged',
      );
      return merged.toString();
    }

    // Non-numeric with weight 0.75: server wins.
    _logger.w(
      'ConflictResolver: key=${correction.idempotencyKey} '
      'field=${correction.field} — non-numeric with weight=$weight, '
      'defaulting to server value',
    );
    return correction.serverValue;
  }

  @override
  Future<void> applyCorrections(
    List<SyncCorrection> corrections, {
    void Function(String idempotencyKey, dynamic value)? onApply,
  }) async {
    for (final correction in corrections) {
      // Skip corrections for fields that no longer exist (empty keys).
      if (correction.idempotencyKey.isEmpty) {
        _logger.w(
          'ConflictResolver: skipping correction with empty idempotency key '
          'field=${correction.field}',
        );
        continue;
      }

      final resolved = resolve(correction);
      onApply?.call(correction.idempotencyKey, resolved);
      _logger.d(
        'ConflictResolver: applied correction key=${correction.idempotencyKey} '
        'field=${correction.field} resolved=$resolved',
      );
    }
  }
}
