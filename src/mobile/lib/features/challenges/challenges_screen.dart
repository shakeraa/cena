// =============================================================================
// Cena — Browse Challenges Grid Screen (MOB-VIS-010)
// =============================================================================
// SmartyMe-style topic grid where students browse challenge cards by subject.
// Each card shows topic name, hero formula, tier badge, and completion status.
// =============================================================================

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/config/app_config.dart';
import '../../core/models/domain_models.dart';
import '../../core/theme/glass_widgets.dart';
import '../../core/theme/micro_interactions.dart';
import '../../l10n/app_localizations.dart';
import '../diagrams/challenge_card_widget.dart';
import '../diagrams/models/diagram_models.dart';
import '../session/widgets/math_text.dart';
import 'card_chain_progress.dart';

// ---------------------------------------------------------------------------
// State — challenge list provider (placeholder until backend wired)
// ---------------------------------------------------------------------------

/// Placeholder provider for available challenge cards.
/// Replace with REST API fetch when backend is ready.
final challengeListProvider =
    StateProvider<List<ChallengeCard>>((ref) => const []);

/// Tracks which challenge IDs the student has completed.
final completedChallengesProvider =
    StateProvider<Set<String>>((ref) => const {});

// ---------------------------------------------------------------------------
// Screen
// ---------------------------------------------------------------------------

class ChallengesScreen extends ConsumerStatefulWidget {
  const ChallengesScreen({super.key});

  @override
  ConsumerState<ChallengesScreen> createState() => _ChallengesScreenState();
}

class _ChallengesScreenState extends ConsumerState<ChallengesScreen> {
  Subject? _selectedSubject;

  @override
  Widget build(BuildContext context) {
    final l = AppLocalizations.of(context);
    final allCards = ref.watch(challengeListProvider);
    final completedIds = ref.watch(completedChallengesProvider);

    // Filter by subject
    final filtered = _selectedSubject == null
        ? allCards
        : allCards.where((c) {
            return c.diagram.subject.toLowerCase() ==
                _selectedSubject!.name.toLowerCase();
          }).toList();

    // Detect card chains (cards with nextCardId)
    final chainCards =
        filtered.where((c) => c.nextCardId != null || filtered.any((o) => o.nextCardId == c.id)).toList();

    return Scaffold(
      appBar: AppBar(title: Text(l.challenges)),
      body: Column(
        children: [
          // Subject filter chips
          SizedBox(
            height: 48,
            child: ListView(
              scrollDirection: Axis.horizontal,
              padding:
                  const EdgeInsets.symmetric(horizontal: SpacingTokens.md),
              children: [
                Padding(
                  padding:
                      const EdgeInsets.only(right: SpacingTokens.sm),
                  child: FilterChip(
                    selected: _selectedSubject == null,
                    label: Text(l.allTopics),
                    onSelected: (_) =>
                        setState(() => _selectedSubject = null),
                  ),
                ),
                ..._subjectChips(l),
              ],
            ),
          ),

          // Card chain progress (if chain detected)
          if (chainCards.length > 1) ...[
            const SizedBox(height: SpacingTokens.sm),
            CardChainProgress(
              cards: chainCards,
              completedIds: completedIds,
              currentCardId: _findCurrentCardId(chainCards, completedIds),
              onCardTap: (id) => _openCard(context, filtered, id),
            ),
            const SizedBox(height: SpacingTokens.sm),
          ],

          // Grid
          Expanded(
            child: filtered.isEmpty
                ? _EmptyState(l: l)
                : GridView.builder(
                    padding: const EdgeInsets.all(SpacingTokens.md),
                    gridDelegate:
                        const SliverGridDelegateWithFixedCrossAxisCount(
                      crossAxisCount: 2,
                      mainAxisSpacing: SpacingTokens.sm,
                      crossAxisSpacing: SpacingTokens.sm,
                      childAspectRatio: 0.75,
                    ),
                    itemCount: filtered.length,
                    itemBuilder: (context, index) {
                      final card = filtered[index];
                      final isCompleted = completedIds.contains(card.id);
                      return _ChallengePreviewCard(
                        card: card,
                        isCompleted: isCompleted,
                        onTap: () =>
                            _openCard(context, filtered, card.id),
                      );
                    },
                  ),
          ),
        ],
      ),
    );
  }

  List<Widget> _subjectChips(AppLocalizations l) {
    final subjects = [
      (Subject.math, l.math, SubjectColorTokens.mathPrimary),
      (Subject.physics, l.physics, SubjectColorTokens.physicsPrimary),
      (Subject.chemistry, l.chemistry, SubjectColorTokens.chemistryPrimary),
      (Subject.biology, l.biology, SubjectColorTokens.biologyPrimary),
      (Subject.cs, l.cs, SubjectColorTokens.csPrimary),
    ];

    return subjects.map((s) {
      final (subject, label, color) = s;
      return Padding(
        padding: const EdgeInsets.only(right: SpacingTokens.sm),
        child: FilterChip(
          selected: _selectedSubject == subject,
          label: Text(label),
          selectedColor: color.withValues(alpha: 0.2),
          checkmarkColor: color,
          onSelected: (_) => setState(() {
            _selectedSubject =
                _selectedSubject == subject ? null : subject;
          }),
        ),
      );
    }).toList();
  }

  String? _findCurrentCardId(
      List<ChallengeCard> chain, Set<String> completed) {
    for (final card in chain) {
      if (!completed.contains(card.id)) return card.id;
    }
    return null;
  }

  void _openCard(
      BuildContext context, List<ChallengeCard> cards, String cardId) {
    final card = cards.firstWhere((c) => c.id == cardId,
        orElse: () => cards.first);

    Navigator.of(context).push(
      MaterialPageRoute<void>(
        builder: (_) => Scaffold(
          appBar: AppBar(),
          body: Padding(
            padding: const EdgeInsets.all(SpacingTokens.md),
            child: ChallengeCardWidget(
              card: card,
              onComplete: (isCorrect, xp) {
                if (isCorrect) {
                  ref.read(completedChallengesProvider.notifier).update(
                      (s) => {...s, card.id});
                }
              },
              onNextCard: (nextId) {
                Navigator.of(context).pop();
                _openCard(context, cards, nextId);
              },
            ),
          ),
        ),
      ),
    );
  }
}

// ---------------------------------------------------------------------------
// Challenge preview card (grid item)
// ---------------------------------------------------------------------------

class _ChallengePreviewCard extends StatelessWidget {
  const _ChallengePreviewCard({
    required this.card,
    required this.isCompleted,
    required this.onTap,
  });

  final ChallengeCard card;
  final bool isCompleted;
  final VoidCallback onTap;

  Color _tierColor(ChallengeTier tier) {
    switch (tier) {
      case ChallengeTier.beginner:
        return Colors.green;
      case ChallengeTier.intermediate:
        return Colors.blue;
      case ChallengeTier.advanced:
        return Colors.orange;
      case ChallengeTier.expert:
        return Colors.red;
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final tierColor = _tierColor(card.tier);

    return TapScaleButton(
      onTap: onTap,
      child: Container(
        decoration: BoxDecoration(
          borderRadius: BorderRadius.circular(RadiusTokens.xl),
          border: Border.all(color: tierColor.withValues(alpha: 0.5), width: 2),
          boxShadow: [
            BoxShadow(
              color: tierColor.withValues(alpha: 0.15),
              blurRadius: 8,
              spreadRadius: 1,
            ),
          ],
        ),
        child: GlassCard(
          borderRadius: BorderRadius.circular(RadiusTokens.xl),
          child: Stack(
            children: [
              Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  // XP badge
                  Align(
                    alignment: Alignment.topRight,
                    child: GlassChip(
                      label: '+${card.xpReward} XP',
                      icon: Icons.bolt_rounded,
                      color: const Color(0xFFFFD700),
                    ),
                  ),
                  const Spacer(),

                  // Formula (first from diagram, or question preview)
                  if (card.diagram.formulas.isNotEmpty)
                    Center(
                      child: MathText(
                        content: card.diagram.formulas.first,
                        textStyle: TextStyle(
                          fontSize: TypographyTokens.titleLarge,
                          fontWeight: FontWeight.w700,
                          color: tierColor,
                        ),
                        mathColor: tierColor,
                      ),
                    )
                  else
                    Text(
                      card.questionHe.length > 50
                          ? '${card.questionHe.substring(0, 50)}...'
                          : card.questionHe,
                      style: theme.textTheme.bodySmall,
                      maxLines: 2,
                      overflow: TextOverflow.ellipsis,
                    ),

                  const Spacer(),

                  // Tier label
                  Center(
                    child: Container(
                      padding: const EdgeInsets.symmetric(
                        horizontal: SpacingTokens.sm,
                        vertical: SpacingTokens.xxs,
                      ),
                      decoration: BoxDecoration(
                        color: tierColor.withValues(alpha: 0.12),
                        borderRadius:
                            BorderRadius.circular(RadiusTokens.full),
                      ),
                      child: Text(
                        card.tier.name,
                        style: theme.textTheme.labelSmall?.copyWith(
                          color: tierColor,
                          fontWeight: FontWeight.w600,
                        ),
                      ),
                    ),
                  ),
                ],
              ),

              // Completion overlay
              if (isCompleted)
                Positioned.fill(
                  child: Container(
                    decoration: BoxDecoration(
                      color: Colors.green.withValues(alpha: 0.1),
                      borderRadius:
                          BorderRadius.circular(RadiusTokens.lg),
                    ),
                    child: const Center(
                      child: Icon(Icons.check_circle_rounded,
                          color: Colors.green, size: 36),
                    ),
                  ),
                ),
            ],
          ),
        ),
      ),
    );
  }
}

// ---------------------------------------------------------------------------
// Empty state
// ---------------------------------------------------------------------------

class _EmptyState extends StatelessWidget {
  const _EmptyState({required this.l});

  final AppLocalizations l;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(SpacingTokens.xl),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(
              Icons.extension_rounded,
              size: 64,
              color: theme.colorScheme.onSurfaceVariant
                  .withValues(alpha: 0.4),
            ),
            const SizedBox(height: SpacingTokens.md),
            Text(
              l.challenges,
              style: theme.textTheme.titleMedium?.copyWith(
                color: theme.colorScheme.onSurfaceVariant,
              ),
            ),
            const SizedBox(height: SpacingTokens.sm),
            Text(
              'Complete sessions to unlock challenges.',
              style: theme.textTheme.bodyMedium?.copyWith(
                color: theme.colorScheme.onSurfaceVariant,
              ),
              textAlign: TextAlign.center,
            ),
          ],
        ),
      ),
    );
  }
}
