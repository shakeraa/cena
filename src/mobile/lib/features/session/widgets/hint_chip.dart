// =============================================================================
// Cena Adaptive Learning Platform — Hint Chip (MOB-034)
// =============================================================================
//
// "Need a hint?" chip that appears after 10 seconds of no interaction.
// Uses Timer to detect idle time.
// Hint progression: clue -> bigger clue -> solution with XP decay.
//   Level 0: no hint (100% XP)
//   Level 1: small clue (80% XP)
//   Level 2: bigger clue (50% XP)
//   Level 3: solution revealed (0% XP, still earn progress)
// =============================================================================

import 'dart:async';

import 'package:flutter/material.dart';

import '../../../core/config/app_config.dart';

// ---------------------------------------------------------------------------
// Hint Level
// ---------------------------------------------------------------------------

/// Hint progression levels with associated XP multiplier.
enum HintLevel {
  none(0, 1.0, ''),
  clue(1, 0.80, 'רמז קטן'),
  biggerClue(2, 0.50, 'רמז גדול'),
  solution(3, 0.0, 'פתרון');

  const HintLevel(this.level, this.xpMultiplier, this.labelHe);

  final int level;

  /// XP multiplier: 100% -> 80% -> 50% -> 0%.
  final double xpMultiplier;

  final String labelHe;

  /// Whether there is a next hint level available.
  bool get hasNext => level < HintLevel.solution.level;

  /// The next hint level, or self if already at solution.
  HintLevel get next {
    if (!hasNext) return this;
    return HintLevel.values[level + 1];
  }

  /// XP percentage as integer (0-100).
  int get xpPercent => (xpMultiplier * 100).round();
}

// ---------------------------------------------------------------------------
// HintData
// ---------------------------------------------------------------------------

/// Contains the actual hint content for each level of a question.
class HintData {
  const HintData({
    required this.clue,
    required this.biggerClue,
    required this.solution,
  });

  /// Level 1 hint: a small clue (e.g., "Think about factoring").
  final String clue;

  /// Level 2 hint: a bigger clue (e.g., "Factor out 2 first").
  final String biggerClue;

  /// Level 3 hint: the full solution explanation.
  final String solution;

  /// Get the hint text for a given level.
  String textForLevel(HintLevel level) {
    switch (level) {
      case HintLevel.none:
        return '';
      case HintLevel.clue:
        return clue;
      case HintLevel.biggerClue:
        return biggerClue;
      case HintLevel.solution:
        return solution;
    }
  }
}

// ---------------------------------------------------------------------------
// HintChip Widget
// ---------------------------------------------------------------------------

/// A chip that appears after idle time and reveals progressive hints.
///
/// The chip starts hidden. After [idleTimeout] of no user interaction
/// (tracked by the parent resetting [lastInteractionTime]), the chip
/// fades in. Each tap reveals the next hint level.
class HintChip extends StatefulWidget {
  const HintChip({
    super.key,
    required this.hintData,
    required this.onHintRevealed,
    this.idleTimeout = const Duration(seconds: 10),
    this.enabled = true,
  });

  /// The hint content for this question.
  final HintData hintData;

  /// Called whenever a new hint level is revealed.
  final ValueChanged<HintLevel> onHintRevealed;

  /// Duration of inactivity before the chip appears.
  final Duration idleTimeout;

  /// Whether the hint system is enabled (false during training wheels session 1).
  final bool enabled;

  @override
  State<HintChip> createState() => HintChipState();
}

class HintChipState extends State<HintChip>
    with SingleTickerProviderStateMixin {
  HintLevel _currentLevel = HintLevel.none;
  bool _visible = false;
  Timer? _idleTimer;

  late final AnimationController _fadeController;
  late final Animation<double> _fadeAnimation;

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
    if (widget.enabled) {
      _startIdleTimer();
    }
  }

  @override
  void didUpdateWidget(HintChip oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (!widget.enabled && _visible) {
      _hideChip();
    } else if (widget.enabled && !oldWidget.enabled) {
      _startIdleTimer();
    }
  }

  @override
  void dispose() {
    _idleTimer?.cancel();
    _fadeController.dispose();
    super.dispose();
  }

  /// Call this from the parent when the user interacts with the question.
  /// Resets the idle timer.
  void resetIdleTimer() {
    _idleTimer?.cancel();
    if (widget.enabled && _currentLevel == HintLevel.none) {
      _startIdleTimer();
    }
  }

  void _startIdleTimer() {
    _idleTimer?.cancel();
    _idleTimer = Timer(widget.idleTimeout, _showChip);
  }

  void _showChip() {
    if (!mounted || !widget.enabled) return;
    setState(() => _visible = true);
    _fadeController.forward();
  }

  void _hideChip() {
    _idleTimer?.cancel();
    _fadeController.reverse().then((_) {
      if (mounted) {
        setState(() => _visible = false);
      }
    });
  }

  void _revealNextHint() {
    if (!_currentLevel.hasNext) return;
    final next = _currentLevel.next;
    setState(() => _currentLevel = next);
    widget.onHintRevealed(next);
  }

  /// Resets hints for a new question.
  void resetForNewQuestion() {
    _idleTimer?.cancel();
    _fadeController.reset();
    setState(() {
      _currentLevel = HintLevel.none;
      _visible = false;
    });
    if (widget.enabled) {
      _startIdleTimer();
    }
  }

  @override
  Widget build(BuildContext context) {
    if (!widget.enabled) return const SizedBox.shrink();

    final theme = Theme.of(context);
    final cs = theme.colorScheme;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      mainAxisSize: MainAxisSize.min,
      children: [
        // "Need a hint?" chip (appears after idle)
        if (_visible && _currentLevel == HintLevel.none)
          FadeTransition(
            opacity: _fadeAnimation,
            child: Center(
              child: ActionChip(
                avatar: Icon(
                  Icons.lightbulb_outline_rounded,
                  size: 18,
                  color: cs.primary,
                ),
                label: Text(
                  'צריך רמז?',
                  style: TextStyle(
                    fontFamily: TypographyTokens.hebrewFontFamily,
                    color: cs.primary,
                  ),
                ),
                onPressed: _revealNextHint,
                backgroundColor: cs.primaryContainer.withValues(alpha: 0.5),
              ),
            ),
          ),

        // Revealed hint content
        if (_currentLevel != HintLevel.none) ...[
          const SizedBox(height: SpacingTokens.sm),
          AnimatedContainer(
            duration: AnimationTokens.normal,
            padding: const EdgeInsets.all(SpacingTokens.md),
            decoration: BoxDecoration(
              borderRadius: BorderRadius.circular(RadiusTokens.lg),
              color: cs.secondaryContainer.withValues(alpha: 0.5),
              border: Border.all(
                color: cs.secondary.withValues(alpha: 0.3),
              ),
            ),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                // Hint level indicator
                Row(
                  children: [
                    Icon(
                      Icons.lightbulb_rounded,
                      size: 16,
                      color: cs.secondary,
                    ),
                    const SizedBox(width: SpacingTokens.xs),
                    Text(
                      _currentLevel.labelHe,
                      style: theme.textTheme.labelMedium?.copyWith(
                        fontFamily: TypographyTokens.hebrewFontFamily,
                        color: cs.secondary,
                        fontWeight: FontWeight.w600,
                      ),
                    ),
                    const Spacer(),
                    // XP indicator
                    Container(
                      padding: const EdgeInsets.symmetric(
                        horizontal: SpacingTokens.xs,
                        vertical: SpacingTokens.xxs,
                      ),
                      decoration: BoxDecoration(
                        borderRadius: BorderRadius.circular(RadiusTokens.sm),
                        color: _xpColor(cs),
                      ),
                      child: Text(
                        '${_currentLevel.xpPercent}% XP',
                        style: theme.textTheme.labelSmall?.copyWith(
                          fontWeight: FontWeight.w700,
                          color: Colors.white,
                        ),
                      ),
                    ),
                  ],
                ),
                const SizedBox(height: SpacingTokens.sm),

                // Hint text
                Text(
                  widget.hintData.textForLevel(_currentLevel),
                  style: theme.textTheme.bodyMedium?.copyWith(
                    fontFamily: TypographyTokens.hebrewFontFamily,
                  ),
                  textDirection: TextDirection.rtl,
                ),

                // "Show more" button if more hints available
                if (_currentLevel.hasNext) ...[
                  const SizedBox(height: SpacingTokens.sm),
                  Align(
                    alignment: AlignmentDirectional.centerEnd,
                    child: TextButton.icon(
                      onPressed: _revealNextHint,
                      icon: const Icon(Icons.arrow_downward_rounded, size: 16),
                      label: const Text(
                        'עוד רמז',
                        style: TextStyle(
                          fontFamily: TypographyTokens.hebrewFontFamily,
                        ),
                      ),
                    ),
                  ),
                ],
              ],
            ),
          ),
        ],
      ],
    );
  }

  Color _xpColor(ColorScheme cs) {
    switch (_currentLevel) {
      case HintLevel.none:
        return cs.primary;
      case HintLevel.clue:
        return const Color(0xFF388E3C); // green
      case HintLevel.biggerClue:
        return const Color(0xFFFF8F00); // amber
      case HintLevel.solution:
        return const Color(0xFFD32F2F); // red
    }
  }
}
