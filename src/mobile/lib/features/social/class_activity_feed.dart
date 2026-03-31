// =============================================================================
// Cena — Class Activity Feed (MOB-SOC-001 + MOB-044)
// =============================================================================
// Blueprint §10: Social Proof Without Shame — aggregate stats only,
// no named leaderboards, opt-in social features.
// Enhanced: card-based feed, activity counters with k-anonymity,
// teacher endorsement cards, pull-to-refresh, lazy loading,
// pre-set reactions for under-13, opt-in toggle.
// =============================================================================

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/config/app_config.dart';
import '../../core/state/app_state.dart';
import '../../core/state/social_feed_state.dart';
import 'widgets/social_feed_card.dart';

export 'daily_challenge_card.dart'
    show DailyChallengeFeedCard, StudyGroupsList;

// ---------------------------------------------------------------------------
// Activity feed item (legacy — kept for backward compatibility)
// ---------------------------------------------------------------------------

class ActivityItem {
  const ActivityItem({
    required this.message,
    required this.timestamp,
    this.icon = Icons.people_rounded,
  });

  final String message;
  final DateTime timestamp;
  final IconData icon;

  factory ActivityItem.fromJson(Map<String, dynamic> json) => ActivityItem(
        message: json['message'] as String? ?? '',
        timestamp: json['timestamp'] != null
            ? DateTime.parse(json['timestamp'] as String)
            : DateTime.now(),
      );
}

// ---------------------------------------------------------------------------
// Study group
// ---------------------------------------------------------------------------

class StudyGroup {
  const StudyGroup({
    required this.id,
    required this.name,
    required this.memberCount,
    this.isJoined = false,
  });

  final String id;
  final String name;
  final int memberCount;
  final bool isJoined;

  factory StudyGroup.fromJson(Map<String, dynamic> json) => StudyGroup(
        id: json['id'] as String? ?? '',
        name: json['name'] as String? ?? '',
        memberCount: (json['memberCount'] as num?)?.toInt() ?? 0,
        isJoined: json['isJoined'] as bool? ?? false,
      );
}

// ---------------------------------------------------------------------------
// Providers
// ---------------------------------------------------------------------------

final classActivityProvider =
    FutureProvider.autoDispose<List<ActivityItem>>((ref) async {
  try {
    final api = ref.watch(apiClientProvider);
    final response = await api.get<Map<String, dynamic>>('/social/activity');
    final items = response.data?['items'] as List<dynamic>? ?? [];
    return items
        .map((e) => ActivityItem.fromJson(e as Map<String, dynamic>))
        .toList();
  } catch (_) {
    return [];
  }
});

final studyGroupsProvider =
    FutureProvider.autoDispose<List<StudyGroup>>((ref) async {
  try {
    final api = ref.watch(apiClientProvider);
    final response = await api.get<Map<String, dynamic>>('/social/groups');
    final items = response.data?['groups'] as List<dynamic>? ?? [];
    return items
        .map((e) => StudyGroup.fromJson(e as Map<String, dynamic>))
        .toList();
  } catch (_) {
    return [];
  }
});

// ---------------------------------------------------------------------------
// Enhanced Class Activity Feed Widget (MOB-044)
// ---------------------------------------------------------------------------

/// Full-featured social feed with card-based items, activity counters,
/// teacher endorsement highlighting, pull-to-refresh, lazy loading,
/// pre-set reactions for under-13, and opt-in toggle.
class ClassActivityFeed extends ConsumerWidget {
  const ClassActivityFeed({
    super.key,
    required this.classId,
    this.isUnder13 = false,
  });

  final String classId;

  /// When true, only pre-set reactions are shown (no text input).
  final bool isUnder13;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;
    final isEnabled = ref.watch(socialFeedEnabledProvider);
    final feedState = ref.watch(socialFeedProvider(classId));

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        // Opt-in toggle
        _FeedOptInToggle(isEnabled: isEnabled),
        if (!isEnabled) ...[
          const SizedBox(height: SpacingTokens.md),
          _buildDisabledState(theme, colorScheme),
        ] else ...[
          // Aggregate activity counter (k-anonymity gated)
          if (feedState.canShowAggregateCount)
            _ActivityCounter(count: feedState.activeStudentCount),
          const SizedBox(height: SpacingTokens.sm),
          // Feed content
          _FeedContent(
            classId: classId,
            feedState: feedState,
            isUnder13: isUnder13,
          ),
        ],
      ],
    );
  }

  Widget _buildDisabledState(ThemeData theme, ColorScheme colorScheme) {
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(SpacingTokens.lg),
        child: Center(
          child: Column(
            children: [
              Icon(
                Icons.visibility_off_rounded,
                size: 36,
                color: colorScheme.onSurfaceVariant.withValues(alpha: 0.3),
              ),
              const SizedBox(height: SpacingTokens.sm),
              Text(
                'Class feed is hidden',
                style: theme.textTheme.bodyMedium?.copyWith(
                  color: colorScheme.onSurfaceVariant,
                ),
              ),
              const SizedBox(height: SpacingTokens.xs),
              Text(
                'Toggle above to see what your classmates are achieving',
                style: theme.textTheme.bodySmall?.copyWith(
                  color: colorScheme.onSurfaceVariant.withValues(alpha: 0.6),
                ),
                textAlign: TextAlign.center,
              ),
            ],
          ),
        ),
      ),
    );
  }
}

// ---------------------------------------------------------------------------
// Opt-in toggle
// ---------------------------------------------------------------------------

class _FeedOptInToggle extends ConsumerWidget {
  const _FeedOptInToggle({required this.isEnabled});

  final bool isEnabled;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;

    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: SpacingTokens.xs),
      child: Row(
        children: [
          Icon(Icons.groups_rounded, size: 20, color: colorScheme.primary),
          const SizedBox(width: SpacingTokens.sm),
          Expanded(
            child: Text(
              'Class Activity',
              style: theme.textTheme.titleMedium?.copyWith(
                fontWeight: FontWeight.w700,
              ),
            ),
          ),
          Switch.adaptive(
            value: isEnabled,
            onChanged: (value) {
              ref.read(socialFeedEnabledProvider.notifier).state = value;
            },
          ),
        ],
      ),
    );
  }
}

// ---------------------------------------------------------------------------
// Aggregate activity counter (k-anonymity)
// ---------------------------------------------------------------------------

class _ActivityCounter extends StatelessWidget {
  const _ActivityCounter({required this.count});

  final int count;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;

    return Card(
      color: colorScheme.primaryContainer.withValues(alpha: 0.3),
      elevation: 0,
      child: Padding(
        padding: const EdgeInsets.symmetric(
          horizontal: SpacingTokens.md,
          vertical: SpacingTokens.sm,
        ),
        child: Row(
          children: [
            Icon(Icons.trending_up_rounded,
                size: 18, color: colorScheme.primary),
            const SizedBox(width: SpacingTokens.sm),
            Text(
              '$count students practiced today',
              style: theme.textTheme.labelLarge?.copyWith(
                fontWeight: FontWeight.w600,
                color: colorScheme.primary,
              ),
            ),
          ],
        ),
      ),
    );
  }
}

// ---------------------------------------------------------------------------
// Feed content with pull-to-refresh and lazy loading
// ---------------------------------------------------------------------------

class _FeedContent extends ConsumerWidget {
  const _FeedContent({
    required this.classId,
    required this.feedState,
    required this.isUnder13,
  });

  final String classId;
  final SocialFeedState feedState;
  final bool isUnder13;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;

    if (feedState.isLoading && feedState.items.isEmpty) {
      return const Card(
        child: Padding(
          padding: EdgeInsets.all(SpacingTokens.lg),
          child: Center(child: CircularProgressIndicator()),
        ),
      );
    }

    if (feedState.error != null && feedState.items.isEmpty) {
      return Card(
        child: Padding(
          padding: const EdgeInsets.all(SpacingTokens.lg),
          child: Center(
            child: Text(
              feedState.error!,
              style: theme.textTheme.bodySmall?.copyWith(
                color: colorScheme.error,
              ),
            ),
          ),
        ),
      );
    }

    if (feedState.items.isEmpty) {
      return Card(
        child: Padding(
          padding: const EdgeInsets.all(SpacingTokens.lg),
          child: Center(
            child: Text(
              'No class activity yet',
              style: theme.textTheme.bodySmall?.copyWith(
                color: colorScheme.onSurfaceVariant,
              ),
            ),
          ),
        ),
      );
    }

    return RefreshIndicator(
      onRefresh: () =>
          ref.read(socialFeedProvider(classId).notifier).refresh(),
      child: ListView.separated(
        shrinkWrap: true,
        physics: const AlwaysScrollableScrollPhysics(),
        itemCount: feedState.items.length,
        separatorBuilder: (_, __) =>
            const SizedBox(height: SpacingTokens.sm),
        itemBuilder: (context, index) {
          final item = feedState.items[index];
          return SocialFeedCard(
            item: item,
            classId: classId,
            isUnder13: isUnder13,
          );
        },
      ),
    );
  }
}

