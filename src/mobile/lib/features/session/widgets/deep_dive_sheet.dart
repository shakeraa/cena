// =============================================================================
// Cena Adaptive Learning Platform — Deep Dive Sheet (MOB-034)
// =============================================================================
//
// Bottom sheet for Level 3 disclosure (Deep Dive).
// Shows full explanation after answer evaluation.
// Related concepts as tappable chips, "Why this answer?" section.
// Lazy-loaded: content is only fetched when the sheet is expanded.
// =============================================================================

import 'dart:async';

import 'package:flutter/material.dart';

import '../../../core/config/app_config.dart';

// ---------------------------------------------------------------------------
// Deep Dive Content Model
// ---------------------------------------------------------------------------

/// Content payload for the deep dive sheet. Lazy-loaded from the backend.
class DeepDiveContent {
  const DeepDiveContent({
    required this.fullExplanation,
    required this.whyThisAnswer,
    required this.relatedConcepts,
    this.theoryReference,
  });

  /// Full step-by-step solution explanation.
  final String fullExplanation;

  /// "Why this answer?" — short justification of the correct answer.
  final String whyThisAnswer;

  /// Related concept names the student can explore.
  final List<RelatedConcept> relatedConcepts;

  /// Optional reference to the underlying theory (chapter/section).
  final String? theoryReference;
}

/// A related concept that can be tapped to navigate deeper.
class RelatedConcept {
  const RelatedConcept({
    required this.id,
    required this.name,
    this.mastery,
  });

  /// Unique identifier for the concept (maps to knowledge graph node).
  final String id;

  /// Display name.
  final String name;

  /// Current mastery level [0.0, 1.0], null if unknown.
  final double? mastery;
}

// ---------------------------------------------------------------------------
// Content Provider (lazy fetch)
// ---------------------------------------------------------------------------

/// Async loader for deep dive content. The parent provides this callback
/// and it is only invoked when the sheet is first expanded.
typedef DeepDiveContentLoader = Future<DeepDiveContent> Function();

// ---------------------------------------------------------------------------
// Deep Dive Sheet Widget
// ---------------------------------------------------------------------------

/// Shows a bottom sheet with Level 3 disclosure content.
///
/// Content is lazy-loaded: the [contentLoader] is called only when the
/// sheet is first shown. Use [DeepDiveSheet.show] as the standard entry point.
class DeepDiveSheet extends StatefulWidget {
  const DeepDiveSheet({
    super.key,
    required this.contentLoader,
    this.onConceptTapped,
  });

  /// Lazy loader that fetches the deep dive content.
  final DeepDiveContentLoader contentLoader;

  /// Called when a related concept chip is tapped.
  final ValueChanged<RelatedConcept>? onConceptTapped;

  /// Shows the deep dive sheet as a modal bottom sheet.
  static Future<void> show({
    required BuildContext context,
    required DeepDiveContentLoader contentLoader,
    ValueChanged<RelatedConcept>? onConceptTapped,
  }) {
    return showModalBottomSheet<void>(
      context: context,
      isScrollControlled: true,
      useSafeArea: true,
      shape: const RoundedRectangleBorder(
        borderRadius: BorderRadius.vertical(
          top: Radius.circular(RadiusTokens.xl),
        ),
      ),
      builder: (context) => DraggableScrollableSheet(
        initialChildSize: 0.6,
        minChildSize: 0.3,
        maxChildSize: 0.9,
        expand: false,
        builder: (context, scrollController) => _SheetBody(
          contentLoader: contentLoader,
          onConceptTapped: onConceptTapped,
          scrollController: scrollController,
        ),
      ),
    );
  }

  @override
  State<DeepDiveSheet> createState() => _DeepDiveSheetState();
}

class _DeepDiveSheetState extends State<DeepDiveSheet> {
  @override
  Widget build(BuildContext context) {
    return _SheetBody(
      contentLoader: widget.contentLoader,
      onConceptTapped: widget.onConceptTapped,
    );
  }
}

// ---------------------------------------------------------------------------
// Sheet Body (handles lazy loading)
// ---------------------------------------------------------------------------

class _SheetBody extends StatefulWidget {
  const _SheetBody({
    required this.contentLoader,
    this.onConceptTapped,
    this.scrollController,
  });

  final DeepDiveContentLoader contentLoader;
  final ValueChanged<RelatedConcept>? onConceptTapped;
  final ScrollController? scrollController;

  @override
  State<_SheetBody> createState() => _SheetBodyState();
}

class _SheetBodyState extends State<_SheetBody> {
  DeepDiveContent? _content;
  Object? _error;
  bool _loading = true;

  @override
  void initState() {
    super.initState();
    _loadContent();
  }

  Future<void> _loadContent() async {
    try {
      final content = await widget.contentLoader();
      if (mounted) {
        setState(() {
          _content = content;
          _loading = false;
        });
      }
    } catch (e) {
      if (mounted) {
        setState(() {
          _error = e;
          _loading = false;
        });
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final cs = theme.colorScheme;

    if (_loading) {
      return const Padding(
        padding: EdgeInsets.all(SpacingTokens.xxl),
        child: Center(child: CircularProgressIndicator()),
      );
    }

    if (_error != null) {
      return Padding(
        padding: const EdgeInsets.all(SpacingTokens.lg),
        child: Center(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              Icon(Icons.error_outline, size: 48, color: cs.error),
              const SizedBox(height: SpacingTokens.sm),
              Text(
                'Could not load explanation',
                style: theme.textTheme.bodyLarge?.copyWith(color: cs.error),
              ),
              const SizedBox(height: SpacingTokens.md),
              FilledButton.tonal(
                onPressed: () {
                  setState(() {
                    _loading = true;
                    _error = null;
                  });
                  _loadContent();
                },
                child: const Text('Retry'),
              ),
            ],
          ),
        ),
      );
    }

    final content = _content!;

    return Directionality(
      textDirection: TextDirection.rtl,
      child: ListView(
        controller: widget.scrollController,
        padding: const EdgeInsets.fromLTRB(
          SpacingTokens.lg,
          SpacingTokens.sm,
          SpacingTokens.lg,
          SpacingTokens.xl,
        ),
        children: [
          // Drag handle
          Center(
            child: Container(
              width: 40,
              height: 4,
              margin: const EdgeInsets.only(bottom: SpacingTokens.md),
              decoration: BoxDecoration(
                color: cs.outlineVariant,
                borderRadius: BorderRadius.circular(RadiusTokens.full),
              ),
            ),
          ),

          // "Why this answer?" section
          _SectionHeader(
            icon: Icons.help_outline_rounded,
            title: 'למה התשובה הזו?',
            color: cs.primary,
          ),
          const SizedBox(height: SpacingTokens.sm),
          Text(
            content.whyThisAnswer,
            style: theme.textTheme.bodyLarge?.copyWith(
              fontFamily: TypographyTokens.hebrewFontFamily,
            ),
          ),
          const SizedBox(height: SpacingTokens.xl),

          // Full explanation
          _SectionHeader(
            icon: Icons.menu_book_rounded,
            title: 'הסבר מלא',
            color: cs.tertiary,
          ),
          const SizedBox(height: SpacingTokens.sm),
          Container(
            padding: const EdgeInsets.all(SpacingTokens.md),
            decoration: BoxDecoration(
              borderRadius: BorderRadius.circular(RadiusTokens.lg),
              color: cs.surfaceContainerHighest.withValues(alpha: 0.5),
            ),
            child: Text(
              content.fullExplanation,
              style: theme.textTheme.bodyMedium?.copyWith(
                fontFamily: TypographyTokens.hebrewFontFamily,
                height: 1.6,
              ),
            ),
          ),

          // Theory reference
          if (content.theoryReference != null) ...[
            const SizedBox(height: SpacingTokens.md),
            Row(
              children: [
                Icon(Icons.bookmark_outline_rounded,
                    size: 16, color: cs.onSurfaceVariant),
                const SizedBox(width: SpacingTokens.xs),
                Expanded(
                  child: Text(
                    content.theoryReference!,
                    style: theme.textTheme.labelMedium?.copyWith(
                      fontFamily: TypographyTokens.hebrewFontFamily,
                      color: cs.onSurfaceVariant,
                      fontStyle: FontStyle.italic,
                    ),
                  ),
                ),
              ],
            ),
          ],

          // Related concepts
          if (content.relatedConcepts.isNotEmpty) ...[
            const SizedBox(height: SpacingTokens.xl),
            _SectionHeader(
              icon: Icons.hub_rounded,
              title: 'מושגים קשורים',
              color: cs.secondary,
            ),
            const SizedBox(height: SpacingTokens.sm),
            Wrap(
              spacing: SpacingTokens.sm,
              runSpacing: SpacingTokens.sm,
              children: content.relatedConcepts.map((concept) {
                return _ConceptChip(
                  concept: concept,
                  onTap: widget.onConceptTapped != null
                      ? () => widget.onConceptTapped!(concept)
                      : null,
                );
              }).toList(),
            ),
          ],
        ],
      ),
    );
  }
}

// ---------------------------------------------------------------------------
// Section Header
// ---------------------------------------------------------------------------

class _SectionHeader extends StatelessWidget {
  const _SectionHeader({
    required this.icon,
    required this.title,
    required this.color,
  });

  final IconData icon;
  final String title;
  final Color color;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Row(
      children: [
        Icon(icon, size: 20, color: color),
        const SizedBox(width: SpacingTokens.sm),
        Text(
          title,
          style: theme.textTheme.titleMedium?.copyWith(
            fontFamily: TypographyTokens.hebrewFontFamily,
            fontWeight: FontWeight.w700,
            color: color,
          ),
        ),
      ],
    );
  }
}

// ---------------------------------------------------------------------------
// Concept Chip
// ---------------------------------------------------------------------------

class _ConceptChip extends StatelessWidget {
  const _ConceptChip({
    required this.concept,
    this.onTap,
  });

  final RelatedConcept concept;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final cs = theme.colorScheme;

    return ActionChip(
      avatar: concept.mastery != null
          ? SizedBox(
              width: 20,
              height: 20,
              child: CircularProgressIndicator(
                value: concept.mastery,
                strokeWidth: 2.5,
                backgroundColor: cs.outlineVariant,
                valueColor: AlwaysStoppedAnimation<Color>(
                  _masteryColor(concept.mastery!, cs),
                ),
              ),
            )
          : Icon(Icons.circle_outlined, size: 16, color: cs.outline),
      label: Text(
        concept.name,
        style: const TextStyle(
          fontFamily: TypographyTokens.hebrewFontFamily,
        ),
      ),
      onPressed: onTap,
    );
  }

  Color _masteryColor(double mastery, ColorScheme cs) {
    if (mastery >= 0.85) return const Color(0xFF388E3C);
    if (mastery >= 0.5) return const Color(0xFFFF8F00);
    return cs.error;
  }
}
