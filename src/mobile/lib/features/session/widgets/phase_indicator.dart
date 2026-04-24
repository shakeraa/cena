// =============================================================================
// Cena Adaptive Learning Platform — Phase Indicator Widget
// Subtle progress bar color shift that signals session phase transitions
// without explicit labels. The student never sees "Phase 2" — only a
// gradual color temperature change.
// =============================================================================

import 'package:flutter/material.dart';

import '../../../core/config/app_config.dart';
import '../models/session_flow_arc.dart';

/// A progress indicator that subtly shifts color based on the current
/// [SessionPhase] of the flow arc.
///
/// The transition is animated at 60fps using [TweenAnimationBuilder] so
/// phase changes feel organic rather than jarring. The student perceives
/// the color shift as ambient feedback — warm-up feels calm (blue),
/// core feels energized (amber), cool-down feels accomplished (green).
///
/// This widget is designed to replace or overlay the existing [ProgressBar]
/// track color when a [SessionFlowArc] is active.
class PhaseIndicator extends StatelessWidget {
  const PhaseIndicator({
    super.key,
    required this.phase,
    required this.progress,
    this.height = 6.0,
    this.transitionDuration = const Duration(milliseconds: 800),
  });

  /// The current session phase determining the target color.
  final SessionPhase phase;

  /// Session progress [0.0, 1.0] for the linear indicator value.
  final double progress;

  /// Height of the progress track in logical pixels.
  final double height;

  /// Duration of the color cross-fade between phases.
  /// Default 800ms ensures the shift is noticeable but not abrupt.
  final Duration transitionDuration;

  @override
  Widget build(BuildContext context) {
    final colorScheme = Theme.of(context).colorScheme;
    final targetColor = PhaseColors.forPhase(phase);

    return ClipRRect(
      borderRadius: BorderRadius.circular(RadiusTokens.full),
      child: TweenAnimationBuilder<Color?>(
        tween: ColorTween(
          begin: targetColor,
          end: targetColor,
        ),
        duration: transitionDuration,
        builder: (context, color, _) {
          return TweenAnimationBuilder<double>(
            tween: Tween(begin: 0, end: progress),
            duration: AnimationTokens.normal,
            builder: (context, progressValue, _) {
              return LinearProgressIndicator(
                value: progressValue,
                minHeight: height,
                backgroundColor: colorScheme.surfaceContainerHighest,
                valueColor: AlwaysStoppedAnimation<Color>(
                  color ?? targetColor,
                ),
              );
            },
          );
        },
      ),
    );
  }
}

/// An animated phase color wrapper that smoothly transitions the color
/// between session phases over [duration].
///
/// Wraps a child builder that receives the interpolated [Color]. This is
/// useful for applying phase-aware tinting to arbitrary widgets (e.g.
/// card borders, glow effects) beyond the progress bar.
class AnimatedPhaseColor extends StatelessWidget {
  const AnimatedPhaseColor({
    super.key,
    required this.phase,
    required this.builder,
    this.duration = const Duration(milliseconds: 800),
  });

  /// The current session phase.
  final SessionPhase phase;

  /// Builder that receives the interpolated color.
  final Widget Function(BuildContext context, Color color) builder;

  /// Duration of the color transition.
  final Duration duration;

  @override
  Widget build(BuildContext context) {
    final targetColor = PhaseColors.forPhase(phase);

    return TweenAnimationBuilder<Color?>(
      tween: ColorTween(
        begin: targetColor,
        end: targetColor,
      ),
      duration: duration,
      builder: (context, color, _) {
        return builder(context, color ?? targetColor);
      },
    );
  }
}

/// A compact phase dot indicator that shows three small dots representing
/// warm-up, core, and cool-down. The active phase dot is larger and filled
/// with the phase color; inactive dots are dimmed outlines.
///
/// This provides an additional subtle cue about session position without
/// ever showing text labels to the student.
class PhaseDotsIndicator extends StatelessWidget {
  const PhaseDotsIndicator({
    super.key,
    required this.currentPhase,
    this.dotSize = 6.0,
    this.activeDotSize = 10.0,
    this.spacing = 6.0,
  });

  final SessionPhase currentPhase;
  final double dotSize;
  final double activeDotSize;
  final double spacing;

  @override
  Widget build(BuildContext context) {
    final colorScheme = Theme.of(context).colorScheme;

    return Row(
      mainAxisSize: MainAxisSize.min,
      children: SessionPhase.values.map((phase) {
        final isActive = phase == currentPhase;
        final color = PhaseColors.forPhase(phase);
        final size = isActive ? activeDotSize : dotSize;

        return Padding(
          padding: EdgeInsets.symmetric(horizontal: spacing / 2),
          child: TweenAnimationBuilder<double>(
            tween: Tween(begin: size, end: size),
            duration: AnimationTokens.normal,
            builder: (context, animatedSize, _) {
              return Container(
                width: animatedSize,
                height: animatedSize,
                decoration: BoxDecoration(
                  shape: BoxShape.circle,
                  color: isActive ? color : Colors.transparent,
                  border: Border.all(
                    color: isActive
                        ? color
                        : colorScheme.outlineVariant,
                    width: 1.5,
                  ),
                ),
              );
            },
          ),
        );
      }).toList(),
    );
  }
}
