// =============================================================================
// Cena — Social Feed Card & Reactions (MOB-044)
// =============================================================================
// Individual feed card widget with event-type icons, teacher endorsement
// badge, rich text messages, timestamps, and pre-set reaction buttons.
// =============================================================================

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/config/app_config.dart';
import '../../../core/state/social_feed_state.dart';

// ---------------------------------------------------------------------------
// Feed card
// ---------------------------------------------------------------------------

class SocialFeedCard extends ConsumerWidget {
  const SocialFeedCard({
    super.key,
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
            Row(
              children: [
                _eventIcon(item.eventType, colorScheme),
                const SizedBox(width: SpacingTokens.sm),
                Expanded(child: _buildMessage(theme)),
                if (isEndorsement) _buildAuthorityBadge(theme),
              ],
            ),
            const SizedBox(height: SpacingTokens.sm),
            Text(
              _formatTimestamp(item.timestamp),
              style: theme.textTheme.labelSmall?.copyWith(
                color: colorScheme.onSurfaceVariant.withValues(alpha: 0.5),
              ),
            ),
            const SizedBox(height: SpacingTokens.sm),
            ReactionBar(item: item, classId: classId, isUnder13: isUnder13),
          ],
        ),
      ),
    );
  }

  Widget _buildMessage(ThemeData theme) {
    final name = item.studentDisplayName;
    final detail = item.detail;
    switch (item.eventType) {
      case SocialFeedEventType.conceptMastered:
        return _rich(theme, [
          _Seg(name, bold: true), const _Seg(' mastered '),
          _Seg(detail, bold: true), const _Seg('!'),
        ]);
      case SocialFeedEventType.badgeEarned:
        return _rich(theme, [
          _Seg(name, bold: true), const _Seg(' earned the '),
          _Seg(detail, bold: true), const _Seg(' badge!'),
        ]);
      case SocialFeedEventType.streakMilestone:
        return _rich(theme, [
          _Seg(name, bold: true), const _Seg(' hit a '),
          _Seg('$detail-day streak', bold: true), const _Seg('!'),
        ]);
      case SocialFeedEventType.teacherEndorsement:
        return _rich(theme, [
          const _Seg('Teacher endorsed '), _Seg(name, bold: true),
          const _Seg(': '), _Seg(detail),
        ]);
      case SocialFeedEventType.questCompleted:
        return _rich(theme, [
          _Seg(name, bold: true), const _Seg(' completed the '),
          _Seg(detail, bold: true), const _Seg(' quest!'),
        ]);
    }
  }

  Widget _rich(ThemeData theme, List<_Seg> segs) {
    return RichText(
      text: TextSpan(
        style: theme.textTheme.bodyMedium,
        children: segs.map((s) => TextSpan(
          text: s.text,
          style: s.bold ? const TextStyle(fontWeight: FontWeight.w600) : null,
        )).toList(),
      ),
    );
  }

  Widget _buildAuthorityBadge(ThemeData theme) {
    return Container(
      padding: const EdgeInsets.symmetric(
          horizontal: SpacingTokens.sm, vertical: SpacingTokens.xxs),
      decoration: BoxDecoration(
        color: Colors.amber.shade100,
        borderRadius: BorderRadius.circular(RadiusTokens.full),
        border: Border.all(color: Colors.amber.shade300),
      ),
      child: Row(mainAxisSize: MainAxisSize.min, children: [
        Icon(Icons.verified_rounded, size: 12, color: Colors.amber.shade700),
        const SizedBox(width: SpacingTokens.xxs),
        Text('Teacher',
            style: theme.textTheme.labelSmall?.copyWith(
                fontWeight: FontWeight.w700,
                color: Colors.amber.shade800,
                fontSize: 10)),
      ]),
    );
  }

  Widget _eventIcon(SocialFeedEventType type, ColorScheme colorScheme) {
    final (IconData icon, Color color) = switch (type) {
      SocialFeedEventType.conceptMastered =>
        (Icons.school_rounded, const Color(0xFF4CAF50)),
      SocialFeedEventType.badgeEarned =>
        (Icons.emoji_events_rounded, Colors.amber.shade700),
      SocialFeedEventType.streakMilestone =>
        (Icons.local_fire_department_rounded, const Color(0xFFFF5722)),
      SocialFeedEventType.teacherEndorsement =>
        (Icons.verified_rounded, Colors.amber.shade700),
      SocialFeedEventType.questCompleted =>
        (Icons.flag_rounded, colorScheme.primary),
    };
    return CircleAvatar(
      radius: 16,
      backgroundColor: color.withValues(alpha: 0.1),
      child: Icon(icon, size: 18, color: color),
    );
  }

  String _formatTimestamp(DateTime timestamp) {
    final diff = DateTime.now().difference(timestamp);
    if (diff.inSeconds < 60) return 'Just now';
    if (diff.inMinutes < 60) return '${diff.inMinutes}m ago';
    if (diff.inHours < 24) return '${diff.inHours}h ago';
    if (diff.inDays == 1) return 'Yesterday';
    return '${diff.inDays}d ago';
  }
}

class _Seg {
  const _Seg(this.text, {this.bold = false});
  final String text;
  final bool bold;
}

// ---------------------------------------------------------------------------
// Reaction bar -- pre-set reactions for under-13 safety
// ---------------------------------------------------------------------------

class ReactionBar extends ConsumerWidget {
  const ReactionBar({
    super.key,
    required this.item,
    required this.classId,
    required this.isUnder13,
  });

  final SocialFeedItem item;
  final String classId;
  final bool isUnder13;

  static const _reactions = [
    ('thumbsUp', '\u{1F44D}'),
    ('star', '\u{2B50}'),
    ('clap', '\u{1F44F}'),
  ];

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;

    return Row(
      children: _reactions.map((r) {
        final (type, emoji) = r;
        final count = item.reactions[type] ?? 0;
        return Padding(
          padding: const EdgeInsets.only(right: SpacingTokens.sm),
          child: InkWell(
            onTap: () => ref
                .read(socialFeedProvider(classId).notifier)
                .addReaction(item.id, type),
            borderRadius: BorderRadius.circular(RadiusTokens.full),
            child: Container(
              padding: const EdgeInsets.symmetric(
                  horizontal: SpacingTokens.sm, vertical: SpacingTokens.xxs),
              decoration: BoxDecoration(
                color: colorScheme.surfaceContainerHighest
                    .withValues(alpha: 0.5),
                borderRadius: BorderRadius.circular(RadiusTokens.full),
                border: Border.all(
                    color: colorScheme.outlineVariant.withValues(alpha: 0.5)),
              ),
              child: Row(mainAxisSize: MainAxisSize.min, children: [
                Text(emoji, style: const TextStyle(fontSize: 14)),
                if (count > 0) ...[
                  const SizedBox(width: SpacingTokens.xxs),
                  Text('$count',
                      style: theme.textTheme.labelSmall?.copyWith(
                          color: colorScheme.onSurfaceVariant,
                          fontWeight: FontWeight.w600)),
                ],
              ]),
            ),
          ),
        );
      }).toList(),
    );
  }
}
