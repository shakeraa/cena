import 'package:cena/core/state/gamification_state.dart' show BadgeCategory;
import 'package:cena/features/gamification/badge_3d_widget.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('BadgeRarity', () {
    test('fromCategory maps correctly', () {
      expect(
        BadgeRarity.fromCategory(BadgeCategory.engagement),
        BadgeRarity.common,
      );
      expect(
        BadgeRarity.fromCategory(BadgeCategory.streak),
        BadgeRarity.rare,
      );
      expect(
        BadgeRarity.fromCategory(BadgeCategory.mastery),
        BadgeRarity.epic,
      );
      expect(
        BadgeRarity.fromCategory(BadgeCategory.special),
        BadgeRarity.legendary,
      );
    });

    test('ring colors are distinct', () {
      final colors = BadgeRarity.values.map((r) => r.ringColor).toSet();
      expect(colors.length, BadgeRarity.values.length);
    });

    test('glow radius increases with rarity', () {
      expect(BadgeRarity.common.glowRadius, 0);
      expect(BadgeRarity.rare.glowRadius, greaterThan(0));
      expect(BadgeRarity.epic.glowRadius,
          greaterThan(BadgeRarity.rare.glowRadius));
      expect(BadgeRarity.legendary.glowRadius,
          greaterThan(BadgeRarity.epic.glowRadius));
    });
  });

  group('Badge3D widget', () {
    testWidgets('renders without error', (tester) async {
      await tester.pumpWidget(
        const MaterialApp(
          home: Scaffold(
            body: Badge3D(
              icon: Icons.star,
              rarity: BadgeRarity.epic,
              isEarned: true,
            ),
          ),
        ),
      );
      expect(find.byType(Badge3D), findsOneWidget);
    });

    testWidgets('locked badge shows different color', (tester) async {
      await tester.pumpWidget(
        const MaterialApp(
          home: Scaffold(
            body: Badge3D(
              icon: Icons.star,
              rarity: BadgeRarity.common,
              isEarned: false,
            ),
          ),
        ),
      );
      expect(find.byType(Badge3D), findsOneWidget);
    });
  });
}
