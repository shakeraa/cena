// =============================================================================
// Cena Adaptive Learning Platform — Glassmorphism Widgets (MOB-VIS-001)
// =============================================================================
//
// Research ref: CENA_UI_UX_Design_Strategy_2026.md §1.1-1.3
// "Liquid Glass + Soft UI — Modern, premium feel"
//
// Uses BackdropFilter for real frosted glass without third-party packages.
// =============================================================================

import 'dart:ui';

import 'package:flutter/material.dart';

import '../config/app_config.dart';

// ---------------------------------------------------------------------------
// GlassCard — frosted glass card with blur and gradient border
// ---------------------------------------------------------------------------

/// A glassmorphic card with backdrop blur, translucent background, and
/// optional gradient border. Adapts to light/dark mode automatically.
class GlassCard extends StatelessWidget {
  const GlassCard({
    super.key,
    required this.child,
    this.blur = 20.0,
    this.opacity = 0.12,
    this.borderRadius,
    this.padding,
    this.margin,
    this.borderOpacity = 0.2,
  });

  final Widget child;
  final double blur;
  final double opacity;
  final BorderRadius? borderRadius;
  final EdgeInsets? padding;
  final EdgeInsets? margin;
  final double borderOpacity;

  @override
  Widget build(BuildContext context) {
    final colorScheme = Theme.of(context).colorScheme;
    final isDark = Theme.of(context).brightness == Brightness.dark;
    final radius = borderRadius ?? BorderRadius.circular(RadiusTokens.lg);

    final bgColor = isDark
        ? Colors.white.withValues(alpha: opacity * 0.5)
        : Colors.white.withValues(alpha: opacity + 0.3);
    final borderColor = isDark
        ? Colors.white.withValues(alpha: borderOpacity)
        : colorScheme.outlineVariant.withValues(alpha: borderOpacity + 0.1);

    return Padding(
      padding: margin ?? EdgeInsets.zero,
      child: ClipRRect(
        borderRadius: radius,
        child: BackdropFilter(
          filter: ImageFilter.blur(sigmaX: blur, sigmaY: blur),
          child: Container(
            decoration: BoxDecoration(
              color: bgColor,
              borderRadius: radius,
              border: Border.all(color: borderColor, width: 1),
            ),
            padding: padding ?? const EdgeInsets.all(SpacingTokens.md),
            child: child,
          ),
        ),
      ),
    );
  }
}

// ---------------------------------------------------------------------------
// GlassContainer — more minimal glass surface (no padding default)
// ---------------------------------------------------------------------------

/// A minimal glass container for custom layouts. Unlike [GlassCard], it has
/// no default padding and uses a lower blur for lighter visual weight.
class GlassContainer extends StatelessWidget {
  const GlassContainer({
    super.key,
    required this.child,
    this.blur = 10.0,
    this.opacity = 0.08,
    this.borderRadius,
    this.width,
    this.height,
  });

  final Widget child;
  final double blur;
  final double opacity;
  final BorderRadius? borderRadius;
  final double? width;
  final double? height;

  @override
  Widget build(BuildContext context) {
    final isDark = Theme.of(context).brightness == Brightness.dark;
    final radius = borderRadius ?? BorderRadius.circular(RadiusTokens.md);

    final bgColor = isDark
        ? Colors.white.withValues(alpha: opacity * 0.4)
        : Colors.white.withValues(alpha: opacity + 0.4);

    return ClipRRect(
      borderRadius: radius,
      child: BackdropFilter(
        filter: ImageFilter.blur(sigmaX: blur, sigmaY: blur),
        child: Container(
          width: width,
          height: height,
          decoration: BoxDecoration(
            color: bgColor,
            borderRadius: radius,
          ),
          child: child,
        ),
      ),
    );
  }
}

// ---------------------------------------------------------------------------
// GlassChip — small glass pill for tags, badges, status indicators
// ---------------------------------------------------------------------------

class GlassChip extends StatelessWidget {
  const GlassChip({
    super.key,
    required this.label,
    this.icon,
    this.color,
  });

  final String label;
  final IconData? icon;
  final Color? color;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final chipColor = color ?? theme.colorScheme.primary;

    return GlassContainer(
      blur: 8,
      opacity: 0.06,
      borderRadius: BorderRadius.circular(RadiusTokens.full),
      child: Padding(
        padding: const EdgeInsets.symmetric(
          horizontal: SpacingTokens.sm + 2,
          vertical: SpacingTokens.xs,
        ),
        child: Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            if (icon != null) ...[
              Icon(icon, size: 14, color: chipColor),
              const SizedBox(width: SpacingTokens.xs),
            ],
            Text(
              label,
              style: theme.textTheme.labelSmall?.copyWith(
                color: chipColor,
                fontWeight: FontWeight.w600,
              ),
            ),
          ],
        ),
      ),
    );
  }
}

// ---------------------------------------------------------------------------
// GlassProgressRing — circular progress with glass background
// ---------------------------------------------------------------------------

class GlassProgressRing extends StatelessWidget {
  const GlassProgressRing({
    super.key,
    required this.progress,
    this.size = 80,
    this.strokeWidth = 6,
    this.label,
    this.color,
  });

  final double progress;
  final double size;
  final double strokeWidth;
  final String? label;
  final Color? color;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;
    final ringColor = color ?? colorScheme.primary;

    return GlassContainer(
      blur: 12,
      opacity: 0.06,
      borderRadius: BorderRadius.circular(size),
      width: size,
      height: size,
      child: Stack(
        alignment: Alignment.center,
        children: [
          SizedBox(
            width: size - 16,
            height: size - 16,
            child: TweenAnimationBuilder<double>(
              tween: Tween(begin: 0, end: progress),
              duration: AnimationTokens.slow,
              curve: Curves.easeOutCubic,
              builder: (context, value, _) {
                return CircularProgressIndicator(
                  value: value,
                  strokeWidth: strokeWidth,
                  backgroundColor:
                      colorScheme.surfaceContainerHighest.withValues(alpha: 0.5),
                  valueColor: AlwaysStoppedAnimation(ringColor),
                  strokeCap: StrokeCap.round,
                );
              },
            ),
          ),
          if (label != null)
            Text(
              label!,
              style: theme.textTheme.labelLarge?.copyWith(
                fontWeight: FontWeight.w700,
                color: colorScheme.onSurface,
              ),
            ),
        ],
      ),
    );
  }
}
