// =============================================================================
// Cena Adaptive Learning Platform — Coach Mark Overlay (MOB-035)
// =============================================================================
//
// Tooltip overlay for the first session:
//   - "Read the question and tap your answer"
//   - First correct answer: extended celebration (1.5x duration)
//   - First session complete: special celebration message
//   - Coach marks only appear in session 1
//   - Positioned near the relevant widget using GlobalKey lookup
// =============================================================================

import 'dart:async';

import 'package:flutter/material.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../../../core/config/app_config.dart';
import '../models/training_wheels_config.dart';

// ---------------------------------------------------------------------------
// Coach Mark Overlay
// ---------------------------------------------------------------------------

/// Overlay that displays sequential coach marks during the first session.
///
/// The overlay finds the target widget using the provided [targetKeys] map
/// (mapping targetKey string -> GlobalKey) and positions the tooltip
/// relative to that widget.
///
/// Coach marks are shown one at a time in [CoachMark.order]. The student
/// dismisses each by tapping. Once all are dismissed, the overlay removes
/// itself. Dismissed marks are persisted to SharedPreferences so they
/// only appear once.
class CoachMarkOverlay extends StatefulWidget {
  const CoachMarkOverlay({
    super.key,
    required this.coachMarks,
    required this.targetKeys,
    required this.onAllDismissed,
    this.langCode = 'he',
    this.autoAdvanceDelay = const Duration(seconds: 5),
  });

  /// The list of coach marks to display (in order).
  final List<CoachMark> coachMarks;

  /// Map from [CoachMark.targetKey] to the [GlobalKey] of the target widget.
  final Map<String, GlobalKey> targetKeys;

  /// Called when all coach marks have been dismissed.
  final VoidCallback onAllDismissed;

  /// Language code for localized messages.
  final String langCode;

  /// Auto-advance to next coach mark after this delay if not tapped.
  final Duration autoAdvanceDelay;

  @override
  State<CoachMarkOverlay> createState() => _CoachMarkOverlayState();
}

class _CoachMarkOverlayState extends State<CoachMarkOverlay>
    with SingleTickerProviderStateMixin {
  int _currentIndex = 0;
  late final AnimationController _fadeController;
  late final Animation<double> _fadeAnimation;
  Timer? _autoAdvanceTimer;
  final Set<String> _dismissed = {};

  @override
  void initState() {
    super.initState();
    _fadeController = AnimationController(
      vsync: this,
      duration: AnimationTokens.normal,
    );
    _fadeAnimation = CurvedAnimation(
      parent: _fadeController,
      curve: Curves.easeInOut,
    );
    _loadDismissed().then((_) {
      if (mounted) _showCurrent();
    });
  }

  @override
  void dispose() {
    _autoAdvanceTimer?.cancel();
    _fadeController.dispose();
    super.dispose();
  }

  Future<void> _loadDismissed() async {
    final prefs = await SharedPreferences.getInstance();
    final dismissedList =
        prefs.getStringList('coach_marks_dismissed') ?? [];
    _dismissed.addAll(dismissedList);

    // Skip already-dismissed marks
    while (_currentIndex < widget.coachMarks.length &&
        _dismissed.contains(widget.coachMarks[_currentIndex].id)) {
      _currentIndex++;
    }

    if (_currentIndex >= widget.coachMarks.length) {
      widget.onAllDismissed();
    }
  }

  void _showCurrent() {
    if (_currentIndex >= widget.coachMarks.length) {
      widget.onAllDismissed();
      return;
    }
    _fadeController.forward(from: 0);
    _startAutoAdvance();
  }

  void _startAutoAdvance() {
    _autoAdvanceTimer?.cancel();
    _autoAdvanceTimer = Timer(widget.autoAdvanceDelay, _advance);
  }

  Future<void> _advance() async {
    if (!mounted) return;
    _autoAdvanceTimer?.cancel();

    // Mark current as dismissed
    final current = widget.coachMarks[_currentIndex];
    _dismissed.add(current.id);
    final prefs = await SharedPreferences.getInstance();
    await prefs.setStringList('coach_marks_dismissed', _dismissed.toList());

    await _fadeController.reverse();
    if (!mounted) return;

    _currentIndex++;

    // Skip already-dismissed
    while (_currentIndex < widget.coachMarks.length &&
        _dismissed.contains(widget.coachMarks[_currentIndex].id)) {
      _currentIndex++;
    }

    if (_currentIndex >= widget.coachMarks.length) {
      widget.onAllDismissed();
    } else {
      _showCurrent();
    }
  }

  @override
  Widget build(BuildContext context) {
    if (_currentIndex >= widget.coachMarks.length) {
      return const SizedBox.shrink();
    }

    final coachMark = widget.coachMarks[_currentIndex];
    final targetKey = widget.targetKeys[coachMark.targetKey];

    return FadeTransition(
      opacity: _fadeAnimation,
      child: GestureDetector(
        onTap: _advance,
        behavior: HitTestBehavior.translucent,
        child: Stack(
          children: [
            // Semi-transparent scrim
            Positioned.fill(
              child: Container(
                color: Colors.black.withValues(alpha: 0.3),
              ),
            ),

            // Tooltip positioned near target
            _PositionedTooltip(
              coachMark: coachMark,
              targetKey: targetKey,
              langCode: widget.langCode,
              stepIndex: _currentIndex,
              totalSteps: widget.coachMarks.length,
            ),
          ],
        ),
      ),
    );
  }
}

// ---------------------------------------------------------------------------
// Positioned Tooltip
// ---------------------------------------------------------------------------

class _PositionedTooltip extends StatelessWidget {
  const _PositionedTooltip({
    required this.coachMark,
    required this.targetKey,
    required this.langCode,
    required this.stepIndex,
    required this.totalSteps,
  });

  final CoachMark coachMark;
  final GlobalKey? targetKey;
  final String langCode;
  final int stepIndex;
  final int totalSteps;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final cs = theme.colorScheme;
    final screenSize = MediaQuery.of(context).size;

    // Try to find the target widget's position
    Rect? targetRect;
    if (targetKey?.currentContext != null) {
      final renderBox =
          targetKey!.currentContext!.findRenderObject() as RenderBox?;
      if (renderBox != null && renderBox.hasSize) {
        final offset = renderBox.localToGlobal(Offset.zero);
        targetRect = offset & renderBox.size;
      }
    }

    // Default to center if target not found
    final tooltipTop = targetRect != null
        ? _computeTop(targetRect, screenSize, coachMark.position)
        : screenSize.height * 0.4;

    return Positioned(
      top: tooltipTop,
      left: SpacingTokens.lg,
      right: SpacingTokens.lg,
      child: Material(
        color: Colors.transparent,
        child: Container(
          padding: const EdgeInsets.all(SpacingTokens.md),
          decoration: BoxDecoration(
            color: cs.primaryContainer,
            borderRadius: BorderRadius.circular(RadiusTokens.xl),
            boxShadow: [
              BoxShadow(
                color: Colors.black.withValues(alpha: 0.15),
                blurRadius: 16,
                offset: const Offset(0, 4),
              ),
            ],
          ),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              // Step indicator
              Row(
                mainAxisAlignment: MainAxisAlignment.center,
                children: List.generate(totalSteps, (i) {
                  return Container(
                    width: i == stepIndex ? 20 : 6,
                    height: 6,
                    margin: const EdgeInsets.symmetric(
                        horizontal: SpacingTokens.xxs),
                    decoration: BoxDecoration(
                      borderRadius: BorderRadius.circular(RadiusTokens.full),
                      color: i == stepIndex
                          ? cs.onPrimaryContainer
                          : cs.onPrimaryContainer.withValues(alpha: 0.3),
                    ),
                  );
                }),
              ),
              const SizedBox(height: SpacingTokens.md),

              // Message
              Text(
                coachMark.localizedMessage(langCode),
                style: theme.textTheme.titleMedium?.copyWith(
                  fontFamily: TypographyTokens.hebrewFontFamily,
                  fontWeight: FontWeight.w600,
                  color: cs.onPrimaryContainer,
                ),
                textAlign: TextAlign.center,
                textDirection: (langCode == 'he' || langCode == 'ar')
                    ? TextDirection.rtl
                    : TextDirection.ltr,
              ),
              const SizedBox(height: SpacingTokens.sm),

              // Tap to continue
              Text(
                langCode == 'he' || langCode == 'ar'
                    ? 'לחצו להמשך'
                    : 'Tap to continue',
                style: theme.textTheme.labelMedium?.copyWith(
                  fontFamily: TypographyTokens.hebrewFontFamily,
                  color: cs.onPrimaryContainer.withValues(alpha: 0.6),
                ),
                textAlign: TextAlign.center,
              ),
            ],
          ),
        ),
      ),
    );
  }

  double _computeTop(
    Rect targetRect,
    Size screenSize,
    CoachMarkPosition position,
  ) {
    switch (position) {
      case CoachMarkPosition.below:
        return (targetRect.bottom + SpacingTokens.md)
            .clamp(0, screenSize.height - 120);
      case CoachMarkPosition.above:
        return (targetRect.top - 120 - SpacingTokens.md)
            .clamp(0, screenSize.height - 120);
      case CoachMarkPosition.left:
      case CoachMarkPosition.right:
        return (targetRect.center.dy - 60)
            .clamp(0, screenSize.height - 120);
    }
  }
}

// ---------------------------------------------------------------------------
// First Session Celebration Message
// ---------------------------------------------------------------------------

/// Special celebration widget for completing the first session.
class FirstSessionCelebration extends StatelessWidget {
  const FirstSessionCelebration({
    super.key,
    required this.onContinue,
    this.langCode = 'he',
  });

  final VoidCallback onContinue;
  final String langCode;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final cs = theme.colorScheme;
    final isRtl = langCode == 'he' || langCode == 'ar';

    return Directionality(
      textDirection: isRtl ? TextDirection.rtl : TextDirection.ltr,
      child: Container(
        padding: const EdgeInsets.all(SpacingTokens.xl),
        decoration: BoxDecoration(
          color: cs.primaryContainer,
          borderRadius: BorderRadius.circular(RadiusTokens.xl),
        ),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(
              Icons.celebration_rounded,
              size: 64,
              color: cs.onPrimaryContainer,
            ),
            const SizedBox(height: SpacingTokens.md),
            Text(
              isRtl ? 'כל הכבוד!' : 'Great job!',
              style: theme.textTheme.headlineMedium?.copyWith(
                fontFamily: TypographyTokens.hebrewFontFamily,
                fontWeight: FontWeight.w800,
                color: cs.onPrimaryContainer,
              ),
              textAlign: TextAlign.center,
            ),
            const SizedBox(height: SpacingTokens.sm),
            Text(
              isRtl
                  ? 'סיימתם את המפגש הראשון! כל מפגש מקרב אתכם למטרה.'
                  : 'You completed your first session! Each session brings you closer to your goal.',
              style: theme.textTheme.bodyLarge?.copyWith(
                fontFamily: TypographyTokens.hebrewFontFamily,
                color: cs.onPrimaryContainer.withValues(alpha: 0.8),
              ),
              textAlign: TextAlign.center,
            ),
            const SizedBox(height: SpacingTokens.lg),
            FilledButton(
              onPressed: onContinue,
              child: Text(isRtl ? 'המשך' : 'Continue'),
            ),
          ],
        ),
      ),
    );
  }
}
