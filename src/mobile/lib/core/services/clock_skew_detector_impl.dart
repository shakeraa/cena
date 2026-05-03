// =============================================================================
// Cena Adaptive Learning Platform — ClockSkewDetectorImpl
// NTP-style client/server clock offset estimation with median filtering.
// =============================================================================

import 'dart:math' as math;

import 'offline_sync_service.dart';

/// NTP-style clock skew estimator.
///
/// Formula:
///   offset = ((T_server - T_clientSend) + (T_server - T_clientReceive)) / 2
///
/// Outliers with round-trip time > 5 s are discarded.
/// The running average is updated with each accepted sample.
/// [confidence] rises toward 1.0 as variance drops over 10 consistent samples.
class ClockSkewDetectorImpl implements ClockSkewDetector {
  static const int _targetSamples = 10;
  static const int _maxRttMs = 5000;

  final List<int> _samples = [];
  int _runningSum = 0;

  @override
  void updateEstimate({
    required DateTime clientSendTime,
    required DateTime serverTimestamp,
    required DateTime clientReceiveTime,
  }) {
    final rttMs =
        clientReceiveTime.difference(clientSendTime).inMilliseconds.abs();

    // Discard outliers.
    if (rttMs > _maxRttMs) return;

    final offsetMs = ((serverTimestamp.difference(clientSendTime).inMilliseconds) +
            (serverTimestamp.difference(clientReceiveTime).inMilliseconds)) ~/
        2;

    _samples.add(offsetMs);
    _runningSum += offsetMs;
  }

  @override
  int get estimatedOffsetMs {
    if (_samples.isEmpty) return 0;
    return _runningSum ~/ _samples.length;
  }

  @override
  DateTime adjustToServerTime(DateTime clientTime) {
    return clientTime.add(Duration(milliseconds: estimatedOffsetMs));
  }

  @override
  int get sampleCount => _samples.length;

  @override
  double get confidence {
    if (_samples.length < 2) return _samples.isEmpty ? 0.0 : 0.1;

    // Compute variance of the samples.
    final mean = estimatedOffsetMs;
    final variance = _samples
            .map((s) => (s - mean) * (s - mean))
            .reduce((a, b) => a + b) /
        _samples.length;

    // Low variance => high confidence. Normalise against a 1-second window.
    final stdDev = math.sqrt(variance);
    final varianceFactor = math.max(0.0, 1.0 - (stdDev / 1000.0));

    // Weight by number of samples vs target.
    final sampleFactor =
        math.min(1.0, _samples.length / _targetSamples.toDouble());

    return sampleFactor * varianceFactor;
  }
}
