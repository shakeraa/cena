// =============================================================================
// Cena Adaptive Learning Platform — Concept Summary Card (MOB-VIS-012)
// =============================================================================
// Formula-first concept introduction shown before the first question on a
// new concept. Student sees the formula + explanation, then taps "I understand"
// to proceed. Matches the SmartyMe formula-card pattern.
// =============================================================================

import 'package:cached_network_image/cached_network_image.dart';
import 'package:flutter/material.dart';
import 'package:shimmer/shimmer.dart';

import '../../../core/config/app_config.dart';
import '../../../core/theme/glass_widgets.dart';
import '../../../l10n/app_localizations.dart';
import 'math_text.dart';

/// Concept introduction card shown before the first question on a new concept.
///
/// Displays the concept name, hero formula, explanation, and optional diagram
/// thumbnail. Student taps "I understand, let's practice" to proceed.
class ConceptSummaryCard extends StatelessWidget {
  const ConceptSummaryCard({
    super.key,
    required this.conceptName,
    required this.formula,
    this.explanation,
    this.diagramThumbnailUrl,
    this.subjectColor,
    required this.onReady,
  });

  /// Concept display name (localized).
  final String conceptName;

  /// Primary formula in LaTeX (e.g., "V = IR", "F = ma").
  final String formula;

  /// Brief 1-2 line explanation of the concept.
  final String? explanation;

  /// Optional diagram thumbnail URL for visual preview.
  final String? diagramThumbnailUrl;

  /// Subject primary color for styling. Falls back to theme primary.
  final Color? subjectColor;

  /// Called when student taps "I understand, let's practice".
  final VoidCallback onReady;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;
    final l = AppLocalizations.of(context);
    final accent = subjectColor ?? colorScheme.primary;

    return Center(
      child: SingleChildScrollView(
        padding: const EdgeInsets.all(SpacingTokens.lg),
        child: GlassCard(
          padding: const EdgeInsets.all(SpacingTokens.xl),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              // Subject color dot
              Container(
                width: 8,
                height: 8,
                decoration: BoxDecoration(
                  shape: BoxShape.circle,
                  color: accent,
                ),
              ),
              const SizedBox(height: SpacingTokens.md),

              // Concept name
              Text(
                conceptName,
                style: theme.textTheme.titleLarge?.copyWith(
                  fontWeight: FontWeight.w700,
                ),
                textAlign: TextAlign.center,
              ),

              const SizedBox(height: SpacingTokens.lg),

              // Hero formula in GlassContainer
              GlassContainer(
                blur: 12,
                opacity: 0.08,
                borderRadius: BorderRadius.circular(RadiusTokens.xl),
                child: Padding(
                  padding: const EdgeInsets.symmetric(
                    horizontal: SpacingTokens.lg,
                    vertical: SpacingTokens.md,
                  ),
                  child: MathText(
                    content: formula,
                    textStyle: TextStyle(
                      fontSize: TypographyTokens.displayMedium,
                      fontWeight: FontWeight.w700,
                      color: accent,
                    ),
                    mathColor: accent,
                    mathBackground: accent.withValues(alpha: 0.08),
                  ),
                ),
              ),

              // Explanation
              if (explanation != null && explanation!.isNotEmpty) ...[
                const SizedBox(height: SpacingTokens.lg),
                Text(
                  explanation!,
                  style: theme.textTheme.bodyMedium?.copyWith(
                    color: colorScheme.onSurfaceVariant,
                    height: 1.6,
                  ),
                  textAlign: TextAlign.center,
                ),
              ],

              // Diagram thumbnail
              if (diagramThumbnailUrl != null) ...[
                const SizedBox(height: SpacingTokens.lg),
                ClipRRect(
                  borderRadius: BorderRadius.circular(RadiusTokens.lg),
                  child: ConstrainedBox(
                    constraints: const BoxConstraints(maxHeight: 120),
                    child: CachedNetworkImage(
                      imageUrl: diagramThumbnailUrl!,
                      fit: BoxFit.contain,
                      placeholder: (_, __) => Shimmer.fromColors(
                        baseColor: colorScheme.surfaceContainerHighest,
                        highlightColor: colorScheme.surface,
                        child: Container(
                          height: 120,
                          color: colorScheme.surfaceContainerHighest,
                        ),
                      ),
                      errorWidget: (_, __, ___) => const SizedBox.shrink(),
                    ),
                  ),
                ),
              ],

              const SizedBox(height: SpacingTokens.xl),

              // CTA button
              FilledButton.icon(
                onPressed: onReady,
                icon: const Icon(Icons.play_arrow_rounded),
                label: Text(l.iUnderstand),
                style: FilledButton.styleFrom(
                  minimumSize: const Size(double.infinity, 48),
                  backgroundColor: accent,
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}
