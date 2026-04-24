// =============================================================================
// Cena Adaptive Learning Platform — Boss Battle Result & Sub-Widgets
// =============================================================================

import 'package:flutter/material.dart';

import '../../../core/config/app_config.dart';
import '../models/boss_battle.dart';

// ---------------------------------------------------------------------------
// Boss HP Bar
// ---------------------------------------------------------------------------

class BossHpBar extends StatelessWidget {
  const BossHpBar({
    super.key,
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
            const Icon(Icons.shield_rounded, size: 20, color: Color(0xFFFF6D00)),
            const SizedBox(width: SpacingTokens.xs),
            Text(moduleName, style: const TextStyle(color: Colors.white, fontSize: 16, fontWeight: FontWeight.w700)),
            const Spacer(),
            Text('$currentHp / $maxHp HP', style: TextStyle(color: hpColor, fontSize: 14, fontWeight: FontWeight.w700)),
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

class LivesDisplay extends StatelessWidget {
  const LivesDisplay({super.key, required this.lives});
  final int lives;

  @override
  Widget build(BuildContext context) {
    return Row(
      mainAxisAlignment: MainAxisAlignment.center,
      children: List.generate(3, (i) => Padding(
        padding: const EdgeInsets.symmetric(horizontal: SpacingTokens.xxs),
        child: Icon(
          i < lives ? Icons.favorite_rounded : Icons.favorite_border_rounded,
          size: 28,
          color: i < lives ? const Color(0xFFEF5350) : Colors.white.withValues(alpha: 0.3),
        ),
      )),
    );
  }
}

// ---------------------------------------------------------------------------
// Question Area
// ---------------------------------------------------------------------------

class BossQuestionArea extends StatelessWidget {
  const BossQuestionArea({
    super.key,
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
          Container(
            padding: const EdgeInsets.all(SpacingTokens.lg),
            decoration: BoxDecoration(
              color: Colors.white.withValues(alpha: 0.05),
              borderRadius: BorderRadius.circular(RadiusTokens.lg),
              border: Border.all(color: Colors.white.withValues(alpha: 0.1)),
            ),
            child: Text(questionText, style: const TextStyle(color: Colors.white, fontSize: 18, fontWeight: FontWeight.w500, height: 1.5)),
          ),
          const SizedBox(height: SpacingTokens.lg),
          if (options != null)
            ...options!.map((option) {
              final isEliminated = eliminatedOptions?.contains(option) ?? false;
              return Padding(
                padding: const EdgeInsets.only(bottom: SpacingTokens.sm),
                child: AnimatedOpacity(
                  opacity: isEliminated ? 0.3 : 1.0,
                  duration: AnimationTokens.fast,
                  child: OutlinedButton(
                    onPressed: isEliminated || isAnswering ? null : () => onAnswer(option),
                    style: OutlinedButton.styleFrom(
                      foregroundColor: Colors.white,
                      side: BorderSide(color: isEliminated ? Colors.white.withValues(alpha: 0.1) : Colors.white.withValues(alpha: 0.3)),
                      padding: const EdgeInsets.all(SpacingTokens.md),
                      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(RadiusTokens.lg)),
                    ),
                    child: Text(option, style: TextStyle(fontSize: 16, decoration: isEliminated ? TextDecoration.lineThrough : null)),
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

class PowerUpBar extends StatelessWidget {
  const PowerUpBar({super.key, required this.powerUps, required this.onUse});
  final List<PowerUp> powerUps;
  final void Function(PowerUp) onUse;

  @override
  Widget build(BuildContext context) {
    final counts = <PowerUp, int>{};
    for (final p in powerUps) {
      counts[p] = (counts[p] ?? 0) + 1;
    }
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: SpacingTokens.md, vertical: SpacingTokens.sm),
      decoration: BoxDecoration(color: Colors.white.withValues(alpha: 0.05), borderRadius: BorderRadius.circular(RadiusTokens.lg)),
      child: Row(
        mainAxisAlignment: MainAxisAlignment.center,
        children: counts.entries.map((e) => Padding(
          padding: const EdgeInsets.symmetric(horizontal: SpacingTokens.xs),
          child: _PowerUpButton(powerUp: e.key, count: e.value, onTap: () => onUse(e.key)),
        )).toList(),
      ),
    );
  }
}

class _PowerUpButton extends StatelessWidget {
  const _PowerUpButton({required this.powerUp, required this.count, required this.onTap});
  final PowerUp powerUp;
  final int count;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final color = _color(powerUp);
    return GestureDetector(
      onTap: onTap,
      child: Container(
        padding: const EdgeInsets.symmetric(horizontal: SpacingTokens.sm, vertical: SpacingTokens.xs),
        decoration: BoxDecoration(color: color.withValues(alpha: 0.2), borderRadius: BorderRadius.circular(RadiusTokens.md), border: Border.all(color: color.withValues(alpha: 0.5))),
        child: Row(mainAxisSize: MainAxisSize.min, children: [
          Icon(_icon(powerUp), size: 18, color: color),
          const SizedBox(width: SpacingTokens.xxs),
          Text('x$count', style: TextStyle(color: color, fontSize: 14, fontWeight: FontWeight.w700)),
        ]),
      ),
    );
  }

  IconData _icon(PowerUp p) => switch (p) { PowerUp.extraLife => Icons.favorite_rounded, PowerUp.fiftyFiftyEliminator => Icons.content_cut_rounded, PowerUp.timeFreeze => Icons.timer_off_rounded };
  Color _color(PowerUp p) => switch (p) { PowerUp.extraLife => const Color(0xFFEF5350), PowerUp.fiftyFiftyEliminator => const Color(0xFF42A5F5), PowerUp.timeFreeze => const Color(0xFF66BB6A) };
}

// ---------------------------------------------------------------------------
// Boss Battle Result Screen
// ---------------------------------------------------------------------------

class BossResultScreen extends StatelessWidget {
  const BossResultScreen({super.key, required this.result, required this.moduleName, this.onRetry});
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
                isVictory ? Icons.emoji_events_rounded : Icons.refresh_rounded,
                size: 80,
                color: isVictory ? const Color(0xFFFFD700) : Colors.white.withValues(alpha: 0.6),
                shadows: isVictory ? [const Shadow(color: Color(0x88FFD700), blurRadius: 30)] : null,
              ),
              const SizedBox(height: SpacingTokens.lg),
              Text(isVictory ? 'Victory!' : 'Defeated', style: TextStyle(color: isVictory ? const Color(0xFFFFD700) : Colors.white, fontSize: 36, fontWeight: FontWeight.w900)),
              const SizedBox(height: SpacingTokens.sm),
              Text(isVictory ? BossBattle.victoryMessage(moduleName) : BossBattle.defeatMessage(), style: TextStyle(color: Colors.white.withValues(alpha: 0.8), fontSize: 16, fontWeight: FontWeight.w500), textAlign: TextAlign.center),
              const SizedBox(height: SpacingTokens.lg),
              Container(
                padding: const EdgeInsets.all(SpacingTokens.md),
                decoration: BoxDecoration(color: Colors.white.withValues(alpha: 0.08), borderRadius: BorderRadius.circular(RadiusTokens.lg)),
                child: Column(children: [
                  _StatRow(label: 'Accuracy', value: '${(result.accuracy * 100).round()}%'),
                  const SizedBox(height: SpacingTokens.xs),
                  _StatRow(label: 'Correct', value: '${result.correctAnswers} / ${result.questionsAnswered}'),
                  const SizedBox(height: SpacingTokens.xs),
                  _StatRow(label: 'Lives left', value: '${result.livesRemaining}'),
                ]),
              ),
              const SizedBox(height: SpacingTokens.xl),
              if (!isVictory && onRetry != null)
                SizedBox(
                  width: double.infinity,
                  child: FilledButton.icon(onPressed: onRetry, icon: const Icon(Icons.refresh_rounded), label: const Text('Try Again'), style: FilledButton.styleFrom(backgroundColor: const Color(0xFFFF6D00), padding: const EdgeInsets.all(SpacingTokens.md))),
                ),
              const SizedBox(height: SpacingTokens.sm),
              SizedBox(
                width: double.infinity,
                child: OutlinedButton(onPressed: () => Navigator.of(context).pop(), style: OutlinedButton.styleFrom(foregroundColor: Colors.white, side: BorderSide(color: Colors.white.withValues(alpha: 0.3)), padding: const EdgeInsets.all(SpacingTokens.md)), child: const Text('Back to Home')),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _StatRow extends StatelessWidget {
  const _StatRow({required this.label, required this.value});
  final String label;
  final String value;

  @override
  Widget build(BuildContext context) {
    return Row(mainAxisAlignment: MainAxisAlignment.spaceBetween, children: [
      Text(label, style: TextStyle(color: Colors.white.withValues(alpha: 0.7), fontSize: 14)),
      Text(value, style: const TextStyle(color: Colors.white, fontSize: 14, fontWeight: FontWeight.w700)),
    ]);
  }
}
