// =============================================================================
// Cena Adaptive Learning Platform — Mastery List Widget (PAR-001)
// Displays per-topic mastery % in a sortable, filterable list view.
// =============================================================================
//
// TODO(l10n): Replace hardcoded strings with ARB keys once added:
//   myMastery, mastered, weakestFirst, strongestFirst, alphabetical,
//   lastStudied, noConceptsYet, subjectMath/Physics/Chemistry/Biology/CS

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/config/app_config.dart';
import '../../core/models/domain_models.dart';
import '../../core/state/knowledge_graph_notifier.dart';

// ---------------------------------------------------------------------------
// Sort Options
// ---------------------------------------------------------------------------

enum MasterySortMode {
  weakestFirst,
  strongestFirst,
  alphabetical,
  lastStudied,
}

// ---------------------------------------------------------------------------
// Widget
// ---------------------------------------------------------------------------

/// Sortable, filterable list of concept mastery percentages.
///
/// Reads from [KnowledgeGraphState.graph.masteryOverlay] and the node list
/// to build a compact list showing topic name, mastery %, and color-coded bar.
class MasteryListWidget extends ConsumerStatefulWidget {
  const MasteryListWidget({super.key});

  @override
  ConsumerState<MasteryListWidget> createState() => _MasteryListWidgetState();
}

class _MasteryListWidgetState extends ConsumerState<MasteryListWidget> {
  MasterySortMode _sortMode = MasterySortMode.weakestFirst;
  Subject? _subjectFilter;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;
    final graphState = ref.watch(knowledgeGraphProvider);
    final graph = graphState.graph;

    if (graphState.isLoading || graph == null) {
      return const Center(child: CircularProgressIndicator());
    }

    // Build list of (node, masteryState) pairs.
    var items = graph.nodes.map((node) {
      final mastery = graph.masteryOverlay[node.conceptId];
      return _MasteryItem(
        conceptId: node.conceptId,
        label: node.labelHe ?? node.label,
        subject: node.subject,
        pKnown: mastery?.pKnown ?? 0.0,
        isMastered: mastery?.isMastered ?? false,
        lastAttempted: mastery?.lastAttempted,
        attemptCount: mastery?.attemptCount ?? 0,
      );
    }).toList();

    // Apply subject filter.
    if (_subjectFilter != null) {
      items = items.where((i) => i.subject == _subjectFilter).toList();
    }

    // Apply sort.
    switch (_sortMode) {
      case MasterySortMode.weakestFirst:
        items.sort((a, b) => a.pKnown.compareTo(b.pKnown));
      case MasterySortMode.strongestFirst:
        items.sort((a, b) => b.pKnown.compareTo(a.pKnown));
      case MasterySortMode.alphabetical:
        items.sort((a, b) => a.label.compareTo(b.label));
      case MasterySortMode.lastStudied:
        items.sort((a, b) {
          final aDate = a.lastAttempted ?? DateTime(2000);
          final bDate = b.lastAttempted ?? DateTime(2000);
          return bDate.compareTo(aDate);
        });
    }

    final masteredCount = items.where((i) => i.isMastered).length;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        // Header with stats.
        Padding(
          padding: const EdgeInsets.symmetric(
            horizontal: SpacingTokens.md,
            vertical: SpacingTokens.sm,
          ),
          child: Row(
            children: [
              Icon(Icons.bar_chart_rounded,
                  size: 20, color: colorScheme.primary),
              const SizedBox(width: SpacingTokens.xs),
              Text(
                'My Mastery',
                style: theme.textTheme.titleMedium
                    ?.copyWith(fontWeight: FontWeight.w700),
              ),
              const Spacer(),
              Text(
                '$masteredCount / ${items.length}',
                style: theme.textTheme.labelMedium?.copyWith(
                  color: colorScheme.onSurfaceVariant,
                ),
              ),
              const SizedBox(width: SpacingTokens.xs),
              Text(
                'mastered',
                style: theme.textTheme.labelMedium?.copyWith(
                  color: colorScheme.onSurfaceVariant,
                ),
              ),
            ],
          ),
        ),

        // Filter + sort row.
        SingleChildScrollView(
          scrollDirection: Axis.horizontal,
          padding: const EdgeInsets.symmetric(horizontal: SpacingTokens.md),
          child: Row(
            children: [
              // Subject filter chips.
              _FilterChip(
                label: 'All',
                selected: _subjectFilter == null,
                onSelected: () => setState(() => _subjectFilter = null),
              ),
              for (final subject in Subject.values)
                Padding(
                  padding: const EdgeInsetsDirectional.only(
                      start: SpacingTokens.xs),
                  child: _FilterChip(
                    label: _subjectLabel(subject),
                    selected: _subjectFilter == subject,
                    onSelected: () =>
                        setState(() => _subjectFilter = subject),
                  ),
                ),
              const SizedBox(width: SpacingTokens.md),
              // Sort dropdown.
              PopupMenuButton<MasterySortMode>(
                initialValue: _sortMode,
                onSelected: (mode) => setState(() => _sortMode = mode),
                icon: const Icon(Icons.sort_rounded, size: 20),
                itemBuilder: (_) => const [
                  PopupMenuItem(
                    value: MasterySortMode.weakestFirst,
                    child: Text('Weakest first'),
                  ),
                  PopupMenuItem(
                    value: MasterySortMode.strongestFirst,
                    child: Text('Strongest first'),
                  ),
                  PopupMenuItem(
                    value: MasterySortMode.alphabetical,
                    child: Text('Alphabetical'),
                  ),
                  PopupMenuItem(
                    value: MasterySortMode.lastStudied,
                    child: Text('Last studied'),
                  ),
                ],
              ),
            ],
          ),
        ),

        const SizedBox(height: SpacingTokens.sm),

        // List.
        if (items.isEmpty)
          Padding(
            padding: const EdgeInsets.all(SpacingTokens.xl),
            child: Center(
              child: Text(
                'No concepts studied yet',
                style: theme.textTheme.bodyMedium?.copyWith(
                  color: colorScheme.onSurfaceVariant,
                ),
              ),
            ),
          )
        else
          ...items.map((item) => _MasteryTile(item: item)),
      ],
    );
  }

  static String _subjectLabel(Subject subject) {
    switch (subject) {
      case Subject.math:
        return 'Math';
      case Subject.physics:
        return 'Physics';
      case Subject.chemistry:
        return 'Chemistry';
      case Subject.biology:
        return 'Biology';
      case Subject.cs:
        return 'CS';
    }
  }
}

// ---------------------------------------------------------------------------
// Filter Chip
// ---------------------------------------------------------------------------

class _FilterChip extends StatelessWidget {
  const _FilterChip({
    required this.label,
    required this.selected,
    required this.onSelected,
  });

  final String label;
  final bool selected;
  final VoidCallback onSelected;

  @override
  Widget build(BuildContext context) {
    final colorScheme = Theme.of(context).colorScheme;
    return FilterChip(
      label: Text(label),
      selected: selected,
      onSelected: (_) => onSelected(),
      selectedColor: colorScheme.primaryContainer,
      checkmarkColor: colorScheme.onPrimaryContainer,
      padding: const EdgeInsets.symmetric(horizontal: SpacingTokens.xs),
      visualDensity: VisualDensity.compact,
    );
  }
}

// ---------------------------------------------------------------------------
// Mastery Tile
// ---------------------------------------------------------------------------

class _MasteryTile extends StatelessWidget {
  const _MasteryTile({required this.item});

  final _MasteryItem item;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;
    final percentage = (item.pKnown * 100).toInt();
    final barColor = _masteryColor(item.pKnown, colorScheme);

    return Padding(
      padding: const EdgeInsets.symmetric(
        horizontal: SpacingTokens.md,
        vertical: SpacingTokens.xxs,
      ),
      child: Card(
        child: Padding(
          padding: const EdgeInsets.all(SpacingTokens.sm),
          child: Row(
            children: [
              // Subject icon.
              Container(
                width: 36,
                height: 36,
                decoration: BoxDecoration(
                  color: barColor.withValues(alpha: 0.12),
                  borderRadius: BorderRadius.circular(RadiusTokens.sm),
                ),
                child: Icon(
                  _subjectIcon(item.subject),
                  size: 18,
                  color: barColor,
                ),
              ),
              const SizedBox(width: SpacingTokens.sm),

              // Label + attempt count.
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      item.label,
                      style: theme.textTheme.bodyMedium?.copyWith(
                        fontWeight: FontWeight.w600,
                      ),
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                    ),
                    if (item.attemptCount > 0)
                      Text(
                        '${item.attemptCount} attempts',
                        style: theme.textTheme.labelSmall?.copyWith(
                          color: colorScheme.onSurfaceVariant,
                        ),
                      ),
                  ],
                ),
              ),

              // Mastery bar + percentage.
              SizedBox(
                width: 100,
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.end,
                  children: [
                    Text(
                      '$percentage%',
                      style: theme.textTheme.labelLarge?.copyWith(
                        fontWeight: FontWeight.w700,
                        color: barColor,
                      ),
                    ),
                    const SizedBox(height: 4),
                    ClipRRect(
                      borderRadius:
                          BorderRadius.circular(RadiusTokens.full),
                      child: LinearProgressIndicator(
                        value: item.pKnown,
                        backgroundColor:
                            colorScheme.surfaceContainerHighest,
                        valueColor: AlwaysStoppedAnimation(barColor),
                        minHeight: 6,
                      ),
                    ),
                  ],
                ),
              ),

              // Mastered checkmark.
              if (item.isMastered)
                Padding(
                  padding:
                      const EdgeInsetsDirectional.only(start: SpacingTokens.xs),
                  child: Icon(
                    Icons.check_circle_rounded,
                    color: colorScheme.primary,
                    size: 20,
                  ),
                ),
            ],
          ),
        ),
      ),
    );
  }

  Color _masteryColor(double mastery, ColorScheme cs) {
    if (mastery >= 0.85) return cs.primary;
    if (mastery >= 0.5) return const Color(0xFFFF8F00); // amber
    return cs.error;
  }

  IconData _subjectIcon(Subject subject) {
    switch (subject) {
      case Subject.math:
        return Icons.calculate_rounded;
      case Subject.physics:
        return Icons.bolt_rounded;
      case Subject.chemistry:
        return Icons.science_rounded;
      case Subject.biology:
        return Icons.biotech_rounded;
      case Subject.cs:
        return Icons.computer_rounded;
    }
  }
}

// ---------------------------------------------------------------------------
// Internal Model
// ---------------------------------------------------------------------------

class _MasteryItem {
  const _MasteryItem({
    required this.conceptId,
    required this.label,
    required this.subject,
    required this.pKnown,
    required this.isMastered,
    this.lastAttempted,
    this.attemptCount = 0,
  });

  final String conceptId;
  final String label;
  final Subject subject;
  final double pKnown;
  final bool isMastered;
  final DateTime? lastAttempted;
  final int attemptCount;
}
