// =============================================================================
// Cena Adaptive Learning Platform — Knowledge Graph Screen
// =============================================================================

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/config/app_config.dart';

/// Interactive knowledge graph visualization screen.
///
/// Renders the concept dependency graph with mastery overlays.
/// Nodes represent curriculum concepts, edges represent prerequisites.
/// Color interpolation shows mastery progress per concept.
///
/// Full implementation in MOB-005 (Knowledge Graph Visualization).
/// This screen establishes the layout and navigation structure.
class KnowledgeGraphScreen extends ConsumerWidget {
  const KnowledgeGraphScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;

    return Scaffold(
      appBar: AppBar(
        title: const Text('Knowledge Map'),
        actions: [
          // Subject filter chips
          PopupMenuButton<String>(
            icon: const Icon(Icons.filter_list_rounded),
            onSelected: (subject) {
              // Subject filtering — wired in MOB-005
            },
            itemBuilder: (context) => [
              const PopupMenuItem(value: 'all', child: Text('All Subjects')),
              const PopupMenuItem(value: 'math', child: Text('Math')),
              const PopupMenuItem(value: 'physics', child: Text('Physics')),
              const PopupMenuItem(value: 'chemistry', child: Text('Chemistry')),
              const PopupMenuItem(value: 'biology', child: Text('Biology')),
              const PopupMenuItem(value: 'cs', child: Text('CS')),
            ],
          ),
        ],
      ),
      body: Center(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(
              Icons.hub_rounded,
              size: 80,
              color: colorScheme.primary.withValues(alpha: 0.5),
            ),
            const SizedBox(height: SpacingTokens.md),
            Text(
              'Knowledge Graph',
              style: theme.textTheme.titleLarge?.copyWith(
                color: colorScheme.onSurfaceVariant,
              ),
            ),
            const SizedBox(height: SpacingTokens.sm),
            Padding(
              padding: const EdgeInsets.symmetric(
                horizontal: SpacingTokens.xl,
              ),
              child: Text(
                'The interactive concept map will appear here as you '
                'progress through your learning sessions.',
                style: theme.textTheme.bodyMedium?.copyWith(
                  color: colorScheme.onSurfaceVariant,
                ),
                textAlign: TextAlign.center,
              ),
            ),
          ],
        ),
      ),
    );
  }
}
