// =============================================================================
// Cena Adaptive Learning Platform — Deep Study Configuration Widget
// Duration selector and block preview for extended study sessions.
// =============================================================================

import 'package:flutter/material.dart';

import '../../../core/config/app_config.dart';
import '../models/deep_study_session.dart';

/// Configuration widget for Deep Study Mode.
///
/// Displays:
///   1. A toggle to enable/disable Deep Study Mode.
///   2. Large tappable duration cards (45/60/75/90 min).
///   3. A block preview showing the session structure with breaks.
///
/// This widget is designed to be embedded in the session configuration
/// screen below the standard duration slider.
class DeepStudyConfig extends StatefulWidget {
  const DeepStudyConfig({
    super.key,
    required this.isEnabled,
    required this.selectedDuration,
    required this.onEnabledChanged,
    required this.onDurationChanged,
  });

  /// Whether Deep Study Mode is currently enabled.
  final bool isEnabled;

  /// The selected duration in minutes (45, 60, 75, or 90).
  final int selectedDuration;

  /// Called when the toggle is flipped.
  final ValueChanged<bool> onEnabledChanged;

  /// Called when a duration card is tapped.
  final ValueChanged<int> onDurationChanged;

  @override
  State<DeepStudyConfig> createState() => _DeepStudyConfigState();
}

class _DeepStudyConfigState extends State<DeepStudyConfig> {
  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        // Deep Study toggle
        _DeepStudyToggle(
          isEnabled: widget.isEnabled,
          onChanged: widget.onEnabledChanged,
        ),

        // Content shown only when enabled
        if (widget.isEnabled) ...[
          const SizedBox(height: SpacingTokens.md),

          // Duration selector
          Text(
            'Session Length',
            style: theme.textTheme.titleMedium?.copyWith(
              fontWeight: FontWeight.w600,
            ),
          ),
          const SizedBox(height: SpacingTokens.sm),

          _DurationSelector(
            selectedDuration: widget.selectedDuration,
            onSelected: widget.onDurationChanged,
          ),

          const SizedBox(height: SpacingTokens.lg),

          // Block preview
          _BlockPreview(
            session: DeepStudySession(
              totalDurationMinutes: widget.selectedDuration,
            ),
          ),

          const SizedBox(height: SpacingTokens.sm),

          // Info text
          Container(
            padding: const EdgeInsets.all(SpacingTokens.sm),
            decoration: BoxDecoration(
              color: colorScheme.primaryContainer.withValues(alpha: 0.3),
              borderRadius: BorderRadius.circular(RadiusTokens.md),
            ),
            child: Row(
              children: [
                Icon(
                  Icons.info_outline_rounded,
                  size: 16,
                  color: colorScheme.primary,
                ),
                const SizedBox(width: SpacingTokens.sm),
                Expanded(
                  child: Text(
                    'Includes ${DeepStudySession.breakDurationMinutes}-minute '
                    'recovery breaks between blocks.',
                    style: theme.textTheme.bodySmall?.copyWith(
                      color: colorScheme.onSurfaceVariant,
                    ),
                  ),
                ),
              ],
            ),
          ),
        ],
      ],
    );
  }
}

// ---------------------------------------------------------------------------
// Deep Study Toggle
// ---------------------------------------------------------------------------

class _DeepStudyToggle extends StatelessWidget {
  const _DeepStudyToggle({
    required this.isEnabled,
    required this.onChanged,
  });

  final bool isEnabled;
  final ValueChanged<bool> onChanged;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;

    return Card(
      elevation: 0,
      color: isEnabled
          ? colorScheme.primaryContainer.withValues(alpha: 0.3)
          : colorScheme.surfaceContainerLow,
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(RadiusTokens.lg),
        side: BorderSide(
          color: isEnabled
              ? colorScheme.primary.withValues(alpha: 0.3)
              : colorScheme.outlineVariant,
        ),
      ),
      child: Padding(
        padding: const EdgeInsets.symmetric(
          horizontal: SpacingTokens.md,
          vertical: SpacingTokens.sm,
        ),
        child: Row(
          children: [
            Icon(
              Icons.self_improvement_rounded,
              color: isEnabled
                  ? colorScheme.primary
                  : colorScheme.onSurfaceVariant,
              size: 24,
            ),
            const SizedBox(width: SpacingTokens.md),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    'Deep Study Mode',
                    style: theme.textTheme.titleSmall?.copyWith(
                      fontWeight: FontWeight.w600,
                      color: isEnabled
                          ? colorScheme.primary
                          : colorScheme.onSurface,
                    ),
                  ),
                  Text(
                    'Extended sessions with structured breaks',
                    style: theme.textTheme.bodySmall?.copyWith(
                      color: colorScheme.onSurfaceVariant,
                    ),
                  ),
                ],
              ),
            ),
            Switch.adaptive(
              value: isEnabled,
              onChanged: onChanged,
            ),
          ],
        ),
      ),
    );
  }
}

// ---------------------------------------------------------------------------
// Duration Selector
// ---------------------------------------------------------------------------

class _DurationSelector extends StatelessWidget {
  const _DurationSelector({
    required this.selectedDuration,
    required this.onSelected,
  });

  final int selectedDuration;
  final ValueChanged<int> onSelected;

  @override
  Widget build(BuildContext context) {
    return Row(
      children: DeepStudyDurations.allowed.map((duration) {
        final isSelected = duration == selectedDuration;
        return Expanded(
          child: Padding(
            padding: const EdgeInsets.symmetric(horizontal: SpacingTokens.xxs),
            child: _DurationCard(
              minutes: duration,
              isSelected: isSelected,
              onTap: () => onSelected(duration),
            ),
          ),
        );
      }).toList(),
    );
  }
}

class _DurationCard extends StatelessWidget {
  const _DurationCard({
    required this.minutes,
    required this.isSelected,
    required this.onTap,
  });

  final int minutes;
  final bool isSelected;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;

    return GestureDetector(
      onTap: onTap,
      child: AnimatedContainer(
        duration: AnimationTokens.fast,
        padding: const EdgeInsets.symmetric(
          vertical: SpacingTokens.md,
          horizontal: SpacingTokens.sm,
        ),
        decoration: BoxDecoration(
          color: isSelected
              ? colorScheme.primaryContainer
              : colorScheme.surfaceContainerLow,
          borderRadius: BorderRadius.circular(RadiusTokens.lg),
          border: Border.all(
            color: isSelected
                ? colorScheme.primary
                : colorScheme.outlineVariant,
            width: isSelected ? 2 : 1,
          ),
        ),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Text(
              '$minutes',
              style: theme.textTheme.headlineSmall?.copyWith(
                fontWeight: FontWeight.w700,
                color: isSelected
                    ? colorScheme.onPrimaryContainer
                    : colorScheme.onSurface,
              ),
            ),
            Text(
              'min',
              style: theme.textTheme.labelSmall?.copyWith(
                color: isSelected
                    ? colorScheme.onPrimaryContainer
                    : colorScheme.onSurfaceVariant,
              ),
            ),
          ],
        ),
      ),
    );
  }
}

// ---------------------------------------------------------------------------
// Block Preview
// ---------------------------------------------------------------------------

class _BlockPreview extends StatelessWidget {
  const _BlockPreview({required this.session});

  final DeepStudySession session;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;
    final blocks = session.blocks;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Text(
          'Session Structure',
          style: theme.textTheme.titleSmall?.copyWith(
            fontWeight: FontWeight.w600,
          ),
        ),
        const SizedBox(height: SpacingTokens.sm),

        // Visual timeline of blocks
        ...blocks.asMap().entries.expand((entry) {
          final index = entry.key;
          final block = entry.value;
          final isLast = index == blocks.length - 1;

          return [
            _BlockTile(block: block),
            if (!isLast)
              const _BreakTile(
                minutes: DeepStudySession.breakDurationMinutes,
              ),
          ];
        }),

        const SizedBox(height: SpacingTokens.sm),

        // Summary row
        Row(
          mainAxisAlignment: MainAxisAlignment.spaceBetween,
          children: [
            Text(
              '${session.totalStudyMinutes} min study',
              style: theme.textTheme.labelMedium?.copyWith(
                color: colorScheme.primary,
                fontWeight: FontWeight.w600,
              ),
            ),
            Text(
              '${session.totalBreakMinutes} min breaks',
              style: theme.textTheme.labelMedium?.copyWith(
                color: colorScheme.onSurfaceVariant,
              ),
            ),
            Text(
              '${session.blockCount} blocks',
              style: theme.textTheme.labelMedium?.copyWith(
                color: colorScheme.onSurfaceVariant,
              ),
            ),
          ],
        ),
      ],
    );
  }
}

class _BlockTile extends StatelessWidget {
  const _BlockTile({required this.block});

  final DeepStudyBlock block;

  IconData get _icon {
    switch (block.type) {
      case DeepStudyBlockType.review:
        return Icons.replay_rounded;
      case DeepStudyBlockType.deep:
        return Icons.psychology_rounded;
      case DeepStudyBlockType.synthesis:
        return Icons.hub_rounded;
    }
  }

  String get _label {
    switch (block.type) {
      case DeepStudyBlockType.review:
        return 'Review + New';
      case DeepStudyBlockType.deep:
        return 'Deep Practice';
      case DeepStudyBlockType.synthesis:
        return 'Synthesis';
    }
  }

  Color _color(ColorScheme colorScheme) {
    switch (block.type) {
      case DeepStudyBlockType.review:
        return const Color(0xFF42A5F5); // Blue
      case DeepStudyBlockType.deep:
        return const Color(0xFFFFB300); // Amber
      case DeepStudyBlockType.synthesis:
        return const Color(0xFF66BB6A); // Green
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;
    final color = _color(colorScheme);

    return Container(
      padding: const EdgeInsets.all(SpacingTokens.sm),
      decoration: BoxDecoration(
        color: color.withValues(alpha: 0.08),
        borderRadius: BorderRadius.circular(RadiusTokens.md),
        border: Border.all(color: color.withValues(alpha: 0.2)),
      ),
      child: Row(
        children: [
          Container(
            width: 32,
            height: 32,
            decoration: BoxDecoration(
              color: color.withValues(alpha: 0.15),
              shape: BoxShape.circle,
            ),
            child: Icon(_icon, size: 16, color: color),
          ),
          const SizedBox(width: SpacingTokens.sm),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  'Block ${block.blockNumber}: $_label',
                  style: theme.textTheme.bodyMedium?.copyWith(
                    fontWeight: FontWeight.w600,
                    color: colorScheme.onSurface,
                  ),
                ),
                Text(
                  '${block.durationMinutes} min · ~${block.estimatedQuestions} questions',
                  style: theme.textTheme.bodySmall?.copyWith(
                    color: colorScheme.onSurfaceVariant,
                  ),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

class _BreakTile extends StatelessWidget {
  const _BreakTile({required this.minutes});

  final int minutes;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;

    return Padding(
      padding: const EdgeInsets.symmetric(vertical: SpacingTokens.xs),
      child: Row(
        children: [
          const SizedBox(width: SpacingTokens.md),
          Container(
            width: 2,
            height: 20,
            color: colorScheme.outlineVariant,
          ),
          const SizedBox(width: SpacingTokens.md),
          Icon(
            Icons.coffee_rounded,
            size: 14,
            color: colorScheme.onSurfaceVariant,
          ),
          const SizedBox(width: SpacingTokens.xs),
          Text(
            '$minutes min break',
            style: theme.textTheme.labelSmall?.copyWith(
              color: colorScheme.onSurfaceVariant,
              fontStyle: FontStyle.italic,
            ),
          ),
        ],
      ),
    );
  }
}
