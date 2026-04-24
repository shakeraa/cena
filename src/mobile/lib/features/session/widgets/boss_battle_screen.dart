// =============================================================================
// Cena Adaptive Learning Platform — Boss Battle Screen
// =============================================================================
//
// Full-screen boss battle experience with:
//   - Dramatic intro animation
//   - Boss HP bar that decreases with correct answers
//   - Student lives display (hearts)
//   - Power-up buttons
//   - No hints (assessment mode)
//   - Victory: epic celebration + unique badge
//   - Defeat: encouraging message + retry option
// =============================================================================

import 'dart:async';
import 'dart:math';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/config/app_config.dart';
import '../../gamification/celebration_overlay.dart';
import '../../gamification/celebration_service.dart';
import '../models/boss_battle.dart';

// ---------------------------------------------------------------------------
// Boss Battle Screen
// ---------------------------------------------------------------------------

/// Full-screen boss battle widget.
///
/// Pass the [battle] model (already initialized with module info and questions),
/// [onAnswer] callback to submit answers, and [onComplete] for result handling.
class BossBattleScreen extends ConsumerStatefulWidget {
  const BossBattleScreen({
    super.key,
    required this.battle,
    required this.questionText,
    required this.options,
    required this.onAnswer,
    required this.onComplete,
    this.onRetry,
  });

  /// The active boss battle state.
  final BossBattle battle;

  /// Current question text (updated externally via provider or callback).
  final String questionText;

  /// MCQ options for the current question (null for non-MCQ).
  final List<String>? options;

  /// Called when the student submits an answer. Returns true if correct.
  final Future<bool> Function(String answer) onAnswer;

  /// Called when the battle ends (victory or defeat).
  final void Function(BossBattleResult result) onComplete;

  /// Called when the student chooses to retry after defeat.
  final VoidCallback? onRetry;

  @override
  ConsumerState<BossBattleScreen> createState() => _BossBattleScreenState();
}

class _BossBattleScreenState extends ConsumerState<BossBattleScreen>
    with TickerProviderStateMixin {
  late final CelebrationController _celebrationController;
  late final AnimationController _introController;
  late final AnimationController _shakeController;
  late final Stopwatch _stopwatch;

  bool _showIntro = true;
  bool _showResult = false;
  BossBattleResult? _result;
  List<String>? _eliminatedOptions;
  bool _isAnswering = false;

  @override
  void initState() {
    super.initState();
    _celebrationController = CelebrationController();
    _stopwatch = Stopwatch()..start();

    _introController = AnimationController(
      vsync: this,
      duration: const Duration(milliseconds: 2000),
    );

    _shakeController = AnimationController(
      vsync: this,
      duration: const Duration(milliseconds: 400),
    );

    _introController.addStatusListener((status) {
      if (status == AnimationStatus.completed && mounted) {
        setState(() => _showIntro = false);
      }
    });

    // Start intro animation.
    _introController.forward();
  }

  @override
  void dispose() {
    _introController.dispose();
    _shakeController.dispose();
    _stopwatch.stop();
    super.dispose();
  }

  Future<void> _submitAnswer(String answer) async {
    if (_isAnswering || !widget.battle.isActive) return;
    setState(() => _isAnswering = true);

    final isCorrect = await widget.onAnswer(answer);

    if (isCorrect) {
      widget.battle.recordCorrectAnswer();
      if (mounted) {
        _celebrationController.celebrate(
          tier: CelebrationTier.micro,
          xp: 0,
        );
      }
    } else {
      widget.battle.recordWrongAnswer();
      if (mounted) {
        _shakeController.forward(from: 0);
      }
    }

    if (!widget.battle.isActive && mounted) {
      _stopwatch.stop();
      final result = widget.battle.getResult(_stopwatch.elapsedMilliseconds);
      setState(() {
        _result = result;
        _showResult = true;
        _isAnswering = false;
      });

      if (result.outcome == BossBattleOutcome.victory) {
        _celebrationController.celebrate(
          tier: CelebrationTier.epic,
          message: BossBattle.victoryMessage(widget.battle.moduleName),
        );
      }

      widget.onComplete(result);
    } else {
      setState(() {
        _isAnswering = false;
        _eliminatedOptions = null;
      });
    }
  }

  void _usePowerUp(PowerUp powerUp) {
    if (!widget.battle.isActive) return;

    final success = widget.battle.usePowerUp(powerUp);
    if (!success) return;

    if (powerUp == PowerUp.fiftyFiftyEliminator &&
        widget.options != null &&
        widget.options!.length >= 4) {
      // Eliminate 2 random wrong options (keep first option as "correct" stand-in).
      final rng = Random();
      final indices = List.generate(widget.options!.length, (i) => i);
      indices.shuffle(rng);
      final toEliminate = indices.take(2).map((i) => widget.options![i]).toList();
      setState(() => _eliminatedOptions = toEliminate);
    }

    setState(() {}); // Refresh power-up availability.
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: const Color(0xFF1A1A2E),
      body: SafeArea(
        child: Stack(
          children: [
            if (_showIntro)
              _BossIntro(
                moduleName: widget.battle.moduleName,
                animation: _introController,
              )
            else if (_showResult && _result != null)
              _BossResult(
                result: _result!,
                moduleName: widget.battle.moduleName,
                onRetry: widget.onRetry,
              )
            else
              _BattleArena(
                battle: widget.battle,
                questionText: widget.questionText,
                options: widget.options,
                eliminatedOptions: _eliminatedOptions,
                onAnswer: _submitAnswer,
                onPowerUp: _usePowerUp,
                shakeController: _shakeController,
                isAnswering: _isAnswering,
              ),
            CelebrationOverlay(controller: _celebrationController),
          ],
        ),
      ),
    );
  }
}

// ---------------------------------------------------------------------------
// Boss Intro Animation
// ---------------------------------------------------------------------------

class _BossIntro extends AnimatedWidget {
  const _BossIntro({
    required this.moduleName,
    required Animation<double> animation,
  }) : super(listenable: animation);

  final String moduleName;

  @override
  Widget build(BuildContext context) {
    final progress = (listenable as Animation<double>).value;
    final textOpacity = (progress * 3).clamp(0.0, 1.0);
    final scale = 0.5 + 0.5 * Curves.elasticOut.transform(
        (progress * 2).clamp(0.0, 1.0));
    final fadeOut = progress > 0.7
        ? ((1.0 - progress) / 0.3).clamp(0.0, 1.0)
        : 1.0;

    return Positioned.fill(
      child: Container(
        color: const Color(0xFF1A1A2E),
        child: Opacity(
          opacity: fadeOut,
          child: Center(
            child: Transform.scale(
              scale: scale.clamp(0.5, 1.2),
              child: Opacity(
                opacity: textOpacity,
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    const Icon(
                      Icons.shield_rounded,
                      size: 80,
                      color: Color(0xFFFF6D00),
                      shadows: [
                        Shadow(
                          color: Color(0x88FF6D00),
                          blurRadius: 30,
                        ),
                      ],
                    ),
                    const SizedBox(height: SpacingTokens.lg),
                    Text(
                      'The $moduleName\nChallenge awaits!',
                      style: const TextStyle(
                        color: Colors.white,
                        fontSize: 28,
                        fontWeight: FontWeight.w900,
                        letterSpacing: 0.5,
                        height: 1.3,
                      ),
                      textAlign: TextAlign.center,
                    ),
                    const SizedBox(height: SpacingTokens.md),
                    Text(
                      'No hints. No shortcuts. Prove your mastery.',
                      style: TextStyle(
                        color: Colors.white.withValues(alpha: 0.7),
                        fontSize: 16,
                        fontWeight: FontWeight.w500,
                      ),
                      textAlign: TextAlign.center,
                    ),
                  ],
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}

// ---------------------------------------------------------------------------
// Battle Arena (Main Battle UI)
// ---------------------------------------------------------------------------

class _BattleArena extends StatelessWidget {
  const _BattleArena({
    required this.battle,
    required this.questionText,
    required this.options,
    required this.eliminatedOptions,
    required this.onAnswer,
    required this.onPowerUp,
    required this.shakeController,
    required this.isAnswering,
  });

  final BossBattle battle;
  final String questionText;
  final List<String>? options;
  final List<String>? eliminatedOptions;
  final Future<void> Function(String) onAnswer;
  final void Function(PowerUp) onPowerUp;
  final AnimationController shakeController;
  final bool isAnswering;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.all(SpacingTokens.md),
      child: Column(
        children: [
          // Boss HP bar
          _BossHpBar(
            currentHp: battle.bossHp,
            maxHp: battle.totalQuestions,
            moduleName: battle.moduleName,
          ),

          const SizedBox(height: SpacingTokens.md),

          // Student lives
          _LivesDisplay(lives: battle.studentLives),

          const SizedBox(height: SpacingTokens.lg),

          // Question
          Expanded(
            child: AnimatedBuilder(
              animation: shakeController,
              builder: (context, child) {
                final shake = sin(shakeController.value * pi * 4) * 8;
                return Transform.translate(
                  offset: Offset(shake, 0),
                  child: child,
                );
              },
              child: _QuestionArea(
                questionText: questionText,
                options: options,
                eliminatedOptions: eliminatedOptions,
                onAnswer: onAnswer,
                isAnswering: isAnswering,
              ),
            ),
          ),

          // Power-ups
          if (battle.availablePowerUps.isNotEmpty)
            _PowerUpBar(
              powerUps: battle.availablePowerUps,
              onUse: onPowerUp,
            ),

          const SizedBox(height: SpacingTokens.sm),
        ],
      ),
    );
  }
}

// ---------------------------------------------------------------------------
// Boss HP Bar
// ---------------------------------------------------------------------------

class _BossHpBar extends StatelessWidget {
  const _BossHpBar({
    required this.currentHp,
    required this.maxHp,
    required this.moduleName,
  });

  final int currentHp;
  final int maxHp;
  final String moduleName;

  @override
  Widget build(BuildContext context) {
    final fraction = maxHp > 0 ? (currentHp / maxHp) : 0.0;
    final hpColor = fraction > 0.5
        ? const Color(0xFFFF6D00)
        : fraction > 0.25
            ? const Color(0xFFFFB300)
            : const Color(0xFFEF5350);

    return Column(
      children: [
        Row(
          children: [
            const Icon(
              Icons.shield_rounded,
              size: 20,
              color: Color(0xFFFF6D00),
            ),
            const SizedBox(width: SpacingTokens.xs),
            Text(
              moduleName,
              style: const TextStyle(
                color: Colors.white,
                fontSize: 16,
                fontWeight: FontWeight.w700,
              ),
            ),
            const Spacer(),
            Text(
              '$currentHp / $maxHp HP',
              style: TextStyle(
                color: hpColor,
                fontSize: 14,
                fontWeight: FontWeight.w700,
              ),
            ),
          ],
        ),
        const SizedBox(height: SpacingTokens.xs),
        ClipRRect(
          borderRadius: BorderRadius.circular(RadiusTokens.full),
          child: TweenAnimationBuilder<double>(
            tween: Tween(begin: 1.0, end: fraction),
            duration: AnimationTokens.normal,
            curve: Curves.easeOutCubic,
            builder: (context, value, _) {
              return LinearProgressIndicator(
                value: value,
                minHeight: 14,
                backgroundColor: Colors.white.withValues(alpha: 0.1),
                valueColor: AlwaysStoppedAnimation<Color>(hpColor),
              );
            },
          ),
        ),
      ],
    );
  }
}

// ---------------------------------------------------------------------------
// Lives Display
// ---------------------------------------------------------------------------

class _LivesDisplay extends StatelessWidget {
  const _LivesDisplay({required this.lives});

  final int lives;

  @override
  Widget build(BuildContext context) {
    return Row(
      mainAxisAlignment: MainAxisAlignment.center,
      children: List.generate(
        3,
        (i) => Padding(
          padding: const EdgeInsets.symmetric(horizontal: SpacingTokens.xxs),
          child: Icon(
            i < lives ? Icons.favorite_rounded : Icons.favorite_border_rounded,
            size: 28,
            color: i < lives
                ? const Color(0xFFEF5350)
                : Colors.white.withValues(alpha: 0.3),
          ),
        ),
      ),
    );
  }
}

// ---------------------------------------------------------------------------
// Question Area
// ---------------------------------------------------------------------------

class _QuestionArea extends StatelessWidget {
  const _QuestionArea({
    required this.questionText,
    required this.options,
    required this.eliminatedOptions,
    required this.onAnswer,
    required this.isAnswering,
  });

  final String questionText;
  final List<String>? options;
  final List<String>? eliminatedOptions;
  final Future<void> Function(String) onAnswer;
  final bool isAnswering;

  @override
  Widget build(BuildContext context) {
    return SingleChildScrollView(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          // Question text
          Container(
            padding: const EdgeInsets.all(SpacingTokens.lg),
            decoration: BoxDecoration(
              color: Colors.white.withValues(alpha: 0.05),
              borderRadius: BorderRadius.circular(RadiusTokens.lg),
              border: Border.all(
                color: Colors.white.withValues(alpha: 0.1),
              ),
            ),
            child: Text(
              questionText,
              style: const TextStyle(
                color: Colors.white,
                fontSize: 18,
                fontWeight: FontWeight.w500,
                height: 1.5,
              ),
            ),
          ),

          const SizedBox(height: SpacingTokens.lg),

          // MCQ options
          if (options != null)
            ...options!.map((option) {
              final isEliminated =
                  eliminatedOptions?.contains(option) ?? false;
              return Padding(
                padding: const EdgeInsets.only(bottom: SpacingTokens.sm),
                child: AnimatedOpacity(
                  opacity: isEliminated ? 0.3 : 1.0,
                  duration: AnimationTokens.fast,
                  child: OutlinedButton(
                    onPressed: isEliminated || isAnswering
                        ? null
                        : () => onAnswer(option),
                    style: OutlinedButton.styleFrom(
                      foregroundColor: Colors.white,
                      side: BorderSide(
                        color: isEliminated
                            ? Colors.white.withValues(alpha: 0.1)
                            : Colors.white.withValues(alpha: 0.3),
                      ),
                      padding: const EdgeInsets.all(SpacingTokens.md),
                      shape: RoundedRectangleBorder(
                        borderRadius:
                            BorderRadius.circular(RadiusTokens.lg),
                      ),
                    ),
                    child: Text(
                      option,
                      style: TextStyle(
                        fontSize: 16,
                        decoration: isEliminated
                            ? TextDecoration.lineThrough
                            : null,
                      ),
                    ),
                  ),
                ),
              );
            }),
        ],
      ),
    );
  }
}

// ---------------------------------------------------------------------------
// Power-Up Bar
// ---------------------------------------------------------------------------

class _PowerUpBar extends StatelessWidget {
  const _PowerUpBar({
    required this.powerUps,
    required this.onUse,
  });

  final List<PowerUp> powerUps;
  final void Function(PowerUp) onUse;

  @override
  Widget build(BuildContext context) {
    // Deduplicate: show count per type.
    final counts = <PowerUp, int>{};
    for (final p in powerUps) {
      counts[p] = (counts[p] ?? 0) + 1;
    }

    return Container(
      padding: const EdgeInsets.symmetric(
        horizontal: SpacingTokens.md,
        vertical: SpacingTokens.sm,
      ),
      decoration: BoxDecoration(
        color: Colors.white.withValues(alpha: 0.05),
        borderRadius: BorderRadius.circular(RadiusTokens.lg),
      ),
      child: Row(
        mainAxisAlignment: MainAxisAlignment.center,
        children: counts.entries.map((entry) {
          return Padding(
            padding:
                const EdgeInsets.symmetric(horizontal: SpacingTokens.xs),
            child: _PowerUpButton(
              powerUp: entry.key,
              count: entry.value,
              onTap: () => onUse(entry.key),
            ),
          );
        }).toList(),
      ),
    );
  }
}

class _PowerUpButton extends StatelessWidget {
  const _PowerUpButton({
    required this.powerUp,
    required this.count,
    required this.onTap,
  });

  final PowerUp powerUp;
  final int count;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    return GestureDetector(
      onTap: onTap,
      child: Container(
        padding: const EdgeInsets.symmetric(
          horizontal: SpacingTokens.sm,
          vertical: SpacingTokens.xs,
        ),
        decoration: BoxDecoration(
          color: _powerUpColor(powerUp).withValues(alpha: 0.2),
          borderRadius: BorderRadius.circular(RadiusTokens.md),
          border: Border.all(
            color: _powerUpColor(powerUp).withValues(alpha: 0.5),
          ),
        ),
        child: Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(
              _powerUpIcon(powerUp),
              size: 18,
              color: _powerUpColor(powerUp),
            ),
            const SizedBox(width: SpacingTokens.xxs),
            Text(
              'x$count',
              style: TextStyle(
                color: _powerUpColor(powerUp),
                fontSize: 14,
                fontWeight: FontWeight.w700,
              ),
            ),
          ],
        ),
      ),
    );
  }

  IconData _powerUpIcon(PowerUp p) {
    switch (p) {
      case PowerUp.extraLife:
        return Icons.favorite_rounded;
      case PowerUp.fiftyFiftyEliminator:
        return Icons.content_cut_rounded;
      case PowerUp.timeFreeze:
        return Icons.timer_off_rounded;
    }
  }

  Color _powerUpColor(PowerUp p) {
    switch (p) {
      case PowerUp.extraLife:
        return const Color(0xFFEF5350);
      case PowerUp.fiftyFiftyEliminator:
        return const Color(0xFF42A5F5);
      case PowerUp.timeFreeze:
        return const Color(0xFF66BB6A);
    }
  }
}

// ---------------------------------------------------------------------------
// Boss Battle Result Screen
// ---------------------------------------------------------------------------

class _BossResult extends StatelessWidget {
  const _BossResult({
    required this.result,
    required this.moduleName,
    this.onRetry,
  });

  final BossBattleResult result;
  final String moduleName;
  final VoidCallback? onRetry;

  @override
  Widget build(BuildContext context) {
    final isVictory = result.outcome == BossBattleOutcome.victory;

    return Positioned.fill(
      child: Center(
        child: Padding(
          padding: const EdgeInsets.all(SpacingTokens.xl),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              Icon(
                isVictory
                    ? Icons.emoji_events_rounded
                    : Icons.refresh_rounded,
                size: 80,
                color: isVictory
                    ? const Color(0xFFFFD700)
                    : Colors.white.withValues(alpha: 0.6),
                shadows: isVictory
                    ? [
                        const Shadow(
                          color: Color(0x88FFD700),
                          blurRadius: 30,
                        ),
                      ]
                    : null,
              ),
              const SizedBox(height: SpacingTokens.lg),

              Text(
                isVictory ? 'Victory!' : 'Defeated',
                style: TextStyle(
                  color: isVictory
                      ? const Color(0xFFFFD700)
                      : Colors.white,
                  fontSize: 36,
                  fontWeight: FontWeight.w900,
                ),
              ),

              const SizedBox(height: SpacingTokens.sm),

              Text(
                isVictory
                    ? BossBattle.victoryMessage(moduleName)
                    : BossBattle.defeatMessage(),
                style: TextStyle(
                  color: Colors.white.withValues(alpha: 0.8),
                  fontSize: 16,
                  fontWeight: FontWeight.w500,
                ),
                textAlign: TextAlign.center,
              ),

              const SizedBox(height: SpacingTokens.lg),

              // Stats
              Container(
                padding: const EdgeInsets.all(SpacingTokens.md),
                decoration: BoxDecoration(
                  color: Colors.white.withValues(alpha: 0.08),
                  borderRadius: BorderRadius.circular(RadiusTokens.lg),
                ),
                child: Column(
                  children: [
                    _StatRow(
                      label: 'Accuracy',
                      value:
                          '${(result.accuracy * 100).round()}%',
                    ),
                    const SizedBox(height: SpacingTokens.xs),
                    _StatRow(
                      label: 'Correct',
                      value:
                          '${result.correctAnswers} / ${result.questionsAnswered}',
                    ),
                    const SizedBox(height: SpacingTokens.xs),
                    _StatRow(
                      label: 'Lives left',
                      value: '${result.livesRemaining}',
                    ),
                  ],
                ),
              ),

              const SizedBox(height: SpacingTokens.xl),

              // Actions
              if (!isVictory && onRetry != null)
                SizedBox(
                  width: double.infinity,
                  child: FilledButton.icon(
                    onPressed: onRetry,
                    icon: const Icon(Icons.refresh_rounded),
                    label: const Text('Try Again'),
                    style: FilledButton.styleFrom(
                      backgroundColor: const Color(0xFFFF6D00),
                      padding: const EdgeInsets.all(SpacingTokens.md),
                    ),
                  ),
                ),
              const SizedBox(height: SpacingTokens.sm),
              SizedBox(
                width: double.infinity,
                child: OutlinedButton(
                  onPressed: () => Navigator.of(context).pop(),
                  style: OutlinedButton.styleFrom(
                    foregroundColor: Colors.white,
                    side: BorderSide(
                      color: Colors.white.withValues(alpha: 0.3),
                    ),
                    padding: const EdgeInsets.all(SpacingTokens.md),
                  ),
                  child: const Text('Back to Home'),
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _StatRow extends StatelessWidget {
  const _StatRow({
    required this.label,
    required this.value,
  });

  final String label;
  final String value;

  @override
  Widget build(BuildContext context) {
    return Row(
      mainAxisAlignment: MainAxisAlignment.spaceBetween,
      children: [
        Text(
          label,
          style: TextStyle(
            color: Colors.white.withValues(alpha: 0.7),
            fontSize: 14,
          ),
        ),
        Text(
          value,
          style: const TextStyle(
            color: Colors.white,
            fontSize: 14,
            fontWeight: FontWeight.w700,
          ),
        ),
      ],
    );
  }
}
