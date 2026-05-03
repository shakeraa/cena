// =============================================================================
// Cena Adaptive Learning Platform — Home Dashboard Skeleton (MOB-039)
// =============================================================================
//
// Shimmer-animated placeholder for the home/dashboard screen.
// Renders skeleton cards for: streak, XP, progress, and recommended content.
// =============================================================================

import 'package:flutter/material.dart';
import 'package:shimmer/shimmer.dart';

import '../../config/app_config.dart';

/// Skeleton placeholder for the home dashboard.
///
/// Mirrors the real dashboard layout:
/// - Header greeting placeholder
/// - Row of 3 stat cards (streak, XP, progress)
/// - "Recommended" section header
/// - List of 3 content card placeholders
class HomeSkeleton extends StatelessWidget {
  const HomeSkeleton({super.key});

  @override
  Widget build(BuildContext context) {
    final isDark = Theme.of(context).brightness == Brightness.dark;
    final baseColor = isDark ? const Color(0xFF1E293B) : const Color(0xFFE0E0E0);
    final highlightColor =
        isDark ? const Color(0xFF334155) : const Color(0xFFF5F5F5);

    return Shimmer.fromColors(
      baseColor: baseColor,
      highlightColor: highlightColor,
      child: SingleChildScrollView(
        physics: const NeverScrollableScrollPhysics(),
        padding: const EdgeInsets.all(SpacingTokens.md),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            // Greeting placeholder
            const _Bone(width: 200, height: 28),
            const SizedBox(height: SpacingTokens.xs),
            const _Bone(width: 140, height: 16),
            const SizedBox(height: SpacingTokens.lg),

            // Stat cards row — streak, XP, progress
            const Row(
              children: [
                Expanded(child: _StatCardSkeleton()),
                SizedBox(width: SpacingTokens.sm),
                Expanded(child: _StatCardSkeleton()),
                SizedBox(width: SpacingTokens.sm),
                Expanded(child: _StatCardSkeleton()),
              ],
            ),
            const SizedBox(height: SpacingTokens.xl),

            // Daily goal / progress card skeleton
            const _Bone(width: double.infinity, height: 100),
            const SizedBox(height: SpacingTokens.xl),

            // Recommended section header
            const _Bone(width: 160, height: 20),
            const SizedBox(height: SpacingTokens.md),

            // Recommended content list
            for (int i = 0; i < 3; i++) ...[
              const _ContentCardSkeleton(),
              if (i < 2) const SizedBox(height: SpacingTokens.sm),
            ],
          ],
        ),
      ),
    );
  }
}

/// Skeleton for a single stat card (streak, XP, or progress).
class _StatCardSkeleton extends StatelessWidget {
  const _StatCardSkeleton();

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.all(SpacingTokens.md),
      decoration: BoxDecoration(
        color: Colors.white,
        borderRadius: BorderRadius.circular(RadiusTokens.lg),
      ),
      child: const Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          _Bone(width: 24, height: 24),
          SizedBox(height: SpacingTokens.sm),
          _Bone(width: 48, height: 22),
          SizedBox(height: SpacingTokens.xs),
          _Bone(width: 60, height: 12),
        ],
      ),
    );
  }
}

/// Skeleton for a recommended content list item.
class _ContentCardSkeleton extends StatelessWidget {
  const _ContentCardSkeleton();

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.all(SpacingTokens.md),
      decoration: BoxDecoration(
        color: Colors.white,
        borderRadius: BorderRadius.circular(RadiusTokens.lg),
      ),
      child: const Row(
        children: [
          // Icon placeholder
          _Bone(width: 48, height: 48),
          SizedBox(width: SpacingTokens.md),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                _Bone(width: double.infinity, height: 16),
                SizedBox(height: SpacingTokens.xs),
                _Bone(width: 120, height: 12),
              ],
            ),
          ),
          SizedBox(width: SpacingTokens.sm),
          // Chevron placeholder
          _Bone(width: 24, height: 24),
        ],
      ),
    );
  }
}

/// A simple rounded rectangle bone for shimmer skeletons.
class _Bone extends StatelessWidget {
  const _Bone({
    required this.width,
    required this.height,
  });

  final double width;
  final double height;

  @override
  Widget build(BuildContext context) {
    return Container(
      width: width,
      height: height,
      decoration: BoxDecoration(
        color: Colors.white,
        borderRadius: BorderRadius.circular(RadiusTokens.sm),
      ),
    );
  }
}
