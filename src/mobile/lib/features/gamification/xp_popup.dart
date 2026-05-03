// =============================================================================
// Cena Adaptive Learning Platform — XP Pop-up Overlay
// =============================================================================
//
// Animated "+N XP" text that floats upward and fades out after a correct
// answer. Compose inside a Stack on the session screen.
// =============================================================================

import 'package:flutter/material.dart';

import '../../core/config/app_config.dart';

/// Controller that drives the [XpPopup] visibility and triggers the animation.
///
/// Call [show] with the XP delta after a correct answer.  The overlay removes
/// itself automatically after the animation completes.
class XpPopupController {
  _XpPopupState? _state;

  void _attach(_XpPopupState state) => _state = state;
  void _detach(_XpPopupState state) {
    if (_state == state) _state = null;
  }

  /// Trigger a "+[xp] XP" float animation.
  void show(int xp) => _state?.trigger(xp);
}

/// A positioned overlay that plays the XP float animation.
///
/// Place this inside a [Stack] that covers the area where you want the popup
/// to appear. The popup floats upward from [bottomFraction] of the stack height.
///
/// ```dart
/// Stack(children: [
///   QuestionCard(...),
///   XpPopup(controller: _xpController),
/// ])
/// ```
class XpPopup extends StatefulWidget {
  const XpPopup({
    super.key,
    required this.controller,
    this.bottomFraction = 0.35,
  });

  final XpPopupController controller;

  /// Starting vertical position as a fraction of the parent height from the
  /// bottom (0.0 = bottom edge, 1.0 = top edge).
  final double bottomFraction;

  @override
  State<XpPopup> createState() => _XpPopupState();
}

class _XpPopupState extends State<XpPopup> with TickerProviderStateMixin {
  late final AnimationController _controller;
  late final Animation<double> _floatAnimation;
  late final Animation<double> _opacityAnimation;
  late final Animation<double> _scaleAnimation;

  int _xp = 0;
  bool _visible = false;

  @override
  void initState() {
    super.initState();
    widget.controller._attach(this);

    _controller = AnimationController(
      vsync: this,
      duration: const Duration(milliseconds: 900),
    );

    // Float 60px upward
    _floatAnimation = Tween<double>(begin: 0.0, end: -60.0).animate(
      CurvedAnimation(
        parent: _controller,
        curve: const Interval(0.0, 1.0, curve: Curves.easeOut),
      ),
    );

    // Fade out in the second half
    _opacityAnimation = Tween<double>(begin: 1.0, end: 0.0).animate(
      CurvedAnimation(
        parent: _controller,
        curve: const Interval(0.5, 1.0, curve: Curves.easeIn),
      ),
    );

    // Bounce-in scale: overshoot then settle
    _scaleAnimation = TweenSequence<double>([
      TweenSequenceItem(
        tween: Tween(begin: 0.6, end: 1.3)
            .chain(CurveTween(curve: Curves.easeOut)),
        weight: 30,
      ),
      TweenSequenceItem(
        tween: Tween(begin: 1.3, end: 1.0)
            .chain(CurveTween(curve: Curves.bounceOut)),
        weight: 70,
      ),
    ]).animate(_controller);

    _controller.addStatusListener((status) {
      if (status == AnimationStatus.completed) {
        if (mounted) {
          setState(() => _visible = false);
        }
        _controller.reset();
      }
    });
  }

  @override
  void dispose() {
    widget.controller._detach(this);
    _controller.dispose();
    super.dispose();
  }

  void trigger(int xp) {
    if (!mounted) return;
    setState(() {
      _xp = xp;
      _visible = true;
    });
    _controller.forward(from: 0.0);
  }

  @override
  Widget build(BuildContext context) {
    if (!_visible) return const SizedBox.shrink();

    return Positioned(
      bottom: MediaQuery.of(context).size.height * widget.bottomFraction,
      left: 0,
      right: 0,
      child: AnimatedBuilder(
        animation: _controller,
        builder: (context, child) {
          return Transform.translate(
            offset: Offset(0, _floatAnimation.value),
            child: Opacity(
              opacity: _opacityAnimation.value,
              child: Transform.scale(
                scale: _scaleAnimation.value,
                child: child,
              ),
            ),
          );
        },
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
              '+$_xp XP',
              style: const TextStyle(
                color: Color(0xFFFFD700),
                fontSize: 22,
                fontWeight: FontWeight.w800,
                letterSpacing: 0.5,
                shadows: [
                  Shadow(
                    color: Color(0x88000000),
                    blurRadius: 4,
                    offset: Offset(0, 2),
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}
