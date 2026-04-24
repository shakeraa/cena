// =============================================================================
// Cena Adaptive Learning Platform — Quest Panel Widget
// =============================================================================
//
// Collapsible card showing active quests on the home screen. Displays
// progress indicators for each quest, celebrates completion with XP
// animation, and provides a quest log of completed quests.
// =============================================================================

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/config/app_config.dart';
import '../models/quest_models.dart';

// ---------------------------------------------------------------------------
// Quest State Providers
// ---------------------------------------------------------------------------

/// Active quests currently assigned to the student.
final activeQuestsProvider = StateProvider<List<Quest>>((ref) => []);

/// Completed quests history (quest log).
final completedQuestsProvider = StateProvider<List<Quest>>((ref) => []);

/// Whether the quest panel is expanded on the home screen.
final questPanelExpandedProvider = StateProvider<bool>((ref) => true);

// ---------------------------------------------------------------------------
// Quest Panel Widget
// ---------------------------------------------------------------------------

/// Collapsible card displaying active quests on the home screen.
///
/// Shows progress indicators for each quest, celebrates completion,
/// and provides access to the quest log.
class QuestPanel extends ConsumerWidget {
  const QuestPanel({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final quests = ref.watch(activeQuestsProvider);
    final isExpanded = ref.watch(questPanelExpandedProvider);
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;

    if (quests.isEmpty) return const SizedBox.shrink();

    final activeQuests =
        quests.where((q) => q.status != QuestStatus.completed).toList();
    final completedCount =
        quests.where((q) => q.status == QuestStatus.completed).length;

    return Card(
      elevation: 2,
      child: Column(
        children: [
          // Header with expand/collapse toggle
          InkWell(
            onTap: () => ref
                .read(questPanelExpandedProvider.notifier)
                .state = !isExpanded,
            borderRadius: BorderRadius.vertical(
              top: const Radius.circular(RadiusTokens.lg),
              bottom: isExpanded
                  ? Radius.zero
                  : const Radius.circular(RadiusTokens.lg),
            ),
            child: Padding(
              padding: const EdgeInsets.all(SpacingTokens.md),
              child: Row(
                children: [
                  Icon(
                    Icons.assignment_rounded,
                    size: 22,
                    color: colorScheme.primary,
                  ),
                  const SizedBox(width: SpacingTokens.sm),
                  Expanded(
                    child: Text(
                      'Quests',
                      style: theme.textTheme.titleMedium?.copyWith(
                        fontWeight: FontWeight.w700,
                      ),
                    ),
                  ),
                  if (completedCount > 0)
                    Container(
                      padding: const EdgeInsets.symmetric(
                        horizontal: SpacingTokens.sm,
                        vertical: SpacingTokens.xxs,
                      ),
                      decoration: BoxDecoration(
                        color: const Color(0xFF4CAF50).withValues(alpha: 0.15),
                        borderRadius:
                            BorderRadius.circular(RadiusTokens.full),
                      ),
                      child: Text(
                        '$completedCount done',
                        style: theme.textTheme.labelSmall?.copyWith(
                          color: const Color(0xFF2E7D32),
                          fontWeight: FontWeight.w600,
                        ),
                      ),
                    ),
                  const SizedBox(width: SpacingTokens.xs),
                  AnimatedRotation(
                    turns: isExpanded ? 0.5 : 0.0,
                    duration: AnimationTokens.fast,
                    child: Icon(
                      Icons.expand_more_rounded,
                      color: colorScheme.onSurfaceVariant,
                    ),
                  ),
                ],
              ),
            ),
          ),

          // Expandable quest list
          AnimatedCrossFade(
            firstChild: Column(
              children: [
                const Divider(height: 1),
                ...activeQuests.map((quest) => _QuestTile(quest: quest)),
                if (activeQuests.isEmpty)
                  Padding(
                    padding: const EdgeInsets.all(SpacingTokens.md),
                    child: Text(
                      'All quests completed! Great work.',
                      style: theme.textTheme.bodyMedium?.copyWith(
                        color: colorScheme.onSurfaceVariant,
                      ),
                    ),
                  ),
                // Quest log button
                Padding(
                  padding: const EdgeInsets.fromLTRB(
                    SpacingTokens.md,
                    0,
                    SpacingTokens.md,
                    SpacingTokens.sm,
                  ),
                  child: Align(
                    alignment: AlignmentDirectional.centerEnd,
                    child: TextButton.icon(
                      onPressed: () => _showQuestLog(context, ref),
                      icon: const Icon(Icons.history_rounded, size: 18),
                      label: const Text('Quest Log'),
                    ),
                  ),
                ),
              ],
            ),
            secondChild: const SizedBox.shrink(),
            crossFadeState: isExpanded
                ? CrossFadeState.showFirst
                : CrossFadeState.showSecond,
            duration: AnimationTokens.normal,
          ),
        ],
      ),
    );
  }

  void _showQuestLog(BuildContext context, WidgetRef ref) {
    final completed = ref.read(completedQuestsProvider);
    showModalBottomSheet<void>(
      context: context,
      isScrollControlled: true,
      shape: const RoundedRectangleBorder(
        borderRadius: BorderRadius.vertical(
          top: Radius.circular(RadiusTokens.xl),
        ),
      ),
      builder: (context) => _QuestLogSheet(quests: completed),
    );
  }
}

// ---------------------------------------------------------------------------
// Quest Tile
// ---------------------------------------------------------------------------

class _QuestTile extends StatelessWidget {
  const _QuestTile({required this.quest});

  final Quest quest;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;
    final isCompleted = quest.status == QuestStatus.completed;

    return Padding(
      padding: const EdgeInsets.symmetric(
        horizontal: SpacingTokens.md,
        vertical: SpacingTokens.sm,
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          // Title row with XP reward
          Row(
            children: [
              Icon(
                _questTypeIcon(quest.type),
                size: 18,
                color: _questTypeColor(quest.type),
              ),
              const SizedBox(width: SpacingTokens.sm),
              Expanded(
                child: Text(
                  quest.title,
                  style: theme.textTheme.bodyMedium?.copyWith(
                    fontWeight: FontWeight.w600,
                    decoration: isCompleted
                        ? TextDecoration.lineThrough
                        : null,
                  ),
                ),
              ),
              Container(
                padding: const EdgeInsets.symmetric(
                  horizontal: SpacingTokens.xs,
                  vertical: SpacingTokens.xxs,
                ),
                decoration: BoxDecoration(
                  color: const Color(0xFFFFD700).withValues(alpha: 0.12),
                  borderRadius: BorderRadius.circular(RadiusTokens.sm),
                ),
                child: Text(
                  '+${quest.xpReward} XP',
                  style: theme.textTheme.labelSmall?.copyWith(
                    color: const Color(0xFFFFAA00),
                    fontWeight: FontWeight.w700,
                  ),
                ),
              ),
            ],
          ),

          const SizedBox(height: SpacingTokens.xs),

          // Progress bar
          Row(
            children: [
              Expanded(
                child: ClipRRect(
                  borderRadius: BorderRadius.circular(RadiusTokens.full),
                  child: TweenAnimationBuilder<double>(
                    tween: Tween(begin: 0, end: quest.progressFraction),
                    duration: AnimationTokens.slow,
                    curve: Curves.easeOutCubic,
                    builder: (context, value, _) {
                      return LinearProgressIndicator(
                        value: value,
                        minHeight: 6,
                        backgroundColor: colorScheme.surfaceContainerHighest,
                        valueColor: AlwaysStoppedAnimation<Color>(
                          isCompleted
                              ? const Color(0xFF4CAF50)
                              : _questTypeColor(quest.type),
                        ),
                      );
                    },
                  ),
                ),
              ),
              const SizedBox(width: SpacingTokens.sm),
              Text(
                '${quest.progress}/${quest.target}',
                style: theme.textTheme.labelSmall?.copyWith(
                  color: colorScheme.onSurfaceVariant,
                  fontWeight: FontWeight.w600,
                ),
              ),
            ],
          ),

          // Type label for side quests
          if (quest.type == QuestType.side) ...[
            const SizedBox(height: SpacingTokens.xxs),
            Text(
              'Optional',
              style: theme.textTheme.labelSmall?.copyWith(
                color: colorScheme.onSurfaceVariant,
                fontStyle: FontStyle.italic,
              ),
            ),
          ],
        ],
      ),
    );
  }

  IconData _questTypeIcon(QuestType type) {
    switch (type) {
      case QuestType.daily:
        return Icons.today_rounded;
      case QuestType.weekly:
        return Icons.date_range_rounded;
      case QuestType.monthly:
        return Icons.calendar_month_rounded;
      case QuestType.side:
        return Icons.explore_rounded;
    }
  }

  Color _questTypeColor(QuestType type) {
    switch (type) {
      case QuestType.daily:
        return const Color(0xFF1565C0);
      case QuestType.weekly:
        return const Color(0xFF6A1B9A);
      case QuestType.monthly:
        return const Color(0xFFFF6D00);
      case QuestType.side:
        return const Color(0xFF00897B);
    }
  }
}

// ---------------------------------------------------------------------------
// Quest Log Bottom Sheet
// ---------------------------------------------------------------------------

class _QuestLogSheet extends StatelessWidget {
  const _QuestLogSheet({required this.quests});

  final List<Quest> quests;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;

    return DraggableScrollableSheet(
      initialChildSize: 0.5,
      maxChildSize: 0.85,
      minChildSize: 0.3,
      expand: false,
      builder: (context, scrollController) {
        return Column(
          children: [
            // Handle bar
            Padding(
              padding: const EdgeInsets.only(top: SpacingTokens.sm),
              child: Container(
                width: 40,
                height: 4,
                decoration: BoxDecoration(
                  color: colorScheme.onSurfaceVariant.withValues(alpha: 0.3),
                  borderRadius: BorderRadius.circular(RadiusTokens.full),
                ),
              ),
            ),

            // Title
            Padding(
              padding: const EdgeInsets.all(SpacingTokens.md),
              child: Row(
                children: [
                  Icon(
                    Icons.history_rounded,
                    color: colorScheme.primary,
                  ),
                  const SizedBox(width: SpacingTokens.sm),
                  Text(
                    'Quest Log',
                    style: theme.textTheme.titleLarge?.copyWith(
                      fontWeight: FontWeight.w700,
                    ),
                  ),
                  const Spacer(),
                  Text(
                    '${quests.length} completed',
                    style: theme.textTheme.labelMedium?.copyWith(
                      color: colorScheme.onSurfaceVariant,
                    ),
                  ),
                ],
              ),
            ),
            const Divider(height: 1),

            // Quest list
            Expanded(
              child: quests.isEmpty
                  ? Center(
                      child: Padding(
                        padding: const EdgeInsets.all(SpacingTokens.xl),
                        child: Column(
                          mainAxisSize: MainAxisSize.min,
                          children: [
                            Icon(
                              Icons.assignment_outlined,
                              size: 48,
                              color: colorScheme.onSurfaceVariant
                                  .withValues(alpha: 0.4),
                            ),
                            const SizedBox(height: SpacingTokens.md),
                            Text(
                              'No completed quests yet',
                              style: theme.textTheme.bodyLarge?.copyWith(
                                color: colorScheme.onSurfaceVariant,
                              ),
                            ),
                            const SizedBox(height: SpacingTokens.xs),
                            Text(
                              'Complete quests to see them here.',
                              style: theme.textTheme.bodySmall?.copyWith(
                                color: colorScheme.onSurfaceVariant
                                    .withValues(alpha: 0.7),
                              ),
                            ),
                          ],
                        ),
                      ),
                    )
                  : ListView.separated(
                      controller: scrollController,
                      padding: const EdgeInsets.all(SpacingTokens.md),
                      itemCount: quests.length,
                      separatorBuilder: (_, __) =>
                          const SizedBox(height: SpacingTokens.sm),
                      itemBuilder: (context, index) {
                        final quest = quests[index];
                        return _CompletedQuestRow(quest: quest);
                      },
                    ),
            ),
          ],
        );
      },
    );
  }
}

class _CompletedQuestRow extends StatelessWidget {
  const _CompletedQuestRow({required this.quest});

  final Quest quest;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;

    return Row(
      children: [
        Container(
          width: 36,
          height: 36,
          decoration: BoxDecoration(
            shape: BoxShape.circle,
            color: const Color(0xFF4CAF50).withValues(alpha: 0.12),
          ),
          child: const Icon(
            Icons.check_rounded,
            size: 20,
            color: Color(0xFF2E7D32),
          ),
        ),
        const SizedBox(width: SpacingTokens.sm),
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(
                quest.title,
                style: theme.textTheme.bodyMedium?.copyWith(
                  fontWeight: FontWeight.w500,
                ),
              ),
              Text(
                _questTypeLabel(quest.type),
                style: theme.textTheme.labelSmall?.copyWith(
                  color: colorScheme.onSurfaceVariant,
                ),
              ),
            ],
          ),
        ),
        Text(
          '+${quest.xpReward} XP',
          style: theme.textTheme.labelMedium?.copyWith(
            color: const Color(0xFFFFAA00),
            fontWeight: FontWeight.w700,
          ),
        ),
      ],
    );
  }

  String _questTypeLabel(QuestType type) {
    switch (type) {
      case QuestType.daily:
        return 'Daily Quest';
      case QuestType.weekly:
        return 'Weekly Quest';
      case QuestType.monthly:
        return 'Monthly Quest';
      case QuestType.side:
        return 'Side Quest';
    }
  }
}
