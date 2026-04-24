// =============================================================================
// Cena Adaptive Learning Platform — Flow Ambient Indicator (MOB-031)
// =============================================================================
//
// A subtle ambient background tint that shifts color temperature based on
// the student's flow state. This is intentionally NON-intrusive — no
// notifications, no text, just a barely-perceptible color wash.
//
// Flow State   -> Color                     -> Opacity
// warming      -> cool blue  (0xFF1565C0)   -> 0.05
// approaching  -> light amber(0xFFFF8F00)   -> 0.05
// inFlow       -> warm gold  (0xFFFFB300)   -> 0.08
// disrupted    -> cool blue  (0xFF1565C0)   -> 0.03
// fatigued     -> none                      -> 0.00
// =============================================================================

import 'package:flutter/material.dart';

import '../../../core/services/flow_monitor_service.dart';

/// Ambient background overlay that shifts color temperature by flow state.
///
/// Place this as the first child in a [Stack] behind session content.
/// Uses [TweenAnimationBuilder] for 60fps transitions with no
/// [AnimationController] overhead.
class FlowAmbientIndicator extends StatelessWidget {
  const FlowAmbientIndicator({
    super.key,
    required this.flowState,
  });

  /// The current flow state to visualize.
  final FlowState flowState;

  /// Transition duration for color changes — long enough to be imperceptible.
  static const Duration _transitionDuration = Duration(milliseconds: 600);

  @override
  Widget build(BuildContext context) {
    final targetColor = _colorForState(flowState);

    return TweenAnimationBuilder<Color?>(
      tween: ColorTween(end: targetColor),
      duration: _transitionDuration,
      curve: Curves.easeInOut,
      builder: (context, color, child) {
        if (color == null || color == Colors.transparent) {
          return const SizedBox.shrink();
        }
        return Positioned.fill(
          child: IgnorePointer(
            child: DecoratedBox(
              decoration: BoxDecoration(color: color),
            ),
          ),
        );
      },
    );
  }

  /// Maps flow state to a very subtle ambient color.
  static Color _colorForState(FlowState state) {
    switch (state) {
      case FlowState.warming:
        return const Color(0xFF1565C0).withValues(alpha: 0.05);
      case FlowState.approaching:
        return const Color(0xFFFF8F00).withValues(alpha: 0.05);
      case FlowState.inFlow:
        return const Color(0xFFFFB300).withValues(alpha: 0.08);
      case FlowState.disrupted:
        return const Color(0xFF1565C0).withValues(alpha: 0.03);
      case FlowState.fatigued:
        return Colors.transparent;
    }
  }
}

/// Summary widget showing flow time percentage after session ends.
///
/// Displays the percentage of session time the student spent in flow
/// alongside a flame icon. Only shows if the percentage is > 0%.
class FlowTimeSummary extends StatelessWidget {
  const FlowTimeSummary({
    super.key,
    required this.flowTimePercentage,
  });

  /// The flow time as a fraction [0.0, 1.0].
  final double flowTimePercentage;

  @override
  Widget build(BuildContext context) {
    if (flowTimePercentage <= 0) return const SizedBox.shrink();

    final theme = Theme.of(context);
    final pct = (flowTimePercentage * 100).round();

    return Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        Icon(
          Icons.local_fire_department_rounded,
          size: 20,
          color: theme.colorScheme.primary,
        ),
        const SizedBox(width: 8),
        Text(
          'Flow time: $pct%',
          style: theme.textTheme.bodyMedium?.copyWith(
            fontWeight: FontWeight.w600,
          ),
        ),
      ],
    );
  }
}
