import 'package:cena/core/state/gamification_state.dart';
import 'package:cena/features/gamification/variable_rewards.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('xpForLevel', () {
    test('level 1 requires 110 XP', () {
      expect(xpForLevel(1), 110);
    });

    test('level 10 requires 2000 XP', () {
      expect(xpForLevel(10), 2000);
    });

    test('xp requirement increases with level', () {
      for (int i = 1; i < 20; i++) {
        expect(xpForLevel(i + 1), greaterThan(xpForLevel(i)));
      }
    });
  });

  group('VariableRewardEngine', () {
    test('roll returns null or a valid reward', () {
      final engine = VariableRewardEngine();
      // Run many rolls — should get some nulls and some rewards
      int nullCount = 0;
      int rewardCount = 0;
      for (int i = 0; i < 100; i++) {
        final reward = engine.roll(
          questionsAnsweredInSession: 5,
          consecutiveCorrect: 2,
        );
        if (reward == null) {
          nullCount++;
        } else {
          rewardCount++;
        }
      }
      // Should have both nulls and rewards over 100 rolls
      expect(nullCount, greaterThan(0));
      expect(rewardCount, greaterThan(0));
    });
  });
}
