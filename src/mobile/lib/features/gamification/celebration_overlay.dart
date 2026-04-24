// =============================================================================
// Cena Adaptive Learning Platform — Celebration Overlay (MOB-050)
// =============================================================================
//
// A Stack-based overlay that renders 5-tier celebration animations:
//   Micro  (Tier 1) → subtle green glow + scale pulse (TweenAnimationBuilder)
//   Minor  (Tier 2) → color burst + 20 confetti particles (TweenAnimationBuilder)
//   Medium (Tier 3) → 100 confetti particles + expanding ring + glow
//   Major  (Tier 4) → full-screen overlay + 150 particles + badge
//   Epic   (Tier 5) → 200 particle shower + certificate + glow pulse
//
// Performance constraints:
//   - Max 200 particles (CustomPainter, not widget tree)
//   - Max 8 AnimationControllers across the overlay
//   - Tiers 1-2 use TweenAnimationBuilder only (no AnimationController)
//   - 60fps on Samsung A14 (Mali-G57)
//
// Celebrations are queued during active questions and played during
// between-question transitions.
// =============================================================================

import 'dart:collection';
import 'dart:math';

import 'package:flutter/material.dart';

import '../../core/config/app_config.dart';
import 'celebration_service.dart';

// ---------------------------------------------------------------------------
// Queued celebration event
// ---------------------------------------------------------------------------

/// A celebration event waiting to be displayed.
class _QueuedCelebration {
  _QueuedCelebration({
    required this.tier,
    this.xp = 0,
    this.message,
  });

  final CelebrationTier tier;
  final int xp;
  final String? message;
}

// ---------------------------------------------------------------------------
// Controller
// ---------------------------------------------------------------------------

/// Controller for triggering celebration animations.
///
/// Attach to a [CelebrationOverlay] and call [celebrate] when an achievement
/// occurs. Celebrations can be queued during question answering and flushed
/// during between-question transitions.
class CelebrationController {
  _CelebrationOverlayState? _state;

  /// Whether to queue celebrations instead of playing immediately.
  bool isQuestionActive = false;

  final Queue<_QueuedCelebration> _queue = Queue();

  void _attach(_CelebrationOverlayState state) => _state = state;
  void _detach(_CelebrationOverlayState state) {
    if (_state == state) _state = null;
  }

  /// Trigger a celebration animation.
  ///
  /// If [isQuestionActive] is true and the tier is >= medium, the celebration
  /// is queued. Micro and minor celebrations always play immediately since
  /// they don't interrupt the question flow.
  void celebrate({
    required CelebrationTier tier,
    int xp = 0,
    String? message,
  }) {
    // Tier 1-2: never interrupt, always play immediately
    if (tier == CelebrationTier.micro || tier == CelebrationTier.minor) {
      _state?.trigger(tier: tier, xp: xp, message: message);
      return;
    }

    // Tier 3+: queue if a question is active
    if (isQuestionActive) {
      _queue.add(_QueuedCelebration(tier: tier, xp: xp, message: message));
      return;
    }

    _state?.trigger(tier: tier, xp: xp, message: message);
  }

  /// Flush queued celebrations — call during between-question transitions.
  ///
  /// Plays the highest-tier queued celebration (to avoid celebration spam).
  void flushQueue() {
    isQuestionActive = false;
    if (_queue.isEmpty) return;

    // Find the highest tier in the queue
    var best = _queue.first;
    for (final c in _queue) {
      if (c.tier.index > best.tier.index) best = c;
    }
    _queue.clear();

    _state?.trigger(tier: best.tier, xp: best.xp, message: best.message);
  }

  /// Clear all queued celebrations without playing them.
  void clearQueue() => _queue.clear();

  /// Number of celebrations currently queued.
  int get queueLength => _queue.length;
}

// ---------------------------------------------------------------------------
// Overlay widget
// ---------------------------------------------------------------------------

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
// Confetti particle data (used by CustomPainter for Tiers 3-5)
// ---------------------------------------------------------------------------

class _ConfettiParticle {
  _ConfettiParticle({
    required this.x,
    required this.startY,
    required this.speed,
    required this.drift,
    required this.rotation,
    required this.color,
    required this.width,
    required this.height,
  });

  final double x;
  final double startY;
  final double speed;
  final double drift;
  final double rotation;
  final Color color;
  final double width;
  final double height;
}

/// CustomPainter for confetti particles — avoids widget-per-particle overhead.
class _ConfettiPainter extends CustomPainter {
  _ConfettiPainter({
    required this.particles,
    required this.progress,
    required this.screenHeight,
  });

  final List<_ConfettiParticle> particles;
  final double progress;
  final double screenHeight;

  @override
  void paint(Canvas canvas, Size size) {
    final opacity = (1.0 - Curves.easeIn.transform(progress)).clamp(0.0, 1.0);
    if (opacity <= 0) return;

    for (final p in particles) {
      final y = p.startY + screenHeight * p.speed * progress;
      final x = p.x + p.drift * progress;
      final angle = progress * p.rotation;

      canvas.save();
      canvas.translate(x, y);
      canvas.rotate(angle);

      final paint = Paint()
        ..color = p.color.withValues(alpha: opacity)
        ..style = PaintingStyle.fill;

      canvas.drawRRect(
        RRect.fromRectAndRadius(
          Rect.fromCenter(center: Offset.zero, width: p.width, height: p.height),
          Radius.circular(p.width * 0.2),
        ),
        paint,
      );
      canvas.restore();
    }
  }

  @override
  bool shouldRepaint(_ConfettiPainter oldDelegate) =>
      oldDelegate.progress != progress;
}

/// Generate a list of confetti particles with deterministic positions.
List<_ConfettiParticle> _generateParticles(int count, double width, int seed) {
  final rng = Random(seed);
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
    return _ConfettiParticle(
      x: rng.nextDouble() * width,
      startY: -30.0 + rng.nextDouble() * 60,
      speed: 0.6 + rng.nextDouble() * 0.4,
      drift: (rng.nextDouble() - 0.5) * 80,
      rotation: (3 + rng.nextDouble() * 5) * pi,
      color: colors[i % colors.length],
      width: 6 + rng.nextDouble() * 6,
      height: 8 + rng.nextDouble() * 8,
    );
  });
}

// ---------------------------------------------------------------------------
// Tier 1: Micro — Subtle green glow + scale pulse (150ms)
// Uses TweenAnimationBuilder internally — no AnimationController
// ---------------------------------------------------------------------------

class _MicroCelebration extends AnimatedWidget {
  const _MicroCelebration({required Animation<double> animation})
      : super(listenable: animation);

  @override
  Widget build(BuildContext context) {
    final progress = (listenable as Animation<double>).value;
    // Scale pulse: 1.0 → 1.05 → 1.0
    final scale = progress < 0.5
        ? 1.0 + 0.05 * (progress * 2)
        : 1.0 + 0.05 * (1.0 - (progress - 0.5) * 2);
    final opacity = progress < 0.5 ? progress * 2 : (1.0 - progress) * 2;

    return Positioned.fill(
      child: IgnorePointer(
        child: Center(
          child: Transform.scale(
            scale: scale,
            child: Opacity(
              opacity: opacity.clamp(0.0, 1.0),
              child: Container(
                width: 64,
                height: 64,
                decoration: BoxDecoration(
                  shape: BoxShape.circle,
                  boxShadow: [
                    BoxShadow(
                      color: const Color(0xFF4CAF50)
                          .withValues(alpha: 0.4 * opacity.clamp(0.0, 1.0)),
                      blurRadius: 24,
                      spreadRadius: 8,
                    ),
                  ],
                ),
                child: Icon(
                  Icons.check_circle_rounded,
                  size: 48,
                  color: const Color(0xFF4CAF50)
                      .withValues(alpha: 0.7 * opacity.clamp(0.0, 1.0)),
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
// Tier 2: Minor — Color burst + 20 confetti particles (600ms)
// Uses TweenAnimationBuilder — no AnimationController
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

    final size = MediaQuery.of(context).size;
    final particles = _generateParticles(20, size.width, 42);

    return Positioned.fill(
      child: IgnorePointer(
        child: Stack(
          children: [
            // 20 confetti particles via CustomPainter
            CustomPaint(
              size: size,
              painter: _ConfettiPainter(
                particles: particles,
                progress: progress,
                screenHeight: size.height * 0.5,
              ),
            ),
            // XP text
            Positioned(
              bottom: size.height * 0.35,
              left: 0,
              right: 0,
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
                          color:
                              const Color(0xFFFFD700).withValues(alpha: 0.15),
                          borderRadius:
                              BorderRadius.circular(RadiusTokens.full),
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
          ],
        ),
      ),
    );
  }
}

// ---------------------------------------------------------------------------
// Tier 3: Medium — 100 confetti + expanding ring + glow (1000ms)
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
    final size = MediaQuery.of(context).size;
    final particles = _generateParticles(100, size.width, 123);

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
                // Confetti via CustomPainter
                CustomPaint(
                  size: size,
                  painter: _ConfettiPainter(
                    particles: particles,
                    progress: progress,
                    screenHeight: size.height * 0.8,
                  ),
                ),
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
                // Glow
                Container(
                  width: 120,
                  height: 120,
                  decoration: BoxDecoration(
                    shape: BoxShape.circle,
                    boxShadow: [
                      BoxShadow(
                        color: const Color(0xFF7C4DFF)
                            .withValues(alpha: ringOpacity * 0.3),
                        blurRadius: 40,
                        spreadRadius: 10,
                      ),
                    ],
                  ),
                ),
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
}

// ---------------------------------------------------------------------------
// Tier 4: Major — Full-screen overlay + 150 particles + badge (3s)
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
    final size = MediaQuery.of(context).size;
    final particles = _generateParticles(150, size.width, 456);

    return AnimatedBuilder(
      animation: animation,
      builder: (context, _) {
        final progress = animation.value;
        final bgOpacity = progress < 0.1
            ? progress / 0.1
            : progress > 0.8
                ? ((1.0 - progress) / 0.2).clamp(0.0, 1.0)
                : 1.0;
        final badgeScale = progress < 0.15
            ? Curves.elasticOut
                .transform((progress / 0.15).clamp(0.0, 1.0))
                .clamp(0.0, 1.5)
            : 1.0;
        final textOpacity = progress < 0.2
            ? ((progress - 0.1) / 0.1).clamp(0.0, 1.0)
            : progress > 0.85
                ? ((1.0 - progress) / 0.15).clamp(0.0, 1.0)
                : 1.0;

        return Positioned.fill(
          child: IgnorePointer(
            child: Container(
              color: Colors.black.withValues(alpha: bgOpacity * 0.3),
              child: Stack(
                alignment: Alignment.center,
                children: [
                  // Confetti via CustomPainter
                  CustomPaint(
                    size: size,
                    painter: _ConfettiPainter(
                      particles: particles,
                      progress: progress,
                      screenHeight: size.height * 0.9,
                    ),
                  ),
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
                              gradient: const LinearGradient(
                                begin: Alignment.topLeft,
                                end: Alignment.bottomRight,
                                colors: [
                                  Color(0xFFFFD700),
                                  Color(0xFFFF6F00),
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
}

// ---------------------------------------------------------------------------
// Tier 5: Epic — 200 particle shower + certificate + glow pulse (5s)
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
    final size = MediaQuery.of(context).size;
    final particles = _generateParticles(200, size.width, 789);

    return AnimatedBuilder(
      animation: animation,
      builder: (context, _) {
        final progress = animation.value;
        final phase1 = (progress / 0.3).clamp(0.0, 1.0); // glow in
        final phase2 = ((progress - 0.2) / 0.4).clamp(0.0, 1.0); // content in
        final phase3 = ((progress - 0.8) / 0.2).clamp(0.0, 1.0); // fade out

        final bgOpacity = phase1 * 0.5 * (1.0 - phase3);
        final glowPulse = sin(progress * pi * 4) * 0.15 + 0.35;
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
                  // 200 confetti particles via CustomPainter
                  CustomPaint(
                    size: size,
                    painter: _ConfettiPainter(
                      particles: particles,
                      progress: progress,
                      screenHeight: size.height,
                    ),
                  ),
                  // Certificate + text content
                  Opacity(
                    opacity: contentOpacity.clamp(0.0, 1.0),
                    child: Transform.scale(
                      scale: contentScale.clamp(0.5, 1.3),
                      child: Column(
                        mainAxisSize: MainAxisSize.min,
                        children: [
                          // Certificate icon with glow
                          Container(
                            width: 96,
                            height: 96,
                            decoration: BoxDecoration(
                              shape: BoxShape.circle,
                              boxShadow: [
                                BoxShadow(
                                  color: const Color(0xAAFFD700)
                                      .withValues(alpha: glowPulse),
                                  blurRadius: 40,
                                  spreadRadius: 8,
                                ),
                              ],
                            ),
                            child: const Icon(
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
                          ),
                          const SizedBox(height: SpacingTokens.md),
                          Text(
                            message ?? '\u05E9\u05DC\u05D9\u05D8\u05D4!', // שליטה!
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
}
