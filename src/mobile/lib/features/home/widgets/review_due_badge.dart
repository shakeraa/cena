// =============================================================================
// Cena Adaptive Learning Platform — Review Due Badge (MOB-037)
// =============================================================================
//
// Badge displayed on the home screen showing count of SRS items due for review.
// - Red dot when 1-5 items due
// - Numeric count when > 5 items due
// - "All caught up!" state when 0 items due
// - Tapping starts a dedicated review session
// =============================================================================

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/config/app_config.dart';
import '../../../core/state/srs_state.dart';

/// A badge widget that displays the number of SRS review items due.
///
/// Place this on the home screen (e.g. in the app bar or as a floating action).
/// Tapping it invokes [onStartReview] which should navigate to the review
/// session screen.
class ReviewDueBadge extends ConsumerWidget {
  const ReviewDueBadge({
    super.key,
    required this.onStartReview,
  });

  /// Callback when the student taps the badge to start a review session.
  final VoidCallback onStartReview;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final dueCount = ref.watch(dueReviewCountProvider);
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;

    // "All caught up!" state when nothing is due.
    if (dueCount == 0) {
      return _AllCaughtUpBadge(colorScheme: colorScheme, theme: theme);
    }

    // Active review items due — tappable card.
    return GestureDetector(
      onTap: onStartReview,
      child: Container(
        padding: const EdgeInsets.symmetric(
          horizontal: SpacingTokens.md,
          vertical: SpacingTokens.sm,
        ),
        decoration: BoxDecoration(
          color: colorScheme.errorContainer.withValues(alpha: 0.15),
          borderRadius: BorderRadius.circular(RadiusTokens.lg),
          border: Border.all(
            color: colorScheme.error.withValues(alpha: 0.3),
          ),
        ),
        child: Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            // Pulsing dot or count badge.
            _DueBadgeIndicator(count: dueCount, colorScheme: colorScheme),
            const SizedBox(width: SpacingTokens.sm),
            // Label.
            Flexible(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                mainAxisSize: MainAxisSize.min,
                children: [
                  Text(
                    'Review Due',
                    style: theme.textTheme.labelLarge?.copyWith(
                      fontWeight: FontWeight.w700,
                      color: colorScheme.error,
                    ),
                  ),
                  Text(
                    dueCount == 1
                        ? '1 concept to review'
                        : '$dueCount concepts to review',
                    style: theme.textTheme.bodySmall?.copyWith(
                      color: colorScheme.onSurfaceVariant,
                    ),
                  ),
                ],
              ),
            ),
            const SizedBox(width: SpacingTokens.sm),
            Icon(
              Icons.arrow_forward_ios_rounded,
              size: 14,
              color: colorScheme.error,
            ),
          ],
        ),
      ),
    );
  }
}

/// The red dot or numeric badge indicator.
class _DueBadgeIndicator extends StatelessWidget {
  const _DueBadgeIndicator({
    required this.count,
    required this.colorScheme,
  });

  final int count;
  final ColorScheme colorScheme;

  @override
  Widget build(BuildContext context) {
    // Small red dot for 1-5 items; numeric badge for > 5.
    if (count <= 5) {
      return Container(
        width: 12,
        height: 12,
        decoration: BoxDecoration(
          color: colorScheme.error,
          shape: BoxShape.circle,
          boxShadow: [
            BoxShadow(
              color: colorScheme.error.withValues(alpha: 0.4),
              blurRadius: 6,
              spreadRadius: 1,
            ),
          ],
        ),
      );
    }

    // Numeric count badge for > 5 items.
    return Container(
      padding: const EdgeInsets.symmetric(
        horizontal: SpacingTokens.sm,
        vertical: SpacingTokens.xxs,
      ),
      decoration: BoxDecoration(
        color: colorScheme.error,
        borderRadius: BorderRadius.circular(RadiusTokens.full),
      ),
      child: Text(
        count > 99 ? '99+' : '$count',
        style: TextStyle(
          color: colorScheme.onError,
          fontSize: 12,
          fontWeight: FontWeight.w800,
        ),
      ),
    );
  }
}

/// The "all caught up" state shown when no items are due.
class _AllCaughtUpBadge extends StatelessWidget {
  const _AllCaughtUpBadge({
    required this.colorScheme,
    required this.theme,
  });

  final ColorScheme colorScheme;
  final ThemeData theme;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(
        horizontal: SpacingTokens.md,
        vertical: SpacingTokens.sm,
      ),
      decoration: BoxDecoration(
        color: colorScheme.primaryContainer.withValues(alpha: 0.15),
        borderRadius: BorderRadius.circular(RadiusTokens.lg),
        border: Border.all(
          color: colorScheme.primary.withValues(alpha: 0.2),
        ),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Icon(
            Icons.check_circle_rounded,
            size: 20,
            color: colorScheme.primary,
          ),
          const SizedBox(width: SpacingTokens.sm),
          Text(
            'All caught up!',
            style: theme.textTheme.labelLarge?.copyWith(
              fontWeight: FontWeight.w600,
              color: colorScheme.primary,
            ),
          ),
        ],
      ),
    );
  }
}

/// A compact version of the badge suitable for app bar actions.
///
/// Shows just the icon with a red badge overlay when items are due.
class ReviewDueBadgeCompact extends ConsumerWidget {
  const ReviewDueBadgeCompact({
    super.key,
    required this.onStartReview,
  });

  final VoidCallback onStartReview;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final dueCount = ref.watch(dueReviewCountProvider);
    final colorScheme = Theme.of(context).colorScheme;

    return IconButton(
      onPressed: dueCount > 0 ? onStartReview : null,
      icon: Stack(
        clipBehavior: Clip.none,
        children: [
          Icon(
            Icons.replay_rounded,
            color: dueCount > 0
                ? colorScheme.error
                : colorScheme.onSurfaceVariant,
          ),
          if (dueCount > 0)
            Positioned(
              right: -4,
              top: -4,
              child: Container(
                padding: const EdgeInsets.all(2),
                decoration: BoxDecoration(
                  color: colorScheme.error,
                  shape: BoxShape.circle,
                ),
                constraints: const BoxConstraints(
                  minWidth: 16,
                  minHeight: 16,
                ),
                child: Text(
                  dueCount > 9 ? '9+' : '$dueCount',
                  style: TextStyle(
                    color: colorScheme.onError,
                    fontSize: 10,
                    fontWeight: FontWeight.w800,
                  ),
                  textAlign: TextAlign.center,
                ),
              ),
            ),
        ],
      ),
      tooltip: dueCount > 0
          ? '$dueCount items due for review'
          : 'No reviews due',
    );
  }
}
