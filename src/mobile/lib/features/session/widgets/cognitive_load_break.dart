// =============================================================================
// Cena Adaptive Learning Platform — Cognitive Load Break Screen
// Breathing animation break screen shown when fatigueScore >= 0.7.
// =============================================================================

import 'dart:async';

import 'package:flutter/material.dart';

import '../../../core/config/app_config.dart';

/// Full-screen overlay that surfaces when the cognitive load monitor
/// recommends a break ([SessionState.isBreakSuggested] == true).
///
/// Shows a breathing animation (expand/contract on a 4-second cycle),
/// a countdown timer, and two actions: continue or end the session.
class CognitiveLoadBreak extends StatefulWidget {
  const CognitiveLoadBreak({
    super.key,
    required this.suggestedMinutes,
    required this.onContinue,
    required this.onEndSession,
  });

  final int suggestedMinutes;
  final VoidCallback onContinue;
  final VoidCallback onEndSession;

  @override
  State<CognitiveLoadBreak> createState() => _CognitiveLoadBreakState();
}

class _CognitiveLoadBreakState extends State<CognitiveLoadBreak>
    with SingleTickerProviderStateMixin {
  late final AnimationController _breathController;
  late final Animation<double> _breathScale;
  late final Animation<Color?> _breathColor;

  late int _remainingSeconds;
  Timer? _countdownTimer;

  @override
  void initState() {
    super.initState();
    _remainingSeconds = widget.suggestedMinutes * 60;

    // 4-second breathing cycle: inhale 2s, exhale 2s
    _breathController = AnimationController(
      vsync: this,
      duration: const Duration(seconds: 4),
    )..repeat(reverse: true);

    _breathScale = Tween<double>(begin: 0.65, end: 1.0).animate(
      CurvedAnimation(parent: _breathController, curve: Curves.easeInOut),
    );

    _breathColor = ColorTween(
      begin: const Color(0xFF81C784), // soft green
      end: const Color(0xFF64B5F6), // soft blue
    ).animate(
      CurvedAnimation(parent: _breathController, curve: Curves.easeInOut),
    );

    _startCountdown();
  }

  @override
  void dispose() {
    _breathController.dispose();
    _countdownTimer?.cancel();
    super.dispose();
  }

  void _startCountdown() {
    _countdownTimer =
        Timer.periodic(const Duration(seconds: 1), (_) {
      if (!mounted) return;
      setState(() {
        if (_remainingSeconds > 0) {
          _remainingSeconds--;
        } else {
          _countdownTimer?.cancel();
          // Auto-continue when break time expires
          widget.onContinue();
        }
      });
    });
  }

  String get _timerLabel {
    final minutes = (_remainingSeconds ~/ 60).toString().padLeft(2, '0');
    final seconds = (_remainingSeconds % 60).toString().padLeft(2, '0');
    return '$minutes:$seconds';
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Material(
      color: const Color(0xFFE8F5E9),
      child: SafeArea(
        child: Padding(
          padding: const EdgeInsets.all(SpacingTokens.xl),
          child: Column(
            mainAxisAlignment: MainAxisAlignment.center,
            children: [
              // Header
              Text(
                'זמן הפסקה',
                style: theme.textTheme.headlineLarge?.copyWith(
                  color: const Color(0xFF2E7D32),
                  fontWeight: FontWeight.w700,
                ),
              ),
              const SizedBox(height: SpacingTokens.sm),
              Text(
                'זיהינו שאתה עייף. קח נשימה עמוקה.',
                style: theme.textTheme.bodyLarge?.copyWith(
                  color: const Color(0xFF388E3C),
                ),
                textAlign: TextAlign.center,
              ),

              const SizedBox(height: SpacingTokens.xxl),

              // Breathing animation circle
              AnimatedBuilder(
                animation: _breathController,
                builder: (context, _) {
                  return Column(
                    children: [
                      Transform.scale(
                        scale: _breathScale.value,
                        child: Container(
                          width: 180,
                          height: 180,
                          decoration: BoxDecoration(
                            shape: BoxShape.circle,
                            color: _breathColor.value
                                ?.withValues(alpha: 0.3),
                            border: Border.all(
                              color: _breathColor.value ?? Colors.green,
                              width: 3,
                            ),
                            boxShadow: [
                              BoxShadow(
                                color: (_breathColor.value ?? Colors.green)
                                    .withValues(alpha: 0.3),
                                blurRadius: 24,
                                spreadRadius: 8,
                              ),
                            ],
                          ),
                          child: Center(
                            child: Text(
                              _breathController.value < 0.5
                                  ? 'שאף...'
                                  : 'נשוף...',
                              style: theme.textTheme.titleMedium?.copyWith(
                                color: const Color(0xFF2E7D32),
                                fontWeight: FontWeight.w600,
                              ),
                            ),
                          ),
                        ),
                      ),
                    ],
                  );
                },
              ),

              const SizedBox(height: SpacingTokens.xl),

              // Countdown timer
              Text(
                _timerLabel,
                style: theme.textTheme.displayMedium?.copyWith(
                  fontFamily: TypographyTokens.monoFontFamily,
                  color: const Color(0xFF1B5E20),
                  fontWeight: FontWeight.w700,
                ),
              ),
              const SizedBox(height: SpacingTokens.sm),
              Text(
                'זמן הפסקה מומלץ: ${widget.suggestedMinutes} דקות',
                style: theme.textTheme.bodySmall?.copyWith(
                  color: const Color(0xFF4CAF50),
                ),
              ),

              const SizedBox(height: SpacingTokens.xxl),

              // Action buttons
              FilledButton.icon(
                onPressed: widget.onContinue,
                icon: const Icon(Icons.play_arrow_rounded),
                label: const Text('המשך שיעור'),
                style: FilledButton.styleFrom(
                  backgroundColor: const Color(0xFF43A047),
                  minimumSize: const Size(double.infinity, 48),
                ),
              ),
              const SizedBox(height: SpacingTokens.sm),
              OutlinedButton.icon(
                onPressed: widget.onEndSession,
                icon: const Icon(Icons.stop_rounded),
                label: const Text('סיים שיעור'),
                style: OutlinedButton.styleFrom(
                  foregroundColor: const Color(0xFF388E3C),
                  side: const BorderSide(color: Color(0xFF388E3C)),
                  minimumSize: const Size(double.infinity, 48),
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}
