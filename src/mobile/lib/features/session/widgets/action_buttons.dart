// =============================================================================
// Cena Adaptive Learning Platform — Session Action Buttons
// Hint, Skip (with confirmation), and Change Approach bottom sheet.
// =============================================================================

import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/config/app_config.dart';
import '../../../core/models/domain_models.dart';
import '../../../core/state/feature_discovery_state.dart';
import '../../../core/state/session_notifier.dart';

/// Row of secondary action buttons shown below the answer input:
/// - Hint (with level badge): reveals up to 3 progressive hints
/// - Skip: delegates to [SessionNotifier.skipQuestion]
/// - Change Approach: bottom sheet with methodology options
///
/// All buttons are disabled when [isDisabled] is true (e.g., during loading).
class ActionButtons extends ConsumerStatefulWidget {
  const ActionButtons({
    super.key,
    this.isDisabled = false,
    this.questionInteracted = false,
  });

  final bool isDisabled;
  final bool questionInteracted;

  @override
  ConsumerState<ActionButtons> createState() => _ActionButtonsState();
}

class _ActionButtonsState extends ConsumerState<ActionButtons>
    with SingleTickerProviderStateMixin {
  Timer? _hintPulseDelay;
  late final AnimationController _pulseController;
  String? _lastExerciseId;

  @override
  void initState() {
    super.initState();
    _pulseController = AnimationController(
      vsync: this,
      duration: const Duration(milliseconds: 750),
      lowerBound: 1.0,
      upperBound: 1.08,
      value: 1.0,
    );
  }

  @override
  void dispose() {
    _hintPulseDelay?.cancel();
    _pulseController.dispose();
    super.dispose();
  }

  void _scheduleHintPulse({
    required String? exerciseId,
    required bool hintsUnlocked,
    required int hintsUsed,
  }) {
    if (_lastExerciseId == exerciseId) {
      // Stop pulse when user engages or consumes a hint.
      if (widget.questionInteracted || hintsUsed > 0 || !hintsUnlocked) {
        _hintPulseDelay?.cancel();
        if (_pulseController.isAnimating) {
          _pulseController.stop();
          _pulseController.value = 1.0;
        }
      }
      return;
    }

    _lastExerciseId = exerciseId;
    _hintPulseDelay?.cancel();
    if (_pulseController.isAnimating) {
      _pulseController.stop();
      _pulseController.value = 1.0;
    }

    // Session 3+ hint discovery: pulse after 10s of inactivity.
    if (exerciseId == null || !hintsUnlocked || widget.questionInteracted) {
      return;
    }

    _hintPulseDelay = Timer(const Duration(seconds: 10), () {
      if (!mounted) return;
      final latest = ref.read(sessionProvider);
      final stillSameQuestion = latest.currentExercise?.id == exerciseId;
      final stillInactive = !widget.questionInteracted && latest.hintsUsed == 0;
      if (stillSameQuestion && stillInactive) {
        _pulseController.repeat(reverse: true);
      }
    });
  }

  @override
  Widget build(BuildContext context) {
    final discovery = ref.watch(featureDiscoveryProvider);
    final sessionState = ref.watch(sessionProvider);
    final notifier = ref.read(sessionProvider.notifier);
    final hintsUsed = sessionState.hintsUsed;
    final hasExercise = sessionState.currentExercise != null;
    final maxHints = sessionState.currentExercise?.hints.length ?? 3;
    final hintsExhausted = hintsUsed >= maxHints;
    final disabled = widget.isDisabled || !hasExercise;

    _scheduleHintPulse(
      exerciseId: sessionState.currentExercise?.id,
      hintsUnlocked: discovery.hintsUnlocked,
      hintsUsed: hintsUsed,
    );

    final showHintButton = discovery.hintsUnlocked;
    final showApproachButton = discovery.methodologyUnlocked;

    if (!showHintButton && !showApproachButton) {
      return const SizedBox.shrink();
    }

    return Row(
      mainAxisAlignment: MainAxisAlignment.center,
      children: [
        // Hint button with badge
        if (showHintButton)
          ScaleTransition(
            scale: _pulseController,
            child: _HintButton(
              hintsUsed: hintsUsed,
              isDisabled: disabled || hintsExhausted,
              showNewTooltip: hintsUsed == 0,
              onPressed:
                  disabled || hintsExhausted ? null : notifier.requestHint,
            ),
          ),
        if (showHintButton && showApproachButton)
          const SizedBox(width: SpacingTokens.sm),
        // Change Approach button
        if (showApproachButton)
          _ChangeApproachButton(
            currentMethodology: sessionState.methodology,
            isDisabled: disabled,
            showIntroSheet: !discovery.methodologyExplained,
            onIntroSeen: () {
              ref
                  .read(featureDiscoveryProvider.notifier)
                  .markMethodologyExplained();
            },
            onSelected: disabled ? null : notifier.switchApproach,
          ),
      ],
    );
  }
}

// ---------------------------------------------------------------------------
// Hint button with progressive level badge
// ---------------------------------------------------------------------------

class _HintButton extends StatelessWidget {
  const _HintButton({
    required this.hintsUsed,
    required this.isDisabled,
    required this.showNewTooltip,
    required this.onPressed,
  });

  final int hintsUsed;
  final bool isDisabled;
  final bool showNewTooltip;
  final VoidCallback? onPressed;

  @override
  Widget build(BuildContext context) {
    final colorScheme = Theme.of(context).colorScheme;
    return Tooltip(
      message: showNewTooltip ? 'חדש: נסו רמז אחרי 10 שניות' : 'קבל/י רמז',
      child: Stack(
        clipBehavior: Clip.none,
        children: [
          OutlinedButton.icon(
            onPressed: onPressed,
            icon: Icon(
              Icons.lightbulb_outline_rounded,
              size: 18,
              color: isDisabled ? null : const Color(0xFFFF9800),
            ),
            label: const Text('רמז'),
            style: OutlinedButton.styleFrom(
              foregroundColor: isDisabled
                  ? colorScheme.onSurfaceVariant
                  : const Color(0xFFFF9800),
              side: BorderSide(
                color: isDisabled
                    ? colorScheme.outlineVariant
                    : const Color(0xFFFF9800),
              ),
              padding: const EdgeInsets.symmetric(
                horizontal: SpacingTokens.md,
                vertical: SpacingTokens.sm,
              ),
            ),
          ),
          if (hintsUsed > 0)
            Positioned(
              top: -6,
              right: -6,
              child: Container(
                width: 20,
                height: 20,
                alignment: Alignment.center,
                decoration: BoxDecoration(
                  color: const Color(0xFFFF9800),
                  shape: BoxShape.circle,
                  border: Border.all(
                      color: Theme.of(context).colorScheme.surface, width: 2),
                ),
                child: Text(
                  '$hintsUsed',
                  style: Theme.of(context).textTheme.labelSmall?.copyWith(
                        color: Colors.white,
                        fontWeight: FontWeight.w700,
                      ),
                ),
              ),
            ),
        ],
      ),
    );
  }
}

// ---------------------------------------------------------------------------
// Change Approach button with bottom sheet
// ---------------------------------------------------------------------------

class _ChangeApproachButton extends StatelessWidget {
  const _ChangeApproachButton({
    required this.currentMethodology,
    required this.isDisabled,
    required this.showIntroSheet,
    required this.onIntroSeen,
    required this.onSelected,
  });

  final Methodology? currentMethodology;
  final bool isDisabled;
  final bool showIntroSheet;
  final VoidCallback onIntroSeen;
  final void Function(String preferenceHint)? onSelected;

  @override
  Widget build(BuildContext context) {
    return OutlinedButton.icon(
      onPressed: isDisabled
          ? null
          : () async {
              if (showIntroSheet) {
                await _showMethodologyIntroSheet(context);
                onIntroSeen();
              }
              if (context.mounted) {
                _showApproachSheet(context);
              }
            },
      icon: const Icon(Icons.tune_rounded, size: 18),
      label: const Text('שנה גישה'),
      style: OutlinedButton.styleFrom(
        padding: const EdgeInsets.symmetric(
          horizontal: SpacingTokens.md,
          vertical: SpacingTokens.sm,
        ),
      ),
    );
  }

  Future<void> _showMethodologyIntroSheet(BuildContext context) {
    return showModalBottomSheet<void>(
      context: context,
      shape: const RoundedRectangleBorder(
        borderRadius: BorderRadius.vertical(
          top: Radius.circular(RadiusTokens.xl),
        ),
      ),
      builder: (_) => Padding(
        padding: const EdgeInsets.all(SpacingTokens.lg),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Text(
              'חדש: התאמת שיטת הלמידה',
              style: Theme.of(context).textTheme.titleLarge,
            ),
            const SizedBox(height: SpacingTokens.sm),
            Text(
              'אפשר לבחור גישה שמתאימה לך: חזרה מרווחת, למידה מעורבת, או קושי מותאם.',
              style: Theme.of(context).textTheme.bodyMedium,
            ),
            const SizedBox(height: SpacingTokens.md),
            FilledButton(
              onPressed: () => Navigator.of(context).pop(),
              child: const Text('הבנתי'),
            ),
          ],
        ),
      ),
    );
  }

  void _showApproachSheet(BuildContext context) {
    showModalBottomSheet<void>(
      context: context,
      shape: const RoundedRectangleBorder(
        borderRadius: BorderRadius.vertical(
          top: Radius.circular(RadiusTokens.xl),
        ),
      ),
      builder: (_) => _ApproachSheet(
        currentMethodology: currentMethodology,
        onSelected: (hint) {
          Navigator.of(context).pop();
          onSelected?.call(hint);
        },
      ),
    );
  }
}

class _ApproachSheet extends StatelessWidget {
  const _ApproachSheet({
    required this.currentMethodology,
    required this.onSelected,
  });

  final Methodology? currentMethodology;
  final ValueChanged<String> onSelected;

  // Student-friendly labels (Hebrew) mapped to server preference hints.
  static const List<
          ({String hint, String label, String description, IconData icon})>
      _options = [
    (
      hint: 'spaced_repetition',
      label: 'חזרה מרווחת',
      description: 'שאלות שנבחרות לחיזוק זיכרון לטווח ארוך',
      icon: Icons.repeat_rounded,
    ),
    (
      hint: 'interleaved',
      label: 'למידה מעורבת',
      description: 'נושאים שונים לסירוגין לחיזוק חיבורים',
      icon: Icons.shuffle_rounded,
    ),
    (
      hint: 'blocked',
      label: 'למידה ממוקדת',
      description: 'מסכים נושא אחד עד לשליטה מלאה',
      icon: Icons.center_focus_strong_rounded,
    ),
    (
      hint: 'adaptive_difficulty',
      label: 'קושי מותאם',
      description: 'רמת הקושי מתאימה לביצועים שלך',
      icon: Icons.auto_graph_rounded,
    ),
    (
      hint: 'socratic',
      label: 'שיטה סוקרטית',
      description: 'שאלות מנחות שמובילות אותך לגלות את התשובה',
      icon: Icons.psychology_rounded,
    ),
  ];

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;

    return Padding(
      padding: const EdgeInsets.symmetric(vertical: SpacingTokens.lg),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Padding(
            padding: const EdgeInsets.symmetric(horizontal: SpacingTokens.lg),
            child: Text(
              'בחר גישת למידה',
              style: theme.textTheme.titleLarge,
            ),
          ),
          const SizedBox(height: SpacingTokens.md),
          ..._options.map((opt) {
            final isActive = currentMethodology?.name == opt.hint;
            return ListTile(
              leading: Container(
                width: 40,
                height: 40,
                alignment: Alignment.center,
                decoration: BoxDecoration(
                  color: isActive
                      ? colorScheme.primary.withValues(alpha: 0.15)
                      : colorScheme.surfaceContainerHighest,
                  shape: BoxShape.circle,
                ),
                child: Icon(
                  opt.icon,
                  size: 20,
                  color: isActive
                      ? colorScheme.primary
                      : colorScheme.onSurfaceVariant,
                ),
              ),
              title: Text(
                opt.label,
                style: theme.textTheme.bodyLarge?.copyWith(
                  fontWeight: isActive ? FontWeight.w700 : FontWeight.normal,
                  color: isActive ? colorScheme.primary : null,
                ),
              ),
              subtitle: Text(
                opt.description,
                style: theme.textTheme.bodySmall?.copyWith(
                  color: colorScheme.onSurfaceVariant,
                ),
              ),
              trailing: isActive
                  ? Icon(Icons.check_circle_rounded, color: colorScheme.primary)
                  : null,
              onTap: () => onSelected(opt.hint),
            );
          }),
          const SizedBox(height: SpacingTokens.md),
        ],
      ),
    );
  }
}
