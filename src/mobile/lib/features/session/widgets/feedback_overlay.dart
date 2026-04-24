// =============================================================================
// Cena Adaptive Learning Platform — Answer Feedback Overlay
// Shown after answer evaluation: correct (green) / wrong (red) / partial (yellow).
// =============================================================================

import 'dart:async';

import 'package:flutter/material.dart';

import '../../../core/config/app_config.dart';
import '../../../core/models/domain_models.dart';
import '../../../core/services/interaction_feedback_service.dart';
import '../../../l10n/app_localizations.dart';
import 'math_text.dart';

/// Full-screen overlay displayed after the server returns an [AnswerResult].
///
/// Auto-dismisses after [autoDismissDelay] or when the student taps.
/// Triggers platform haptics on display.
class FeedbackOverlay extends StatefulWidget {
  const FeedbackOverlay({
    super.key,
    required this.result,
    required this.onDismiss,
    this.autoDismissDelay = const Duration(seconds: 3),
  });

  final AnswerResult result;
  final VoidCallback onDismiss;
  final Duration autoDismissDelay;

  @override
  State<FeedbackOverlay> createState() => _FeedbackOverlayState();
}

class _FeedbackOverlayState extends State<FeedbackOverlay>
    with SingleTickerProviderStateMixin {
  late final AnimationController _controller;
  late final Animation<double> _scale;
  late final Animation<double> _fade;
  Timer? _dismissTimer;

  @override
  void initState() {
    super.initState();

    _controller = AnimationController(
      vsync: this,
      duration: AnimationTokens.slow,
    );

    _scale = CurvedAnimation(
      parent: _controller,
      curve: Curves.elasticOut,
    );

    _fade = CurvedAnimation(
      parent: _controller,
      curve: Curves.easeIn,
    );

    _controller.forward();
    _triggerHaptic();

    _dismissTimer = Timer(widget.autoDismissDelay, widget.onDismiss);
  }

  @override
  void dispose() {
    _dismissTimer?.cancel();
    _controller.dispose();
    super.dispose();
  }

  void _triggerHaptic() {
    if (widget.result.isCorrect) {
      InteractionFeedbackService.correctAnswer();
    } else {
      InteractionFeedbackService.incorrectAnswer();
    }
  }

  Color get _overlayColor {
    if (widget.result.isCorrect) {
      return const Color(0xFF4CAF50);
    }
    if (widget.result.errorType == ErrorType.none) {
      // Partial credit — yellow
      return const Color(0xFFFF9800);
    }
    return const Color(0xFFF44336);
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final l = AppLocalizations.of(context);
    final isCorrect = widget.result.isCorrect;
    final isPartial = !isCorrect && widget.result.errorType == ErrorType.none;

    return GestureDetector(
      onTap: () {
        _dismissTimer?.cancel();
        widget.onDismiss();
      },
      child: FadeTransition(
        opacity: _fade,
        child: Container(
          color: _overlayColor.withValues(alpha: 0.92),
          child: SafeArea(
            child: Padding(
              padding: const EdgeInsets.all(SpacingTokens.xl),
              child: Column(
                mainAxisAlignment: MainAxisAlignment.center,
                children: [
                  // Animated result icon
                  ScaleTransition(
                    scale: _scale,
                    child: Icon(
                      isCorrect
                          ? Icons.check_circle
                          : isPartial
                              ? Icons.remove_circle
                              : Icons.cancel,
                      size: 96,
                      color: Colors.white,
                    ),
                  ),

                  const SizedBox(height: SpacingTokens.lg),

                  // Result headline
                  Text(
                    isCorrect
                        ? l.wellDone
                        : isPartial
                            ? l.partiallyCorrect
                            : l.incorrect,
                    style: theme.textTheme.headlineLarge?.copyWith(
                      color: Colors.white,
                      fontWeight: FontWeight.w800,
                    ),
                    textAlign: TextAlign.center,
                  ),

                  // XP award (correct answers)
                  if (isCorrect && widget.result.xpEarned > 0) ...[
                    const SizedBox(height: SpacingTokens.sm),
                    _XpBadge(xp: widget.result.xpEarned),
                  ],

                  const SizedBox(height: SpacingTokens.md),

                  // Error type badge (wrong answers)
                  if (!isCorrect &&
                      widget.result.errorType != ErrorType.none) ...[
                    _ErrorTypeBadge(errorType: widget.result.errorType),
                    const SizedBox(height: SpacingTokens.md),
                  ],

                  // Feedback text
                  if (widget.result.feedback.isNotEmpty)
                    Container(
                      padding: const EdgeInsets.all(SpacingTokens.md),
                      decoration: BoxDecoration(
                        color: Colors.white.withValues(alpha: 0.2),
                        borderRadius: BorderRadius.circular(RadiusTokens.xl),
                      ),
                      child: MathText(
                        content: widget.result.feedback,
                        textStyle: theme.textTheme.bodyLarge?.copyWith(color: Colors.white),
                        mathColor: Colors.white,
                        mathBackground: Colors.white.withValues(alpha: 0.15),
                      ),
                    ),

                  // Worked solution for wrong answers
                  if (!isCorrect && widget.result.workedSolution != null) ...[
                    const SizedBox(height: SpacingTokens.md),
                    _WorkedSolutionCard(
                        solution: widget.result.workedSolution!),
                  ],

                  const SizedBox(height: SpacingTokens.xl),

                  // Tap hint
                  Text(
                    l.tapToContinue,
                    style: theme.textTheme.bodyMedium?.copyWith(
                      color: Colors.white.withValues(alpha: 0.7),
                    ),
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}

// ---------------------------------------------------------------------------
// Supporting widgets
// ---------------------------------------------------------------------------

class _XpBadge extends StatelessWidget {
  const _XpBadge({required this.xp});

  final int xp;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Container(
      padding: const EdgeInsets.symmetric(
          horizontal: SpacingTokens.md, vertical: SpacingTokens.xs),
      decoration: BoxDecoration(
        color: Colors.white.withValues(alpha: 0.25),
        borderRadius: BorderRadius.circular(RadiusTokens.full),
        border: Border.all(color: Colors.white.withValues(alpha: 0.6)),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          const Icon(Icons.star_rounded, size: 18, color: Colors.yellow),
          const SizedBox(width: SpacingTokens.xs),
          Text(
            '+$xp XP',
            style: theme.textTheme.labelLarge?.copyWith(
              color: Colors.white,
              fontWeight: FontWeight.w700,
            ),
          ),
        ],
      ),
    );
  }
}

class _ErrorTypeBadge extends StatelessWidget {
  const _ErrorTypeBadge({required this.errorType});

  final ErrorType errorType;

  String _label(ErrorType t, AppLocalizations l) {
    switch (t) {
      case ErrorType.conceptual:
        return l.conceptualError;
      case ErrorType.procedural:
        return l.proceduralError;
      case ErrorType.careless:
        return l.carelessError;
      case ErrorType.notation:
        return l.notationError;
      case ErrorType.incomplete:
        return l.incompleteAnswer;
      case ErrorType.none:
        return '';
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final l = AppLocalizations.of(context);
    final label = _label(errorType, l);
    if (label.isEmpty) return const SizedBox.shrink();

    return Container(
      padding: const EdgeInsets.symmetric(
          horizontal: SpacingTokens.md, vertical: SpacingTokens.xs),
      decoration: BoxDecoration(
        color: Colors.white.withValues(alpha: 0.2),
        borderRadius: BorderRadius.circular(RadiusTokens.full),
        border: Border.all(color: Colors.white.withValues(alpha: 0.5)),
      ),
      child: Text(
        label,
        style: theme.textTheme.labelMedium?.copyWith(
          color: Colors.white,
          fontWeight: FontWeight.w600,
        ),
      ),
    );
  }
}

class _WorkedSolutionCard extends StatelessWidget {
  const _WorkedSolutionCard({required this.solution});

  final String solution;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Container(
      width: double.infinity,
      padding: const EdgeInsets.all(SpacingTokens.md),
      decoration: BoxDecoration(
        color: Colors.white.withValues(alpha: 0.15),
        borderRadius: BorderRadius.circular(RadiusTokens.lg),
        border: Border.all(color: Colors.white.withValues(alpha: 0.3)),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              const Icon(Icons.lightbulb_rounded,
                  size: 16, color: Colors.white),
              const SizedBox(width: SpacingTokens.xs),
              Text(
                AppLocalizations.of(context).workedSolution,
                style: theme.textTheme.labelLarge?.copyWith(
                  color: Colors.white,
                  fontWeight: FontWeight.w700,
                ),
              ),
            ],
          ),
          const SizedBox(height: SpacingTokens.sm),
          MathText(
            content: solution,
            textStyle: theme.textTheme.bodyMedium?.copyWith(
              color: Colors.white.withValues(alpha: 0.9),
            ),
            mathColor: Colors.white,
            mathBackground: Colors.white.withValues(alpha: 0.1),
          ),
        ],
      ),
    );
  }
}
