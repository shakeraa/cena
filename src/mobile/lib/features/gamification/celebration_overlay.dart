// =============================================================================
// Cena Adaptive Learning Platform — Celebration Overlay
// =============================================================================
//
// A Stack-based overlay that renders tiered celebration animations:
//   Micro  → subtle checkmark color flash
//   Minor  → "+N XP" float-up with bounce (delegates to XpPopup)
//   Medium → expanding ring + sparkle particles
//   Major  → full-screen confetti effect + badge
//   Epic   → immersive glow pulse + confetti + text
// =============================================================================

import 'dart:math';

import 'package:flutter/material.dart';

import '../../core/config/app_config.dart';
import 'celebration_service.dart';

/// Controller for triggering celebration animations.
///
/// Attach to a [CelebrationOverlay] and call [celebrate] when an achievement
/// occurs. The overlay automatically dismisses after the tier's duration.
class CelebrationController {
  _CelebrationOverlayState? _state;

  void _attach(_CelebrationOverlayState state) => _state = state;
  void _detach(_CelebrationOverlayState state) {
    if (_state == state) _state = null;
  }

  /// Trigger a celebration animation.
  void celebrate({
    required CelebrationTier tier,
    int xp = 0,
    String? message,
  }) {
    _state?.trigger(tier: tier, xp: xp, message: message);
  }
}

/// Celebration overlay widget — place inside a Stack.
class CelebrationOverlay extends StatefulWidget {
  const CelebrationOverlay({
    super.key,
    required this.controller,
  });

  final CelebrationController controller;

  @override
  State<CelebrationOverlay> createState() => _CelebrationOverlayState();
}

class _CelebrationOverlayState extends State<CelebrationOverlay>
    with TickerProviderStateMixin {
  AnimationController? _animController;
  CelebrationTier? _activeTier;
  int _xp = 0;
  String? _message;
  bool _visible = false;

  @override
  void initState() {
    super.initState();
    widget.controller._attach(this);
  }

  @override
  void dispose() {
    widget.controller._detach(this);
    _animController?.dispose();
    super.dispose();
  }

  void trigger({
    required CelebrationTier tier,
    int xp = 0,
    String? message,
  }) {
    if (!mounted) return;

    _animController?.dispose();
    _animController = AnimationController(
      vsync: this,
      duration: CelebrationService.duration(tier),
    );

    _animController!.addStatusListener((status) {
      if (status == AnimationStatus.completed && mounted) {
        setState(() => _visible = false);
        _animController?.reset();
      }
    });

    setState(() {
      _activeTier = tier;
      _xp = xp;
      _message = message;
      _visible = true;
    });
    _animController!.forward(from: 0.0);
  }

  @override
  Widget build(BuildContext context) {
    if (!_visible || _activeTier == null || _animController == null) {
      return const SizedBox.shrink();
    }

    final controller = _animController!;
    switch (_activeTier!) {
      case CelebrationTier.micro:
        return _MicroCelebration(animation: controller);
      case CelebrationTier.minor:
        return _MinorCelebration(animation: controller, xp: _xp);
      case CelebrationTier.medium:
        return _MediumCelebration(
            animation: controller, xp: _xp, message: _message);
      case CelebrationTier.major:
        return _MajorCelebration(
            animation: controller, xp: _xp, message: _message);
      case CelebrationTier.epic:
        return _EpicCelebration(
            animation: controller, xp: _xp, message: _message);
    }
  }
}

// ---------------------------------------------------------------------------
// Tier 1: Micro — Subtle green checkmark flash
// ---------------------------------------------------------------------------

class _MicroCelebration extends AnimatedWidget {
  const _MicroCelebration({required Animation<double> animation})
      : super(listenable: animation);

  @override
  Widget build(BuildContext context) {
    final progress = (listenable as Animation<double>).value;
    final opacity = progress < 0.5 ? progress * 2 : (1.0 - progress) * 2;

    return Positioned.fill(
      child: IgnorePointer(
        child: Center(
          child: Opacity(
            opacity: opacity.clamp(0.0, 1.0),
            child: Icon(
              Icons.check_circle_rounded,
              size: 48,
              color: const Color(0xFF4CAF50).withValues(alpha: 0.7),
            ),
          ),
        ),
      ),
    );
  }
}

// ---------------------------------------------------------------------------
// Tier 2: Minor — "+N XP" float-up with bounce scale
// ---------------------------------------------------------------------------

class _MinorCelebration extends AnimatedWidget {
  const _MinorCelebration({
    required Animation<double> animation,
    required this.xp,
  }) : super(listenable: animation);

  final int xp;

  @override
  Widget build(BuildContext context) {
    final progress = (listenable as Animation<double>).value;

    // Float upward 60px
    final yOffset = -60.0 * Curves.easeOut.transform(progress);
    // Fade out in second half
    final opacity =
        progress < 0.5 ? 1.0 : (1.0 - (progress - 0.5) * 2).clamp(0.0, 1.0);
    // Bounce-in scale
    final scale = progress < 0.3
        ? 0.6 + 0.7 * Curves.easeOut.transform(progress / 0.3)
        : 1.3 - 0.3 * Curves.bounceOut.transform((progress - 0.3) / 0.7);

    return Positioned(
      bottom: MediaQuery.of(context).size.height * 0.35,
      left: 0,
      right: 0,
      child: IgnorePointer(
        child: Transform.translate(
          offset: Offset(0, yOffset),
          child: Opacity(
            opacity: opacity,
            child: Transform.scale(
              scale: scale.clamp(0.5, 1.5),
              child: Center(
                child: Container(
                  padding: const EdgeInsets.symmetric(
                    horizontal: SpacingTokens.md,
                    vertical: SpacingTokens.xs,
                  ),
                  decoration: BoxDecoration(
                    color: const Color(0xFFFFD700).withValues(alpha: 0.15),
                    borderRadius: BorderRadius.circular(RadiusTokens.full),
                    border: Border.all(
                      color: const Color(0xFFFFD700),
                      width: 1.5,
                    ),
                  ),
                  child: Text(
                    '+$xp XP',
                    style: const TextStyle(
                      color: Color(0xFFFFD700),
                      fontSize: 22,
                      fontWeight: FontWeight.w800,
                      letterSpacing: 0.5,
                    ),
                  ),
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}

// ---------------------------------------------------------------------------
// Tier 3: Medium — Expanding ring + sparkle particles
// ---------------------------------------------------------------------------

class _MediumCelebration extends StatelessWidget {
  const _MediumCelebration({
    required this.animation,
    required this.xp,
    this.message,
  });

  final AnimationController animation;
  final int xp;
  final String? message;

  @override
  Widget build(BuildContext context) {
    return AnimatedBuilder(
      animation: animation,
      builder: (context, _) {
        final progress = animation.value;
        final ringScale = 0.5 + 2.5 * Curves.easeOut.transform(progress);
        final ringOpacity =
            (1.0 - Curves.easeIn.transform(progress)).clamp(0.0, 1.0);
        final textOpacity = progress < 0.2
            ? progress / 0.2
            : progress > 0.7
                ? ((1.0 - progress) / 0.3).clamp(0.0, 1.0)
                : 1.0;

        return Positioned.fill(
          child: IgnorePointer(
            child: Stack(
              alignment: Alignment.center,
              children: [
                // Expanding ring
                Transform.scale(
                  scale: ringScale,
                  child: Container(
                    width: 100,
                    height: 100,
                    decoration: BoxDecoration(
                      shape: BoxShape.circle,
                      border: Border.all(
                        color: const Color(0xFF7C4DFF)
                            .withValues(alpha: ringOpacity * 0.6),
                        width: 3,
                      ),
                    ),
                  ),
                ),
                // Sparkle particles
                ..._buildSparkles(progress, context),
                // Text
                Opacity(
                  opacity: textOpacity,
                  child: Column(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      Text(
                        '+$xp XP',
                        style: const TextStyle(
                          color: Color(0xFFFFD700),
                          fontSize: 28,
                          fontWeight: FontWeight.w800,
                        ),
                      ),
                      if (message != null)
                        Padding(
                          padding: const EdgeInsets.only(top: SpacingTokens.xs),
                          child: Text(
                            message!,
                            style: TextStyle(
                              color: Theme.of(context)
                                  .colorScheme
                                  .onSurface
                                  .withValues(alpha: 0.8),
                              fontSize: 14,
                              fontWeight: FontWeight.w600,
                            ),
                          ),
                        ),
                    ],
                  ),
                ),
              ],
            ),
          ),
        );
      },
    );
  }

  List<Widget> _buildSparkles(double progress, BuildContext context) {
    final size = MediaQuery.of(context).size;
    final center = Offset(size.width / 2, size.height / 2);
    final rng = Random(42); // deterministic for consistent look
    const count = 8;

    return List.generate(count, (i) {
      final angle = (i / count) * 2 * pi + rng.nextDouble() * 0.5;
      final distance = 40 + 80 * Curves.easeOut.transform(progress);
      final sparkleOpacity =
          (1.0 - Curves.easeIn.transform(progress)).clamp(0.0, 1.0);
      final sparkleScale =
          progress < 0.3 ? progress / 0.3 : 1.0 - (progress - 0.3) / 0.7;

      return Positioned(
        left: center.dx + cos(angle) * distance - 6,
        top: center.dy + sin(angle) * distance - 6,
        child: Opacity(
          opacity: sparkleOpacity,
          child: Transform.scale(
            scale: sparkleScale.clamp(0.0, 1.0),
            child: const Icon(
              Icons.auto_awesome,
              size: 12,
              color: Color(0xFFFFD700),
            ),
          ),
        ),
      );
    });
  }
}

// ---------------------------------------------------------------------------
// Tier 4: Major — Full-screen confetti + level badge
// ---------------------------------------------------------------------------

class _MajorCelebration extends StatelessWidget {
  const _MajorCelebration({
    required this.animation,
    required this.xp,
    this.message,
  });

  final AnimationController animation;
  final int xp;
  final String? message;

  @override
  Widget build(BuildContext context) {
    return AnimatedBuilder(
      animation: animation,
      builder: (context, _) {
        final progress = animation.value;
        final bgOpacity = progress < 0.1
            ? progress / 0.1
            : progress > 0.8
                ? ((1.0 - progress) / 0.2).clamp(0.0, 1.0)
                : 1.0;
        final badgeScale = progress < 0.2
            ? Curves.elasticOut.transform(progress / 0.2).clamp(0.0, 1.5)
            : 1.0;
        final textOpacity = progress < 0.3
            ? ((progress - 0.15) / 0.15).clamp(0.0, 1.0)
            : progress > 0.75
                ? ((1.0 - progress) / 0.25).clamp(0.0, 1.0)
                : 1.0;

        return Positioned.fill(
          child: IgnorePointer(
            child: Container(
              color: Colors.black.withValues(alpha: bgOpacity * 0.3),
              child: Stack(
                alignment: Alignment.center,
                children: [
                  // Confetti particles
                  ..._buildConfetti(progress, context),
                  // Badge + text
                  Opacity(
                    opacity: textOpacity,
                    child: Transform.scale(
                      scale: badgeScale,
                      child: Column(
                        mainAxisSize: MainAxisSize.min,
                        children: [
                          Container(
                            width: 80,
                            height: 80,
                            decoration: BoxDecoration(
                              shape: BoxShape.circle,
                              gradient: LinearGradient(
                                begin: Alignment.topLeft,
                                end: Alignment.bottomRight,
                                colors: [
                                  const Color(0xFFFFD700),
                                  const Color(0xFFFF6F00),
                                ],
                              ),
                              boxShadow: [
                                BoxShadow(
                                  color: const Color(0xFFFFD700)
                                      .withValues(alpha: 0.5),
                                  blurRadius: 20,
                                  spreadRadius: 4,
                                ),
                              ],
                            ),
                            child: const Icon(
                              Icons.emoji_events_rounded,
                              size: 40,
                              color: Colors.white,
                            ),
                          ),
                          const SizedBox(height: SpacingTokens.md),
                          Text(
                            '+$xp XP',
                            style: const TextStyle(
                              color: Color(0xFFFFD700),
                              fontSize: 32,
                              fontWeight: FontWeight.w900,
                            ),
                          ),
                          if (message != null)
                            Padding(
                              padding:
                                  const EdgeInsets.only(top: SpacingTokens.sm),
                              child: Text(
                                message!,
                                style: const TextStyle(
                                  color: Colors.white,
                                  fontSize: 18,
                                  fontWeight: FontWeight.w700,
                                ),
                                textAlign: TextAlign.center,
                              ),
                            ),
                        ],
                      ),
                    ),
                  ),
                ],
              ),
            ),
          ),
        );
      },
    );
  }

  List<Widget> _buildConfetti(double progress, BuildContext context) {
    final size = MediaQuery.of(context).size;
    final rng = Random(123);
    const count = 24;
    const colors = [
      Color(0xFFFFD700),
      Color(0xFFFF4081),
      Color(0xFF448AFF),
      Color(0xFF69F0AE),
      Color(0xFFFF6E40),
      Color(0xFFE040FB),
    ];

    return List.generate(count, (i) {
      final x = rng.nextDouble() * size.width;
      final fallDistance = size.height * 0.8 * progress;
      final startY = -20.0 + rng.nextDouble() * 40;
      final drift = (rng.nextDouble() - 0.5) * 60 * progress;
      final opacity = (1.0 - Curves.easeIn.transform(progress)).clamp(0.0, 1.0);
      final rotation = progress * (2 + rng.nextDouble() * 4) * pi;

      return Positioned(
        left: x + drift,
        top: startY + fallDistance,
        child: Opacity(
          opacity: opacity,
          child: Transform.rotate(
            angle: rotation,
            child: Container(
              width: 8,
              height: 12,
              decoration: BoxDecoration(
                color: colors[i % colors.length],
                borderRadius: BorderRadius.circular(2),
              ),
            ),
          ),
        ),
      );
    });
  }
}

// ---------------------------------------------------------------------------
// Tier 5: Epic — Immersive glow pulse + confetti + bold text
// ---------------------------------------------------------------------------

class _EpicCelebration extends StatelessWidget {
  const _EpicCelebration({
    required this.animation,
    required this.xp,
    this.message,
  });

  final AnimationController animation;
  final int xp;
  final String? message;

  @override
  Widget build(BuildContext context) {
    return AnimatedBuilder(
      animation: animation,
      builder: (context, _) {
        final progress = animation.value;
        final phase1 = (progress / 0.4).clamp(0.0, 1.0); // glow in
        final phase2 = ((progress - 0.3) / 0.4).clamp(0.0, 1.0); // content in
        final phase3 = ((progress - 0.75) / 0.25).clamp(0.0, 1.0); // fade out

        final bgOpacity = phase1 * 0.5 * (1.0 - phase3);
        final glowPulse = sin(progress * pi * 3) * 0.15 + 0.35;
        final contentOpacity = phase2 * (1.0 - phase3);
        final contentScale = phase2 < 1.0
            ? Curves.elasticOut.transform(phase2).clamp(0.0, 1.2)
            : 1.0 + phase3 * 0.1;

        return Positioned.fill(
          child: IgnorePointer(
            child: Container(
              decoration: BoxDecoration(
                gradient: RadialGradient(
                  center: Alignment.center,
                  radius: 0.8,
                  colors: [
                    const Color(0xFFFFD700)
                        .withValues(alpha: glowPulse * (1.0 - phase3)),
                    Colors.black.withValues(alpha: bgOpacity),
                  ],
                ),
              ),
              child: Stack(
                alignment: Alignment.center,
                children: [
                  ..._buildConfetti(progress, context),
                  Opacity(
                    opacity: contentOpacity.clamp(0.0, 1.0),
                    child: Transform.scale(
                      scale: contentScale.clamp(0.5, 1.3),
                      child: Column(
                        mainAxisSize: MainAxisSize.min,
                        children: [
                          const Icon(
                            Icons.star_rounded,
                            size: 72,
                            color: Color(0xFFFFD700),
                            shadows: [
                              Shadow(
                                color: Color(0xAAFFD700),
                                blurRadius: 30,
                              ),
                            ],
                          ),
                          const SizedBox(height: SpacingTokens.md),
                          Text(
                            message ?? 'שליטה!',
                            style: const TextStyle(
                              color: Colors.white,
                              fontSize: 28,
                              fontWeight: FontWeight.w900,
                              letterSpacing: 1.0,
                            ),
                            textAlign: TextAlign.center,
                          ),
                          const SizedBox(height: SpacingTokens.sm),
                          Text(
                            '+$xp XP',
                            style: const TextStyle(
                              color: Color(0xFFFFD700),
                              fontSize: 36,
                              fontWeight: FontWeight.w900,
                            ),
                          ),
                        ],
                      ),
                    ),
                  ),
                ],
              ),
            ),
          ),
        );
      },
    );
  }

  List<Widget> _buildConfetti(double progress, BuildContext context) {
    final size = MediaQuery.of(context).size;
    final rng = Random(456);
    const count = 32;
    const colors = [
      Color(0xFFFFD700),
      Color(0xFFFF4081),
      Color(0xFF448AFF),
      Color(0xFF69F0AE),
      Color(0xFFFF6E40),
      Color(0xFFE040FB),
      Color(0xFF00E5FF),
    ];

    return List.generate(count, (i) {
      final x = rng.nextDouble() * size.width;
      final fallDistance = size.height * 0.9 * progress;
      final startY = -30.0 + rng.nextDouble() * 60;
      final drift = (rng.nextDouble() - 0.5) * 80 * progress;
      final opacity = (1.0 - Curves.easeIn.transform(progress)).clamp(0.0, 1.0);
      final rotation = progress * (3 + rng.nextDouble() * 5) * pi;

      return Positioned(
        left: x + drift,
        top: startY + fallDistance,
        child: Opacity(
          opacity: opacity,
          child: Transform.rotate(
            angle: rotation,
            child: Container(
              width: 10,
              height: 14,
              decoration: BoxDecoration(
                color: colors[i % colors.length],
                borderRadius: BorderRadius.circular(3),
              ),
            ),
          ),
        ),
      );
    });
  }
}
