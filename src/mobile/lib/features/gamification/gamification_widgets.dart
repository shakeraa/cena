// =============================================================================
// Cena Adaptive Learning Platform — Gamification Widgets
// =============================================================================

import 'package:flutter/material.dart';

import '../../core/config/app_config.dart';

/// Streak indicator widget displayed on the home screen and session summary.
///
/// Shows the current consecutive-day streak with a fire icon.
/// Animates on streak increment (celebration animation in MOB-007).
class StreakIndicator extends StatelessWidget {
  const StreakIndicator({
    super.key,
    required this.streakDays,
    this.size = StreakIndicatorSize.medium,
  });

  final int streakDays;
  final StreakIndicatorSize size;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final iconSize = size == StreakIndicatorSize.large ? 32.0 : 20.0;
    final textStyle = size == StreakIndicatorSize.large
        ? theme.textTheme.headlineMedium
        : theme.textTheme.labelLarge;

    return Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        Icon(
          Icons.local_fire_department_rounded,
          color: streakDays > 0
              ? theme.colorScheme.secondary
              : theme.colorScheme.onSurfaceVariant.withValues(alpha: 0.5),
          size: iconSize,
        ),
        const SizedBox(width: SpacingTokens.xs),
        Text(
          '$streakDays',
          style: textStyle?.copyWith(
            color: streakDays > 0
                ? theme.colorScheme.secondary
                : theme.colorScheme.onSurfaceVariant,
            fontWeight: FontWeight.w700,
          ),
        ),
      ],
    );
  }
}

/// Size variants for the streak indicator.
enum StreakIndicatorSize {
  medium,
  large,
}

/// XP progress bar widget showing current level progress.
///
/// Displays a linear progress indicator with current XP / next level XP.
/// The XP-to-level formula and animation will be finalized in MOB-007.
class XpProgressBar extends StatelessWidget {
  const XpProgressBar({
    super.key,
    required this.currentXp,
    required this.levelXp,
    required this.level,
  });

  /// Current XP within the current level.
  final int currentXp;

  /// XP required to reach the next level.
  final int levelXp;

  /// Current student level.
  final int level;

  double get _progress => levelXp > 0 ? (currentXp / levelXp).clamp(0.0, 1.0) : 0.0;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Row(
          mainAxisAlignment: MainAxisAlignment.spaceBetween,
          children: [
            Text(
              'Level $level',
              style: theme.textTheme.labelLarge?.copyWith(
                fontWeight: FontWeight.w600,
              ),
            ),
            Text(
              '$currentXp / $levelXp XP',
              style: theme.textTheme.labelMedium?.copyWith(
                color: colorScheme.onSurfaceVariant,
              ),
            ),
          ],
        ),
        const SizedBox(height: SpacingTokens.xs),
        ClipRRect(
          borderRadius: BorderRadius.circular(RadiusTokens.full),
          child: LinearProgressIndicator(
            value: _progress,
            minHeight: 8,
            backgroundColor: colorScheme.surfaceContainerHighest,
            valueColor: AlwaysStoppedAnimation<Color>(colorScheme.primary),
          ),
        ),
      ],
    );
  }
}

/// Badge display chip shown in session summaries and profile.
///
/// Shows the badge icon and name. New badges pulse with a glow effect
/// (animation wired in MOB-007).
class BadgeChip extends StatelessWidget {
  const BadgeChip({
    super.key,
    required this.name,
    required this.iconAsset,
    this.isNew = false,
  });

  final String name;
  final String iconAsset;
  final bool isNew;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;

    return Container(
      padding: const EdgeInsets.symmetric(
        horizontal: SpacingTokens.sm,
        vertical: SpacingTokens.xs,
      ),
      decoration: BoxDecoration(
        color: isNew
            ? colorScheme.primaryContainer
            : colorScheme.surfaceContainerHighest,
        borderRadius: BorderRadius.circular(RadiusTokens.full),
        border: isNew
            ? Border.all(color: colorScheme.primary, width: 2)
            : null,
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Icon(
            Icons.emoji_events_rounded,
            size: 16,
            color: isNew ? colorScheme.primary : colorScheme.onSurfaceVariant,
          ),
          const SizedBox(width: SpacingTokens.xs),
          Text(
            name,
            style: theme.textTheme.labelSmall?.copyWith(
              color: isNew
                  ? colorScheme.onPrimaryContainer
                  : colorScheme.onSurfaceVariant,
              fontWeight: isNew ? FontWeight.w600 : FontWeight.w400,
            ),
          ),
        ],
      ),
    );
  }
}
