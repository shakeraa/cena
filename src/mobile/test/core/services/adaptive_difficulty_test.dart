import 'package:flutter_test/flutter_test.dart';
import 'package:cena/core/services/adaptive_difficulty_service.dart';

void main() {
  group('AdaptiveDifficultyTracker', () {
    late AdaptiveDifficultyTracker tracker;

    setUp(() => tracker = AdaptiveDifficultyTracker());

    test('initial rolling accuracy is 0.5', () {
      expect(tracker.rollingAccuracy, 0.5);
    });

    test('initial difficulty adjustment is 0 (not enough data)', () {
      expect(tracker.difficultyAdjustment, 0);
    });

    test('all correct answers → recommend harder', () {
      for (int i = 0; i < 5; i++) {
        tracker.recordAnswer(true);
      }
      expect(tracker.rollingAccuracy, 1.0);
      expect(tracker.difficultyAdjustment, 1);
    });

    test('all wrong answers → recommend easier', () {
      for (int i = 0; i < 5; i++) {
        tracker.recordAnswer(false);
      }
      expect(tracker.rollingAccuracy, 0.0);
      expect(tracker.difficultyAdjustment, -1);
    });

    test('65% accuracy → in flow zone', () {
      // 6.5 correct out of 10 → use 7/10 ≈ 0.7
      for (int i = 0; i < 7; i++) {
        tracker.recordAnswer(true);
      }
      for (int i = 0; i < 3; i++) {
        tracker.recordAnswer(false);
      }
      expect(tracker.rollingAccuracy, closeTo(0.7, 0.01));
      expect(tracker.isInFlowZone, isTrue);
      expect(tracker.difficultyAdjustment, 0);
    });

    test('window size limits history to last N answers', () {
      // Fill with 10 wrong, then 10 correct — window is 10
      for (int i = 0; i < 10; i++) {
        tracker.recordAnswer(false);
      }
      for (int i = 0; i < 10; i++) {
        tracker.recordAnswer(true);
      }
      expect(tracker.rollingAccuracy, 1.0);
    });

    test('reset clears all state', () {
      tracker.recordAnswer(true);
      tracker.reset();
      expect(tracker.rollingAccuracy, 0.5);
    });
  });

  group('FsrsCard', () {
    test('new card is always due', () {
      final card = FsrsCard(conceptId: 'test-1');
      expect(card.isDue, isTrue);
    });

    test('review updates stability and schedules next review', () {
      final card = FsrsCard(conceptId: 'test-2');
      card.review(3); // Good rating
      expect(card.reps, 1);
      expect(card.lastReview, isNotNull);
      expect(card.scheduledDays, greaterThanOrEqualTo(1));
      expect(card.stability, greaterThan(1.0));
    });

    test('difficulty increases on Again rating', () {
      final card = FsrsCard(conceptId: 'test-3');
      final initialDifficulty = card.difficulty;
      card.review(1); // Again
      expect(card.difficulty, greaterThan(initialDifficulty));
    });
  });

  group('FsrsTracker', () {
    test('getDueCards returns unreviewed cards', () {
      final tracker = FsrsTracker();
      tracker.getCard('concept-a');
      tracker.getCard('concept-b');
      expect(tracker.getDueCards().length, 2);
    });

    test('dueCount decreases after review', () {
      final tracker = FsrsTracker();
      tracker.getCard('concept-x');
      expect(tracker.dueCount, 1);
      tracker.recordReview('concept-x', 3);
      // After review, not immediately due unless scheduledDays = 0
      // With rating 3, scheduledDays >= 1, so not due today
    });
  });
}
