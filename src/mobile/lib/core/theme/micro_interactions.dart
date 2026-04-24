// =============================================================================
// Cena Adaptive Learning Platform — Micro-interactions (MOB-VIS-004)
// =============================================================================
//
// Research ref: CENA_UI_UX_Design_Strategy_2026.md §1.7, §4
// - Button press: subtle scale 0.95x + haptic
// - Correct answer: green pulse + confetti
// - Wrong answer: gentle shake
// - Streak flame: flicker animation
// - Progress: liquid fill animation
// =============================================================================

import 'dart:math';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../config/app_config.dart';

// ---------------------------------------------------------------------------
// TapScaleButton — press-down scale animation for any button
// ---------------------------------------------------------------------------

/// Wraps any child with a press-down scale animation (0.95x) and optional
/// haptic feedback on tap. Use for primary action buttons.
class TapScaleButton extends StatefulWidget {
  const TapScaleButton({
    super.key,
    required this.child,
    required this.onTap,
    this.scaleFactor = 0.95,
    this.enableHaptic = true,
  });

  final Widget child;
  final VoidCallback? onTap;
  final double scaleFactor;
  final bool enableHaptic;

  @override
  State<TapScaleButton> createState() => _TapScaleButtonState();
}

class _TapScaleButtonState extends State<TapScaleButton>
    with SingleTickerProviderStateMixin {
  late final AnimationController _controller;
  late final Animation<double> _scale;

  @override
  void initState() {
    super.initState();
    _controller = AnimationController(
      vsync: this,
      duration: const Duration(milliseconds: 100),
    );
    _scale = Tween(begin: 1.0, end: widget.scaleFactor).animate(
      CurvedAnimation(parent: _controller, curve: Curves.easeInOut),
    );
  }

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return GestureDetector(
      onTapDown: (_) => _controller.forward(),
      onTapUp: (_) {
        _controller.reverse();
        if (widget.enableHaptic) {
          HapticFeedback.lightImpact();
        }
        widget.onTap?.call();
      },
      onTapCancel: () => _controller.reverse(),
      child: ScaleTransition(scale: _scale, child: widget.child),
    );
  }
}

// ---------------------------------------------------------------------------
// ShakeWidget — gentle horizontal shake for wrong answers
// ---------------------------------------------------------------------------

class ShakeWidget extends StatefulWidget {
  const ShakeWidget({
    super.key,
    required this.child,
    this.shakeCount = 3,
    this.shakeOffset = 6.0,
  });

  final Widget child;
  final int shakeCount;
  final double shakeOffset;

  @override
  State<ShakeWidget> createState() => ShakeWidgetState();
}

class ShakeWidgetState extends State<ShakeWidget>
    with SingleTickerProviderStateMixin {
  late final AnimationController _controller;

  @override
  void initState() {
    super.initState();
    _controller = AnimationController(
      vsync: this,
      duration: const Duration(milliseconds: 400),
    );
  }

  /// Call this to trigger the shake animation.
  void shake() {
    _controller.forward(from: 0);
    HapticFeedback.mediumImpact();
  }

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return AnimatedBuilder(
      animation: _controller,
      builder: (context, child) {
        final sineValue =
            sin(widget.shakeCount * 2 * pi * _controller.value);
        return Transform.translate(
          offset: Offset(sineValue * widget.shakeOffset, 0),
          child: child,
        );
      },
      child: widget.child,
    );
  }
}

// ---------------------------------------------------------------------------
// PulseGlow — pulsing glow effect for streak flame, active badges
// ---------------------------------------------------------------------------

class PulseGlow extends StatefulWidget {
  const PulseGlow({
    super.key,
    required this.child,
    this.color,
    this.glowRadius = 12.0,
    this.duration = const Duration(milliseconds: 1500),
  });

  final Widget child;
  final Color? color;
  final double glowRadius;
  final Duration duration;

  @override
  State<PulseGlow> createState() => _PulseGlowState();
}

class _PulseGlowState extends State<PulseGlow>
    with SingleTickerProviderStateMixin {
  late final AnimationController _controller;

  @override
  void initState() {
    super.initState();
    _controller = AnimationController(vsync: this, duration: widget.duration)
      ..repeat(reverse: true);
  }

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final glowColor =
        widget.color ?? Theme.of(context).colorScheme.primary;

    return AnimatedBuilder(
      animation: _controller,
      builder: (context, child) {
        return Container(
          decoration: BoxDecoration(
            shape: BoxShape.circle,
            boxShadow: [
              BoxShadow(
                color: glowColor.withValues(alpha: 0.3 * _controller.value),
                blurRadius: widget.glowRadius * _controller.value,
                spreadRadius: widget.glowRadius * 0.3 * _controller.value,
              ),
            ],
          ),
          child: child,
        );
      },
      child: widget.child,
    );
  }
}

// ---------------------------------------------------------------------------
// CountUpText — animated number counter (XP, streak, score)
// ---------------------------------------------------------------------------

class CountUpText extends StatelessWidget {
  const CountUpText({
    super.key,
    required this.value,
    this.prefix = '',
    this.suffix = '',
    this.style,
    this.duration = AnimationTokens.slow,
  });

  final int value;
  final String prefix;
  final String suffix;
  final TextStyle? style;
  final Duration duration;

  @override
  Widget build(BuildContext context) {
    return TweenAnimationBuilder<int>(
      tween: IntTween(begin: 0, end: value),
      duration: duration,
      curve: Curves.easeOutCubic,
      builder: (context, val, _) {
        return Text('$prefix$val$suffix', style: style);
      },
    );
  }
}

// ---------------------------------------------------------------------------
// StreakFlame — animated flame icon with flicker
// ---------------------------------------------------------------------------

class StreakFlame extends StatefulWidget {
  const StreakFlame({super.key, this.size = 24});

  final double size;

  @override
  State<StreakFlame> createState() => _StreakFlameState();
}

class _StreakFlameState extends State<StreakFlame>
    with SingleTickerProviderStateMixin {
  late final AnimationController _controller;

  @override
  void initState() {
    super.initState();
    _controller = AnimationController(
      vsync: this,
      duration: const Duration(milliseconds: 800),
    )..repeat(reverse: true);
  }

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return AnimatedBuilder(
      animation: _controller,
      builder: (context, _) {
        final scale = 0.9 + 0.1 * _controller.value;
        final rotation = 0.05 * sin(_controller.value * pi * 2);
        return Transform(
          alignment: Alignment.bottomCenter,
          transform: Matrix4.identity()
            ..scale(scale, scale)
            ..rotateZ(rotation),
          child: Icon(
            Icons.local_fire_department_rounded,
            size: widget.size,
            color: Color.lerp(
              const Color(0xFFFF6D00),
              const Color(0xFFFFAB00),
              _controller.value,
            ),
          ),
        );
      },
    );
  }
}
