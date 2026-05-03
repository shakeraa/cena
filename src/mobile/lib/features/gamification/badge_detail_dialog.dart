// =============================================================================
// Cena Adaptive Learning Platform — Badge Detail Dialog
// =============================================================================

import 'package:flutter/material.dart' hide Badge;
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intl/intl.dart';

import '../../core/config/app_config.dart';
import '../../core/models/domain_models.dart';
import '../../core/state/gamification_state.dart';

/// Shows a badge detail dialog for [definition].
///
/// Pass the corresponding earned [Badge] if the student has earned it, or
/// null to show the locked/progress view.
void showBadgeDetail(
  BuildContext context, {
  required BadgeDefinition definition,
  Badge? earnedBadge,
  int currentProgress = 0,
}) {
  showDialog<void>(
    context: context,
    builder: (_) => BadgeDetailDialog(
      definition: definition,
      earnedBadge: earnedBadge,
      currentProgress: currentProgress,
    ),
  );
}

/// Modal dialog showing badge details: icon, name, description, earn date,
/// and progress bar for unearned badges.
class BadgeDetailDialog extends ConsumerWidget {
  const BadgeDetailDialog({
    super.key,
    required this.definition,
    this.earnedBadge,
    this.currentProgress = 0,
  });

  final BadgeDefinition definition;
  final Badge? earnedBadge;
  final int currentProgress;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;
    final isEarned = earnedBadge != null;

    return Dialog(
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(RadiusTokens.xl),
      ),
      child: Padding(
        padding: const EdgeInsets.all(SpacingTokens.lg),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            // Badge icon circle
            Container(
              width: 80,
              height: 80,
              decoration: BoxDecoration(
                shape: BoxShape.circle,
                color: isEarned
                    ? _categoryColor(definition.category)
                    : colorScheme.surfaceContainerHighest,
                boxShadow: isEarned
                    ? [
                        BoxShadow(
                          color: _categoryColor(definition.category)
                              .withValues(alpha: 0.4),
                          blurRadius: 16,
                          spreadRadius: 2,
                        ),
                      ]
                    : null,
              ),
              child: isEarned
                  ? Icon(
                      _iconData(definition.icon),
                      size: 40,
                      color: Colors.white,
                    )
                  : Stack(
                      alignment: Alignment.center,
                      children: [
                        Icon(
                          _iconData(definition.icon),
                          size: 40,
                          color: colorScheme.onSurfaceVariant
                              .withValues(alpha: 0.3),
                        ),
                        Positioned(
                          bottom: 6,
                          right: 6,
                          child: Container(
                            width: 22,
                            height: 22,
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
                              size: 13,
                              color: colorScheme.onSurfaceVariant,
                            ),
                          ),
                        ),
                      ],
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

            const SizedBox(height: SpacingTokens.xxs),

            // Category chip
            Container(
              padding: const EdgeInsets.symmetric(
                horizontal: SpacingTokens.sm,
                vertical: SpacingTokens.xxs,
              ),
              decoration: BoxDecoration(
                color: _categoryColor(definition.category)
                    .withValues(alpha: 0.12),
                borderRadius: BorderRadius.circular(RadiusTokens.full),
              ),
              child: Text(
                _categoryLabel(definition.category),
                style: theme.textTheme.labelSmall?.copyWith(
                  color: _categoryColor(definition.category),
                  fontWeight: FontWeight.w600,
                ),
              ),
            ),

            const SizedBox(height: SpacingTokens.sm),

            // Description
            Text(
              definition.description,
              style: theme.textTheme.bodyMedium?.copyWith(
                color: colorScheme.onSurfaceVariant,
              ),
              textAlign: TextAlign.center,
            ),

            const SizedBox(height: SpacingTokens.md),

            // Earned date or progress bar
            if (isEarned) ...[
              Row(
                mainAxisAlignment: MainAxisAlignment.center,
                children: [
                  Icon(
                    Icons.check_circle_rounded,
                    size: 18,
                    color: colorScheme.primary,
                  ),
                  const SizedBox(width: SpacingTokens.xs),
                  Text(
                    'Earned ${DateFormat.yMMMd().format(earnedBadge!.earnedAt ?? DateTime.now())}',
                    style: theme.textTheme.labelLarge?.copyWith(
                      color: colorScheme.primary,
                      fontWeight: FontWeight.w600,
                    ),
                  ),
                ],
              ),
            ] else ...[
              _ProgressSection(
                currentProgress: currentProgress,
                requiredValue: definition.requiredValue,
              ),
            ],

            const SizedBox(height: SpacingTokens.lg),

            // Close button
            SizedBox(
              width: double.infinity,
              child: FilledButton(
                onPressed: () => Navigator.of(context).pop(),
                child: const Text('Close'),
              ),
            ),
          ],
        ),
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

  String _categoryLabel(BadgeCategory cat) {
    switch (cat) {
      case BadgeCategory.streak:
        return 'Streak';
      case BadgeCategory.mastery:
        return 'Mastery';
      case BadgeCategory.engagement:
        return 'Engagement';
      case BadgeCategory.special:
        return 'Special';
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

/// Progress bar for unearned badges.
class _ProgressSection extends StatelessWidget {
  const _ProgressSection({
    required this.currentProgress,
    required this.requiredValue,
  });

  final int currentProgress;
  final int requiredValue;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;
    final progress =
        (currentProgress / requiredValue).clamp(0.0, 1.0).toDouble();

    return Column(
      children: [
        Row(
          mainAxisAlignment: MainAxisAlignment.spaceBetween,
          children: [
            Text(
              'Progress',
              style: theme.textTheme.labelMedium?.copyWith(
                color: colorScheme.onSurfaceVariant,
              ),
            ),
            Text(
              '$currentProgress / $requiredValue',
              style: theme.textTheme.labelMedium?.copyWith(
                fontWeight: FontWeight.w600,
              ),
            ),
          ],
        ),
        const SizedBox(height: SpacingTokens.xs),
        ClipRRect(
          borderRadius: BorderRadius.circular(RadiusTokens.full),
          child: LinearProgressIndicator(
            value: progress,
            minHeight: 8,
            backgroundColor: colorScheme.surfaceContainerHighest,
            valueColor:
                AlwaysStoppedAnimation<Color>(colorScheme.primary),
          ),
        ),
      ],
    );
  }
}
