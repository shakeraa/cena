// =============================================================================
// Cena Adaptive Learning Platform — Try Question Before Signup (MOB-CORE-006)
// =============================================================================
//
// Blueprint Principle 1: Time-to-Value < 30 seconds.
// Show one seed question anonymously before the auth gate. Student experiences
// the core value proposition (answer → feedback) without creating an account.
// On completion, show the value prop and auth screen.
// =============================================================================

import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../../core/config/app_config.dart';
import '../../core/router.dart';

// ---------------------------------------------------------------------------
// Seed question data — hardcoded for instant load, no backend needed
// ---------------------------------------------------------------------------

class _SeedQuestion {
  const _SeedQuestion({
    required this.text,
    required this.options,
    required this.correctIndex,
    required this.explanation,
  });

  final String text;
  final List<String> options;
  final int correctIndex;
  final String explanation;
}

const _seedQuestion = _SeedQuestion(
  text: 'What is the value of x if 3x - 7 = 14?',
  options: ['x = 3', 'x = 5', 'x = 7', 'x = 9'],
  correctIndex: 2, // x = 7
  explanation:
      '3x - 7 = 14\n3x = 14 + 7 = 21\nx = 21 ÷ 3 = 7',
);

// ---------------------------------------------------------------------------
// Screen
// ---------------------------------------------------------------------------

/// Try-question screen shown before auth gate.
///
/// Flow: question → tap answer → show feedback → call-to-action → auth screen.
class TryQuestionScreen extends StatefulWidget {
  const TryQuestionScreen({super.key});

  @override
  State<TryQuestionScreen> createState() => _TryQuestionScreenState();
}

class _TryQuestionScreenState extends State<TryQuestionScreen> {
  int? _selectedIndex;
  bool _answered = false;

  void _selectOption(int index) {
    if (_answered) return;
    setState(() {
      _selectedIndex = index;
      _answered = true;
    });
  }

  bool get _isCorrect =>
      _selectedIndex == _seedQuestion.correctIndex;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;

    return Scaffold(
      body: SafeArea(
        child: Padding(
          padding: const EdgeInsets.all(SpacingTokens.lg),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              const Spacer(),

              // Logo + tagline
              if (!_answered) ...[
                Icon(Icons.school_rounded,
                    size: 48, color: colorScheme.primary),
                const SizedBox(height: SpacingTokens.md),
                Text(
                  'Try Cena — answer one question',
                  style: theme.textTheme.titleLarge?.copyWith(
                    fontWeight: FontWeight.w700,
                  ),
                  textAlign: TextAlign.center,
                ),
                const SizedBox(height: SpacingTokens.xl),
              ],

              // Question
              Card(
                child: Padding(
                  padding: const EdgeInsets.all(SpacingTokens.lg),
                  child: Text(
                    _seedQuestion.text,
                    style: theme.textTheme.titleMedium?.copyWith(
                      fontWeight: FontWeight.w600,
                    ),
                    textAlign: TextAlign.center,
                  ),
                ),
              ),

              const SizedBox(height: SpacingTokens.md),

              // Options
              ...List.generate(_seedQuestion.options.length, (i) {
                final isSelected = _selectedIndex == i;
                final isCorrectOption = i == _seedQuestion.correctIndex;

                Color? cardColor;
                if (_answered && isCorrectOption) {
                  cardColor = Colors.green.withValues(alpha: 0.12);
                } else if (_answered && isSelected && !isCorrectOption) {
                  cardColor = Colors.red.withValues(alpha: 0.12);
                }

                return Padding(
                  padding: const EdgeInsets.only(bottom: SpacingTokens.sm),
                  child: Card(
                    color: cardColor,
                    child: InkWell(
                      onTap: () => _selectOption(i),
                      borderRadius: BorderRadius.circular(RadiusTokens.lg),
                      child: Padding(
                        padding: const EdgeInsets.symmetric(
                          horizontal: SpacingTokens.md,
                          vertical: SpacingTokens.md,
                        ),
                        child: Row(
                          children: [
                            Text(
                              String.fromCharCode(65 + i), // A, B, C, D
                              style: theme.textTheme.titleMedium?.copyWith(
                                fontWeight: FontWeight.w700,
                                color: colorScheme.primary,
                              ),
                            ),
                            const SizedBox(width: SpacingTokens.md),
                            Expanded(
                              child: Text(
                                _seedQuestion.options[i],
                                style: theme.textTheme.bodyLarge,
                              ),
                            ),
                            if (_answered && isCorrectOption)
                              const Icon(Icons.check_circle_rounded,
                                  color: Colors.green, size: 20),
                            if (_answered && isSelected && !isCorrectOption)
                              const Icon(Icons.cancel_rounded,
                                  color: Colors.red, size: 20),
                          ],
                        ),
                      ),
                    ),
                  ),
                );
              }),

              // Feedback + explanation
              if (_answered) ...[
                const SizedBox(height: SpacingTokens.sm),
                Card(
                  color: _isCorrect
                      ? Colors.green.withValues(alpha: 0.08)
                      : Colors.orange.withValues(alpha: 0.08),
                  child: Padding(
                    padding: const EdgeInsets.all(SpacingTokens.md),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Row(
                          children: [
                            Icon(
                              _isCorrect
                                  ? Icons.celebration_rounded
                                  : Icons.lightbulb_rounded,
                              size: 20,
                              color:
                                  _isCorrect ? Colors.green : Colors.orange,
                            ),
                            const SizedBox(width: SpacingTokens.sm),
                            Text(
                              _isCorrect ? 'Correct!' : 'Not quite — here\'s how:',
                              style: theme.textTheme.titleSmall?.copyWith(
                                fontWeight: FontWeight.w700,
                                color: _isCorrect
                                    ? Colors.green
                                    : Colors.orange,
                              ),
                            ),
                          ],
                        ),
                        const SizedBox(height: SpacingTokens.sm),
                        Text(
                          _seedQuestion.explanation,
                          style: theme.textTheme.bodyMedium,
                        ),
                      ],
                    ),
                  ),
                ),
              ],

              const Spacer(),

              // CTA
              if (_answered) ...[
                Text(
                  'Cena adapts to your level with AI-powered tutoring.',
                  style: theme.textTheme.bodyMedium?.copyWith(
                    color: colorScheme.onSurfaceVariant,
                  ),
                  textAlign: TextAlign.center,
                ),
                const SizedBox(height: SpacingTokens.md),
                FilledButton.icon(
                  onPressed: () => context.go(CenaRoutes.login),
                  icon: const Icon(Icons.arrow_forward_rounded),
                  label: const Text('Create Account & Continue'),
                  style: FilledButton.styleFrom(
                    minimumSize: const Size(double.infinity, 48),
                  ),
                ),
                const SizedBox(height: SpacingTokens.sm),
                TextButton(
                  onPressed: () => context.go(CenaRoutes.login),
                  child: const Text('Already have an account? Sign in'),
                ),
              ],

              if (!_answered) ...[
                Text(
                  'No account needed — just tap an answer',
                  style: theme.textTheme.bodySmall?.copyWith(
                    color: colorScheme.onSurfaceVariant,
                  ),
                  textAlign: TextAlign.center,
                ),
              ],
            ],
          ),
        ),
      ),
    );
  }
}
