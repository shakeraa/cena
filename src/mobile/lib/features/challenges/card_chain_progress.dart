// =============================================================================
// Cena — Card Chain Progression Visualization (MOB-VIS-014)
// =============================================================================
// Horizontal scrollable chain showing completed/current/locked challenge cards.
// Inspired by SmartyMe's DIA 1-14 sequential progression.
// =============================================================================

import 'package:flutter/material.dart';

import '../../core/config/app_config.dart';
import '../../core/theme/micro_interactions.dart';
import '../../l10n/app_localizations.dart';
import '../diagrams/models/diagram_models.dart';

/// Horizontal card chain showing sequential challenge progression.
///
/// ```
///   ✓ ── ✓ ── ● ── 🔒 ── 🔒 ── 🔒
///   1    2    3    4     5     6
///        "3 of 6 completed"
/// ```
class CardChainProgress extends StatelessWidget {
  const CardChainProgress({
    super.key,
    required this.cards,
    required this.completedIds,
    this.currentCardId,
    this.onCardTap,
  });

  final List<ChallengeCard> cards;
  final Set<String> completedIds;
  final String? currentCardId;
  final void Function(String cardId)? onCardTap;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final l = AppLocalizations.of(context);
    final completedCount = completedIds.length;

    return Column(
      mainAxisSize: MainAxisSize.min,
      children: [
        // Chain nodes
        SizedBox(
          height: 56,
          child: SingleChildScrollView(
            scrollDirection: Axis.horizontal,
            padding:
                const EdgeInsets.symmetric(horizontal: SpacingTokens.md),
            child: Row(
              children: List.generate(cards.length * 2 - 1, (i) {
                if (i.isOdd) {
                  // Connecting line
                  final leftIdx = i ~/ 2;
                  final leftCompleted =
                      completedIds.contains(cards[leftIdx].id);
                  final rightCompleted =
                      completedIds.contains(cards[leftIdx + 1].id);
                  final isSolid = leftCompleted && rightCompleted;
                  return _ConnectorLine(isSolid: isSolid);
                }

                final idx = i ~/ 2;
                final card = cards[idx];
                final isCompleted = completedIds.contains(card.id);
                final isCurrent = card.id == currentCardId;

                return _ChainNode(
                  index: idx + 1,
                  isCompleted: isCompleted,
                  isCurrent: isCurrent,
                  onTap: (isCompleted || isCurrent)
                      ? () => onCardTap?.call(card.id)
                      : null,
                  tooltip: (!isCompleted && !isCurrent)
                      ? l.completePreviousFirst
                      : null,
                );
              }),
            ),
          ),
        ),

        // Progress label
        const SizedBox(height: SpacingTokens.xs),
        Text(
          l.nOfTotal(completedCount, cards.length),
          style: theme.textTheme.labelSmall?.copyWith(
            color: theme.colorScheme.onSurfaceVariant,
          ),
        ),
      ],
    );
  }
}

/// A single node in the progression chain.
class _ChainNode extends StatelessWidget {
  const _ChainNode({
    required this.index,
    required this.isCompleted,
    required this.isCurrent,
    this.onTap,
    this.tooltip,
  });

  final int index;
  final bool isCompleted;
  final bool isCurrent;
  final VoidCallback? onTap;
  final String? tooltip;

  @override
  Widget build(BuildContext context) {
    final colorScheme = Theme.of(context).colorScheme;

    Widget node;
    if (isCompleted) {
      node = CircleAvatar(
        radius: 20,
        backgroundColor: Colors.green,
        child:
            const Icon(Icons.check_rounded, color: Colors.white, size: 18),
      );
    } else if (isCurrent) {
      node = PulseGlow(
        color: colorScheme.primary,
        glowRadius: 10,
        child: CircleAvatar(
          radius: 20,
          backgroundColor: colorScheme.primary,
          child: Text(
            '$index',
            style: const TextStyle(
              color: Colors.white,
              fontWeight: FontWeight.w700,
              fontSize: 14,
            ),
          ),
        ),
      );
    } else {
      // Locked
      node = CircleAvatar(
        radius: 20,
        backgroundColor: colorScheme.surfaceContainerHighest,
        child: Icon(Icons.lock_rounded,
            color: colorScheme.onSurfaceVariant, size: 16),
      );
    }

    final tappable = GestureDetector(
      onTap: onTap,
      child: node,
    );

    if (tooltip != null) {
      return Tooltip(message: tooltip!, child: tappable);
    }
    return tappable;
  }
}

/// Connector line between chain nodes.
class _ConnectorLine extends StatelessWidget {
  const _ConnectorLine({required this.isSolid});

  final bool isSolid;

  @override
  Widget build(BuildContext context) {
    return SizedBox(
      width: 24,
      child: Center(
        child: Container(
          height: 2,
          width: 24,
          decoration: BoxDecoration(
            color: isSolid ? Colors.green : Colors.grey.shade300,
            borderRadius: BorderRadius.circular(1),
          ),
        ),
      ),
    );
  }
}
