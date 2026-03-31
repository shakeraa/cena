// =============================================================================
// Cena Adaptive Learning Platform — Gamification Screen
// =============================================================================
//
// "Progress" tab content: XP & level card, streak section, badge grid, and
// recent achievements list.
// =============================================================================

import 'package:flutter/material.dart' hide Badge;
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intl/intl.dart';
import 'dart:async';

import '../../core/config/app_config.dart';
import '../../core/models/domain_models.dart';
import '../../core/services/interaction_feedback_service.dart';
import '../../core/state/gamification_state.dart';
import '../../core/state/momentum_state.dart';
import 'badge_detail_dialog.dart';
import 'momentum_meter.dart';
import 'streak_widget.dart';

/// Full gamification progress screen, shown in the "Progress" bottom nav tab.
class GamificationScreen extends ConsumerWidget {
  const GamificationScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final useMomentum = ref.watch(useMomentumMeterProvider);
    final momentumPct = ref.watch(momentumPercentageProvider);
    final momentumDays = ref.watch(momentumDaysStudiedProvider);
    final anxiety = ref.watch(streakAnxietyProvider);

    return ListView(
      padding: const EdgeInsets.all(SpacingTokens.md),
      children: [
        const _XpLevelCard(),
        const SizedBox(height: SpacingTokens.md),
        if (useMomentum)
          MomentumMeter(
            percentage: momentumPct,
            daysStudied: momentumDays,
          )
        else
          const StreakWidget(),
        if (!useMomentum && anxiety.suggestSwitch) ...[
          const SizedBox(height: SpacingTokens.sm),
          const _MomentumSuggestionCard(),
        ],
        const SizedBox(height: SpacingTokens.md),
        const _BadgeGrid(),
        const SizedBox(height: SpacingTokens.md),
        const _RecentAchievements(),
        const SizedBox(height: SpacingTokens.lg),
      ],
    );
  }
}

class _MomentumSuggestionCard extends ConsumerWidget {
  const _MomentumSuggestionCard();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final colorScheme = Theme.of(context).colorScheme;
    return Container(
      padding: const EdgeInsets.all(SpacingTokens.md),
      decoration: BoxDecoration(
        color: colorScheme.primaryContainer.withValues(alpha: 0.45),
        borderRadius: BorderRadius.circular(RadiusTokens.lg),
      ),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Icon(Icons.favorite_outline_rounded, color: colorScheme.primary),
          const SizedBox(width: SpacingTokens.sm),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  'נראה שרצף יומי מוסיף לחץ. לעבור ל-Momentum?',
                  style: Theme.of(context).textTheme.bodyMedium?.copyWith(
                        fontWeight: FontWeight.w600,
                      ),
                ),
                const SizedBox(height: SpacingTokens.xs),
                TextButton(
                  onPressed: () {
                    ref
                        .read(useMomentumMeterProvider.notifier)
                        .setUseMomentum(true);
                  },
                  child: const Text('כן, לעבור למצב Momentum'),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

// ---------------------------------------------------------------------------
// XP & Level Card
// ---------------------------------------------------------------------------

class _XpLevelCard extends ConsumerStatefulWidget {
  const _XpLevelCard();

  @override
  ConsumerState<_XpLevelCard> createState() => _XpLevelCardState();
}

class _XpLevelCardState extends ConsumerState<_XpLevelCard> {
  @override
  Widget build(BuildContext context) {
    ref.listen<int>(levelProvider, (prev, next) {
      if (prev != null && next > prev) {
        unawaited(InteractionFeedbackService.levelUp());
      }
    });

    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;

    final level = ref.watch(levelProvider);
    final xpWithin = ref.watch(xpWithinLevelProvider);
    final xpNeeded = ref.watch(xpForCurrentLevelProvider);
    final progress = ref.watch(xpProgressProvider);
    final dailyXp = ref.watch(dailyXpProvider);
    final totalXp = ref.watch(xpProvider);

    return Card(
      elevation: 2,
      child: Padding(
        padding: const EdgeInsets.all(SpacingTokens.md),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            // Level badge row
            Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                // Level badge circle
                Container(
                  width: 56,
                  height: 56,
                  decoration: BoxDecoration(
                    shape: BoxShape.circle,
                    gradient: LinearGradient(
                      begin: Alignment.topLeft,
                      end: Alignment.bottomRight,
                      colors: [
                        colorScheme.primary,
                        colorScheme.secondary,
                      ],
                    ),
                  ),
                  child: Center(
                    child: Text(
                      '$level',
                      style: theme.textTheme.titleLarge?.copyWith(
                        color: colorScheme.onPrimary,
                        fontWeight: FontWeight.w800,
                      ),
                    ),
                  ),
                ),
                const SizedBox(width: SpacingTokens.md),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        'Level $level',
                        style: theme.textTheme.titleMedium?.copyWith(
                          fontWeight: FontWeight.w700,
                        ),
                      ),
                      Text(
                        '$totalXp XP total',
                        style: theme.textTheme.labelMedium?.copyWith(
                          color: colorScheme.onSurfaceVariant,
                        ),
                      ),
                    ],
                  ),
                ),
                // Daily XP badge
                if (dailyXp > 0)
                  Container(
                    padding: const EdgeInsets.symmetric(
                      horizontal: SpacingTokens.sm,
                      vertical: SpacingTokens.xxs,
                    ),
                    decoration: BoxDecoration(
                      color: const Color(0xFFFFD700).withValues(alpha: 0.15),
                      borderRadius: BorderRadius.circular(RadiusTokens.full),
                      border: Border.all(
                        color: const Color(0xFFFFD700),
                        width: 1,
                      ),
                    ),
                    child: Row(
                      mainAxisSize: MainAxisSize.min,
                      children: [
                        const Icon(
                          Icons.bolt_rounded,
                          size: 14,
                          color: Color(0xFFFFD700),
                        ),
                        const SizedBox(width: SpacingTokens.xxs),
                        Text(
                          '+$dailyXp today',
                          style: theme.textTheme.labelSmall?.copyWith(
                            color: const Color(0xFFFFAA00),
                            fontWeight: FontWeight.w700,
                          ),
                        ),
                      ],
                    ),
                  ),
              ],
            ),

            const SizedBox(height: SpacingTokens.md),

            // XP progress bar
            Row(
              mainAxisAlignment: MainAxisAlignment.spaceBetween,
              children: [
                Text(
                  'Progress to Level ${level + 1}',
                  style: theme.textTheme.labelMedium?.copyWith(
                    color: colorScheme.onSurfaceVariant,
                  ),
                ),
                Text(
                  '$xpWithin / $xpNeeded XP',
                  style: theme.textTheme.labelMedium?.copyWith(
                    fontWeight: FontWeight.w600,
                  ),
                ),
              ],
            ),
            const SizedBox(height: SpacingTokens.xs),
            _AnimatedXpBar(progress: progress),
          ],
        ),
      ),
    );
  }
}

/// Animated XP progress bar that transitions from previous to current value.
class _AnimatedXpBar extends StatelessWidget {
  const _AnimatedXpBar({required this.progress});

  final double progress;

  @override
  Widget build(BuildContext context) {
    final colorScheme = Theme.of(context).colorScheme;
    return TweenAnimationBuilder<double>(
      tween: Tween(begin: 0.0, end: progress),
      duration: AnimationTokens.slow,
      curve: Curves.easeOutCubic,
      builder: (context, value, _) {
        return ClipRRect(
          borderRadius: BorderRadius.circular(RadiusTokens.full),
          child: LinearProgressIndicator(
            value: value,
            minHeight: 10,
            backgroundColor: colorScheme.surfaceContainerHighest,
            valueColor: AlwaysStoppedAnimation<Color>(colorScheme.primary),
          ),
        );
      },
    );
  }
}

// ---------------------------------------------------------------------------
// Badge Grid
// ---------------------------------------------------------------------------

class _BadgeGrid extends ConsumerWidget {
  const _BadgeGrid();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final theme = Theme.of(context);
    final catalogue = ref.watch(badgeCatalogueProvider);
    final earned = ref.watch(badgesProvider);
    final earnedIds = {for (final b in earned) b.id: b};

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(
          'Badges',
          style: theme.textTheme.titleMedium?.copyWith(
            fontWeight: FontWeight.w700,
          ),
        ),
        const SizedBox(height: SpacingTokens.sm),
        GridView.builder(
          shrinkWrap: true,
          physics: const NeverScrollableScrollPhysics(),
          gridDelegate: const SliverGridDelegateWithFixedCrossAxisCount(
            crossAxisCount: 4,
            mainAxisSpacing: SpacingTokens.sm,
            crossAxisSpacing: SpacingTokens.sm,
            childAspectRatio: 0.85,
          ),
          itemCount: catalogue.length,
          itemBuilder: (context, index) {
            final def = catalogue[index];
            final earnedBadge = earnedIds[def.id];
            return _BadgeCell(
              definition: def,
              earnedBadge: earnedBadge,
            );
          },
        ),
      ],
    );
  }
}

class _BadgeCell extends StatelessWidget {
  const _BadgeCell({
    required this.definition,
    required this.earnedBadge,
  });

  final BadgeDefinition definition;
  final Badge? earnedBadge;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;
    final isEarned = earnedBadge != null;

    return GestureDetector(
      onTap: () => showBadgeDetail(
        context,
        definition: definition,
        earnedBadge: earnedBadge,
      ),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          Container(
            width: 54,
            height: 54,
            decoration: BoxDecoration(
              shape: BoxShape.circle,
              color: isEarned
                  ? _categoryColor(definition.category)
                  : colorScheme.surfaceContainerHighest,
              boxShadow: isEarned
                  ? [
                      BoxShadow(
                        color: _categoryColor(definition.category)
                            .withValues(alpha: 0.3),
                        blurRadius: 8,
                        spreadRadius: 1,
                      ),
                    ]
                  : null,
            ),
            child: Stack(
              alignment: Alignment.center,
              children: [
                Icon(
                  _iconData(definition.icon),
                  size: 26,
                  color: isEarned
                      ? Colors.white
                      : colorScheme.onSurfaceVariant.withValues(alpha: 0.3),
                ),
                if (!isEarned)
                  Positioned(
                    bottom: 4,
                    right: 4,
                    child: Container(
                      width: 16,
                      height: 16,
                      decoration: BoxDecoration(
                        shape: BoxShape.circle,
                        color: colorScheme.surface,
                        border: Border.all(
                          color: colorScheme.outline,
                          width: 1,
                        ),
                      ),
                      child: Icon(
                        Icons.lock_rounded,
                        size: 9,
                        color: colorScheme.onSurfaceVariant,
                      ),
                    ),
                  ),
              ],
            ),
          ),
          const SizedBox(height: SpacingTokens.xxs),
          Text(
            definition.name,
            style: theme.textTheme.labelSmall?.copyWith(
              color: isEarned
                  ? colorScheme.onSurface
                  : colorScheme.onSurfaceVariant.withValues(alpha: 0.6),
              fontWeight: isEarned ? FontWeight.w600 : FontWeight.w400,
            ),
            maxLines: 2,
            overflow: TextOverflow.ellipsis,
            textAlign: TextAlign.center,
          ),
        ],
      ),
    );
  }

  Color _categoryColor(BadgeCategory cat) {
    switch (cat) {
      case BadgeCategory.streak:
        return const Color(0xFFFF6D00);
      case BadgeCategory.mastery:
        return const Color(0xFF1565C0);
      case BadgeCategory.engagement:
        return const Color(0xFF2E7D32);
      case BadgeCategory.special:
        return const Color(0xFF6A1B9A);
    }
  }

  IconData _iconData(String name) {
    const map = {
      'local_fire_department': Icons.local_fire_department_rounded,
      'whatshot': Icons.whatshot_rounded,
      'star': Icons.star_rounded,
      'school': Icons.school_rounded,
      'explore': Icons.explore_rounded,
      'psychology': Icons.psychology_rounded,
      'play_circle': Icons.play_circle_rounded,
      'emoji_events': Icons.emoji_events_rounded,
      'hub': Icons.hub_rounded,
      'military_tech': Icons.military_tech_rounded,
    };
    return map[name] ?? Icons.emoji_events_rounded;
  }
}

// ---------------------------------------------------------------------------
// Recent Achievements
// ---------------------------------------------------------------------------

class _RecentAchievements extends ConsumerWidget {
  const _RecentAchievements();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final theme = Theme.of(context);
    final events = ref.watch(recentAchievementsProvider);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(
          'Recent Activity',
          style: theme.textTheme.titleMedium?.copyWith(
            fontWeight: FontWeight.w700,
          ),
        ),
        const SizedBox(height: SpacingTokens.sm),
        if (events.isEmpty)
          _EmptyActivityState()
        else
          ListView.separated(
            shrinkWrap: true,
            physics: const NeverScrollableScrollPhysics(),
            itemCount: events.length > 20 ? 20 : events.length,
            separatorBuilder: (_, __) =>
                const SizedBox(height: SpacingTokens.xs),
            itemBuilder: (context, index) =>
                _AchievementRow(event: events[index]),
          ),
      ],
    );
  }
}

class _EmptyActivityState extends StatelessWidget {
  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: SpacingTokens.lg),
      child: Center(
        child: Text(
          'Complete a session to see your achievements here.',
          style: theme.textTheme.bodyMedium?.copyWith(
            color: theme.colorScheme.onSurfaceVariant,
          ),
          textAlign: TextAlign.center,
        ),
      ),
    );
  }
}

class _AchievementRow extends StatelessWidget {
  const _AchievementRow({required this.event});

  final AchievementEvent event;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;
    final timeStr = _formatRelativeTime(event.timestamp);

    return Row(
      children: [
        Container(
          width: 36,
          height: 36,
          decoration: BoxDecoration(
            shape: BoxShape.circle,
            color: _eventColor(event.type).withValues(alpha: 0.12),
          ),
          child: Icon(
            _eventIcon(event.type),
            size: 18,
            color: _eventColor(event.type),
          ),
        ),
        const SizedBox(width: SpacingTokens.sm),
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(
                event.label,
                style: theme.textTheme.bodySmall?.copyWith(
                  fontWeight: FontWeight.w500,
                ),
              ),
              Text(
                timeStr,
                style: theme.textTheme.labelSmall?.copyWith(
                  color: colorScheme.onSurfaceVariant,
                ),
              ),
            ],
          ),
        ),
        if (event.xpDelta > 0)
          Text(
            '+${event.xpDelta} XP',
            style: theme.textTheme.labelMedium?.copyWith(
              color: const Color(0xFFFFAA00),
              fontWeight: FontWeight.w700,
            ),
          ),
      ],
    );
  }

  Color _eventColor(AchievementEventType type) {
    switch (type) {
      case AchievementEventType.correctAnswer:
        return const Color(0xFF2E7D32);
      case AchievementEventType.streakDay:
        return const Color(0xFFFF6D00);
      case AchievementEventType.conceptMastered:
        return const Color(0xFF1565C0);
      case AchievementEventType.badgeEarned:
        return const Color(0xFF6A1B9A);
      case AchievementEventType.levelUp:
        return const Color(0xFFFFAA00);
    }
  }

  IconData _eventIcon(AchievementEventType type) {
    switch (type) {
      case AchievementEventType.correctAnswer:
        return Icons.check_circle_rounded;
      case AchievementEventType.streakDay:
        return Icons.local_fire_department_rounded;
      case AchievementEventType.conceptMastered:
        return Icons.school_rounded;
      case AchievementEventType.badgeEarned:
        return Icons.emoji_events_rounded;
      case AchievementEventType.levelUp:
        return Icons.arrow_upward_rounded;
    }
  }

  String _formatRelativeTime(DateTime time) {
    final diff = DateTime.now().difference(time);
    if (diff.inSeconds < 60) return 'Just now';
    if (diff.inMinutes < 60) return '${diff.inMinutes}m ago';
    if (diff.inHours < 24) return '${diff.inHours}h ago';
    if (diff.inDays == 1) return 'Yesterday';
    return DateFormat.MMMd().format(time);
  }
}
