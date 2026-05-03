// =============================================================================
// Cena Adaptive Learning Platform — Review Session Screen (MOB-037)
// =============================================================================
//
// A dedicated review-only session that presents SRS due items in a
// flashcard-style swipe interface:
//   - Swipe RIGHT = "I knew it" (maps to Good/Easy rating)
//   - Swipe LEFT  = "I forgot" (maps to Again rating)
//   - Session ends when all due items are reviewed
//   - Completion celebration proportional to items reviewed
//   - Memory strength visualization via opacity/glow on concept cards
// =============================================================================

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/config/app_config.dart';
import '../../../core/services/fsrs_scheduler.dart';
import '../../../core/state/srs_state.dart';
import '../../gamification/celebration_overlay.dart';
import '../../gamification/celebration_service.dart';

/// Review-only session screen with flashcard swipe interface.
class ReviewSessionScreen extends ConsumerStatefulWidget {
  const ReviewSessionScreen({super.key});

  @override
  ConsumerState<ReviewSessionScreen> createState() =>
      _ReviewSessionScreenState();
}

class _ReviewSessionScreenState extends ConsumerState<ReviewSessionScreen> {
  final CelebrationController _celebrationController = CelebrationController();

  @override
  void initState() {
    super.initState();
    // Start the review session with all due items.
    WidgetsBinding.instance.addPostFrameCallback((_) {
      final dueItems = ref.read(dueReviewItemsProvider);
      ref.read(reviewSessionProvider.notifier).startSession(dueItems);
    });
  }

  @override
  Widget build(BuildContext context) {
    final sessionState = ref.watch(reviewSessionProvider);
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;

    // Session completed — show celebration/summary.
    if (!sessionState.isActive && sessionState.completedAt != null) {
      return _buildCompletionScreen(context, sessionState);
    }

    // No items to review.
    if (!sessionState.isActive && sessionState.items.isEmpty) {
      return _buildEmptyScreen(context);
    }

    final currentItem = sessionState.currentItem;
    if (currentItem == null) {
      return _buildCompletionScreen(context, sessionState);
    }

    return Scaffold(
      body: Stack(
        children: [
          SafeArea(
            child: Column(
              children: [
                // Progress header
                _ReviewProgressHeader(
                  reviewed: sessionState.reviewedCount,
                  total: sessionState.totalItems,
                  progress: sessionState.progress,
                  onClose: _confirmEndSession,
                ),

                const SizedBox(height: SpacingTokens.lg),

                // Current flashcard with swipe gesture
                Expanded(
                  child: Padding(
                    padding: const EdgeInsets.symmetric(
                      horizontal: SpacingTokens.lg,
                    ),
                    child: _SwipeableFlashcard(
                      key: ValueKey(currentItem.conceptId),
                      item: currentItem,
                      onSwipeRight: () => _rateItem(FsrsRating.good),
                      onSwipeLeft: () => _rateItem(FsrsRating.again),
                    ),
                  ),
                ),

                // Swipe hint labels
                Padding(
                  padding: const EdgeInsets.symmetric(
                    horizontal: SpacingTokens.xl,
                    vertical: SpacingTokens.md,
                  ),
                  child: Row(
                    mainAxisAlignment: MainAxisAlignment.spaceBetween,
                    children: [
                      Row(
                        children: [
                          Icon(Icons.arrow_back_rounded,
                              size: 16, color: colorScheme.error),
                          const SizedBox(width: SpacingTokens.xs),
                          Text(
                            'Forgot',
                            style: theme.textTheme.labelMedium?.copyWith(
                              color: colorScheme.error,
                            ),
                          ),
                        ],
                      ),
                      Row(
                        children: [
                          Text(
                            'Knew it',
                            style: theme.textTheme.labelMedium?.copyWith(
                              color: colorScheme.primary,
                            ),
                          ),
                          const SizedBox(width: SpacingTokens.xs),
                          Icon(Icons.arrow_forward_rounded,
                              size: 16, color: colorScheme.primary),
                        ],
                      ),
                    ],
                  ),
                ),

                // Manual buttons as fallback for accessibility
                Padding(
                  padding: const EdgeInsets.only(
                    left: SpacingTokens.lg,
                    right: SpacingTokens.lg,
                    bottom: SpacingTokens.xl,
                  ),
                  child: Row(
                    children: [
                      Expanded(
                        child: OutlinedButton.icon(
                          onPressed: () => _rateItem(FsrsRating.again),
                          icon: const Icon(Icons.close_rounded),
                          label: const Text('Forgot'),
                          style: OutlinedButton.styleFrom(
                            foregroundColor: colorScheme.error,
                            side: BorderSide(color: colorScheme.error),
                            minimumSize: const Size(0, 48),
                          ),
                        ),
                      ),
                      const SizedBox(width: SpacingTokens.md),
                      Expanded(
                        child: FilledButton.icon(
                          onPressed: () => _rateItem(FsrsRating.good),
                          icon: const Icon(Icons.check_rounded),
                          label: const Text('Knew it'),
                          style: FilledButton.styleFrom(
                            minimumSize: const Size(0, 48),
                          ),
                        ),
                      ),
                    ],
                  ),
                ),
              ],
            ),
          ),

          // Celebration overlay
          CelebrationOverlay(controller: _celebrationController),
        ],
      ),
    );
  }

  void _rateItem(FsrsRating rating) {
    HapticFeedback.selectionClick();
    ref.read(reviewSessionProvider.notifier).rateCurrentItem(rating);

    // Check if session just completed.
    final updated = ref.read(reviewSessionProvider);
    if (updated.isComplete) {
      // Trigger celebration proportional to items reviewed.
      final count = updated.reviewedCount;
      final CelebrationTier tier;
      if (count >= 20) {
        tier = CelebrationTier.epic;
      } else if (count >= 10) {
        tier = CelebrationTier.major;
      } else if (count >= 5) {
        tier = CelebrationTier.medium;
      } else {
        tier = CelebrationTier.minor;
      }
      _celebrationController.celebrate(tier: tier, xp: count * 5);
    }
  }

  Future<void> _confirmEndSession() async {
    final sessionState = ref.read(reviewSessionProvider);
    if (sessionState.reviewedCount == 0) {
      // No items reviewed yet — just close.
      ref.read(reviewSessionProvider.notifier).endSession();
      if (mounted) Navigator.of(context).pop();
      return;
    }

    final confirmed = await showDialog<bool>(
      context: context,
      builder: (_) {
        return AlertDialog(
          title: const Text('End Review?'),
          content: Text(
            'You have reviewed ${sessionState.reviewedCount} of '
            '${sessionState.totalItems} items. End now?',
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.of(context).pop(false),
              child: const Text('Continue'),
            ),
            FilledButton(
              onPressed: () => Navigator.of(context).pop(true),
              child: const Text('End Review'),
            ),
          ],
        );
      },
    );

    if (confirmed == true && mounted) {
      ref.read(reviewSessionProvider.notifier).endSession();
      Navigator.of(context).pop();
    }
  }

  Widget _buildCompletionScreen(
      BuildContext context, ReviewSessionState sessionState) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;
    final reviewedCount = sessionState.reviewedCount;
    final knewCount = sessionState.results.values
        .where((r) => r != FsrsRating.again)
        .length;
    final forgotCount = reviewedCount - knewCount;
    final accuracyPct =
        reviewedCount > 0 ? (knewCount / reviewedCount * 100).toInt() : 0;

    return Scaffold(
      body: SafeArea(
        child: Stack(
          children: [
            Padding(
              padding: const EdgeInsets.all(SpacingTokens.xl),
              child: Column(
                mainAxisAlignment: MainAxisAlignment.center,
                children: [
                  Icon(
                    Icons.auto_awesome_rounded,
                    size: 72,
                    color: colorScheme.primary,
                  ),
                  const SizedBox(height: SpacingTokens.lg),
                  Text(
                    'Review Complete!',
                    style: theme.textTheme.headlineLarge?.copyWith(
                      fontWeight: FontWeight.w800,
                    ),
                    textAlign: TextAlign.center,
                  ),
                  const SizedBox(height: SpacingTokens.xl),
                  _StatRow(
                    icon: Icons.check_circle_outline_rounded,
                    label: 'Recalled',
                    value: '$knewCount',
                    color: colorScheme.primary,
                  ),
                  _StatRow(
                    icon: Icons.refresh_rounded,
                    label: 'Need practice',
                    value: '$forgotCount',
                    color: colorScheme.error,
                  ),
                  _StatRow(
                    icon: Icons.percent_rounded,
                    label: 'Recall rate',
                    value: '$accuracyPct%',
                    color: colorScheme.tertiary,
                  ),
                  const SizedBox(height: SpacingTokens.xxl),

                  // Memory strength bar for each reviewed item.
                  if (sessionState.items.isNotEmpty)
                    _MemoryStrengthGrid(
                      items: sessionState.items,
                      results: sessionState.results,
                    ),

                  const SizedBox(height: SpacingTokens.xxl),
                  FilledButton.icon(
                    onPressed: () {
                      ref.read(reviewSessionProvider.notifier).reset();
                      Navigator.of(context).pop();
                    },
                    icon: const Icon(Icons.home_rounded),
                    label: const Text('Back to Home'),
                    style: FilledButton.styleFrom(
                      minimumSize: const Size(double.infinity, 48),
                    ),
                  ),
                ],
              ),
            ),
            CelebrationOverlay(controller: _celebrationController),
          ],
        ),
      ),
    );
  }

  Widget _buildEmptyScreen(BuildContext context) {
    final theme = Theme.of(context);
    return Scaffold(
      appBar: AppBar(
        title: const Text('Review'),
      ),
      body: Center(
        child: Padding(
          padding: const EdgeInsets.all(SpacingTokens.xl),
          child: Column(
            mainAxisAlignment: MainAxisAlignment.center,
            children: [
              Icon(
                Icons.check_circle_rounded,
                size: 72,
                color: theme.colorScheme.primary,
              ),
              const SizedBox(height: SpacingTokens.lg),
              Text(
                'All caught up!',
                style: theme.textTheme.headlineMedium?.copyWith(
                  fontWeight: FontWeight.w700,
                ),
              ),
              const SizedBox(height: SpacingTokens.sm),
              Text(
                'No concepts are due for review right now.\nKeep learning to build your memory!',
                style: theme.textTheme.bodyLarge?.copyWith(
                  color: theme.colorScheme.onSurfaceVariant,
                ),
                textAlign: TextAlign.center,
              ),
              const SizedBox(height: SpacingTokens.xl),
              FilledButton(
                onPressed: () => Navigator.of(context).pop(),
                child: const Text('Back to Home'),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

// ---------------------------------------------------------------------------
// Supporting Widgets
// ---------------------------------------------------------------------------

/// Progress bar at the top of the review session.
class _ReviewProgressHeader extends StatelessWidget {
  const _ReviewProgressHeader({
    required this.reviewed,
    required this.total,
    required this.progress,
    required this.onClose,
  });

  final int reviewed;
  final int total;
  final double progress;
  final VoidCallback onClose;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;

    return Padding(
      padding: const EdgeInsets.symmetric(
        horizontal: SpacingTokens.md,
        vertical: SpacingTokens.sm,
      ),
      child: Row(
        children: [
          IconButton(
            onPressed: onClose,
            icon: const Icon(Icons.close_rounded),
          ),
          Expanded(
            child: Column(
              children: [
                Text(
                  '$reviewed / $total reviewed',
                  style: theme.textTheme.labelMedium?.copyWith(
                    color: colorScheme.onSurfaceVariant,
                  ),
                ),
                const SizedBox(height: SpacingTokens.xs),
                ClipRRect(
                  borderRadius: BorderRadius.circular(RadiusTokens.full),
                  child: LinearProgressIndicator(
                    value: progress,
                    minHeight: 6,
                    backgroundColor:
                        colorScheme.surfaceContainerHighest,
                    valueColor: AlwaysStoppedAnimation<Color>(
                      colorScheme.primary,
                    ),
                  ),
                ),
              ],
            ),
          ),
          const SizedBox(width: SpacingTokens.xl),
        ],
      ),
    );
  }
}

/// A swipeable flashcard that uses Dismissible for gesture recognition.
class _SwipeableFlashcard extends StatefulWidget {
  const _SwipeableFlashcard({
    super.key,
    required this.item,
    required this.onSwipeRight,
    required this.onSwipeLeft,
  });

  final DueReviewItem item;
  final VoidCallback onSwipeRight;
  final VoidCallback onSwipeLeft;

  @override
  State<_SwipeableFlashcard> createState() => _SwipeableFlashcardState();
}

class _SwipeableFlashcardState extends State<_SwipeableFlashcard> {
  bool _revealed = false;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;
    final card = widget.item.card;

    // Memory strength opacity: higher retrievability = more solid/glowing.
    final retrievability = card.retrievability.clamp(0.0, 1.0);
    // Invert: low retrievability = more transparent (weaker memory).
    final strengthOpacity = 0.3 + (retrievability * 0.7);

    return Dismissible(
      key: ValueKey(widget.item.conceptId),
      direction: DismissDirection.horizontal,
      onDismissed: (direction) {
        if (direction == DismissDirection.startToEnd) {
          widget.onSwipeRight(); // Knew it
        } else {
          widget.onSwipeLeft(); // Forgot
        }
      },
      background: _SwipeBackground(
        alignment: Alignment.centerLeft,
        color: colorScheme.primary,
        icon: Icons.check_rounded,
        label: 'Knew it',
      ),
      secondaryBackground: _SwipeBackground(
        alignment: Alignment.centerRight,
        color: colorScheme.error,
        icon: Icons.close_rounded,
        label: 'Forgot',
      ),
      child: GestureDetector(
        onTap: () => setState(() => _revealed = !_revealed),
        child: AnimatedContainer(
          duration: AnimationTokens.normal,
          width: double.infinity,
          padding: const EdgeInsets.all(SpacingTokens.xl),
          decoration: BoxDecoration(
            color: colorScheme.surfaceContainerLow,
            borderRadius: BorderRadius.circular(RadiusTokens.xl),
            border: Border.all(
              color: colorScheme.outlineVariant.withValues(
                alpha: strengthOpacity,
              ),
              width: 2,
            ),
            boxShadow: [
              BoxShadow(
                color: colorScheme.primary.withValues(
                  alpha: retrievability * 0.15,
                ),
                blurRadius: 16 * retrievability,
                spreadRadius: 2 * retrievability,
              ),
            ],
          ),
          child: Column(
            mainAxisAlignment: MainAxisAlignment.center,
            children: [
              // Memory strength indicator.
              _MemoryStrengthBar(retrievability: retrievability),
              const SizedBox(height: SpacingTokens.lg),

              // Concept ID as the "question" side.
              Text(
                widget.item.conceptId,
                style: theme.textTheme.headlineMedium?.copyWith(
                  fontWeight: FontWeight.w700,
                ),
                textAlign: TextAlign.center,
              ),

              const SizedBox(height: SpacingTokens.md),

              // Overdue indicator.
              if (widget.item.overdueFactor > 1.0)
                Container(
                  padding: const EdgeInsets.symmetric(
                    horizontal: SpacingTokens.sm,
                    vertical: SpacingTokens.xxs,
                  ),
                  decoration: BoxDecoration(
                    color: colorScheme.errorContainer,
                    borderRadius: BorderRadius.circular(RadiusTokens.full),
                  ),
                  child: Text(
                    '${widget.item.overdueFactor.toStringAsFixed(1)}x overdue',
                    style: theme.textTheme.labelSmall?.copyWith(
                      color: colorScheme.onErrorContainer,
                    ),
                  ),
                ),

              const SizedBox(height: SpacingTokens.xl),

              // Tap to reveal prompt.
              if (!_revealed)
                Text(
                  'Tap to reveal answer',
                  style: theme.textTheme.bodyMedium?.copyWith(
                    color: colorScheme.onSurfaceVariant,
                    fontStyle: FontStyle.italic,
                  ),
                )
              else
                Column(
                  children: [
                    Divider(
                      color: colorScheme.outlineVariant,
                      height: SpacingTokens.xl,
                    ),
                    Text(
                      'Do you remember this concept?',
                      style: theme.textTheme.bodyLarge?.copyWith(
                        color: colorScheme.onSurface,
                      ),
                      textAlign: TextAlign.center,
                    ),
                    const SizedBox(height: SpacingTokens.sm),
                    Text(
                      'Swipe right if yes, left if no',
                      style: theme.textTheme.bodySmall?.copyWith(
                        color: colorScheme.onSurfaceVariant,
                      ),
                    ),
                  ],
                ),
            ],
          ),
        ),
      ),
    );
  }
}

/// Background shown during swipe gestures.
class _SwipeBackground extends StatelessWidget {
  const _SwipeBackground({
    required this.alignment,
    required this.color,
    required this.icon,
    required this.label,
  });

  final Alignment alignment;
  final Color color;
  final IconData icon;
  final String label;

  @override
  Widget build(BuildContext context) {
    return Container(
      alignment: alignment,
      padding: const EdgeInsets.symmetric(horizontal: SpacingTokens.xl),
      decoration: BoxDecoration(
        color: color.withValues(alpha: 0.15),
        borderRadius: BorderRadius.circular(RadiusTokens.xl),
      ),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          Icon(icon, color: color, size: 48),
          const SizedBox(height: SpacingTokens.xs),
          Text(
            label,
            style: TextStyle(
              color: color,
              fontWeight: FontWeight.w700,
            ),
          ),
        ],
      ),
    );
  }
}

/// Horizontal bar showing memory strength (retrievability).
class _MemoryStrengthBar extends StatelessWidget {
  const _MemoryStrengthBar({required this.retrievability});

  final double retrievability;

  @override
  Widget build(BuildContext context) {
    final colorScheme = Theme.of(context).colorScheme;
    final theme = Theme.of(context);

    // Color interpolation: red (weak) -> yellow (medium) -> green (strong).
    final Color barColor;
    if (retrievability < 0.4) {
      barColor = colorScheme.error;
    } else if (retrievability < 0.7) {
      barColor = const Color(0xFFFF9800); // Orange
    } else {
      barColor = colorScheme.primary;
    }

    return Column(
      children: [
        Row(
          mainAxisAlignment: MainAxisAlignment.spaceBetween,
          children: [
            Text(
              'Memory Strength',
              style: theme.textTheme.labelSmall?.copyWith(
                color: colorScheme.onSurfaceVariant,
              ),
            ),
            Text(
              '${(retrievability * 100).toInt()}%',
              style: theme.textTheme.labelSmall?.copyWith(
                color: barColor,
                fontWeight: FontWeight.w700,
              ),
            ),
          ],
        ),
        const SizedBox(height: SpacingTokens.xs),
        ClipRRect(
          borderRadius: BorderRadius.circular(RadiusTokens.full),
          child: LinearProgressIndicator(
            value: retrievability,
            minHeight: 4,
            backgroundColor: colorScheme.surfaceContainerHighest,
            valueColor: AlwaysStoppedAnimation<Color>(barColor),
          ),
        ),
      ],
    );
  }
}

/// Grid of memory strength indicators shown on the completion screen.
class _MemoryStrengthGrid extends StatelessWidget {
  const _MemoryStrengthGrid({
    required this.items,
    required this.results,
  });

  final List<DueReviewItem> items;
  final Map<String, FsrsRating> results;

  @override
  Widget build(BuildContext context) {
    final colorScheme = Theme.of(context).colorScheme;
    final theme = Theme.of(context);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(
          'Memory Map',
          style: theme.textTheme.titleSmall?.copyWith(
            fontWeight: FontWeight.w600,
          ),
        ),
        const SizedBox(height: SpacingTokens.sm),
        Wrap(
          spacing: SpacingTokens.xs,
          runSpacing: SpacingTokens.xs,
          children: items.map((item) {
            final rating = results[item.conceptId];
            final knew = rating != null && rating != FsrsRating.again;
            return Container(
              width: 24,
              height: 24,
              decoration: BoxDecoration(
                color: knew
                    ? colorScheme.primary.withValues(alpha: 0.8)
                    : colorScheme.error.withValues(alpha: 0.5),
                borderRadius: BorderRadius.circular(RadiusTokens.sm),
              ),
              child: Icon(
                knew ? Icons.check_rounded : Icons.close_rounded,
                size: 14,
                color: knew ? colorScheme.onPrimary : colorScheme.onError,
              ),
            );
          }).toList(),
        ),
      ],
    );
  }
}

/// A row in the completion summary.
class _StatRow extends StatelessWidget {
  const _StatRow({
    required this.icon,
    required this.label,
    required this.value,
    required this.color,
  });

  final IconData icon;
  final String label;
  final String value;
  final Color color;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: SpacingTokens.xs),
      child: Row(
        children: [
          Icon(icon, size: 20, color: color),
          const SizedBox(width: SpacingTokens.md),
          Text(label, style: theme.textTheme.bodyLarge),
          const Spacer(),
          Text(
            value,
            style: theme.textTheme.bodyLarge?.copyWith(
              fontWeight: FontWeight.w700,
              color: color,
            ),
          ),
        ],
      ),
    );
  }
}
