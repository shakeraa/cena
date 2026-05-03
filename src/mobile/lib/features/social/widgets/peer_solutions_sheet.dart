// =============================================================================
// Cena Adaptive Learning Platform — Peer Solution Replays (MOB-053)
// =============================================================================
// Bottom sheet showing 2-3 anonymous peer solutions sorted by methodology
// diversity. "See how others solved it" — Level 3 progressive disclosure.
// =============================================================================

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/config/app_config.dart';
import '../../../core/state/app_state.dart';
import '../../../core/state/peer_solutions_state.dart';

// ---------------------------------------------------------------------------
// Entry point
// ---------------------------------------------------------------------------

/// Shows the peer solutions bottom sheet.
/// Call from the feedback overlay's "See how others solved it" button.
void showPeerSolutionsSheet({
  required BuildContext context,
  required String conceptId,
  required String questionId,
}) {
  showModalBottomSheet<void>(
    context: context,
    isScrollControlled: true,
    useSafeArea: true,
    shape: const RoundedRectangleBorder(
      borderRadius: BorderRadius.vertical(
        top: Radius.circular(RadiusTokens.xl),
      ),
    ),
    builder: (context) => PeerSolutionsSheet(
      conceptId: conceptId,
      questionId: questionId,
    ),
  );
}

// ---------------------------------------------------------------------------
// Sheet widget
// ---------------------------------------------------------------------------

/// Bottom sheet displaying anonymous peer solutions for a question.
///
/// Shows 2-3 solutions sorted by methodology diversity so the student
/// sees different approaches. All solutions are anonymous ("A classmate
/// solved it this way"). Includes simple yes/no helpfulness voting.
class PeerSolutionsSheet extends ConsumerWidget {
  const PeerSolutionsSheet({
    super.key,
    required this.conceptId,
    required this.questionId,
  });

  final String conceptId;
  final String questionId;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;
    final request = PeerSolutionRequest(
      conceptId: conceptId,
      questionId: questionId,
    );
    final solutionsAsync = ref.watch(peerSolutionsProvider(request));

    return DraggableScrollableSheet(
      initialChildSize: 0.6,
      minChildSize: 0.3,
      maxChildSize: 0.85,
      expand: false,
      builder: (context, scrollController) {
        return Column(
          children: [
            // Drag handle
            _buildDragHandle(colorScheme),

            // Header
            _buildHeader(theme, colorScheme),

            // Content
            Expanded(
              child: solutionsAsync.when(
                loading: () => const Center(
                  child: CircularProgressIndicator(),
                ),
                error: (_, __) => _buildErrorState(theme, colorScheme),
                data: (solutions) {
                  if (solutions.isEmpty) {
                    return _buildEmptyState(theme, colorScheme);
                  }
                  return ListView.separated(
                    controller: scrollController,
                    padding: const EdgeInsets.all(SpacingTokens.md),
                    itemCount: solutions.length,
                    separatorBuilder: (_, __) =>
                        const SizedBox(height: SpacingTokens.md),
                    itemBuilder: (context, index) => _PeerSolutionCard(
                      solution: solutions[index],
                      index: index,
                    ),
                  );
                },
              ),
            ),
          ],
        );
      },
    );
  }

  Widget _buildDragHandle(ColorScheme colorScheme) {
    return Padding(
      padding: const EdgeInsets.only(top: SpacingTokens.sm),
      child: Center(
        child: Container(
          width: 40,
          height: 4,
          decoration: BoxDecoration(
            color: colorScheme.onSurfaceVariant.withValues(alpha: 0.3),
            borderRadius: BorderRadius.circular(RadiusTokens.full),
          ),
        ),
      ),
    );
  }

  Widget _buildHeader(ThemeData theme, ColorScheme colorScheme) {
    return Padding(
      padding: const EdgeInsets.all(SpacingTokens.md),
      child: Row(
        children: [
          Icon(
            Icons.people_outline_rounded,
            size: 22,
            color: colorScheme.primary,
          ),
          const SizedBox(width: SpacingTokens.sm),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  'How Others Solved It',
                  style: theme.textTheme.titleMedium?.copyWith(
                    fontWeight: FontWeight.w700,
                  ),
                ),
                Text(
                  'See different approaches from your classmates',
                  style: theme.textTheme.bodySmall?.copyWith(
                    color: colorScheme.onSurfaceVariant,
                  ),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildEmptyState(ThemeData theme, ColorScheme colorScheme) {
    return Center(
      child: Column(
        mainAxisAlignment: MainAxisAlignment.center,
        children: [
          Icon(
            Icons.lightbulb_outline_rounded,
            size: 48,
            color: colorScheme.onSurfaceVariant.withValues(alpha: 0.4),
          ),
          const SizedBox(height: SpacingTokens.md),
          Text(
            'No peer solutions available yet',
            style: theme.textTheme.bodyMedium?.copyWith(
              color: colorScheme.onSurfaceVariant,
            ),
          ),
          const SizedBox(height: SpacingTokens.xs),
          Text(
            'Be one of the first to solve this!',
            style: theme.textTheme.bodySmall?.copyWith(
              color: colorScheme.onSurfaceVariant.withValues(alpha: 0.6),
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildErrorState(ThemeData theme, ColorScheme colorScheme) {
    return Center(
      child: Column(
        mainAxisAlignment: MainAxisAlignment.center,
        children: [
          Icon(
            Icons.cloud_off_rounded,
            size: 48,
            color: colorScheme.onSurfaceVariant.withValues(alpha: 0.4),
          ),
          const SizedBox(height: SpacingTokens.md),
          Text(
            'Could not load peer solutions',
            style: theme.textTheme.bodyMedium?.copyWith(
              color: colorScheme.onSurfaceVariant,
            ),
          ),
        ],
      ),
    );
  }
}

// ---------------------------------------------------------------------------
// Peer solution card
// ---------------------------------------------------------------------------

class _PeerSolutionCard extends ConsumerStatefulWidget {
  const _PeerSolutionCard({
    required this.solution,
    required this.index,
  });

  final PeerSolution solution;
  final int index;

  @override
  ConsumerState<_PeerSolutionCard> createState() => _PeerSolutionCardState();
}

class _PeerSolutionCardState extends ConsumerState<_PeerSolutionCard> {
  bool? _helpfulVote;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;
    final solution = widget.solution;

    return Card(
      elevation: 0,
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(RadiusTokens.lg),
        side: BorderSide(color: colorScheme.outlineVariant),
      ),
      child: Padding(
        padding: const EdgeInsets.all(SpacingTokens.md),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            // Anonymous header
            Row(
              children: [
                CircleAvatar(
                  radius: 14,
                  backgroundColor: _avatarColor(widget.index, colorScheme),
                  child: Icon(
                    Icons.person_rounded,
                    size: 16,
                    color: colorScheme.onPrimary,
                  ),
                ),
                const SizedBox(width: SpacingTokens.sm),
                Expanded(
                  child: Text(
                    'A classmate solved it this way',
                    style: theme.textTheme.labelLarge?.copyWith(
                      fontWeight: FontWeight.w600,
                    ),
                  ),
                ),
              ],
            ),
            const SizedBox(height: SpacingTokens.sm),

            // Methodology and time badges
            Wrap(
              spacing: SpacingTokens.sm,
              children: [
                _InfoChip(
                  icon: Icons.route_rounded,
                  label: solution.methodologyLabel,
                ),
                _InfoChip(
                  icon: Icons.timer_rounded,
                  label: solution.formattedTime,
                ),
              ],
            ),
            const SizedBox(height: SpacingTokens.md),

            // Approach steps
            ...solution.approachSteps.asMap().entries.map((entry) {
              return Padding(
                padding: const EdgeInsets.only(bottom: SpacingTokens.sm),
                child: Row(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    // Step number
                    Container(
                      width: 22,
                      height: 22,
                      decoration: BoxDecoration(
                        color: colorScheme.primaryContainer,
                        shape: BoxShape.circle,
                      ),
                      child: Center(
                        child: Text(
                          '${entry.key + 1}',
                          style: theme.textTheme.labelSmall?.copyWith(
                            fontWeight: FontWeight.w700,
                            color: colorScheme.onPrimaryContainer,
                          ),
                        ),
                      ),
                    ),
                    const SizedBox(width: SpacingTokens.sm),
                    Expanded(
                      child: Text(
                        entry.value,
                        style: theme.textTheme.bodySmall,
                      ),
                    ),
                  ],
                ),
              );
            }),

            const Divider(height: SpacingTokens.md),

            // Helpfulness vote
            _buildVoteRow(theme, colorScheme, solution),
          ],
        ),
      ),
    );
  }

  Widget _buildVoteRow(
    ThemeData theme,
    ColorScheme colorScheme,
    PeerSolution solution,
  ) {
    final hasVoted = _helpfulVote != null || solution.hasVoted;

    if (hasVoted) {
      return Row(
        children: [
          Icon(
            Icons.check_circle_rounded,
            size: 14,
            color: colorScheme.onSurfaceVariant.withValues(alpha: 0.5),
          ),
          const SizedBox(width: SpacingTokens.xs),
          Text(
            'Thanks for your feedback!',
            style: theme.textTheme.labelSmall?.copyWith(
              color: colorScheme.onSurfaceVariant.withValues(alpha: 0.5),
            ),
          ),
        ],
      );
    }

    return Row(
      children: [
        Text(
          'Was this helpful?',
          style: theme.textTheme.labelSmall?.copyWith(
            color: colorScheme.onSurfaceVariant,
          ),
        ),
        const SizedBox(width: SpacingTokens.md),
        _VoteButton(
          label: 'Yes',
          icon: Icons.thumb_up_alt_rounded,
          onTap: () => _vote(true),
        ),
        const SizedBox(width: SpacingTokens.sm),
        _VoteButton(
          label: 'No',
          icon: Icons.thumb_down_alt_rounded,
          onTap: () => _vote(false),
        ),
      ],
    );
  }

  void _vote(bool helpful) {
    setState(() => _helpfulVote = helpful);

    // Submit vote to backend.
    final api = ref.read(apiClientProvider);
    api.post<Map<String, dynamic>>(
      '/social/peer-solutions/${widget.solution.id}/vote',
      data: {'helpful': helpful},
    );
  }

  Color _avatarColor(int index, ColorScheme colorScheme) {
    const palette = [
      Color(0xFF0097A7),
      Color(0xFFFF8F00),
      Color(0xFF388E3C),
    ];
    return palette[index % palette.length];
  }
}

// ---------------------------------------------------------------------------
// Supporting widgets
// ---------------------------------------------------------------------------

class _InfoChip extends StatelessWidget {
  const _InfoChip({required this.icon, required this.label});

  final IconData icon;
  final String label;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;

    return Container(
      padding: const EdgeInsets.symmetric(
        horizontal: SpacingTokens.sm,
        vertical: SpacingTokens.xxs,
      ),
      decoration: BoxDecoration(
        color: colorScheme.surfaceContainerHighest.withValues(alpha: 0.5),
        borderRadius: BorderRadius.circular(RadiusTokens.full),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Icon(icon, size: 12, color: colorScheme.onSurfaceVariant),
          const SizedBox(width: SpacingTokens.xxs),
          Text(
            label,
            style: theme.textTheme.labelSmall?.copyWith(
              color: colorScheme.onSurfaceVariant,
            ),
          ),
        ],
      ),
    );
  }
}

class _VoteButton extends StatelessWidget {
  const _VoteButton({
    required this.label,
    required this.icon,
    required this.onTap,
  });

  final String label;
  final IconData icon;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;

    return InkWell(
      onTap: onTap,
      borderRadius: BorderRadius.circular(RadiusTokens.full),
      child: Container(
        padding: const EdgeInsets.symmetric(
          horizontal: SpacingTokens.sm,
          vertical: SpacingTokens.xs,
        ),
        decoration: BoxDecoration(
          border: Border.all(color: colorScheme.outlineVariant),
          borderRadius: BorderRadius.circular(RadiusTokens.full),
        ),
        child: Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(icon, size: 14, color: colorScheme.onSurfaceVariant),
            const SizedBox(width: SpacingTokens.xxs),
            Text(
              label,
              style: theme.textTheme.labelSmall?.copyWith(
                color: colorScheme.onSurfaceVariant,
              ),
            ),
          ],
        ),
      ),
    );
  }
}
