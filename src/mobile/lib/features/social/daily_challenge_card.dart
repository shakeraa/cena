// =============================================================================
// Cena — Daily Challenge Feed Card (MOB-VIS-016) + Study Groups (MOB-SOC-002)
// =============================================================================
// Extracted from class_activity_feed.dart to keep files under 500 lines.
// =============================================================================

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/config/app_config.dart';
import '../session/widgets/math_text.dart';
import 'class_activity_feed.dart';

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
