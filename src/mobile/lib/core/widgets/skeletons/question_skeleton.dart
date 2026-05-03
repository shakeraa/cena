// =============================================================================
// Cena Adaptive Learning Platform — Question Skeleton Screen (MOB-039)
// =============================================================================
//
// Shimmer-animated placeholder matching the question card layout.
// Uses the `shimmer` package already declared in pubspec.yaml.
// =============================================================================

import 'package:flutter/material.dart';
import 'package:shimmer/shimmer.dart';

import '../../config/app_config.dart';

/// Skeleton placeholder for a loading question card.
///
/// Renders gray rectangles matching the real [QuestionCard] layout:
/// - A progress bar placeholder at top
/// - A large text block for the question stem
/// - Four rounded rectangles for MCQ option pills
class QuestionSkeleton extends StatelessWidget {
  const QuestionSkeleton({super.key});

  @override
  Widget build(BuildContext context) {
    final isDark = Theme.of(context).brightness == Brightness.dark;
    final baseColor = isDark ? const Color(0xFF1E293B) : const Color(0xFFE0E0E0);
    final highlightColor =
        isDark ? const Color(0xFF334155) : const Color(0xFFF5F5F5);

    return Shimmer.fromColors(
      baseColor: baseColor,
      highlightColor: highlightColor,
      child: Padding(
        padding: const EdgeInsets.all(SpacingTokens.md),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            // Question number placeholder
            const _SkeletonBox(
              width: 100,
              height: 16,
              borderRadius: RadiusTokens.sm,
            ),
            const SizedBox(height: SpacingTokens.lg),

            // Question stem — multi-line text block
            const _SkeletonBox(
              width: double.infinity,
              height: 18,
              borderRadius: RadiusTokens.sm,
            ),
            const SizedBox(height: SpacingTokens.sm),
            const _SkeletonBox(
              width: double.infinity,
              height: 18,
              borderRadius: RadiusTokens.sm,
            ),
            const SizedBox(height: SpacingTokens.sm),
            const _SkeletonBox(
              width: 220,
              height: 18,
              borderRadius: RadiusTokens.sm,
            ),
            const SizedBox(height: SpacingTokens.xl),

            // Four MCQ option placeholders
            for (int i = 0; i < 4; i++) ...[
              const _SkeletonBox(
                width: double.infinity,
                height: 52,
                borderRadius: RadiusTokens.lg,
              ),
              if (i < 3) const SizedBox(height: SpacingTokens.sm),
            ],

            const SizedBox(height: SpacingTokens.xl),

            // Submit button placeholder
            const Center(
              child: _SkeletonBox(
                width: 180,
                height: 48,
                borderRadius: RadiusTokens.md,
              ),
            ),
          ],
        ),
      ),
    );
  }
}

/// Reusable skeleton rectangle with configurable dimensions.
class _SkeletonBox extends StatelessWidget {
  const _SkeletonBox({
    required this.width,
    required this.height,
    required this.borderRadius,
  });

  final double width;
  final double height;
  final double borderRadius;

  @override
  Widget build(BuildContext context) {
    return Container(
      width: width,
      height: height,
      decoration: BoxDecoration(
        color: Colors.white,
        borderRadius: BorderRadius.circular(borderRadius),
      ),
    );
  }
}
