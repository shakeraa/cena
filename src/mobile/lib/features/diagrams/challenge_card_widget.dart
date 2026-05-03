// =============================================================================
// Cena Platform — Challenge Card Widget (MOB-DIAG-002)
// Renders a ChallengeCard as a game-like interactive card with diagram,
// question (LaTeX), answer input, hints, XP reward, and feedback.
// =============================================================================

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../core/config/app_config.dart';
import '../../core/theme/glass_widgets.dart';
import '../../core/theme/micro_interactions.dart';
import '../session/widgets/math_text.dart';
import 'diagram_viewer.dart';
import 'models/diagram_models.dart';

class ChallengeCardWidget extends StatefulWidget {
  const ChallengeCardWidget({
    super.key,
    required this.card,
    this.onComplete,
    this.onNextCard,
  });

  final ChallengeCard card;
  final void Function(bool isCorrect, int xpEarned)? onComplete;
  final void Function(String nextCardId)? onNextCard;

  @override
  State<ChallengeCardWidget> createState() => _ChallengeCardWidgetState();
}

class _ChallengeCardWidgetState extends State<ChallengeCardWidget> {
  int? _selectedOptionIndex;
  final _numericController = TextEditingController();
  final _expressionController = TextEditingController();
  bool _isAnswered = false;
  bool _isCorrect = false;
  String? _wrongFeedback;
  final _shakeKey = GlobalKey<ShakeWidgetState>();

  @override
  void dispose() {
    _numericController.dispose();
    _expressionController.dispose();
    super.dispose();
  }

  Color _tierColor() => switch (widget.card.tier) {
        ChallengeTier.beginner => const Color(0xFF4CAF50),
        ChallengeTier.intermediate => const Color(0xFF2196F3),
        ChallengeTier.advanced => const Color(0xFFFF9800),
        ChallengeTier.expert => const Color(0xFFF44336),
      };

  // -- Answer evaluation ----------------------------------------------------

  void _submitMcq(int index) {
    if (_isAnswered) return;
    final option = widget.card.options[index];
    final locale = Localizations.localeOf(context).languageCode;
    setState(() {
      _selectedOptionIndex = index;
      _isAnswered = true;
      _isCorrect = option.isCorrect;
      // FIND-pedagogy-004: resolve per-option distractor feedback in the
      // student's current locale (was hard-coded to Hebrew).
      _wrongFeedback = option.isCorrect ? null : option.localizedFeedback(locale);
    });
    _handleResult();
  }

  void _submitNumeric() {
    if (_isAnswered) return;
    final value = double.tryParse(_numericController.text);
    if (value == null) return;
    final expected = widget.card.expectedValue ?? 0;
    final tol = widget.card.tolerance ?? 0.01;
    setState(() { _isAnswered = true; _isCorrect = (value - expected).abs() <= tol; });
    _handleResult();
  }

  void _submitExpression() {
    if (_isAnswered) return;
    final input = _expressionController.text.trim();
    if (input.isEmpty) return;
    setState(() { _isAnswered = true; _isCorrect = input == (widget.card.expectedExpression ?? ''); });
    _handleResult();
  }

  void _handleResult() {
    if (_isCorrect) {
      HapticFeedback.mediumImpact();
      widget.onComplete?.call(true, widget.card.xpReward);
    } else {
      _shakeKey.currentState?.shake();
      widget.onComplete?.call(false, 0);
    }
  }

  // -- Build ----------------------------------------------------------------

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final cs = theme.colorScheme;
    final tierColor = _tierColor();
    final locale = Localizations.localeOf(context).languageCode;

    // FIND-pedagogy-004 — refuse to render Hebrew content for English
    // students. If the active locale is 'en' and any ChallengeOption is
    // missing an English translation, hide the card entirely. This
    // matches the product stance that English is the primary locale
    // and Hebrew must be hideable outside Israel.
    if (!challengeCardSupportsLocale(widget.card, locale)) {
      return _unavailablePlaceholder(theme, cs);
    }

    return ShakeWidget(
      key: _shakeKey,
      child: GlassCard(
        borderOpacity: 0.5,
        child: GestureDetector(
          onLongPress: _showHint,
          child: Stack(
            children: [
              // Tier glow border overlay
              Positioned.fill(
                child: IgnorePointer(
                  child: DecoratedBox(
                    decoration: BoxDecoration(
                      borderRadius: BorderRadius.circular(RadiusTokens.lg),
                      border: Border.all(color: tierColor.withValues(alpha: 0.6), width: 2),
                      boxShadow: [BoxShadow(color: tierColor.withValues(alpha: 0.25), blurRadius: 16, spreadRadius: 2)],
                    ),
                  ),
                ),
              ),
              // Content
              Padding(
                padding: const EdgeInsets.all(SpacingTokens.sm),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    _xpBadge(theme),
                    const SizedBox(height: SpacingTokens.sm),
                    SizedBox(
                      height: 180,
                      child: InteractiveDiagramViewer(diagram: widget.card.diagram),
                    ),
                    const SizedBox(height: SpacingTokens.md),
                    MathText(content: widget.card.questionHe, textStyle: theme.textTheme.titleMedium?.copyWith(fontWeight: FontWeight.w600)),
                    const SizedBox(height: SpacingTokens.md),
                    _buildAnswerInput(theme, cs),
                    if (_isAnswered && !_isCorrect && _wrongFeedback != null) ...[
                      const SizedBox(height: SpacingTokens.sm),
                      _feedbackBanner(cs, theme),
                    ],
                    if (_isAnswered && _isCorrect) ...[
                      const SizedBox(height: SpacingTokens.md),
                      _correctOverlay(theme, cs),
                    ],
                  ],
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }

  // -- Sub-widgets ----------------------------------------------------------

  Widget _xpBadge(ThemeData theme) => Align(
        alignment: AlignmentDirectional.topEnd,
        child: Container(
          padding: const EdgeInsets.symmetric(horizontal: SpacingTokens.sm, vertical: SpacingTokens.xs),
          decoration: BoxDecoration(
            gradient: const LinearGradient(colors: [Color(0xFFFFD54F), Color(0xFFFFA000)]),
            borderRadius: BorderRadius.circular(RadiusTokens.full),
            boxShadow: [BoxShadow(color: const Color(0xFFFFA000).withValues(alpha: 0.3), blurRadius: 6, offset: const Offset(0, 2))],
          ),
          child: Row(mainAxisSize: MainAxisSize.min, children: [
            const Icon(Icons.star_rounded, size: 14, color: Colors.white),
            const SizedBox(width: SpacingTokens.xxs),
            Text('${widget.card.xpReward} XP', style: theme.textTheme.labelSmall?.copyWith(color: Colors.white, fontWeight: FontWeight.w700)),
          ]),
        ),
      );

  Widget _feedbackBanner(ColorScheme cs, ThemeData theme) => Container(
        padding: const EdgeInsets.all(SpacingTokens.sm),
        decoration: BoxDecoration(color: cs.errorContainer, borderRadius: BorderRadius.circular(RadiusTokens.md)),
        child: Text(_wrongFeedback!, style: theme.textTheme.bodySmall?.copyWith(color: cs.onErrorContainer)),
      );

  Widget _buildAnswerInput(ThemeData theme, ColorScheme cs) => switch (widget.card.answerType) {
        ChallengeAnswerType.multipleChoice => _mcqOptions(theme, cs),
        ChallengeAnswerType.numeric => _numericInput(theme, cs),
        ChallengeAnswerType.expression => _expressionInput(theme, cs),
        ChallengeAnswerType.dragLabel => DragLabelDiagram(
            diagram: widget.card.diagram,
            onAllPlaced: (ok) { if (_isAnswered) return; setState(() { _isAnswered = true; _isCorrect = ok; }); _handleResult(); },
          ),
        ChallengeAnswerType.tapHotspot => Container(
            padding: const EdgeInsets.all(SpacingTokens.sm),
            decoration: BoxDecoration(color: cs.tertiaryContainer, borderRadius: BorderRadius.circular(RadiusTokens.md)),
            child: Text('Tap the correct part of the diagram above', textAlign: TextAlign.center,
                style: theme.textTheme.bodySmall?.copyWith(color: cs.onTertiaryContainer, fontWeight: FontWeight.w500)),
          ),
      };

  // -- MCQ ------------------------------------------------------------------

  Widget _mcqOptions(ThemeData theme, ColorScheme cs) {
    final locale = Localizations.localeOf(context).languageCode;
    return Column(
      children: widget.card.options.asMap().entries.map((e) {
        final i = e.key;
        final opt = e.value;
        final selected = _selectedOptionIndex == i;
        final correct = _isAnswered && opt.isCorrect;
        final wrong = _isAnswered && selected && !opt.isCorrect;
        final c = correct ? const Color(0xFF4CAF50) : wrong ? cs.error : selected ? cs.primary : cs.surfaceContainerHighest;

        return Padding(
          padding: const EdgeInsets.only(bottom: SpacingTokens.sm),
          child: TapScaleButton(
            onTap: _isAnswered ? null : () => _submitMcq(i),
            child: AnimatedContainer(
              duration: AnimationTokens.fast,
              padding: const EdgeInsets.symmetric(horizontal: SpacingTokens.md, vertical: SpacingTokens.sm),
              decoration: BoxDecoration(
                color: c.withValues(alpha: 0.15),
                borderRadius: BorderRadius.circular(RadiusTokens.lg),
                border: Border.all(color: c.withValues(alpha: 0.6), width: selected || correct ? 2 : 1),
              ),
              child: Row(children: [
                // FIND-pedagogy-004: resolve option text in the student's
                // current locale, not hard-coded Hebrew.
                Expanded(child: Text(opt.localizedText(locale), style: theme.textTheme.bodyMedium?.copyWith(fontWeight: selected ? FontWeight.w600 : FontWeight.normal))),
                if (correct) const Icon(Icons.check_circle_rounded, size: 20, color: Color(0xFF4CAF50)),
                if (wrong) Icon(Icons.cancel_rounded, size: 20, color: cs.error),
              ]),
            ),
          ),
        );
      }).toList(),
    );
  }

  // -- Hidden / unavailable placeholder (FIND-pedagogy-004) ----------------

  /// Rendered when the current locale lacks a translation for at least
  /// one option on the card. We deliberately render an inert placeholder
  /// (not a popup, not an error) so the parent screen can still lay out
  /// the slot without crashing, while guaranteeing no Hebrew text leaks
  /// into an English-locale student's session.
  Widget _unavailablePlaceholder(ThemeData theme, ColorScheme cs) => GlassCard(
        borderOpacity: 0.3,
        child: Padding(
          padding: const EdgeInsets.all(SpacingTokens.lg),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              Icon(Icons.translate_rounded, size: 32, color: cs.onSurface.withValues(alpha: 0.5)),
              const SizedBox(height: SpacingTokens.sm),
              Text(
                'This challenge is not available in your language yet.',
                textAlign: TextAlign.center,
                style: theme.textTheme.bodySmall?.copyWith(color: cs.onSurface.withValues(alpha: 0.7)),
              ),
            ],
          ),
        ),
      );

  // -- Numeric / Expression -------------------------------------------------

  Widget _numericInput(ThemeData theme, ColorScheme cs) => Row(children: [
        Expanded(child: TextField(
          controller: _numericController, keyboardType: const TextInputType.numberWithOptions(decimal: true), enabled: !_isAnswered,
          decoration: InputDecoration(hintText: 'Enter value', border: OutlineInputBorder(borderRadius: BorderRadius.circular(RadiusTokens.md)),
              suffixText: widget.card.tolerance != null ? '\u00B1${widget.card.tolerance}' : null),
        )),
        const SizedBox(width: SpacingTokens.sm),
        FilledButton(onPressed: _isAnswered ? null : _submitNumeric, child: const Text('Check')),
      ]);

  Widget _expressionInput(ThemeData theme, ColorScheme cs) => Row(children: [
        Expanded(child: TextField(
          controller: _expressionController, enabled: !_isAnswered,
          decoration: InputDecoration(hintText: 'LaTeX expression', border: OutlineInputBorder(borderRadius: BorderRadius.circular(RadiusTokens.md))),
        )),
        const SizedBox(width: SpacingTokens.sm),
        FilledButton(onPressed: _isAnswered ? null : _submitExpression, child: const Text('Check')),
      ]);

  // -- Correct overlay + Next -----------------------------------------------

  Widget _correctOverlay(ThemeData theme, ColorScheme cs) => Column(children: [
        Container(
          padding: const EdgeInsets.all(SpacingTokens.sm),
          decoration: BoxDecoration(color: const Color(0xFF4CAF50).withValues(alpha: 0.15), borderRadius: BorderRadius.circular(RadiusTokens.md)),
          child: Row(mainAxisAlignment: MainAxisAlignment.center, children: [
            const Icon(Icons.check_circle_rounded, color: Color(0xFF4CAF50), size: 24),
            const SizedBox(width: SpacingTokens.sm),
            Text('+${widget.card.xpReward} XP', style: theme.textTheme.titleSmall?.copyWith(color: const Color(0xFF4CAF50), fontWeight: FontWeight.w700)),
          ]),
        ),
        if (widget.card.nextCardId != null) ...[
          const SizedBox(height: SpacingTokens.sm),
          TapScaleButton(
            onTap: () => widget.onNextCard?.call(widget.card.nextCardId!),
            child: Container(
              padding: const EdgeInsets.symmetric(horizontal: SpacingTokens.lg, vertical: SpacingTokens.sm),
              decoration: BoxDecoration(color: cs.primary, borderRadius: BorderRadius.circular(RadiusTokens.lg)),
              child: Text('Next Card', textAlign: TextAlign.center, style: theme.textTheme.labelLarge?.copyWith(color: cs.onPrimary, fontWeight: FontWeight.w600)),
            ),
          ),
        ],
      ]);

  // -- Hint (long-press) ----------------------------------------------------

  void _showHint() {
    final hint = widget.card.hintHe;
    if (hint == null || hint.isEmpty) return;
    HapticFeedback.lightImpact();
    ScaffoldMessenger.of(context).showSnackBar(SnackBar(
      content: Text(hint),
      behavior: SnackBarBehavior.floating,
      duration: const Duration(seconds: 3),
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(RadiusTokens.md)),
    ));
  }
}
