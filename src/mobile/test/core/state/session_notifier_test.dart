import 'package:cena/core/state/session_notifier.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('SessionState', () {
    test('initial state has no active session', () {
      const state = SessionState();
      expect(state.isActive, isFalse);
      expect(state.accuracy, 0.0);
      expect(state.questionsAttempted, 0);
      expect(state.isInFlowState, isFalse);
    });

    test('accuracy computed from correct/attempted', () {
      const state = SessionState(
        questionsAttempted: 10,
        questionsCorrect: 7,
      );
      expect(state.accuracy, closeTo(0.7, 0.01));
    });

    test('flow state requires 3+ consecutive correct', () {
      const state = SessionState(consecutiveCorrect: 2);
      expect(state.isInFlowState, isFalse);

      const flowState = SessionState(consecutiveCorrect: 3);
      expect(flowState.isInFlowState, isTrue);
    });

    test('copyWith preserves unchanged fields', () {
      const original = SessionState(
        questionsAttempted: 5,
        questionsCorrect: 3,
        fatigueScore: 0.4,
      );
      final updated = original.copyWith(questionsAttempted: 6);
      expect(updated.questionsAttempted, 6);
      expect(updated.questionsCorrect, 3);
      expect(updated.fatigueScore, 0.4);
    });

    test('copyWith clearError sets error to null', () {
      const state = SessionState(error: 'something went wrong');
      final cleared = state.copyWith(clearError: true);
      expect(cleared.error, isNull);
    });

    test('isBreakSuggested defaults to false', () {
      const state = SessionState();
      expect(state.isBreakSuggested, isFalse);
    });
  });
}
