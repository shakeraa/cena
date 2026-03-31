// =============================================================================
// Cena Adaptive Learning Platform — Teach-Back Prompt Widget (MOB-048)
// =============================================================================
// After mastering a concept (P(known) > 0.85), prompts the student to
// explain the concept in their own words. Skippable, 2.5x XP bonus.
// Appears at most 2x per session.
// =============================================================================

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/config/app_config.dart';
import '../../../core/state/app_state.dart';
import '../models/teach_back_models.dart';

// ---------------------------------------------------------------------------
// State provider
// ---------------------------------------------------------------------------

/// Tracks teach-back prompts shown in the current session.
class _TeachBackSessionTracker {
  _TeachBackSessionTracker();

  final List<TeachBackState> _states = [];
  int _promptsShownThisSession = 0;

  int get promptsShown => _promptsShownThisSession;

  bool get canShowMore =>
      _promptsShownThisSession < TeachBackConfig.defaultConfig.maxPerSession;

  void recordPromptShown(String conceptId) {
    _promptsShownThisSession++;
    _states.add(TeachBackState(conceptId: conceptId));
  }

  TeachBackState? getState(String conceptId) {
    try {
      return _states.firstWhere((s) => s.conceptId == conceptId);
    } catch (_) {
      return null;
    }
  }

  void updateState(String conceptId, TeachBackState state) {
    final index = _states.indexWhere((s) => s.conceptId == conceptId);
    if (index >= 0) {
      _states[index] = state;
    }
  }

  void reset() {
    _states.clear();
    _promptsShownThisSession = 0;
  }
}

final _teachBackTrackerProvider = Provider.autoDispose<_TeachBackSessionTracker>(
  (ref) => _TeachBackSessionTracker(),
);

/// Provider to check if a teach-back prompt should be shown for a concept.
final shouldShowTeachBackProvider =
    Provider.autoDispose.family<bool, _TeachBackCheck>((ref, check) {
  final tracker = ref.watch(_teachBackTrackerProvider);
  if (!tracker.canShowMore) return false;
  if (check.pKnown < TeachBackConfig.defaultConfig.minMasteryThreshold) {
    return false;
  }
  // Don't show if already prompted for this concept.
  final existing = tracker.getState(check.conceptId);
  if (existing != null) return false;
  return true;
});

class _TeachBackCheck {
  const _TeachBackCheck({required this.conceptId, required this.pKnown});
  final String conceptId;
  final double pKnown;

  @override
  bool operator ==(Object other) =>
      other is _TeachBackCheck &&
      other.conceptId == conceptId &&
      other.pKnown == pKnown;

  @override
  int get hashCode => Object.hash(conceptId, pKnown);
}

// ---------------------------------------------------------------------------
// Widget
// ---------------------------------------------------------------------------

/// Teach-back prompt overlay shown after mastering a concept.
///
/// Presents a text input area (with voice-to-text option) where the student
/// can explain the concept in their own words. Skippable with no penalty.
/// Awards 2.5x XP bonus on completion.
class TeachBackPrompt extends ConsumerStatefulWidget {
  const TeachBackPrompt({
    super.key,
    required this.conceptId,
    required this.conceptName,
    required this.onSubmit,
    required this.onSkip,
    this.baseXp = 10,
  });

  final String conceptId;
  final String conceptName;

  /// Called when the student submits their explanation.
  final void Function(TeachBackSubmission submission) onSubmit;

  /// Called when the student skips the teach-back.
  final VoidCallback onSkip;

  /// Base XP for this concept (multiplied by 2.5x for teach-back).
  final int baseXp;

  @override
  ConsumerState<TeachBackPrompt> createState() => _TeachBackPromptState();
}

class _TeachBackPromptState extends ConsumerState<TeachBackPrompt>
    with SingleTickerProviderStateMixin {
  final _textController = TextEditingController();
  late final AnimationController _animController;
  late final Animation<double> _slideAnimation;
  bool _isSubmitting = false;
  TeachBackEvaluation? _evaluation;

  @override
  void initState() {
    super.initState();
    _animController = AnimationController(
      vsync: this,
      duration: AnimationTokens.slow,
    );
    _slideAnimation = CurvedAnimation(
      parent: _animController,
      curve: Curves.easeOutCubic,
    );
    _animController.forward();

    // Record that we showed this prompt.
    final tracker = ref.read(_teachBackTrackerProvider);
    tracker.recordPromptShown(widget.conceptId);
  }

  @override
  void dispose() {
    _textController.dispose();
    _animController.dispose();
    super.dispose();
  }

  int get _wordCount {
    final text = _textController.text.trim();
    if (text.isEmpty) return 0;
    return text.split(RegExp(r'\s+')).length;
  }

  bool get _isValidLength =>
      _wordCount >= TeachBackConfig.defaultConfig.minWordCount;

  int get _bonusXp =>
      (widget.baseXp * TeachBackConfig.defaultConfig.xpMultiplier).round();

  Future<void> _handleSubmit() async {
    if (!_isValidLength || _isSubmitting) return;

    setState(() => _isSubmitting = true);

    final student = ref.read(currentStudentProvider);
    final submission = TeachBackSubmission(
      studentId: student?.id ?? '',
      conceptId: widget.conceptId,
      explanationText: _textController.text.trim(),
      wordCount: _wordCount,
      timestamp: DateTime.now(),
    );

    widget.onSubmit(submission);
  }

  void _handleSkip() {
    final tracker = ref.read(_teachBackTrackerProvider);
    tracker.updateState(
      widget.conceptId,
      TeachBackState(conceptId: widget.conceptId, isSkipped: true),
    );
    widget.onSkip();
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;

    return SlideTransition(
      position: Tween<Offset>(
        begin: const Offset(0, 1),
        end: Offset.zero,
      ).animate(_slideAnimation),
      child: Container(
        margin: const EdgeInsets.all(SpacingTokens.md),
        decoration: BoxDecoration(
          color: colorScheme.surface,
          borderRadius: BorderRadius.circular(RadiusTokens.xl),
          border: Border.all(
            color: colorScheme.primary.withValues(alpha: 0.3),
            width: 1.5,
          ),
          boxShadow: [
            BoxShadow(
              color: colorScheme.shadow.withValues(alpha: 0.1),
              blurRadius: 16,
              offset: const Offset(0, 4),
            ),
          ],
        ),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            // Header
            _buildHeader(theme, colorScheme),

            // Body
            Padding(
              padding: const EdgeInsets.symmetric(
                horizontal: SpacingTokens.md,
              ),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  // Prompt text
                  Text(
                    'Can you explain "${widget.conceptName}" to a classmate?',
                    style: theme.textTheme.bodyLarge?.copyWith(
                      fontWeight: FontWeight.w500,
                    ),
                  ),
                  const SizedBox(height: SpacingTokens.sm),

                  // XP bonus badge
                  _buildXpBadge(theme, colorScheme),
                  const SizedBox(height: SpacingTokens.md),

                  // Text input
                  _buildTextInput(theme, colorScheme),
                  const SizedBox(height: SpacingTokens.sm),

                  // Word count indicator
                  _buildWordCount(theme, colorScheme),
                  const SizedBox(height: SpacingTokens.md),

                  // Evaluation result
                  if (_evaluation != null)
                    _buildEvaluationResult(theme, colorScheme),

                  // Action buttons
                  _buildActions(theme, colorScheme),
                  const SizedBox(height: SpacingTokens.md),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }

  Widget _buildHeader(ThemeData theme, ColorScheme colorScheme) {
    return Container(
      padding: const EdgeInsets.all(SpacingTokens.md),
      decoration: BoxDecoration(
        color: colorScheme.primaryContainer.withValues(alpha: 0.3),
        borderRadius: const BorderRadius.only(
          topLeft: Radius.circular(RadiusTokens.xl),
          topRight: Radius.circular(RadiusTokens.xl),
        ),
      ),
      child: Row(
        children: [
          Icon(
            Icons.school_rounded,
            size: 24,
            color: colorScheme.primary,
          ),
          const SizedBox(width: SpacingTokens.sm),
          Expanded(
            child: Text(
              'Teach-Back Challenge',
              style: theme.textTheme.titleMedium?.copyWith(
                fontWeight: FontWeight.w700,
                color: colorScheme.primary,
              ),
            ),
          ),
          // Skip button
          TextButton(
            onPressed: _handleSkip,
            child: Text(
              'Skip',
              style: theme.textTheme.labelMedium?.copyWith(
                color: colorScheme.onSurfaceVariant,
              ),
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildXpBadge(ThemeData theme, ColorScheme colorScheme) {
    return Container(
      padding: const EdgeInsets.symmetric(
        horizontal: SpacingTokens.sm,
        vertical: SpacingTokens.xs,
      ),
      decoration: BoxDecoration(
        color: Colors.amber.shade100,
        borderRadius: BorderRadius.circular(RadiusTokens.full),
        border: Border.all(color: Colors.amber.shade300),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Icon(Icons.star_rounded, size: 16, color: Colors.amber.shade700),
          const SizedBox(width: SpacingTokens.xs),
          Text(
            '2.5x XP Bonus (+$_bonusXp XP)',
            style: theme.textTheme.labelSmall?.copyWith(
              fontWeight: FontWeight.w700,
              color: Colors.amber.shade800,
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildTextInput(ThemeData theme, ColorScheme colorScheme) {
    return TextField(
      controller: _textController,
      maxLines: 4,
      maxLength: TeachBackConfig.defaultConfig.maxCharacters,
      enabled: !_isSubmitting && _evaluation == null,
      onChanged: (_) => setState(() {}),
      decoration: InputDecoration(
        hintText: 'Explain the concept in your own words...',
        hintStyle: theme.textTheme.bodyMedium?.copyWith(
          color: colorScheme.onSurfaceVariant.withValues(alpha: 0.5),
        ),
        filled: true,
        fillColor: colorScheme.surfaceContainerLowest,
        border: OutlineInputBorder(
          borderRadius: BorderRadius.circular(RadiusTokens.lg),
          borderSide: BorderSide(color: colorScheme.outlineVariant),
        ),
        enabledBorder: OutlineInputBorder(
          borderRadius: BorderRadius.circular(RadiusTokens.lg),
          borderSide: BorderSide(color: colorScheme.outlineVariant),
        ),
        focusedBorder: OutlineInputBorder(
          borderRadius: BorderRadius.circular(RadiusTokens.lg),
          borderSide: BorderSide(color: colorScheme.primary, width: 1.5),
        ),
        // Voice-to-text button
        suffixIcon: IconButton(
          icon: Icon(
            Icons.mic_rounded,
            color: colorScheme.primary,
          ),
          tooltip: 'Voice input',
          onPressed: _isSubmitting ? null : _handleVoiceInput,
        ),
      ),
    );
  }

  Widget _buildWordCount(ThemeData theme, ColorScheme colorScheme) {
    final count = _wordCount;
    final minRequired = TeachBackConfig.defaultConfig.minWordCount;
    final isEnough = count >= minRequired;

    return Row(
      children: [
        Icon(
          isEnough ? Icons.check_circle_rounded : Icons.info_outline_rounded,
          size: 14,
          color: isEnough ? Colors.green : colorScheme.onSurfaceVariant,
        ),
        const SizedBox(width: SpacingTokens.xs),
        Text(
          '$count words${isEnough ? '' : ' (minimum $minRequired)'}',
          style: theme.textTheme.labelSmall?.copyWith(
            color: isEnough ? Colors.green : colorScheme.onSurfaceVariant,
          ),
        ),
      ],
    );
  }

  Widget _buildEvaluationResult(ThemeData theme, ColorScheme colorScheme) {
    final eval = _evaluation!;
    return Container(
      margin: const EdgeInsets.only(bottom: SpacingTokens.md),
      padding: const EdgeInsets.all(SpacingTokens.md),
      decoration: BoxDecoration(
        color: Colors.green.shade50,
        borderRadius: BorderRadius.circular(RadiusTokens.lg),
        border: Border.all(color: Colors.green.shade200),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              const Icon(Icons.auto_awesome, size: 18, color: Colors.green),
              const SizedBox(width: SpacingTokens.xs),
              Text(
                '+${eval.xpAwarded} XP earned!',
                style: theme.textTheme.titleSmall?.copyWith(
                  fontWeight: FontWeight.w700,
                  color: Colors.green.shade800,
                ),
              ),
            ],
          ),
          if (eval.feedback.isNotEmpty) ...[
            const SizedBox(height: SpacingTokens.sm),
            Text(
              eval.feedback,
              style: theme.textTheme.bodySmall?.copyWith(
                color: Colors.green.shade700,
              ),
            ),
          ],
          const SizedBox(height: SpacingTokens.sm),
          // Score breakdown
          Row(
            children: [
              _ScoreChip(label: 'Completeness', score: eval.completenessScore),
              const SizedBox(width: SpacingTokens.sm),
              _ScoreChip(label: 'Accuracy', score: eval.accuracyScore),
              const SizedBox(width: SpacingTokens.sm),
              _ScoreChip(label: 'Clarity', score: eval.clarityScore),
            ],
          ),
        ],
      ),
    );
  }

  Widget _buildActions(ThemeData theme, ColorScheme colorScheme) {
    if (_evaluation != null) {
      return FilledButton(
        onPressed: widget.onSkip, // Continue to next question
        child: const Text('Continue'),
      );
    }

    return Row(
      children: [
        Expanded(
          child: FilledButton(
            onPressed: _isValidLength && !_isSubmitting ? _handleSubmit : null,
            child: _isSubmitting
                ? const SizedBox(
                    width: 18,
                    height: 18,
                    child: CircularProgressIndicator(
                      strokeWidth: 2,
                      color: Colors.white,
                    ),
                  )
                : const Text('Submit Explanation'),
          ),
        ),
      ],
    );
  }

  /// Placeholder for voice-to-text input. In production this would use
  /// the speech_to_text package to transcribe the student's voice.
  void _handleVoiceInput() {
    // Voice-to-text integration point.
    // The actual implementation would use speech_to_text package:
    // final stt = SpeechToText();
    // await stt.listen(onResult: (result) { ... });
    // For now, we show a snackbar indicating the feature.
    ScaffoldMessenger.of(context).showSnackBar(
      const SnackBar(
        content: Text('Voice input: Tap and hold to speak'),
        duration: Duration(seconds: 2),
      ),
    );
  }

  /// Called by the parent when the server returns an evaluation.
  void setEvaluation(TeachBackEvaluation evaluation) {
    setState(() {
      _evaluation = evaluation;
      _isSubmitting = false;
    });

    // Update tracker state.
    final tracker = ref.read(_teachBackTrackerProvider);
    tracker.updateState(
      widget.conceptId,
      TeachBackState(
        conceptId: widget.conceptId,
        explanation: _textController.text.trim(),
        wordCount: _wordCount,
        isSubmitted: true,
        evaluation: evaluation,
      ),
    );
  }
}

// ---------------------------------------------------------------------------
// Supporting widgets
// ---------------------------------------------------------------------------

class _ScoreChip extends StatelessWidget {
  const _ScoreChip({required this.label, required this.score});

  final String label;
  final double score;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final percentage = (score * 100).round();

    return Column(
      children: [
        Text(
          '$percentage%',
          style: theme.textTheme.labelMedium?.copyWith(
            fontWeight: FontWeight.w700,
            color: Colors.green.shade800,
          ),
        ),
        Text(
          label,
          style: theme.textTheme.labelSmall?.copyWith(
            color: Colors.green.shade600,
            fontSize: 9,
          ),
        ),
      ],
    );
  }
}
