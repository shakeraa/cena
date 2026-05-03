// =============================================================================
// Cena Adaptive Learning Platform — Badge Showcase Widget
// =============================================================================
//
// Profile screen badge display with:
//   - Grid layout grouped by category
//   - Pin up to 3 badges for public display
//   - Badge detail view with criteria, date earned, rarity percentage
//   - Silhouettes for unearned badges (discovery motivation)
//   - Rarity tier colors: common=gray, uncommon=green, rare=blue,
//     epic=purple, secret=gold
// =============================================================================

import 'package:flutter/material.dart' hide Badge;
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intl/intl.dart';

import '../../../core/config/app_config.dart';
import '../../../core/models/domain_models.dart';
import '../../../core/state/gamification_state.dart';

// ---------------------------------------------------------------------------
// Pinned Badges Provider
// ---------------------------------------------------------------------------

/// Up to 3 badge IDs the student has pinned for public display.
final pinnedBadgeIdsProvider = StateProvider<List<String>>((ref) => []);

// ---------------------------------------------------------------------------
// Badge Showcase Widget
// ---------------------------------------------------------------------------

/// Full badge showcase for the profile screen.
///
/// Displays all badges in a grid grouped by category, with rarity-tier
/// coloring and silhouettes for unearned badges.
class BadgeShowcase extends ConsumerWidget {
  const BadgeShowcase({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final catalogue = ref.watch(badgeCatalogueProvider);
    final earned = ref.watch(badgesProvider);
    final earnedIds = {for (final b in earned) b.id: b};
    final pinned = ref.watch(pinnedBadgeIdsProvider);
    final theme = Theme.of(context);

    // Group by category.
    final grouped = <BadgeCategory, List<BadgeDefinition>>{};
    for (final def in catalogue) {
      grouped.putIfAbsent(def.category, () => []).add(def);
    }

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        // Pinned badges section
        if (pinned.isNotEmpty) ...[
          Text(
            'Pinned Badges',
            style: theme.textTheme.titleMedium?.copyWith(
              fontWeight: FontWeight.w700,
            ),
          ),
          const SizedBox(height: SpacingTokens.sm),
          Row(
            children: pinned.map((id) {
              final def = catalogue.where((d) => d.id == id).firstOrNull;
              if (def == null) return const SizedBox.shrink();
              final badge = earnedIds[id];
              return Padding(
                padding:
                    const EdgeInsets.only(right: SpacingTokens.sm),
                child: _BadgeShowcaseCell(
                  definition: def,
                  earnedBadge: badge,
                  isPinned: true,
                  onTap: () =>
                      _showBadgeShowcaseDetail(context, ref, def, badge),
                ),
              );
            }).toList(),
          ),
          const SizedBox(height: SpacingTokens.lg),
        ],

        // All badges by category
        for (final category in BadgeCategory.values) ...[
          if (grouped.containsKey(category)) ...[
            _CategoryHeader(
              category: category,
              earned: grouped[category]!
                  .where((d) => earnedIds.containsKey(d.id))
                  .length,
              total: grouped[category]!.length,
            ),
            const SizedBox(height: SpacingTokens.sm),
            GridView.builder(
              shrinkWrap: true,
              physics: const NeverScrollableScrollPhysics(),
              gridDelegate:
                  const SliverGridDelegateWithFixedCrossAxisCount(
                crossAxisCount: 4,
                mainAxisSpacing: SpacingTokens.sm,
                crossAxisSpacing: SpacingTokens.sm,
                childAspectRatio: 0.78,
              ),
              itemCount: grouped[category]!.length,
              itemBuilder: (context, index) {
                final def = grouped[category]![index];
                final badge = earnedIds[def.id];
                return _BadgeShowcaseCell(
                  definition: def,
                  earnedBadge: badge,
                  isPinned: pinned.contains(def.id),
                  onTap: () => _showBadgeShowcaseDetail(
                      context, ref, def, badge),
                );
              },
            ),
            const SizedBox(height: SpacingTokens.md),
          ],
        ],
      ],
    );
  }

  void _showBadgeShowcaseDetail(
    BuildContext context,
    WidgetRef ref,
    BadgeDefinition definition,
    Badge? earnedBadge,
  ) {
    showDialog<void>(
      context: context,
      builder: (_) => _BadgeShowcaseDetailDialog(
        definition: definition,
        earnedBadge: earnedBadge,
        onPin: earnedBadge != null
            ? () {
                final pinned =
                    ref.read(pinnedBadgeIdsProvider);
                final notifier =
                    ref.read(pinnedBadgeIdsProvider.notifier);
                if (pinned.contains(definition.id)) {
                  notifier.state = pinned
                      .where((id) => id != definition.id)
                      .toList();
                } else if (pinned.length < 3) {
                  notifier.state = [...pinned, definition.id];
                }
              }
            : null,
        isPinned: ref
            .read(pinnedBadgeIdsProvider)
            .contains(definition.id),
      ),
    );
  }
}

// ---------------------------------------------------------------------------
// Category Header
// ---------------------------------------------------------------------------

class _CategoryHeader extends StatelessWidget {
  const _CategoryHeader({
    required this.category,
    required this.earned,
    required this.total,
  });

  final BadgeCategory category;
  final int earned;
  final int total;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Row(
      children: [
        Container(
          width: 4,
          height: 20,
          decoration: BoxDecoration(
            color: _categoryColor(category),
            borderRadius: BorderRadius.circular(2),
          ),
        ),
        const SizedBox(width: SpacingTokens.sm),
        Text(
          _categoryLabel(category),
          style: theme.textTheme.titleSmall?.copyWith(
            fontWeight: FontWeight.w700,
          ),
        ),
        const Spacer(),
        Text(
          '$earned / $total',
          style: theme.textTheme.labelMedium?.copyWith(
            color: theme.colorScheme.onSurfaceVariant,
            fontWeight: FontWeight.w600,
          ),
        ),
      ],
    );
  }

  String _categoryLabel(BadgeCategory cat) {
    switch (cat) {
      case BadgeCategory.streak:
        return 'Learning Behavior';
      case BadgeCategory.mastery:
        return 'Subject Mastery';
      case BadgeCategory.engagement:
        return 'Social';
      case BadgeCategory.special:
        return 'Meta & Hidden';
    }
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
}

// ---------------------------------------------------------------------------
// Badge Cell for Showcase
// ---------------------------------------------------------------------------

class _BadgeShowcaseCell extends StatelessWidget {
  const _BadgeShowcaseCell({
    required this.definition,
    required this.earnedBadge,
    required this.isPinned,
    required this.onTap,
  });

  final BadgeDefinition definition;
  final Badge? earnedBadge;
  final bool isPinned;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;
    final isEarned = earnedBadge != null;
    final rarityColor = _rarityColor(definition.rarity);

    return GestureDetector(
      onTap: onTap,
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          Stack(
            clipBehavior: Clip.none,
            children: [
              Container(
                width: 54,
                height: 54,
                decoration: BoxDecoration(
                  shape: BoxShape.circle,
                  color: isEarned
                      ? rarityColor.withValues(alpha: 0.15)
                      : colorScheme.surfaceContainerHighest,
                  border: Border.all(
                    color: isEarned
                        ? rarityColor
                        : colorScheme.outline.withValues(alpha: 0.2),
                    width: isEarned ? 2.5 : 1,
                  ),
                  boxShadow: isEarned
                      ? [
                          BoxShadow(
                            color: rarityColor.withValues(alpha: 0.25),
                            blurRadius: 8,
                            spreadRadius: 1,
                          ),
                        ]
                      : null,
                ),
                child: Center(
                  child: isEarned
                      ? Icon(
                          _iconData(definition.icon),
                          size: 26,
                          color: rarityColor,
                        )
                      : Icon(
                          _iconData(definition.icon),
                          size: 26,
                          color: colorScheme.onSurfaceVariant
                              .withValues(alpha: 0.15),
                        ),
                ),
              ),
              // Pin indicator
              if (isPinned)
                Positioned(
                  top: -4,
                  right: -4,
                  child: Container(
                    width: 18,
                    height: 18,
                    decoration: BoxDecoration(
                      shape: BoxShape.circle,
                      color: colorScheme.primary,
                      border: Border.all(
                        color: colorScheme.surface,
                        width: 2,
                      ),
                    ),
                    child: const Icon(
                      Icons.push_pin_rounded,
                      size: 10,
                      color: Colors.white,
                    ),
                  ),
                ),
              // Lock icon for unearned
              if (!isEarned &&
                  definition.rarity != BadgeRarity.secret)
                Positioned(
                  bottom: 2,
                  right: 2,
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
              // Question mark for secret unearned badges
              if (!isEarned &&
                  definition.rarity == BadgeRarity.secret)
                Positioned(
                  bottom: 2,
                  right: 2,
                  child: Container(
                    width: 16,
                    height: 16,
                    decoration: BoxDecoration(
                      shape: BoxShape.circle,
                      color: const Color(0xFFFFD700).withValues(alpha: 0.2),
                      border: Border.all(
                        color: const Color(0xFFFFD700).withValues(alpha: 0.5),
                        width: 1,
                      ),
                    ),
                    child: const Icon(
                      Icons.question_mark_rounded,
                      size: 9,
                      color: Color(0xFFFFD700),
                    ),
                  ),
                ),
            ],
          ),
          const SizedBox(height: SpacingTokens.xxs),
          Text(
            isEarned || definition.rarity != BadgeRarity.secret
                ? definition.name
                : '???',
            style: theme.textTheme.labelSmall?.copyWith(
              color: isEarned
                  ? colorScheme.onSurface
                  : colorScheme.onSurfaceVariant.withValues(alpha: 0.5),
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

  Color _rarityColor(BadgeRarity rarity) {
    switch (rarity) {
      case BadgeRarity.common:
        return const Color(0xFF9E9E9E); // gray
      case BadgeRarity.uncommon:
        return const Color(0xFF4CAF50); // green
      case BadgeRarity.rare:
        return const Color(0xFF2196F3); // blue
      case BadgeRarity.epic:
        return const Color(0xFF9C27B0); // purple
      case BadgeRarity.secret:
        return const Color(0xFFFFD700); // gold
    }
  }

  IconData _iconData(String name) {
    return badgeIconMap[name] ?? Icons.emoji_events_rounded;
  }
}

// ---------------------------------------------------------------------------
// Badge Showcase Detail Dialog
// ---------------------------------------------------------------------------

class _BadgeShowcaseDetailDialog extends StatelessWidget {
  const _BadgeShowcaseDetailDialog({
    required this.definition,
    this.earnedBadge,
    this.onPin,
    this.isPinned = false,
  });

  final BadgeDefinition definition;
  final Badge? earnedBadge;
  final VoidCallback? onPin;
  final bool isPinned;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;
    final isEarned = earnedBadge != null;
    final rarityColor = _rarityColor(definition.rarity);

    return Dialog(
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(RadiusTokens.xl),
      ),
      child: Padding(
        padding: const EdgeInsets.all(SpacingTokens.lg),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            // Badge icon with rarity glow
            Container(
              width: 88,
              height: 88,
              decoration: BoxDecoration(
                shape: BoxShape.circle,
                color: isEarned
                    ? rarityColor.withValues(alpha: 0.12)
                    : colorScheme.surfaceContainerHighest,
                border: Border.all(
                  color: isEarned ? rarityColor : colorScheme.outline,
                  width: isEarned ? 3 : 1,
                ),
                boxShadow: isEarned
                    ? [
                        BoxShadow(
                          color: rarityColor.withValues(alpha: 0.35),
                          blurRadius: 20,
                          spreadRadius: 3,
                        ),
                      ]
                    : null,
              ),
              child: isEarned
                  ? Icon(
                      badgeIconMap[definition.icon] ??
                          Icons.emoji_events_rounded,
                      size: 44,
                      color: rarityColor,
                    )
                  : Icon(
                      badgeIconMap[definition.icon] ??
                          Icons.emoji_events_rounded,
                      size: 44,
                      color: colorScheme.onSurfaceVariant
                          .withValues(alpha: 0.2),
                    ),
            ),

            const SizedBox(height: SpacingTokens.md),

            // Badge name
            Text(
              definition.name,
              style: theme.textTheme.titleLarge?.copyWith(
                fontWeight: FontWeight.w700,
              ),
              textAlign: TextAlign.center,
            ),

            const SizedBox(height: SpacingTokens.xs),

            // Rarity chip
            Container(
              padding: const EdgeInsets.symmetric(
                horizontal: SpacingTokens.sm,
                vertical: SpacingTokens.xxs,
              ),
              decoration: BoxDecoration(
                color: rarityColor.withValues(alpha: 0.12),
                borderRadius: BorderRadius.circular(RadiusTokens.full),
              ),
              child: Text(
                _rarityLabel(definition.rarity),
                style: theme.textTheme.labelSmall?.copyWith(
                  color: rarityColor,
                  fontWeight: FontWeight.w700,
                ),
              ),
            ),

            const SizedBox(height: SpacingTokens.sm),

            // Description / criteria
            Text(
              definition.description,
              style: theme.textTheme.bodyMedium?.copyWith(
                color: colorScheme.onSurfaceVariant,
              ),
              textAlign: TextAlign.center,
            ),

            const SizedBox(height: SpacingTokens.md),

            // Earned date or locked status
            if (isEarned) ...[
              Row(
                mainAxisAlignment: MainAxisAlignment.center,
                children: [
                  Icon(
                    Icons.check_circle_rounded,
                    size: 18,
                    color: const Color(0xFF4CAF50),
                  ),
                  const SizedBox(width: SpacingTokens.xs),
                  Text(
                    'Earned ${DateFormat.yMMMd().format(earnedBadge!.earnedAt ?? DateTime.now())}',
                    style: theme.textTheme.labelLarge?.copyWith(
                      color: const Color(0xFF2E7D32),
                      fontWeight: FontWeight.w600,
                    ),
                  ),
                ],
              ),
            ] else ...[
              Row(
                mainAxisAlignment: MainAxisAlignment.center,
                children: [
                  Icon(
                    Icons.lock_outline_rounded,
                    size: 18,
                    color: colorScheme.onSurfaceVariant,
                  ),
                  const SizedBox(width: SpacingTokens.xs),
                  Text(
                    'Not yet earned',
                    style: theme.textTheme.labelLarge?.copyWith(
                      color: colorScheme.onSurfaceVariant,
                    ),
                  ),
                ],
              ),
            ],

            const SizedBox(height: SpacingTokens.lg),

            // Action buttons
            Row(
              children: [
                if (isEarned && onPin != null)
                  Expanded(
                    child: OutlinedButton.icon(
                      onPressed: () {
                        onPin!();
                        Navigator.of(context).pop();
                      },
                      icon: Icon(
                        isPinned
                            ? Icons.push_pin_rounded
                            : Icons.push_pin_outlined,
                        size: 18,
                      ),
                      label: Text(isPinned ? 'Unpin' : 'Pin'),
                    ),
                  ),
                if (isEarned && onPin != null)
                  const SizedBox(width: SpacingTokens.sm),
                Expanded(
                  child: FilledButton(
                    onPressed: () => Navigator.of(context).pop(),
                    child: const Text('Close'),
                  ),
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }

  Color _rarityColor(BadgeRarity rarity) {
    switch (rarity) {
      case BadgeRarity.common:
        return const Color(0xFF9E9E9E);
      case BadgeRarity.uncommon:
        return const Color(0xFF4CAF50);
      case BadgeRarity.rare:
        return const Color(0xFF2196F3);
      case BadgeRarity.epic:
        return const Color(0xFF9C27B0);
      case BadgeRarity.secret:
        return const Color(0xFFFFD700);
    }
  }

  String _rarityLabel(BadgeRarity rarity) {
    switch (rarity) {
      case BadgeRarity.common:
        return 'Common';
      case BadgeRarity.uncommon:
        return 'Uncommon';
      case BadgeRarity.rare:
        return 'Rare';
      case BadgeRarity.epic:
        return 'Epic';
      case BadgeRarity.secret:
        return 'Secret';
    }
  }
}

/// Shared icon map used by badge showcase and other badge widgets.
const badgeIconMap = <String, IconData>{
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
  'auto_awesome': Icons.auto_awesome_rounded,
  'trending_up': Icons.trending_up_rounded,
  'calendar_today': Icons.calendar_today_rounded,
  'science': Icons.science_rounded,
  'biotech': Icons.biotech_rounded,
  'calculate': Icons.calculate_rounded,
  'code': Icons.code_rounded,
  'functions': Icons.functions_rounded,
  'bolt': Icons.bolt_rounded,
  'speed': Icons.speed_rounded,
  'diversity_3': Icons.diversity_3_rounded,
  'groups': Icons.groups_rounded,
  'handshake': Icons.handshake_rounded,
  'volunteer_activism': Icons.volunteer_activism_rounded,
  'forum': Icons.forum_rounded,
  'support_agent': Icons.support_agent_rounded,
  'nightlight': Icons.nightlight_rounded,
  'percent': Icons.percent_rounded,
  'workspaces': Icons.workspaces_rounded,
  'rocket_launch': Icons.rocket_launch_rounded,
  'diamond': Icons.diamond_rounded,
  'psychology_alt': Icons.psychology_alt_rounded,
  'timer': Icons.timer_rounded,
  'verified': Icons.verified_rounded,
  'self_improvement': Icons.self_improvement_rounded,
  'favorite': Icons.favorite_rounded,
  'shield': Icons.shield_rounded,
};
