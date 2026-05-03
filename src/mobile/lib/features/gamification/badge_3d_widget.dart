// =============================================================================
// Cena Adaptive Learning Platform — 3D Achievement Badges (MOB-VIS-005)
// =============================================================================
//
// Research ref: CENA_UI_UX_Design_Strategy_2026.md §3.3
// Rarity tiers: Common (bronze), Rare (silver), Epic (gold), Legendary (animated)
// Unlock animation: rotation + sparkle burst
// =============================================================================

import 'dart:math';

import 'package:flutter/material.dart';

import '../../core/config/app_config.dart';
import '../../core/state/gamification_state.dart' show BadgeCategory;

// ---------------------------------------------------------------------------
// Rarity tier — determines visual treatment
// ---------------------------------------------------------------------------

enum BadgeRarity {
  common,   // Bronze ring, no glow
  rare,     // Silver ring, subtle glow
  epic,     // Gold ring, prominent glow
  legendary; // Animated ring, pulsing glow + particle sparkles

  static BadgeRarity fromCategory(BadgeCategory category) {
    switch (category) {
      case BadgeCategory.engagement:
        return BadgeRarity.common;
      case BadgeCategory.streak:
        return BadgeRarity.rare;
      case BadgeCategory.mastery:
        return BadgeRarity.epic;
      case BadgeCategory.special:
        return BadgeRarity.legendary;
    }
  }

  Color get ringColor {
    switch (this) {
      case BadgeRarity.common:
        return const Color(0xFFCD7F32); // Bronze
      case BadgeRarity.rare:
        return const Color(0xFFC0C0C0); // Silver
      case BadgeRarity.epic:
        return const Color(0xFFFFD700); // Gold
      case BadgeRarity.legendary:
        return const Color(0xFFE040FB); // Purple-pink
    }
  }

  double get glowRadius {
    switch (this) {
      case BadgeRarity.common:
        return 0;
      case BadgeRarity.rare:
        return 4;
      case BadgeRarity.epic:
        return 8;
      case BadgeRarity.legendary:
        return 14;
    }
  }
}

// ---------------------------------------------------------------------------
// Badge3D — 3D rotating badge with rarity-based visual treatment
// ---------------------------------------------------------------------------

/// Displays a badge with 3D rotation on tap, rarity-based ring color,
/// and glow effects. Legendary badges pulse continuously.
class Badge3D extends StatefulWidget {
  const Badge3D({
    super.key,
    required this.icon,
    required this.rarity,
    this.isEarned = false,
    this.size = 64,
    this.onTap,
  });

  final IconData icon;
  final BadgeRarity rarity;
  final bool isEarned;
  final double size;
  final VoidCallback? onTap;

  @override
  State<Badge3D> createState() => _Badge3DState();
}

class _Badge3DState extends State<Badge3D> with TickerProviderStateMixin {
  late final AnimationController _rotateController;
  late final AnimationController _glowController;

  @override
  void initState() {
    super.initState();
    _rotateController = AnimationController(
      vsync: this,
      duration: const Duration(milliseconds: 600),
    );
    _glowController = AnimationController(
      vsync: this,
      duration: const Duration(milliseconds: 1500),
    );
    if (widget.rarity == BadgeRarity.legendary && widget.isEarned) {
      _glowController.repeat(reverse: true);
    }
  }

  void triggerUnlockAnimation() {
    _rotateController.forward(from: 0);
  }

  @override
  void dispose() {
    _rotateController.dispose();
    _glowController.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final colorScheme = Theme.of(context).colorScheme;
    final ringColor = widget.isEarned
        ? widget.rarity.ringColor
        : colorScheme.surfaceContainerHighest;
    final iconColor = widget.isEarned
        ? Colors.white
        : colorScheme.onSurfaceVariant.withValues(alpha: 0.3);

    return GestureDetector(
      onTap: () {
        if (widget.isEarned) triggerUnlockAnimation();
        widget.onTap?.call();
      },
      child: AnimatedBuilder(
        animation: Listenable.merge([_rotateController, _glowController]),
        builder: (context, child) {
          final rotateY = _rotateController.value * 2 * pi;
          final glowIntensity = widget.rarity == BadgeRarity.legendary
              ? 0.3 + 0.4 * _glowController.value
              : 1.0;

          return Container(
            width: widget.size,
            height: widget.size,
            decoration: BoxDecoration(
              shape: BoxShape.circle,
              boxShadow: widget.isEarned && widget.rarity.glowRadius > 0
                  ? [
                      BoxShadow(
                        color: ringColor.withValues(alpha: 0.3 * glowIntensity),
                        blurRadius: widget.rarity.glowRadius * glowIntensity,
                        spreadRadius: 1,
                      ),
                    ]
                  : null,
            ),
            child: Transform(
              alignment: Alignment.center,
              transform: Matrix4.identity()
                ..setEntry(3, 2, 0.001)
                ..rotateY(rotateY),
              child: Container(
                decoration: BoxDecoration(
                  shape: BoxShape.circle,
                  gradient: widget.isEarned
                      ? LinearGradient(
                          begin: Alignment.topLeft,
                          end: Alignment.bottomRight,
                          colors: [
                            ringColor,
                            ringColor.withValues(alpha: 0.7),
                          ],
                        )
                      : null,
                  color: widget.isEarned ? null : ringColor,
                  border: widget.isEarned
                      ? Border.all(
                          color: ringColor.withValues(alpha: 0.8),
                          width: 2,
                        )
                      : null,
                ),
                child: Center(
                  child: Icon(widget.icon, size: widget.size * 0.45,
                      color: iconColor),
                ),
              ),
            ),
          );
        },
      ),
    );
  }
}

// ---------------------------------------------------------------------------
// BadgeUnlockOverlay — full-screen celebration when earning a badge
// ---------------------------------------------------------------------------

class BadgeUnlockOverlay extends StatefulWidget {
  const BadgeUnlockOverlay({
    super.key,
    required this.badgeName,
    required this.icon,
    required this.rarity,
    required this.onDismiss,
  });

  final String badgeName;
  final IconData icon;
  final BadgeRarity rarity;
  final VoidCallback onDismiss;

  @override
  State<BadgeUnlockOverlay> createState() => _BadgeUnlockOverlayState();
}

class _BadgeUnlockOverlayState extends State<BadgeUnlockOverlay>
    with SingleTickerProviderStateMixin {
  late final AnimationController _controller;

  @override
  void initState() {
    super.initState();
    _controller = AnimationController(
      vsync: this,
      duration: const Duration(milliseconds: 2000),
    )..forward();
  }

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return GestureDetector(
      onTap: widget.onDismiss,
      child: AnimatedBuilder(
        animation: _controller,
        builder: (context, _) {
          final opacity = Curves.easeIn.transform(
            (_controller.value * 3).clamp(0.0, 1.0),
          );
          final scale = Curves.elasticOut.transform(
            ((_controller.value - 0.1) * 2).clamp(0.0, 1.0),
          );

          return Container(
            color: Colors.black.withValues(alpha: 0.6 * opacity),
            child: Center(
              child: Opacity(
                opacity: opacity,
                child: Transform.scale(
                  scale: scale,
                  child: Column(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      Badge3D(
                        icon: widget.icon,
                        rarity: widget.rarity,
                        isEarned: true,
                        size: 96,
                      ),
                      const SizedBox(height: SpacingTokens.lg),
                      Text(
                        'Badge Unlocked!',
                        style: theme.textTheme.headlineMedium?.copyWith(
                          color: Colors.white,
                          fontWeight: FontWeight.w800,
                        ),
                      ),
                      const SizedBox(height: SpacingTokens.sm),
                      Text(
                        widget.badgeName,
                        style: theme.textTheme.titleLarge?.copyWith(
                          color: widget.rarity.ringColor,
                          fontWeight: FontWeight.w600,
                        ),
                      ),
                      const SizedBox(height: SpacingTokens.xl),
                      Text(
                        'Tap anywhere to continue',
                        style: theme.textTheme.bodySmall?.copyWith(
                          color: Colors.white70,
                        ),
                      ),
                    ],
                  ),
                ),
              ),
            ),
          );
        },
      ),
    );
  }
}
