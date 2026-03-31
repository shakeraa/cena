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
import '../session/widgets/math_text.dart';

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
          return _SocialFeedCard(
            item: item,
            classId: classId,
            isUnder13: isUnder13,
          );
        },
      ),
    );
  }
}

// ---------------------------------------------------------------------------
// Individual feed card
// ---------------------------------------------------------------------------

class _SocialFeedCard extends ConsumerWidget {
  const _SocialFeedCard({
    required this.item,
    required this.classId,
    required this.isUnder13,
  });

  final SocialFeedItem item;
  final String classId;
  final bool isUnder13;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;
    final isEndorsement =
        item.eventType == SocialFeedEventType.teacherEndorsement;

    return Card(
      elevation: isEndorsement ? 2 : 0,
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(RadiusTokens.lg),
        side: isEndorsement
            ? BorderSide(color: Colors.amber.shade400, width: 1.5)
            : BorderSide(color: colorScheme.outlineVariant),
      ),
      child: Padding(
        padding: const EdgeInsets.all(SpacingTokens.md),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            // Header row
            Row(
              children: [
                _eventIcon(item.eventType, colorScheme, isEndorsement),
                const SizedBox(width: SpacingTokens.sm),
                Expanded(child: _buildMessage(theme, colorScheme)),
                if (isEndorsement) _buildAuthorityBadge(theme),
              ],
            ),
            const SizedBox(height: SpacingTokens.sm),

            // Timestamp
            Text(
              _formatTimestamp(item.timestamp),
              style: theme.textTheme.labelSmall?.copyWith(
                color: colorScheme.onSurfaceVariant.withValues(alpha: 0.5),
              ),
            ),
            const SizedBox(height: SpacingTokens.sm),

            // Reactions row
            _ReactionBar(
              item: item,
              classId: classId,
              isUnder13: isUnder13,
            ),
          ],
        ),
      ),
    );
  }

  Widget _buildMessage(ThemeData theme, ColorScheme colorScheme) {
    final name = item.studentDisplayName;
    final detail = item.detail;

    switch (item.eventType) {
      case SocialFeedEventType.conceptMastered:
        return _RichFeedText(
          segments: [
            _TextSegment(name, bold: true),
            const _TextSegment(' mastered '),
            _TextSegment(detail, bold: true),
            const _TextSegment('!'),
          ],
        );
      case SocialFeedEventType.badgeEarned:
        return _RichFeedText(
          segments: [
            _TextSegment(name, bold: true),
            const _TextSegment(' earned the '),
            _TextSegment(detail, bold: true),
            const _TextSegment(' badge!'),
          ],
        );
      case SocialFeedEventType.streakMilestone:
        return _RichFeedText(
          segments: [
            _TextSegment(name, bold: true),
            const _TextSegment(' hit a '),
            _TextSegment('$detail-day streak', bold: true),
            const _TextSegment('!'),
          ],
        );
      case SocialFeedEventType.teacherEndorsement:
        return _RichFeedText(
          segments: [
            const _TextSegment('Teacher endorsed '),
            _TextSegment(name, bold: true),
            const _TextSegment(': '),
            _TextSegment(detail),
          ],
        );
      case SocialFeedEventType.questCompleted:
        return _RichFeedText(
          segments: [
            _TextSegment(name, bold: true),
            const _TextSegment(' completed the '),
            _TextSegment(detail, bold: true),
            const _TextSegment(' quest!'),
          ],
        );
    }
  }

  Widget _buildAuthorityBadge(ThemeData theme) {
    return Container(
      padding: const EdgeInsets.symmetric(
        horizontal: SpacingTokens.sm,
        vertical: SpacingTokens.xxs,
      ),
      decoration: BoxDecoration(
        color: Colors.amber.shade100,
        borderRadius: BorderRadius.circular(RadiusTokens.full),
        border: Border.all(color: Colors.amber.shade300),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Icon(Icons.verified_rounded, size: 12, color: Colors.amber.shade700),
          const SizedBox(width: SpacingTokens.xxs),
          Text(
            'Teacher',
            style: theme.textTheme.labelSmall?.copyWith(
              fontWeight: FontWeight.w700,
              color: Colors.amber.shade800,
              fontSize: 10,
            ),
          ),
        ],
      ),
    );
  }

  Widget _eventIcon(
    SocialFeedEventType type,
    ColorScheme colorScheme,
    bool isEndorsement,
  ) {
    final IconData icon;
    final Color color;

    switch (type) {
      case SocialFeedEventType.conceptMastered:
        icon = Icons.school_rounded;
        color = const Color(0xFF4CAF50);
      case SocialFeedEventType.badgeEarned:
        icon = Icons.emoji_events_rounded;
        color = Colors.amber.shade700;
      case SocialFeedEventType.streakMilestone:
        icon = Icons.local_fire_department_rounded;
        color = const Color(0xFFFF5722);
      case SocialFeedEventType.teacherEndorsement:
        icon = Icons.verified_rounded;
        color = Colors.amber.shade700;
      case SocialFeedEventType.questCompleted:
        icon = Icons.flag_rounded;
        color = colorScheme.primary;
    }

    return CircleAvatar(
      radius: 16,
      backgroundColor: color.withValues(alpha: 0.1),
      child: Icon(icon, size: 18, color: color),
    );
  }

  String _formatTimestamp(DateTime timestamp) {
    final now = DateTime.now();
    final diff = now.difference(timestamp);

    if (diff.inSeconds < 60) return 'Just now';
    if (diff.inMinutes < 60) return '${diff.inMinutes}m ago';
    if (diff.inHours < 24) return '${diff.inHours}h ago';
    if (diff.inDays == 1) return 'Yesterday';
    return '${diff.inDays}d ago';
  }
}

// ---------------------------------------------------------------------------
// Rich text helper
// ---------------------------------------------------------------------------

class _TextSegment {
  const _TextSegment(this.text, {this.bold = false});
  final String text;
  final bool bold;
}

class _RichFeedText extends StatelessWidget {
  const _RichFeedText({required this.segments});
  final List<_TextSegment> segments;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return RichText(
      text: TextSpan(
        style: theme.textTheme.bodyMedium,
        children: segments
            .map((s) => TextSpan(
                  text: s.text,
                  style: s.bold
                      ? const TextStyle(fontWeight: FontWeight.w600)
                      : null,
                ))
            .toList(),
      ),
    );
  }
}

// ---------------------------------------------------------------------------
// Reaction bar — pre-set reactions for under-13 safety
// ---------------------------------------------------------------------------

class _ReactionBar extends ConsumerWidget {
  const _ReactionBar({
    required this.item,
    required this.classId,
    required this.isUnder13,
  });

  final SocialFeedItem item;
  final String classId;
  final bool isUnder13;

  static const _reactions = [
    _ReactionDef(type: 'thumbsUp', emoji: '\u{1F44D}', label: 'Like'),
    _ReactionDef(type: 'star', emoji: '\u{2B50}', label: 'Star'),
    _ReactionDef(type: 'clap', emoji: '\u{1F44F}', label: 'Clap'),
  ];

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;

    return Row(
      children: _reactions.map((reaction) {
        final count = item.reactions[reaction.type] ?? 0;
        return Padding(
          padding: const EdgeInsets.only(right: SpacingTokens.sm),
          child: InkWell(
            onTap: () {
              ref
                  .read(socialFeedProvider(classId).notifier)
                  .addReaction(item.id, reaction.type);
            },
            borderRadius: BorderRadius.circular(RadiusTokens.full),
            child: Container(
              padding: const EdgeInsets.symmetric(
                horizontal: SpacingTokens.sm,
                vertical: SpacingTokens.xxs,
              ),
              decoration: BoxDecoration(
                color: colorScheme.surfaceContainerHighest
                    .withValues(alpha: 0.5),
                borderRadius: BorderRadius.circular(RadiusTokens.full),
                border: Border.all(
                  color: colorScheme.outlineVariant.withValues(alpha: 0.5),
                ),
              ),
              child: Row(
                mainAxisSize: MainAxisSize.min,
                children: [
                  Text(reaction.emoji, style: const TextStyle(fontSize: 14)),
                  if (count > 0) ...[
                    const SizedBox(width: SpacingTokens.xxs),
                    Text(
                      '$count',
                      style: theme.textTheme.labelSmall?.copyWith(
                        color: colorScheme.onSurfaceVariant,
                        fontWeight: FontWeight.w600,
                      ),
                    ),
                  ],
                ],
              ),
            ),
          ),
        );
      }).toList(),
    );
  }
}

class _ReactionDef {
  const _ReactionDef({
    required this.type,
    required this.emoji,
    required this.label,
  });

  final String type;
  final String emoji;
  final String label;
}

// ---------------------------------------------------------------------------
// Daily Challenge Feed Card (MOB-VIS-016)
// ---------------------------------------------------------------------------

/// Feed card showing a "Challenge of the Day" MCQ that classmates can vote on.
/// Anonymous — no names shown, only aggregate percentages (Blueprint §6).
class DailyChallengeFeedCard extends StatefulWidget {
  const DailyChallengeFeedCard({
    super.key,
    required this.question,
    required this.options,
    this.classVotes,
    this.onVote,
  });

  /// Question text (may contain LaTeX).
  final String question;

  /// MCQ options (4 typically).
  final List<String> options;

  /// Aggregate class vote counts per option index. Null = not yet voted.
  final Map<int, int>? classVotes;

  /// Called when student votes for an option.
  final void Function(int optionIndex)? onVote;

  @override
  State<DailyChallengeFeedCard> createState() => _DailyChallengeFeedCardState();
}

class _DailyChallengeFeedCardState extends State<DailyChallengeFeedCard> {
  int? _selectedIndex;

  bool get _hasVoted => _selectedIndex != null || widget.classVotes != null;

  Map<int, int> get _votes => widget.classVotes ?? {};

  int get _totalVotes =>
      _votes.values.fold<int>(0, (sum, v) => sum + v);

  double _percent(int index) {
    final total = _totalVotes;
    if (total == 0) return 0.0;
    return (_votes[index] ?? 0) / total;
  }

  void _vote(int index) {
    if (_hasVoted) return;
    setState(() => _selectedIndex = index);
    widget.onVote?.call(index);
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;

    return Card(
      child: Padding(
        padding: const EdgeInsets.all(SpacingTokens.md),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            // Header
            Row(
              children: [
                Icon(Icons.emoji_events_rounded,
                    size: 20, color: Colors.amber.shade700),
                const SizedBox(width: SpacingTokens.sm),
                Expanded(
                  child: Text(
                    'Challenge of the Day',
                    style: theme.textTheme.titleMedium?.copyWith(
                      fontWeight: FontWeight.w700,
                      color: Colors.amber.shade800,
                    ),
                  ),
                ),
              ],
            ),
            const SizedBox(height: SpacingTokens.md),

            // Question (supports LaTeX)
            MathText(content: widget.question),
            const SizedBox(height: SpacingTokens.md),

            // Options
            ...List.generate(widget.options.length, (i) {
              if (_hasVoted) {
                return _buildVotedOption(i, theme, colorScheme);
              }
              return _buildVotableOption(i, theme, colorScheme);
            }),

            // Vote count footer
            if (_hasVoted && _totalVotes > 0) ...[
              const SizedBox(height: SpacingTokens.sm),
              Text(
                '$_totalVotes classmates voted',
                style: theme.textTheme.labelSmall?.copyWith(
                  color: colorScheme.onSurfaceVariant,
                ),
              ),
            ],
          ],
        ),
      ),
    );
  }

  /// Option chip before voting — tappable, no percentages.
  Widget _buildVotableOption(
    int index,
    ThemeData theme,
    ColorScheme colorScheme,
  ) {
    return Padding(
      padding: const EdgeInsets.only(bottom: SpacingTokens.sm),
      child: InkWell(
        onTap: () => _vote(index),
        borderRadius: BorderRadius.circular(RadiusTokens.md),
        child: Container(
          width: double.infinity,
          padding: const EdgeInsets.symmetric(
            horizontal: SpacingTokens.md,
            vertical: SpacingTokens.sm + 2,
          ),
          decoration: BoxDecoration(
            border: Border.all(
              color: colorScheme.outlineVariant,
            ),
            borderRadius: BorderRadius.circular(RadiusTokens.md),
          ),
          child: MathText(
            content: widget.options[index],
            textStyle: theme.textTheme.bodyMedium,
          ),
        ),
      ),
    );
  }

  /// Option chip after voting — shows percentage bar.
  Widget _buildVotedOption(
    int index,
    ThemeData theme,
    ColorScheme colorScheme,
  ) {
    final pct = _percent(index);
    final isSelected = _selectedIndex == index;
    final barColor = isSelected
        ? Colors.amber.shade600
        : colorScheme.surfaceContainerHighest;

    return Padding(
      padding: const EdgeInsets.only(bottom: SpacingTokens.sm),
      child: ClipRRect(
        borderRadius: BorderRadius.circular(RadiusTokens.md),
        child: Stack(
          children: [
            // Percentage bar background
            Container(
              width: double.infinity,
              padding: const EdgeInsets.symmetric(
                horizontal: SpacingTokens.md,
                vertical: SpacingTokens.sm + 2,
              ),
              decoration: BoxDecoration(
                border: Border.all(
                  color: isSelected
                      ? Colors.amber.shade600
                      : colorScheme.outlineVariant,
                  width: isSelected ? 1.5 : 1.0,
                ),
                borderRadius: BorderRadius.circular(RadiusTokens.md),
              ),
              child: Row(
                children: [
                  Expanded(
                    child: MathText(
                      content: widget.options[index],
                      textStyle: theme.textTheme.bodyMedium?.copyWith(
                        fontWeight:
                            isSelected ? FontWeight.w600 : FontWeight.normal,
                      ),
                    ),
                  ),
                  const SizedBox(width: SpacingTokens.sm),
                  Text(
                    '${(pct * 100).round()}%',
                    style: theme.textTheme.labelMedium?.copyWith(
                      fontWeight: FontWeight.w700,
                      color: isSelected
                          ? Colors.amber.shade800
                          : colorScheme.onSurfaceVariant,
                    ),
                  ),
                ],
              ),
            ),
            // Percentage fill bar
            Positioned.fill(
              child: FractionallySizedBox(
                alignment: AlignmentDirectional.centerStart,
                widthFactor: pct,
                child: Container(
                  decoration: BoxDecoration(
                    color: barColor.withValues(alpha: 0.15),
                    borderRadius: BorderRadius.circular(RadiusTokens.md),
                  ),
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }
}

// ---------------------------------------------------------------------------
// Study Groups Widget
// ---------------------------------------------------------------------------

class StudyGroupsList extends ConsumerWidget {
  const StudyGroupsList({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;
    final groups = ref.watch(studyGroupsProvider);

    return Card(
      child: Padding(
        padding: const EdgeInsets.all(SpacingTokens.md),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Icon(Icons.group_work_rounded,
                    size: 20, color: colorScheme.tertiary),
                const SizedBox(width: SpacingTokens.sm),
                Text('Study Groups',
                    style: theme.textTheme.titleMedium
                        ?.copyWith(fontWeight: FontWeight.w700)),
              ],
            ),
            const SizedBox(height: SpacingTokens.sm),
            groups.when(
              loading: () => const Center(child: CircularProgressIndicator()),
              error: (_, __) => Text('Could not load groups',
                  style: theme.textTheme.bodySmall),
              data: (list) {
                if (list.isEmpty) {
                  return Text('No study groups available yet',
                      style: theme.textTheme.bodySmall
                          ?.copyWith(color: colorScheme.onSurfaceVariant));
                }
                return Column(
                  children: list
                      .map((g) => ListTile(
                            dense: true,
                            leading: CircleAvatar(
                              radius: 16,
                              backgroundColor: colorScheme.tertiaryContainer,
                              child: Text(g.name.isNotEmpty ? g.name[0] : '?',
                                  style: theme.textTheme.labelSmall),
                            ),
                            title: Text(g.name),
                            subtitle: Text('${g.memberCount} members'),
                            trailing: g.isJoined
                                ? const Chip(label: Text('Joined'))
                                : OutlinedButton(
                                    onPressed: () {},
                                    child: const Text('Join'),
                                  ),
                          ))
                      .toList(),
                );
              },
            ),
          ],
        ),
      ),
    );
  }
}
