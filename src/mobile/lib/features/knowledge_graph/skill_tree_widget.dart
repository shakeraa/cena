// =============================================================================
// Cena — Skill Tree / Mastery Map Widget (MOB-CORE-004)
// =============================================================================
// Visual node tree: locked → in-progress → completed states
// Animated paths between concept nodes
// =============================================================================

import 'package:flutter/material.dart';

import '../../core/config/app_config.dart';

// ---------------------------------------------------------------------------
// Skill tree node data
// ---------------------------------------------------------------------------

enum SkillNodeStatus { locked, inProgress, completed }

class SkillNode {
  const SkillNode({
    required this.id,
    required this.label,
    required this.status,
    this.mastery = 0.0,
    this.children = const [],
  });

  final String id;
  final String label;
  final SkillNodeStatus status;
  final double mastery;
  final List<SkillNode> children;
}

// ---------------------------------------------------------------------------
// Skill tree widget
// ---------------------------------------------------------------------------

class SkillTreeWidget extends StatelessWidget {
  const SkillTreeWidget({super.key, required this.roots});

  final List<SkillNode> roots;

  @override
  Widget build(BuildContext context) {
    if (roots.isEmpty) {
      return Center(
        child: Text(
          'Complete sessions to unlock your skill tree',
          style: Theme.of(context).textTheme.bodyMedium?.copyWith(
                color: Theme.of(context).colorScheme.onSurfaceVariant,
              ),
        ),
      );
    }

    return SingleChildScrollView(
      scrollDirection: Axis.horizontal,
      padding: const EdgeInsets.all(SpacingTokens.md),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: roots
            .map((root) => _SkillBranch(node: root, depth: 0))
            .toList(),
      ),
    );
  }
}

class _SkillBranch extends StatelessWidget {
  const _SkillBranch({required this.node, required this.depth});

  final SkillNode node;
  final int depth;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: EdgeInsets.only(left: depth > 0 ? SpacingTokens.lg : 0),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          _SkillNodeCircle(node: node),
          if (node.children.isNotEmpty) ...[
            // Connector line
            Container(
              width: 2,
              height: 20,
              margin: const EdgeInsets.only(left: 22),
              color: Theme.of(context)
                  .colorScheme
                  .outlineVariant
                  .withValues(alpha: 0.4),
            ),
            ...node.children.map((child) =>
                _SkillBranch(node: child, depth: depth + 1)),
          ],
        ],
      ),
    );
  }
}

class _SkillNodeCircle extends StatelessWidget {
  const _SkillNodeCircle({required this.node});

  final SkillNode node;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;

    final Color nodeColor;
    final Color textColor;
    final IconData? statusIcon;

    switch (node.status) {
      case SkillNodeStatus.locked:
        nodeColor = colorScheme.surfaceContainerHighest;
        textColor = colorScheme.onSurfaceVariant.withValues(alpha: 0.4);
        statusIcon = Icons.lock_rounded;
      case SkillNodeStatus.inProgress:
        nodeColor = colorScheme.primaryContainer;
        textColor = colorScheme.onPrimaryContainer;
        statusIcon = null;
      case SkillNodeStatus.completed:
        nodeColor = colorScheme.primary;
        textColor = colorScheme.onPrimary;
        statusIcon = Icons.check_rounded;
    }

    return Padding(
      padding: const EdgeInsets.symmetric(vertical: SpacingTokens.xs),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          // Node circle
          Container(
            width: 44,
            height: 44,
            decoration: BoxDecoration(
              shape: BoxShape.circle,
              color: nodeColor,
              boxShadow: node.status == SkillNodeStatus.inProgress
                  ? [
                      BoxShadow(
                        color: colorScheme.primary.withValues(alpha: 0.3),
                        blurRadius: 8,
                      ),
                    ]
                  : null,
            ),
            child: Center(
              child: statusIcon != null
                  ? Icon(statusIcon, size: 18, color: textColor)
                  : Text(
                      '${(node.mastery * 100).toInt()}%',
                      style: theme.textTheme.labelSmall?.copyWith(
                        color: textColor,
                        fontWeight: FontWeight.w700,
                        fontSize: 10,
                      ),
                    ),
            ),
          ),
          const SizedBox(width: SpacingTokens.sm),
          // Label
          Text(
            node.label,
            style: theme.textTheme.bodySmall?.copyWith(
              color: node.status == SkillNodeStatus.locked
                  ? colorScheme.onSurfaceVariant.withValues(alpha: 0.5)
                  : colorScheme.onSurface,
              fontWeight: node.status == SkillNodeStatus.completed
                  ? FontWeight.w600
                  : FontWeight.w400,
            ),
          ),
        ],
      ),
    );
  }
}
