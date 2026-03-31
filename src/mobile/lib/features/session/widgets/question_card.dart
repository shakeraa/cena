// =============================================================================
// Cena Adaptive Learning Platform — Question Card
// Renders an Exercise for MCQ, free-text, numeric, proof, and diagram types.
// =============================================================================

import 'dart:async';

import 'package:cached_network_image/cached_network_image.dart';
import 'package:flutter/material.dart';

import '../../../core/config/app_config.dart';
import '../../../core/models/domain_models.dart';
import '../../../core/services/interaction_feedback_service.dart';
import '../../../l10n/app_localizations.dart';
import '../../diagrams/diagram_viewer.dart';
import '../../diagrams/models/diagram_models.dart';
import 'math_text.dart';

/// Renders the current [exercise] and dispatches selection events for MCQ.
///
/// For MCQ the card shows [OptionTile]s. For other types it shows the
/// content and delegates input capture to [AnswerInput] (sibling widget).
class QuestionCard extends StatelessWidget {
  const QuestionCard({
    super.key,
    required this.exercise,
    this.selectedOption,
    this.onOptionSelected,
    this.isSubmitting = false,
    this.interactiveDiagram,
  });

  final Exercise exercise;

  /// Index into [exercise.options] that is currently selected (MCQ only).
  final int? selectedOption;

  /// Callback invoked when the student taps an MCQ option.
  final ValueChanged<int>? onOptionSelected;

  /// When true, option tiles are non-interactive (answer being evaluated).
  final bool isSubmitting;

  /// Optional interactive SVG diagram (from the generation pipeline).
  /// When provided, renders [InteractiveDiagramViewer] instead of the
  /// flat image fallback [_DiagramView].
  final ConceptDiagram? interactiveDiagram;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;

    return Card(
      elevation: 2,
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(RadiusTokens.xl),
      ),
      child: Padding(
        padding: const EdgeInsets.all(SpacingTokens.md),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            // Difficulty badge + question type pill
            Row(
              children: [
                _DifficultyBadge(difficulty: exercise.difficulty),
                const SizedBox(width: SpacingTokens.sm),
                _TypePill(questionType: exercise.questionType),
              ],
            ),
            const SizedBox(height: SpacingTokens.md),

            // Question content (supports inline LaTeX notation)
            _QuestionText(content: exercise.content),

            // Interactive diagram (ConceptDiagram) takes priority over
            // flat URL-based diagram image for backward compatibility.
            if (interactiveDiagram != null) ...[
              const SizedBox(height: SpacingTokens.md),
              InteractiveDiagramViewer(diagram: interactiveDiagram!),
            ] else if (exercise.diagram != null) ...[
              const SizedBox(height: SpacingTokens.md),
              _DiagramView(diagramUrl: exercise.diagram!),
            ],

            // MCQ options
            if (exercise.questionType == QuestionType.multipleChoice &&
                exercise.options != null) ...[
              const SizedBox(height: SpacingTokens.md),
              _OptionList(
                options: exercise.options!,
                selectedIndex: selectedOption,
                onSelected: isSubmitting ? null : onOptionSelected,
                colorScheme: colorScheme,
              ),
            ],
          ],
        ),
      ),
    );
  }
}

// ---------------------------------------------------------------------------
// Question text — renders content with LaTeX math via flutter_math_fork.
// ---------------------------------------------------------------------------

/// Renders question text with proper LaTeX math rendering.
/// Delegates to [MathText] which handles `$...$` (inline) and `$$...$$`
/// (display) math delimiters using flutter_math_fork.
class _QuestionText extends StatelessWidget {
  const _QuestionText({required this.content});

  final String content;

  @override
  Widget build(BuildContext context) {
    return MathText(
      content: content,
      textStyle: Theme.of(context).textTheme.bodyLarge,
      mathColor: SubjectColorTokens.mathPrimary,
      mathBackground: SubjectColorTokens.mathBackground,
    );
  }
}

// ---------------------------------------------------------------------------
// MCQ option list
// ---------------------------------------------------------------------------

class _OptionList extends StatelessWidget {
  const _OptionList({
    required this.options,
    required this.selectedIndex,
    required this.onSelected,
    required this.colorScheme,
  });

  final List<String> options;
  final int? selectedIndex;
  final ValueChanged<int>? onSelected;
  final ColorScheme colorScheme;

  @override
  Widget build(BuildContext context) {
    return Column(
      children: options.asMap().entries.map((entry) {
        return Padding(
          padding: const EdgeInsets.only(bottom: SpacingTokens.sm),
          child: OptionTile(
            index: entry.key,
            text: entry.value,
            isSelected: selectedIndex == entry.key,
            isEnabled: onSelected != null,
            onTap: onSelected == null
                ? null
                : () {
                    unawaited(InteractionFeedbackService.selectionClick());
                    onSelected!(entry.key);
                  },
            colorScheme: colorScheme,
          ),
        );
      }).toList(),
    );
  }
}

/// A single selectable MCQ option tile.
class OptionTile extends StatelessWidget {
  const OptionTile({
    super.key,
    required this.index,
    required this.text,
    required this.isSelected,
    required this.isEnabled,
    required this.onTap,
    required this.colorScheme,
  });

  final int index;
  final String text;
  final bool isSelected;
  final bool isEnabled;
  final VoidCallback? onTap;
  final ColorScheme colorScheme;

  static const List<String> _labels = ['א', 'ב', 'ג', 'ד'];

  String get _label => index < _labels.length ? _labels[index] : '${index + 1}';

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final borderColor =
        isSelected ? colorScheme.primary : colorScheme.outlineVariant;
    final bgColor = isSelected
        ? colorScheme.primary.withValues(alpha: 0.1)
        : colorScheme.surface;
    final labelColor =
        isSelected ? colorScheme.primary : colorScheme.onSurfaceVariant;

    return AnimatedContainer(
      duration: AnimationTokens.fast,
      decoration: BoxDecoration(
        color: bgColor,
        borderRadius: BorderRadius.circular(RadiusTokens.lg),
        border: Border.all(color: borderColor, width: isSelected ? 2 : 1),
      ),
      child: Material(
        color: Colors.transparent,
        child: InkWell(
          borderRadius: BorderRadius.circular(RadiusTokens.lg),
          onTap: isEnabled ? onTap : null,
          child: Padding(
            padding: const EdgeInsets.symmetric(
              horizontal: SpacingTokens.md,
              vertical: SpacingTokens.sm,
            ),
            child: Row(
              children: [
                Container(
                  width: 28,
                  height: 28,
                  alignment: Alignment.center,
                  decoration: BoxDecoration(
                    shape: BoxShape.circle,
                    color: isSelected
                        ? colorScheme.primary
                        : colorScheme.surfaceContainerHighest,
                  ),
                  child: Text(
                    _label,
                    style: theme.textTheme.labelMedium?.copyWith(
                      color: isSelected
                          ? colorScheme.onPrimary
                          : colorScheme.onSurfaceVariant,
                      fontWeight: FontWeight.w700,
                    ),
                  ),
                ),
                const SizedBox(width: SpacingTokens.md),
                Expanded(
                  child: Text(
                    text,
                    style: theme.textTheme.bodyMedium?.copyWith(
                      color: isSelected
                          ? colorScheme.onSurface
                          : colorScheme.onSurface,
                      fontWeight:
                          isSelected ? FontWeight.w600 : FontWeight.normal,
                    ),
                  ),
                ),
                if (isSelected)
                  Icon(Icons.check_circle_rounded, size: 20, color: labelColor),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

// ---------------------------------------------------------------------------
// Supporting small widgets
// ---------------------------------------------------------------------------

class _DifficultyBadge extends StatelessWidget {
  const _DifficultyBadge({required this.difficulty});

  final int difficulty;

  Color _color(int d) {
    if (d <= 3) return const Color(0xFF4CAF50);
    if (d <= 6) return const Color(0xFFFF9800);
    return const Color(0xFFF44336);
  }

  String _label(int d, AppLocalizations l) {
    if (d <= 3) return l.easy;
    if (d <= 6) return l.medium;
    return l.hard;
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final l = AppLocalizations.of(context);
    final color = _color(difficulty);
    return Container(
      padding: const EdgeInsets.symmetric(
          horizontal: SpacingTokens.sm, vertical: SpacingTokens.xxs),
      decoration: BoxDecoration(
        color: color.withValues(alpha: 0.15),
        borderRadius: BorderRadius.circular(RadiusTokens.full),
        border: Border.all(color: color.withValues(alpha: 0.4)),
      ),
      child: Text(
        '${_label(difficulty, l)} $difficulty/10',
        style: theme.textTheme.labelSmall?.copyWith(
          color: color,
          fontWeight: FontWeight.w600,
        ),
      ),
    );
  }
}

class _TypePill extends StatelessWidget {
  const _TypePill({required this.questionType});

  final QuestionType questionType;

  String _label(QuestionType t, AppLocalizations l) {
    switch (t) {
      case QuestionType.multipleChoice:
        return l.multipleChoice;
      case QuestionType.freeText:
        return l.freeText;
      case QuestionType.numeric:
        return l.numeric;
      case QuestionType.proof:
        return l.proof;
      case QuestionType.diagram:
        return l.diagram;
    }
  }

  IconData _icon(QuestionType t) {
    switch (t) {
      case QuestionType.multipleChoice:
        return Icons.radio_button_checked_rounded;
      case QuestionType.freeText:
        return Icons.edit_rounded;
      case QuestionType.numeric:
        return Icons.calculate_rounded;
      case QuestionType.proof:
        return Icons.format_list_numbered_rounded;
      case QuestionType.diagram:
        return Icons.image_rounded;
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final l = AppLocalizations.of(context);
    final color = theme.colorScheme.secondary;
    return Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        Icon(_icon(questionType), size: 12, color: color),
        const SizedBox(width: SpacingTokens.xxs),
        Text(
          _label(questionType, l),
          style: theme.textTheme.labelSmall?.copyWith(color: color),
        ),
      ],
    );
  }
}

class _DiagramView extends StatelessWidget {
  const _DiagramView({required this.diagramUrl});

  final String diagramUrl;

  bool get _isBase64 => diagramUrl.startsWith('data:');

  @override
  Widget build(BuildContext context) {
    if (_isBase64) {
      // Base64 images are rendered by the network image with a data URI
      return ClipRRect(
        borderRadius: BorderRadius.circular(RadiusTokens.md),
        child: Image.network(
          diagramUrl,
          fit: BoxFit.contain,
          errorBuilder: (_, __, ___) => _ErrorPlaceholder(),
        ),
      );
    }
    return ClipRRect(
      borderRadius: BorderRadius.circular(RadiusTokens.md),
      child: CachedNetworkImage(
        imageUrl: diagramUrl,
        fit: BoxFit.contain,
        placeholder: (_, __) => const SizedBox(
          height: 160,
          child: Center(child: CircularProgressIndicator()),
        ),
        errorWidget: (_, __, ___) => _ErrorPlaceholder(),
      ),
    );
  }
}

class _ErrorPlaceholder extends StatelessWidget {
  @override
  Widget build(BuildContext context) {
    final color = Theme.of(context).colorScheme.error;
    return Container(
      height: 100,
      decoration: BoxDecoration(
        color: color.withValues(alpha: 0.1),
        borderRadius: BorderRadius.circular(RadiusTokens.md),
      ),
      child: Center(
        child: Icon(Icons.broken_image_rounded, color: color),
      ),
    );
  }
}
