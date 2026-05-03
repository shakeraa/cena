// =============================================================================
// Cena Adaptive Learning Platform — Confidence Rating Widget (MOB-047)
// =============================================================================
//
// Post-answer confidence calibration UI:
// - Appears every 3rd question (not every question, to avoid fatigue)
// - Age-appropriate:
//   - Under 14 (grade < 9): 3 emoji levels ("Guess", "Think so", "Sure")
//   - 14+ (grade >= 9): 5-point slider
// - Quick tap targets for younger students
// - Returns int confidence level (1-3 or 1-5)
// =============================================================================

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../../core/config/app_config.dart';

/// Callback that receives the confidence level from the student.
typedef ConfidenceCallback = void Function(int confidenceLevel);

/// Confidence rating widget shown after answering, before seeing the result.
///
/// [questionIndex] is the 0-based index of the current question in the session.
/// The widget only renders content every 3rd question (index 2, 5, 8, ...);
/// on other questions it calls [onSkipped] and renders nothing.
///
/// [isYoungerStudent] controls whether the 3-level emoji UI or 5-point
/// slider is shown. Callers should derive this from the student's grade level
/// (e.g. grade 8 and below = younger, grade 9+ = older).
class ConfidenceRating extends StatelessWidget {
  const ConfidenceRating({
    super.key,
    required this.questionIndex,
    required this.isYoungerStudent,
    required this.onRated,
    this.onSkipped,
  });

  /// 0-based index of the current question in the session.
  final int questionIndex;

  /// True for students under 14 (grade < 9); shows simplified 3-level UI.
  final bool isYoungerStudent;

  /// Called when the student selects a confidence level.
  final ConfidenceCallback onRated;

  /// Called when this question is not a calibration question (every 3rd only).
  final VoidCallback? onSkipped;

  /// Whether this question index triggers a confidence check.
  /// Fires on every 3rd question: index 2, 5, 8, 11, ...
  static bool shouldShow(int questionIndex) {
    return questionIndex >= 2 && (questionIndex + 1) % 3 == 0;
  }

  @override
  Widget build(BuildContext context) {
    if (!shouldShow(questionIndex)) {
      // Not a calibration question — invoke skip callback and render empty.
      WidgetsBinding.instance.addPostFrameCallback((_) {
        onSkipped?.call();
      });
      return const SizedBox.shrink();
    }

    if (isYoungerStudent) {
      return _YoungConfidenceRating(onRated: onRated);
    } else {
      return _MatureConfidenceRating(onRated: onRated);
    }
  }
}

// ---------------------------------------------------------------------------
// Young student UI: 3 emoji tap targets
// ---------------------------------------------------------------------------

/// Age-appropriate confidence UI for students under 14.
/// Three large, tappable cards with emoji + label.
class _YoungConfidenceRating extends StatelessWidget {
  const _YoungConfidenceRating({required this.onRated});

  final ConfidenceCallback onRated;

  static const _levels = [
    _YoungLevel(level: 1, emoji: '\u{1F937}', label: 'Guess'),     // shrug
    _YoungLevel(level: 2, emoji: '\u{1F914}', label: 'Think so'),  // thinking
    _YoungLevel(level: 3, emoji: '\u{1F60E}', label: 'Sure!'),     // cool
  ];

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;

    return Container(
      padding: const EdgeInsets.all(SpacingTokens.md),
      decoration: BoxDecoration(
        color: colorScheme.surfaceContainerLow,
        borderRadius: BorderRadius.circular(RadiusTokens.lg),
        border: Border.all(color: colorScheme.outlineVariant),
      ),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          Text(
            'How confident are you?',
            style: theme.textTheme.titleMedium?.copyWith(
              fontWeight: FontWeight.w600,
            ),
          ),
          const SizedBox(height: SpacingTokens.md),
          Row(
            mainAxisAlignment: MainAxisAlignment.spaceEvenly,
            children: _levels.map((level) {
              return _YoungConfidenceTile(
                level: level,
                onTap: () {
                  HapticFeedback.selectionClick();
                  onRated(level.level);
                },
              );
            }).toList(),
          ),
        ],
      ),
    );
  }
}

class _YoungLevel {
  const _YoungLevel({
    required this.level,
    required this.emoji,
    required this.label,
  });

  final int level;
  final String emoji;
  final String label;
}

class _YoungConfidenceTile extends StatelessWidget {
  const _YoungConfidenceTile({
    required this.level,
    required this.onTap,
  });

  final _YoungLevel level;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;

    // Color progression: red (guess) -> yellow (think so) -> green (sure).
    final Color tileColor;
    switch (level.level) {
      case 1:
        tileColor = colorScheme.errorContainer;
      case 2:
        tileColor = const Color(0xFFFFF3E0); // light orange
      case 3:
        tileColor = const Color(0xFFE8F5E9); // light green
      default:
        tileColor = colorScheme.surfaceContainerHighest;
    }

    return GestureDetector(
      onTap: onTap,
      child: Container(
        width: 90,
        padding: const EdgeInsets.symmetric(
          vertical: SpacingTokens.md,
          horizontal: SpacingTokens.sm,
        ),
        decoration: BoxDecoration(
          color: tileColor,
          borderRadius: BorderRadius.circular(RadiusTokens.lg),
          border: Border.all(
            color: colorScheme.outlineVariant.withValues(alpha: 0.5),
          ),
        ),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Text(
              level.emoji,
              style: const TextStyle(fontSize: 32),
            ),
            const SizedBox(height: SpacingTokens.xs),
            Text(
              level.label,
              style: theme.textTheme.labelMedium?.copyWith(
                fontWeight: FontWeight.w600,
              ),
              textAlign: TextAlign.center,
            ),
          ],
        ),
      ),
    );
  }
}

// ---------------------------------------------------------------------------
// Mature student UI: 5-point slider
// ---------------------------------------------------------------------------

/// Confidence slider for students aged 14+ (grades 9-12).
/// 5-point discrete slider with labeled endpoints.
class _MatureConfidenceRating extends StatefulWidget {
  const _MatureConfidenceRating({required this.onRated});

  final ConfidenceCallback onRated;

  @override
  State<_MatureConfidenceRating> createState() =>
      _MatureConfidenceRatingState();
}

class _MatureConfidenceRatingState extends State<_MatureConfidenceRating> {
  double _value = 3.0; // Default to middle (neutral).
  bool _submitted = false;

  static const _labels = [
    'Wild guess',
    'Not sure',
    'Maybe',
    'Fairly sure',
    'Certain',
  ];

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;
    final labelIndex = (_value - 1).round().clamp(0, 4);

    return Container(
      padding: const EdgeInsets.all(SpacingTokens.md),
      decoration: BoxDecoration(
        color: colorScheme.surfaceContainerLow,
        borderRadius: BorderRadius.circular(RadiusTokens.lg),
        border: Border.all(color: colorScheme.outlineVariant),
      ),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          Text(
            'How confident are you?',
            style: theme.textTheme.titleMedium?.copyWith(
              fontWeight: FontWeight.w600,
            ),
          ),
          const SizedBox(height: SpacingTokens.sm),

          // Current label.
          AnimatedSwitcher(
            duration: AnimationTokens.fast,
            child: Text(
              _labels[labelIndex],
              key: ValueKey(labelIndex),
              style: theme.textTheme.bodyLarge?.copyWith(
                fontWeight: FontWeight.w700,
                color: _colorForLevel(labelIndex, colorScheme),
              ),
            ),
          ),

          // Discrete slider: 1-5.
          SliderTheme(
            data: SliderTheme.of(context).copyWith(
              trackHeight: 6,
              thumbShape: const RoundSliderThumbShape(enabledThumbRadius: 12),
              overlayShape: const RoundSliderOverlayShape(overlayRadius: 20),
              tickMarkShape: const RoundSliderTickMarkShape(
                tickMarkRadius: 3,
              ),
            ),
            child: Slider(
              value: _value,
              min: 1,
              max: 5,
              divisions: 4,
              label: _labels[labelIndex],
              activeColor: _colorForLevel(labelIndex, colorScheme),
              onChanged: _submitted
                  ? null
                  : (v) {
                      HapticFeedback.selectionClick();
                      setState(() => _value = v);
                    },
            ),
          ),

          // Endpoint labels.
          Padding(
            padding: const EdgeInsets.symmetric(horizontal: SpacingTokens.md),
            child: Row(
              mainAxisAlignment: MainAxisAlignment.spaceBetween,
              children: [
                Text(
                  '1 — Guess',
                  style: theme.textTheme.labelSmall?.copyWith(
                    color: colorScheme.onSurfaceVariant,
                  ),
                ),
                Text(
                  '5 — Certain',
                  style: theme.textTheme.labelSmall?.copyWith(
                    color: colorScheme.onSurfaceVariant,
                  ),
                ),
              ],
            ),
          ),

          const SizedBox(height: SpacingTokens.md),

          // Submit button.
          FilledButton(
            onPressed: _submitted
                ? null
                : () {
                    HapticFeedback.mediumImpact();
                    setState(() => _submitted = true);
                    widget.onRated(_value.round());
                  },
            style: FilledButton.styleFrom(
              minimumSize: const Size(double.infinity, 44),
            ),
            child: const Text('Confirm'),
          ),
        ],
      ),
    );
  }

  Color _colorForLevel(int index, ColorScheme cs) {
    switch (index) {
      case 0:
        return cs.error;
      case 1:
        return const Color(0xFFFF9800);
      case 2:
        return const Color(0xFFFFC107);
      case 3:
        return const Color(0xFF8BC34A);
      case 4:
        return cs.primary;
      default:
        return cs.primary;
    }
  }
}
