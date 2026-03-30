// =============================================================================
// Cena Adaptive Learning Platform — Session Progress Bar
// Shows question count, accuracy %, and fatigue via color gradient.
// =============================================================================

import 'package:flutter/material.dart';

import '../../../core/config/app_config.dart';

/// Displays session progress: questions attempted, accuracy rate, and a
/// fatigue-aware color gradient (green -> yellow -> red).
///
/// The progress track shifts color as [fatigueScore] approaches 1.0, giving
/// the student a visual cue that a break may be coming.
class ProgressBar extends StatelessWidget {
  const ProgressBar({
    super.key,
    required this.questionsAttempted,
    required this.accuracy,
    required this.fatigueScore,
    required this.elapsed,
    required this.targetDurationMinutes,
  });

  final int questionsAttempted;

  /// Correct answer ratio [0.0, 1.0].
  final double accuracy;

  /// Cognitive load score [0.0, 1.0]. Above 0.7 triggers break suggestion.
  final double fatigueScore;

  final Duration elapsed;
  final int targetDurationMinutes;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;

    final sessionProgress = _sessionProgress;
    final trackColor = _fatigueColor(fatigueScore);
    final accuracyPct = (accuracy * 100).toInt();

    return Container(
      padding: const EdgeInsets.symmetric(
        horizontal: SpacingTokens.md,
        vertical: SpacingTokens.sm,
      ),
      decoration: BoxDecoration(
        color: colorScheme.surface,
        border: Border(
          bottom: BorderSide(
            color: colorScheme.outlineVariant,
            width: 1,
          ),
        ),
      ),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          // Stats row
          Row(
            children: [
              _StatChip(
                icon: Icons.help_outline_rounded,
                label: '$questionsAttempted',
                tooltip: 'Questions answered',
                color: colorScheme.primary,
              ),
              const SizedBox(width: SpacingTokens.sm),
              _StatChip(
                icon: Icons.check_circle_outline_rounded,
                label: '$accuracyPct%',
                tooltip: 'Accuracy',
                color: _accuracyColor(accuracy),
              ),
              const Spacer(),
              _FatigueIndicator(fatigueScore: fatigueScore),
              const SizedBox(width: SpacingTokens.sm),
              _TimerDisplay(elapsed: elapsed),
            ],
          ),
          const SizedBox(height: SpacingTokens.xs),
          // Progress track
          ClipRRect(
            borderRadius: BorderRadius.circular(RadiusTokens.full),
            child: TweenAnimationBuilder<double>(
              tween: Tween(begin: 0, end: sessionProgress),
              duration: AnimationTokens.normal,
              builder: (context, value, _) {
                return LinearProgressIndicator(
                  value: value,
                  minHeight: 6,
                  backgroundColor: colorScheme.surfaceContainerHighest,
                  valueColor: AlwaysStoppedAnimation<Color>(trackColor),
                );
              },
            ),
          ),
        ],
      ),
    );
  }

  /// Session time progress [0.0, 1.0] based on target duration.
  double get _sessionProgress {
    if (targetDurationMinutes <= 0) return 0;
    final targetMs = targetDurationMinutes * 60 * 1000;
    return (elapsed.inMilliseconds / targetMs).clamp(0.0, 1.0);
  }

  /// Interpolates green -> yellow -> red as fatigue rises.
  Color _fatigueColor(double fatigue) {
    if (fatigue < 0.4) {
      return Color.lerp(
        const Color(0xFF4CAF50),
        const Color(0xFFFFEB3B),
        fatigue / 0.4,
      )!;
    } else {
      return Color.lerp(
        const Color(0xFFFFEB3B),
        const Color(0xFFF44336),
        (fatigue - 0.4) / 0.6,
      )!;
    }
  }

  Color _accuracyColor(double acc) {
    if (acc >= 0.8) return const Color(0xFF4CAF50);
    if (acc >= 0.5) return const Color(0xFFFF9800);
    return const Color(0xFFF44336);
  }
}

// ---------------------------------------------------------------------------
// Sub-widgets
// ---------------------------------------------------------------------------

class _StatChip extends StatelessWidget {
  const _StatChip({
    required this.icon,
    required this.label,
    required this.tooltip,
    required this.color,
  });

  final IconData icon;
  final String label;
  final String tooltip;
  final Color color;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Tooltip(
      message: tooltip,
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Icon(icon, size: 14, color: color),
          const SizedBox(width: SpacingTokens.xxs),
          Text(
            label,
            style: theme.textTheme.labelMedium?.copyWith(
              color: color,
              fontWeight: FontWeight.w600,
            ),
          ),
        ],
      ),
    );
  }
}

/// Emoji-based fatigue indicator with tooltip.
class _FatigueIndicator extends StatelessWidget {
  const _FatigueIndicator({required this.fatigueScore});

  final double fatigueScore;

  @override
  Widget build(BuildContext context) {
    final String emoji;
    final String label;
    if (fatigueScore < 0.4) {
      emoji = '😊';
      label = 'Focused';
    } else if (fatigueScore < 0.7) {
      emoji = '😐';
      label = 'Distracted';
    } else {
      emoji = '😴';
      label = 'Fatigued';
    }
    return Tooltip(
      message: label,
      child: Text(emoji, style: const TextStyle(fontSize: 16)),
    );
  }
}

/// Elapsed session timer formatted as MM:SS.
class _TimerDisplay extends StatelessWidget {
  const _TimerDisplay({required this.elapsed});

  final Duration elapsed;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final minutes = elapsed.inMinutes.remainder(60).toString().padLeft(2, '0');
    final seconds = elapsed.inSeconds.remainder(60).toString().padLeft(2, '0');
    return Text(
      '$minutes:$seconds',
      style: theme.textTheme.labelMedium?.copyWith(
        fontFamily: TypographyTokens.monoFontFamily,
        color: theme.colorScheme.onSurfaceVariant,
      ),
    );
  }
}
