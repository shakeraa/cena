// =============================================================================
// Cena Adaptive Learning Platform — Variable Reward System (MOB-GAM-002)
// + Age-Stratified Gamification Intensity (MOB-GAM-003)
// =============================================================================
//
// Blueprint §2.2: Variable Reward Schedule
// - Unpredictable rewards create stronger habit loops
// - Mystery box, daily wheel, random XP drops, hidden achievements
//
// Blueprint §8: Age-Stratified Gamification
// - 12-14: High extrinsic (XP 2x, frequent badges)
// - 15-17: Moderate (XP 1x, meaningful milestones)
// - 18+: Low/opt-in (mastery-focused, XP de-emphasized)
// =============================================================================

import 'dart:math';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/config/app_config.dart';

// ---------------------------------------------------------------------------
// Age-stratified intensity (MOB-GAM-003)
// ---------------------------------------------------------------------------

/// Gamification intensity based on student age bracket.
/// Controls XP multiplier, celebration frequency, and UI surface allocation.
enum AgeGamificationTier {
  junior(
    label: 'Junior (12-14)',
    xpMultiplier: 2.0,
    celebrationFrequency: 1.0,  // Celebrate every achievement
    showXpPopups: true,
    showStreakFlame: true,
    showLeaderboard: true,
  ),
  standard(
    label: 'Standard (15-17)',
    xpMultiplier: 1.0,
    celebrationFrequency: 0.6,  // Celebrate ~60% of minor achievements
    showXpPopups: true,
    showStreakFlame: true,
    showLeaderboard: true,
  ),
  mature(
    label: 'Mature (18+)',
    xpMultiplier: 0.5,
    celebrationFrequency: 0.3,  // Only celebrate significant milestones
    showXpPopups: false,        // XP de-emphasized
    showStreakFlame: false,
    showLeaderboard: false,     // Opt-in only
  );

  const AgeGamificationTier({
    required this.label,
    required this.xpMultiplier,
    required this.celebrationFrequency,
    required this.showXpPopups,
    required this.showStreakFlame,
    required this.showLeaderboard,
  });

  final String label;
  final double xpMultiplier;
  final double celebrationFrequency;
  final bool showXpPopups;
  final bool showStreakFlame;
  final bool showLeaderboard;

  /// Determine tier from student age.
  static AgeGamificationTier fromAge(int age) {
    if (age <= 14) return junior;
    if (age <= 17) return standard;
    return mature;
  }
}

/// Provider for the current gamification tier.
/// Defaults to standard (15-17) until we know the student's age.
final gamificationTierProvider = StateProvider<AgeGamificationTier>(
  (ref) => AgeGamificationTier.standard,
);

// ---------------------------------------------------------------------------
// Variable Reward System (MOB-GAM-002)
// ---------------------------------------------------------------------------

/// Types of variable rewards that can be triggered.
enum RewardType {
  bonusXp,       // Random +5-25 XP
  mysteryBadge,  // Hidden badge reveal
  streakFreeze,  // Free streak protection
  dailyBonus,    // Daily login bonus
}

/// A variable reward instance.
class VariableReward {
  const VariableReward({
    required this.type,
    required this.value,
    required this.label,
    required this.icon,
  });

  final RewardType type;
  final int value;
  final String label;
  final IconData icon;
}

/// Determines if and what variable reward to grant after a session action.
///
/// Uses a weighted random system — roughly:
/// - 70% chance: no reward (keeps it unpredictable)
/// - 15% chance: bonus XP (5-25)
/// - 8% chance: mystery badge progress
/// - 5% chance: streak freeze
/// - 2% chance: daily bonus multiplier
class VariableRewardEngine {
  VariableRewardEngine({Random? random}) : _random = random ?? Random();

  final Random _random;

  /// Roll for a potential reward. Returns null if no reward (70% of the time).
  VariableReward? roll({
    required int questionsAnsweredInSession,
    required int consecutiveCorrect,
  }) {
    // Higher chance with more questions and streaks
    final bonusChance = 0.3 + (consecutiveCorrect * 0.05).clamp(0.0, 0.2);
    final roll = _random.nextDouble();

    if (roll > bonusChance) return null; // No reward this time

    // Weighted selection among reward types
    final typeRoll = _random.nextDouble();
    if (typeRoll < 0.50) {
      final xp = 5 + _random.nextInt(21); // 5-25 XP
      return VariableReward(
        type: RewardType.bonusXp,
        value: xp,
        label: '+$xp Bonus XP!',
        icon: Icons.bolt_rounded,
      );
    } else if (typeRoll < 0.75) {
      return const VariableReward(
        type: RewardType.mysteryBadge,
        value: 1,
        label: 'Mystery badge progress!',
        icon: Icons.help_outline_rounded,
      );
    } else if (typeRoll < 0.92) {
      return const VariableReward(
        type: RewardType.streakFreeze,
        value: 1,
        label: 'Streak freeze earned!',
        icon: Icons.ac_unit_rounded,
      );
    } else {
      return const VariableReward(
        type: RewardType.dailyBonus,
        value: 2,
        label: '2x XP bonus active!',
        icon: Icons.star_rounded,
      );
    }
  }
}

/// Singleton provider for the reward engine.
final variableRewardEngineProvider = Provider<VariableRewardEngine>(
  (ref) => VariableRewardEngine(),
);

// ---------------------------------------------------------------------------
// Reward popup widget
// ---------------------------------------------------------------------------

/// A popup that appears when a variable reward is granted.
/// Slides in from the bottom with a bounce animation.
class RewardPopup extends StatefulWidget {
  const RewardPopup({
    super.key,
    required this.reward,
    required this.onDismiss,
  });

  final VariableReward reward;
  final VoidCallback onDismiss;

  @override
  State<RewardPopup> createState() => _RewardPopupState();
}

class _RewardPopupState extends State<RewardPopup>
    with SingleTickerProviderStateMixin {
  late final AnimationController _controller;

  @override
  void initState() {
    super.initState();
    _controller = AnimationController(
      vsync: this,
      duration: const Duration(milliseconds: 800),
    )..forward();

    // Auto-dismiss after 3 seconds
    Future.delayed(const Duration(seconds: 3), () {
      if (mounted) widget.onDismiss();
    });
  }

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return AnimatedBuilder(
      animation: _controller,
      builder: (context, child) {
        final slide = Curves.elasticOut.transform(_controller.value);
        return Transform.translate(
          offset: Offset(0, 100 * (1 - slide)),
          child: Opacity(
            opacity: _controller.value.clamp(0.0, 1.0),
            child: child,
          ),
        );
      },
      child: GestureDetector(
        onTap: widget.onDismiss,
        child: Container(
          margin: const EdgeInsets.all(SpacingTokens.md),
          padding: const EdgeInsets.symmetric(
            horizontal: SpacingTokens.lg,
            vertical: SpacingTokens.md,
          ),
          decoration: BoxDecoration(
            gradient: LinearGradient(
              colors: [
                const Color(0xFFFFD700).withValues(alpha: 0.9),
                const Color(0xFFFF8F00).withValues(alpha: 0.9),
              ],
            ),
            borderRadius: BorderRadius.circular(RadiusTokens.lg),
            boxShadow: [
              BoxShadow(
                color: const Color(0xFFFFD700).withValues(alpha: 0.3),
                blurRadius: 16,
                spreadRadius: 2,
              ),
            ],
          ),
          child: Row(
            mainAxisSize: MainAxisSize.min,
            children: [
              Icon(widget.reward.icon, color: Colors.white, size: 28),
              const SizedBox(width: SpacingTokens.md),
              Text(
                widget.reward.label,
                style: theme.textTheme.titleMedium?.copyWith(
                  color: Colors.white,
                  fontWeight: FontWeight.w700,
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}
