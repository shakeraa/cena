// =============================================================================
// Cena Adaptive Learning Platform — Momentum Meter Widget
// =============================================================================

import 'package:flutter/material.dart';

import '../../core/config/app_config.dart';

/// 7-day rolling momentum gauge as a low-anxiety alternative to streaks.
class MomentumMeter extends StatelessWidget {
  const MomentumMeter({
    super.key,
    required this.percentage,
    required this.daysStudied,
  });

  final int percentage;
  final int daysStudied;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;
    final value = (percentage / 100).clamp(0.0, 1.0);

    return Card(
      elevation: 2,
      child: Padding(
        padding: const EdgeInsets.all(SpacingTokens.md),
        child: Row(
          children: [
            SizedBox(
              width: 96,
              height: 96,
              child: TweenAnimationBuilder<double>(
                tween: Tween(begin: 0, end: value),
                duration: AnimationTokens.slow,
                curve: Curves.easeOutCubic,
                builder: (context, progress, _) {
                  return Stack(
                    alignment: Alignment.center,
                    children: [
                      CircularProgressIndicator(
                        value: progress,
                        strokeWidth: 10,
                        backgroundColor: colorScheme.surfaceContainerHighest,
                        valueColor: AlwaysStoppedAnimation<Color>(
                          _momentumColor(percentage),
                        ),
                      ),
                      Text(
                        '$percentage%',
                        style: theme.textTheme.titleMedium?.copyWith(
                          fontWeight: FontWeight.w800,
                          color: _momentumColor(percentage),
                        ),
                      ),
                    ],
                  );
                },
              ),
            ),
            const SizedBox(width: SpacingTokens.md),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    'Momentum Meter',
                    style: theme.textTheme.titleMedium?.copyWith(
                      fontWeight: FontWeight.w700,
                    ),
                  ),
                  const SizedBox(height: SpacingTokens.xs),
                  Text(
                    'למדת $daysStudied מתוך 7 הימים האחרונים',
                    style: theme.textTheme.bodyMedium?.copyWith(
                      color: colorScheme.onSurfaceVariant,
                    ),
                  ),
                  const SizedBox(height: SpacingTokens.xs),
                  Text(
                    'כל יום לימוד מקדם אותך קדימה.',
                    style: theme.textTheme.labelLarge?.copyWith(
                      color: colorScheme.primary,
                      fontWeight: FontWeight.w600,
                    ),
                  ),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }

  Color _momentumColor(int pct) {
    if (pct >= 80) return const Color(0xFF2E7D32);
    if (pct >= 50) return const Color(0xFF00897B);
    if (pct >= 30) return const Color(0xFFF9A825);
    return const Color(0xFFEF6C00);
  }
}
