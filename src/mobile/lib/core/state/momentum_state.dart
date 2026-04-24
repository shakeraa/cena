// =============================================================================
// Cena Adaptive Learning Platform — Momentum Meter & Streak Anxiety State
// =============================================================================

import 'dart:convert';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'feature_discovery_state.dart';
import 'gamification_state.dart';

const String _kUseMomentumMeter = 'use_momentum_meter';
const String _kRecentSessionSignals = 'recent_session_signals_v1';

/// Compact signal snapshot used for streak-anxiety detection.
class SessionSignal {
  const SessionSignal({
    required this.durationSeconds,
    required this.hour,
    required this.accuracy,
  });

  final int durationSeconds;
  final int hour;
  final double accuracy;

  Map<String, dynamic> toJson() => {
        'duration_seconds': durationSeconds,
        'hour': hour,
        'accuracy': accuracy,
      };

  factory SessionSignal.fromJson(Map<String, dynamic> json) {
    return SessionSignal(
      durationSeconds: (json['duration_seconds'] as num?)?.toInt() ?? 0,
      hour: (json['hour'] as num?)?.toInt() ?? 0,
      accuracy: (json['accuracy'] as num?)?.toDouble() ?? 0.0,
    );
  }
}

/// Output of anxiety heuristics used to suggest Momentum mode.
class StreakAnxietyState {
  const StreakAnxietyState({
    this.signals = const [],
    this.suggestSwitch = false,
    this.reasons = const [],
  });

  final List<SessionSignal> signals;
  final bool suggestSwitch;
  final List<String> reasons;

  StreakAnxietyState copyWith({
    List<SessionSignal>? signals,
    bool? suggestSwitch,
    List<String>? reasons,
  }) {
    return StreakAnxietyState(
      signals: signals ?? this.signals,
      suggestSwitch: suggestSwitch ?? this.suggestSwitch,
      reasons: reasons ?? this.reasons,
    );
  }
}

class StreakAnxietyNotifier extends StateNotifier<StreakAnxietyState> {
  StreakAnxietyNotifier() : super(const StreakAnxietyState()) {
    _load();
  }

  Future<void> _load() async {
    final prefs = await SharedPreferences.getInstance();
    final raw = prefs.getStringList(_kRecentSessionSignals) ?? const [];
    final signals = raw
        .map((encoded) => SessionSignal.fromJson(
              jsonDecode(encoded) as Map<String, dynamic>,
            ))
        .toList();
    state = _evaluate(signals);
  }

  Future<void> recordSessionOutcome({
    required Duration duration,
    required double accuracy,
    required DateTime endedAt,
  }) async {
    final next = [
      ...state.signals,
      SessionSignal(
        durationSeconds: duration.inSeconds,
        hour: endedAt.hour,
        accuracy: accuracy,
      ),
    ];

    // Keep the detector window small and recent.
    final clipped = next.length > 12 ? next.sublist(next.length - 12) : next;

    final prefs = await SharedPreferences.getInstance();
    await prefs.setStringList(
      _kRecentSessionSignals,
      clipped.map((s) => jsonEncode(s.toJson())).toList(),
    );

    state = _evaluate(clipped);
  }

  StreakAnxietyState _evaluate(List<SessionSignal> signals) {
    if (signals.length < 4) {
      return StreakAnxietyState(signals: signals);
    }

    final reasons = <String>[];

    final shortSessions = signals.where((s) => s.durationSeconds < 30).length;
    if (shortSessions >= 3) {
      reasons.add('sessions_too_short');
    }

    final lateNight = signals.where((s) => s.hour == 2).length;
    if (lateNight >= 2) {
      reasons.add('late_night_2am_pattern');
    }

    final first = signals.take(3).toList();
    final last = signals.skip(signals.length - 3).toList();
    final firstAvg =
        first.fold<double>(0, (acc, s) => acc + s.accuracy) / first.length;
    final lastAvg =
        last.fold<double>(0, (acc, s) => acc + s.accuracy) / last.length;
    if (firstAvg - lastAvg >= 0.15) {
      reasons.add('accuracy_declining');
    }

    // Trigger when at least 2 anxiety signals are present.
    final suggest = reasons.length >= 2;

    return StreakAnxietyState(
      signals: signals,
      suggestSwitch: suggest,
      reasons: reasons,
    );
  }
}

class MomentumPreferenceNotifier extends StateNotifier<bool> {
  MomentumPreferenceNotifier() : super(false) {
    _load();
  }

  Future<void> _load() async {
    final prefs = await SharedPreferences.getInstance();
    state = prefs.getBool(_kUseMomentumMeter) ?? false;
  }

  Future<void> setUseMomentum(bool useMomentum) async {
    final prefs = await SharedPreferences.getInstance();
    await prefs.setBool(_kUseMomentumMeter, useMomentum);
    state = useMomentum;
  }

  Future<void> toggle() => setUseMomentum(!state);
}

final useMomentumMeterProvider =
    StateNotifierProvider<MomentumPreferenceNotifier, bool>(
  (ref) => MomentumPreferenceNotifier(),
);

final streakAnxietyProvider =
    StateNotifierProvider<StreakAnxietyNotifier, StreakAnxietyState>(
  (ref) => StreakAnxietyNotifier(),
);

/// Momentum % = (days studied in last 7 / 7) * 100.
///
/// To avoid "back to zero" discouragement, once the student has ever completed
/// at least one session we floor the display at 14% (1/7).
final momentumPercentageProvider = Provider<int>((ref) {
  final days = ref.watch(last7DaysActivityProvider);
  final sessionsCompleted =
      ref.watch(featureDiscoveryProvider).sessionsCompleted;
  final studiedDays = days.where((d) => d.isActive).length;

  if (studiedDays == 0 && sessionsCompleted > 0) {
    return 14;
  }

  return ((studiedDays / 7) * 100).round();
});

final momentumDaysStudiedProvider = Provider<int>((ref) {
  final days = ref.watch(last7DaysActivityProvider);
  return days.where((d) => d.isActive).length;
});
