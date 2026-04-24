// =============================================================================
// Cena Adaptive Learning Platform — Interaction Feedback Preferences
// =============================================================================

import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../services/interaction_feedback_service.dart';

class InteractionFeedbackState {
  const InteractionFeedbackState({
    this.soundsEnabled = false,
    this.hapticsEnabled = true,
  });

  final bool soundsEnabled;
  final bool hapticsEnabled;

  InteractionFeedbackState copyWith({
    bool? soundsEnabled,
    bool? hapticsEnabled,
  }) {
    return InteractionFeedbackState(
      soundsEnabled: soundsEnabled ?? this.soundsEnabled,
      hapticsEnabled: hapticsEnabled ?? this.hapticsEnabled,
    );
  }
}

class InteractionFeedbackNotifier
    extends StateNotifier<InteractionFeedbackState> {
  InteractionFeedbackNotifier() : super(const InteractionFeedbackState()) {
    _load();
  }

  Future<void> _load() async {
    await InteractionFeedbackService.preload();
    state = state.copyWith(
      soundsEnabled: await InteractionFeedbackService.soundsEnabled(),
      hapticsEnabled: await InteractionFeedbackService.hapticsEnabled(),
    );
  }

  Future<void> setSoundsEnabled(bool enabled) async {
    await InteractionFeedbackService.setSoundsEnabled(enabled);
    state = state.copyWith(soundsEnabled: enabled);
  }

  Future<void> setHapticsEnabled(bool enabled) async {
    await InteractionFeedbackService.setHapticsEnabled(enabled);
    state = state.copyWith(hapticsEnabled: enabled);
  }
}

final interactionFeedbackProvider = StateNotifierProvider<
    InteractionFeedbackNotifier, InteractionFeedbackState>(
  (ref) => InteractionFeedbackNotifier(),
);
