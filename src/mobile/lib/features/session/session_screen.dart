// =============================================================================
// Cena Adaptive Learning Platform — Session Screen
// =============================================================================

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/config/app_config.dart';
import '../../core/router.dart';

/// Learning session screen.
///
/// This is the session configuration and launch screen. The full adaptive
/// session flow (question presentation, answer evaluation, mastery tracking)
/// will be implemented in MOB-003 through MOB-006.
///
/// Current functionality:
/// - Subject selection
/// - Duration selection (within SessionDefaults bounds)
/// - Session start button
class SessionScreen extends ConsumerStatefulWidget {
  const SessionScreen({super.key});

  @override
  ConsumerState<SessionScreen> createState() => _SessionScreenState();
}

class _SessionScreenState extends ConsumerState<SessionScreen> {
  int _selectedDuration = SessionDefaults.defaultDurationMinutes;
  int? _selectedSubjectIndex;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;

    return Scaffold(
      appBar: AppBar(
        title: const Text('New Session'),
        leading: IconButton(
          icon: const Icon(Icons.close_rounded),
          onPressed: () => context.go(CenaRoutes.home),
        ),
      ),
      body: ListView(
        padding: const EdgeInsets.all(SpacingTokens.md),
        children: [
          // Subject Selection
          Text(
            'Choose a Subject',
            style: theme.textTheme.titleLarge,
          ),
          const SizedBox(height: SpacingTokens.sm),
          _buildSubjectChips(context),

          const SizedBox(height: SpacingTokens.xl),

          // Duration Selection
          Text(
            'Session Duration',
            style: theme.textTheme.titleLarge,
          ),
          const SizedBox(height: SpacingTokens.sm),
          Text(
            '$_selectedDuration minutes',
            style: theme.textTheme.headlineLarge?.copyWith(
              color: colorScheme.primary,
              fontWeight: FontWeight.w700,
            ),
            textAlign: TextAlign.center,
          ),
          Slider(
            value: _selectedDuration.toDouble(),
            min: SessionDefaults.minDurationMinutes.toDouble(),
            max: SessionDefaults.maxDurationMinutes.toDouble(),
            divisions: SessionDefaults.maxDurationMinutes -
                SessionDefaults.minDurationMinutes,
            label: '$_selectedDuration min',
            onChanged: (value) {
              setState(() {
                _selectedDuration = value.round();
              });
            },
          ),
          Row(
            mainAxisAlignment: MainAxisAlignment.spaceBetween,
            children: [
              Text(
                '${SessionDefaults.minDurationMinutes} min',
                style: theme.textTheme.bodySmall?.copyWith(
                  color: colorScheme.onSurfaceVariant,
                ),
              ),
              Text(
                '${SessionDefaults.maxDurationMinutes} min',
                style: theme.textTheme.bodySmall?.copyWith(
                  color: colorScheme.onSurfaceVariant,
                ),
              ),
            ],
          ),

          const SizedBox(height: SpacingTokens.xl),

          // Session Info
          Card(
            child: Padding(
              padding: const EdgeInsets.all(SpacingTokens.md),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Row(
                    children: [
                      Icon(
                        Icons.info_outline_rounded,
                        size: 20,
                        color: colorScheme.primary,
                      ),
                      const SizedBox(width: SpacingTokens.sm),
                      Text(
                        'Session Details',
                        style: theme.textTheme.titleMedium,
                      ),
                    ],
                  ),
                  const SizedBox(height: SpacingTokens.sm),
                  _InfoRow(
                    label: 'Max questions',
                    value: '${SessionDefaults.maxQuestionsPerSession}',
                  ),
                  _InfoRow(
                    label: 'Mastery threshold',
                    value:
                        '${(SessionDefaults.masteryThreshold * 100).toInt()}%',
                  ),
                  _InfoRow(
                    label: LlmBudget.label,
                    value: '${LlmBudget.dailyCap} remaining',
                  ),
                ],
              ),
            ),
          ),

          const SizedBox(height: SpacingTokens.xl),

          // Start Button
          FilledButton.icon(
            onPressed: () {
              // Session start — full implementation in MOB-003
              // Will create a Session model and navigate to the question flow
              ScaffoldMessenger.of(context).showSnackBar(
                SnackBar(
                  content: Text(
                    'Starting $_selectedDuration-minute session...',
                  ),
                ),
              );
            },
            icon: const Icon(Icons.play_arrow_rounded),
            label: const Text('Start Session'),
          ),
        ],
      ),
    );
  }

  Widget _buildSubjectChips(BuildContext context) {
    final subjects = [
      (
        'Math',
        Icons.functions_rounded,
        SubjectColorTokens.mathPrimary,
      ),
      (
        'Physics',
        Icons.speed_rounded,
        SubjectColorTokens.physicsPrimary,
      ),
      (
        'Chemistry',
        Icons.science_rounded,
        SubjectColorTokens.chemistryPrimary,
      ),
      (
        'Biology',
        Icons.biotech_rounded,
        SubjectColorTokens.biologyPrimary,
      ),
      (
        'CS',
        Icons.computer_rounded,
        SubjectColorTokens.csPrimary,
      ),
    ];

    return Wrap(
      spacing: SpacingTokens.sm,
      runSpacing: SpacingTokens.sm,
      children: subjects.asMap().entries.map((entry) {
        final index = entry.key;
        final (name, icon, color) = entry.value;
        final isSelected = _selectedSubjectIndex == index;

        return FilterChip(
          selected: isSelected,
          label: Text(name),
          avatar: Icon(icon, size: 18),
          selectedColor: color.withValues(alpha: 0.2),
          checkmarkColor: color,
          onSelected: (selected) {
            setState(() {
              _selectedSubjectIndex = selected ? index : null;
            });
          },
        );
      }).toList(),
    );
  }
}

/// Simple key-value row for session info display.
class _InfoRow extends StatelessWidget {
  const _InfoRow({required this.label, required this.value});

  final String label;
  final String value;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Padding(
      padding: const EdgeInsets.symmetric(vertical: SpacingTokens.xs),
      child: Row(
        mainAxisAlignment: MainAxisAlignment.spaceBetween,
        children: [
          Text(
            label,
            style: theme.textTheme.bodyMedium?.copyWith(
              color: theme.colorScheme.onSurfaceVariant,
            ),
          ),
          Text(
            value,
            style: theme.textTheme.bodyMedium?.copyWith(
              fontWeight: FontWeight.w600,
            ),
          ),
        ],
      ),
    );
  }
}
