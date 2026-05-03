// =============================================================================
// Cena Adaptive Learning Platform — Quick Review Button (PAR-004)
// A prominent CTA for 5-minute SRS-priority micro-lesson sessions.
// =============================================================================

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/config/app_config.dart';
import '../../core/router.dart';
import '../../core/state/srs_state.dart';

// ---------------------------------------------------------------------------
// Quick Review Session Config
// ---------------------------------------------------------------------------

/// Configuration for the 5-minute Quick Review micro-lesson.
///
/// Uses existing session architecture with a short duration preset
/// and SRS-priority question selection.
abstract class QuickReviewConfig {
  /// Duration in minutes for a quick review session.
  static const int durationMinutes = 5;

  /// Maximum questions in a quick review (keeps it snappy).
  static const int maxQuestions = 10;
}

// ---------------------------------------------------------------------------
// Widget
// ---------------------------------------------------------------------------

/// Quick Review CTA button for the home screen.
///
/// Shows a 5-min review badge with overdue count. Taps launch a
/// session pre-configured with [QuickReviewConfig.durationMinutes]
/// and SRS-priority question selection.
class QuickReviewButton extends ConsumerWidget {
  const QuickReviewButton({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;
    final overdueCount = ref.watch(dueReviewCountProvider);

    return Card(
      clipBehavior: Clip.antiAlias,
      child: InkWell(
        onTap: () => _startQuickReview(context),
        child: Padding(
          padding: const EdgeInsets.all(SpacingTokens.md),
          child: Row(
            children: [
              // Icon with overdue badge.
              Stack(
                clipBehavior: Clip.none,
                children: [
                  Container(
                    width: 44,
                    height: 44,
                    decoration: BoxDecoration(
                      color: colorScheme.tertiaryContainer,
                      borderRadius: BorderRadius.circular(RadiusTokens.md),
                    ),
                    child: Icon(
                      Icons.replay_rounded,
                      color: colorScheme.onTertiaryContainer,
                      size: 24,
                    ),
                  ),
                  if (overdueCount > 0)
                    Positioned(
                      top: -4,
                      right: -4,
                      child: Container(
                        padding: const EdgeInsets.symmetric(
                          horizontal: 5,
                          vertical: 1,
                        ),
                        decoration: BoxDecoration(
                          color: colorScheme.error,
                          borderRadius:
                              BorderRadius.circular(RadiusTokens.full),
                        ),
                        child: Text(
                          overdueCount > 99 ? '99+' : '$overdueCount',
                          style: theme.textTheme.labelSmall?.copyWith(
                            color: colorScheme.onError,
                            fontWeight: FontWeight.w700,
                            fontSize: 10,
                          ),
                        ),
                      ),
                    ),
                ],
              ),

              const SizedBox(width: SpacingTokens.sm),

              // Text.
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      'Quick Review',
                      style: theme.textTheme.titleSmall?.copyWith(
                        fontWeight: FontWeight.w700,
                      ),
                    ),
                    const SizedBox(height: 2),
                    Text(
                      overdueCount > 0
                          ? '$overdueCount cards due — ${QuickReviewConfig.durationMinutes} min'
                          : 'Strengthen your memory — ${QuickReviewConfig.durationMinutes} min',
                      style: theme.textTheme.bodySmall?.copyWith(
                        color: colorScheme.onSurfaceVariant,
                      ),
                    ),
                  ],
                ),
              ),

              // Arrow.
              Icon(
                Icons.arrow_forward_rounded,
                color: colorScheme.onSurfaceVariant,
                size: 20,
              ),
            ],
          ),
        ),
      ),
    );
  }

  void _startQuickReview(BuildContext context) {
    // Navigate to session screen with quick review parameters.
    // The session screen reads query params to configure duration and mode.
    context.go(
      '${CenaRoutes.session}?mode=quick_review'
      '&duration=${QuickReviewConfig.durationMinutes}'
      '&maxQuestions=${QuickReviewConfig.maxQuestions}',
    );
  }
}
