// =============================================================================
// Cena Adaptive Learning Platform — Session Screen
// The core adaptive learning loop: question → answer → feedback → next.
// =============================================================================

import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/config/app_config.dart';
import '../../core/models/domain_models.dart';
import '../../core/router.dart';
import '../../core/services/analytics_service.dart';
import '../../core/state/feature_discovery_state.dart';
import '../../core/state/momentum_state.dart';
import '../../core/state/session_notifier.dart';
import '../../l10n/app_localizations.dart';
import '../gamification/celebration_overlay.dart';
import '../gamification/celebration_service.dart';
import 'widgets/action_buttons.dart';
import 'widgets/answer_input.dart';
import 'widgets/cognitive_load_break.dart';
import 'widgets/feedback_overlay.dart';
import 'widgets/progress_bar.dart';
import 'widgets/question_card.dart';

/// The active learning session screen.
///
/// Renders the full session loop driven by [SessionState]:
///   • [ProgressBar] at the top with fatigue-aware color
///   • [QuestionCard] for the current exercise
///   • [AnswerInput] for MCQ / free-text / numeric / proof
///   • [ActionButtons] for hints, skip, and approach changes
///   • [FeedbackOverlay] after answer evaluation
///   • [CognitiveLoadBreak] overlay when break is suggested
///
/// The screen is also the session *configuration* entry point: when no
/// session is active it shows subject/duration pickers and a Start button.
class SessionScreen extends ConsumerStatefulWidget {
  const SessionScreen({super.key});

  @override
  ConsumerState<SessionScreen> createState() => _SessionScreenState();
}

class _SessionScreenState extends ConsumerState<SessionScreen> {
  // Configuration state (pre-session)
  int _selectedDuration = SessionDefaults.defaultDurationMinutes;
  int? _selectedSubjectIndex;

  // In-session state
  int? _selectedOptionIndex;
  AnswerResult? _pendingFeedback;

  // Celebration system — tiered celebrations for achievements
  final CelebrationController _celebrationController = CelebrationController();

  // Timer for periodic rebuild to update the elapsed clock
  Timer? _elapsedTimer;

  // Prevents double-counting the same session completion.
  String? _lastTrackedCompletedSessionId;

  // Tracks when the current question was displayed so _submitAnswer
  // can compute timeSpentMs without delegating to AnswerInput internals.
  DateTime _questionDisplayedAt = DateTime.now();

  @override
  void dispose() {
    _elapsedTimer?.cancel();
    _restoreSystemChrome();
    super.dispose();
  }

  /// Enter immersive mode — hide status bar for distraction-free learning.
  void _enterImmersiveMode() {
    SystemChrome.setEnabledSystemUIMode(
      SystemUiMode.immersiveSticky,
      overlays: [],
    );
  }

  /// Restore system chrome when leaving the active session.
  void _restoreSystemChrome() {
    SystemChrome.setEnabledSystemUIMode(
      SystemUiMode.edgeToEdge,
      overlays: SystemUiOverlay.values,
    );
  }

  void _startElapsedTimer() {
    _elapsedTimer?.cancel();
    _elapsedTimer = Timer.periodic(const Duration(seconds: 1), (_) {
      if (mounted) setState(() {});
    });
  }

  void _stopElapsedTimer() {
    _elapsedTimer?.cancel();
    _elapsedTimer = null;
  }

  @override
  Widget build(BuildContext context) {
    // -----------------------------------------------------------------------
    // MOB-058: Granular Riverpod .select() optimization.
    //
    // Instead of watching the entire SessionState (which rebuilds this widget
    // on every field change), we select only the fields that drive top-level
    // branching: isActive, isBreakSuggested, currentSession, and
    // currentExercise. This avoids unnecessary rebuilds when only fatigue,
    // accuracy, or history changes (those are consumed by child widgets that
    // do their own selects).
    //
    // Side-effects (celebrations, analytics, feature discovery) remain in
    // ref.listen which does NOT trigger rebuilds.
    // -----------------------------------------------------------------------
    final isActive = ref.watch(
      sessionProvider.select((s) => s.isActive),
    );
    final isBreakSuggested = ref.watch(
      sessionProvider.select((s) => s.isBreakSuggested),
    );
    final currentSession = ref.watch(
      sessionProvider.select((s) => s.currentSession),
    );
    final currentExercise = ref.watch(
      sessionProvider.select((s) => s.currentExercise),
    );

    // Read the full state only when needed for passing to child builders.
    // This read does NOT subscribe — it is a one-shot snapshot.
    final sessionState = ref.read(sessionProvider);

    // Track when a new exercise arrives so we can measure time spent
    ref.listen<SessionState>(sessionProvider, (prev, next) {
      final prevId = prev?.currentExercise?.id;
      final nextId = next.currentExercise?.id;
      if (nextId != null && nextId != prevId) {
        setState(() {
          _questionDisplayedAt = DateTime.now();
          _selectedOptionIndex = null;
          _pendingFeedback = null;
        });
      }

      // Capture feedback result when AnswerEvaluated arrives
      final prevHistory = prev?.sessionHistory.length ?? 0;
      final nextHistory = next.sessionHistory.length;
      if (nextHistory > prevHistory) {
        final latestResult = next.sessionHistory.last;
        setState(() {
          _pendingFeedback = latestResult;
        });

        // Trigger tiered celebration for correct answers
        if (latestResult.isCorrect) {
          final xp = latestResult.xpEarned;
          // Don't interrupt flow state with XP celebrations during immersion
          if (!next.isInFlowState || xp >= 26) {
            final tier = CelebrationService.classify(
              event: CelebrationEvent.correctAnswer,
              xpDelta: xp,
            );
            _celebrationController.celebrate(tier: tier, xp: xp);
          }
        }
      }

      // Start/stop the elapsed timer with session lifecycle.
      // MOB-058: Use next.isActive (from the listener), not the ref.read
      // snapshot, since the listener fires after state has changed.
      if (!next.isActive) {
        _stopElapsedTimer();
        _restoreSystemChrome();
      } else if (prev?.isActive != true && next.isActive) {
        _startElapsedTimer();
        _enterImmersiveMode();
      }

      // Progressive feature discovery: count completed sessions once.
      final transitionedToEnded = prev?.isActive == true && !next.isActive;
      final completedSessionId = next.currentSession?.id;
      if (transitionedToEnded && completedSessionId != null) {
        if (_lastTrackedCompletedSessionId != completedSessionId) {
          _lastTrackedCompletedSessionId = completedSessionId;
          ref.read(featureDiscoveryProvider.notifier).recordSessionCompleted();
          ref.read(streakAnxietyProvider.notifier).recordSessionOutcome(
                duration: next.elapsed,
                accuracy: next.accuracy,
                endedAt: DateTime.now(),
              );
        }
      }
    });

    // MOB-058: Branching uses the .select()-watched values so only
    // the relevant state transitions trigger a rebuild of this widget.

    // Cognitive load break takes priority over everything else
    if (isBreakSuggested && isActive) {
      return _buildBreakScreen(sessionState);
    }

    // Session ended → summary (navigate to home with summary data)
    if (currentSession != null && !isActive) {
      return _buildSessionEndedView(context, sessionState);
    }

    // Active session
    if (isActive) {
      return _buildActiveSession(context, sessionState);
    }

    // No session yet → configuration screen
    return _buildConfigScreen(context, sessionState);
  }

  // ---------------------------------------------------------------------------
  // Configuration screen (before session starts)
  // ---------------------------------------------------------------------------

  Widget _buildConfigScreen(BuildContext context, SessionState state) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;
    final l = AppLocalizations.of(context);

    final subjects = _getSubjects(l);

    return Scaffold(
      appBar: AppBar(
        title: Text(l.newLesson),
        leading: IconButton(
          icon: const Icon(Icons.close_rounded),
          onPressed: () => context.go(CenaRoutes.home),
        ),
      ),
      body: state.isLoading
          ? const Center(child: CircularProgressIndicator())
          : ListView(
              padding: const EdgeInsets.all(SpacingTokens.md),
              children: [
                if (state.error != null) _ErrorBanner(message: state.error!),
                Text(l.selectSubject, style: theme.textTheme.titleLarge),
                const SizedBox(height: SpacingTokens.sm),
                Wrap(
                  spacing: SpacingTokens.sm,
                  runSpacing: SpacingTokens.sm,
                  children: subjects.asMap().entries.map((entry) {
                    final index = entry.key;
                    final (:name, :icon, :color) = entry.value;
                    final isSelected = _selectedSubjectIndex == index;
                    return FilterChip(
                      selected: isSelected,
                      label: Text(name),
                      avatar: Icon(icon, size: 18),
                      selectedColor: color.withValues(alpha: 0.2),
                      checkmarkColor: color,
                      onSelected: (selected) => setState(() {
                        _selectedSubjectIndex = selected ? index : null;
                      }),
                    );
                  }).toList(),
                ),
                const SizedBox(height: SpacingTokens.xl),
                Text(l.sessionDuration, style: theme.textTheme.titleLarge),
                const SizedBox(height: SpacingTokens.sm),
                Text(
                  l.nMinutes(_selectedDuration),
                  style: theme.textTheme.headlineLarge?.copyWith(
                    color: colorScheme.primary,
                    fontWeight: FontWeight.w700,
                  ),
                  textAlign: TextAlign.center,
                ),
                Slider(
                  value: _selectedDuration.toDouble(),
                  min: SessionDefaults.minDurationMinutes.toDouble(),
                  max: SessionDefaults.maxDurationMinutes.toDouble(),
                  divisions: SessionDefaults.maxDurationMinutes -
                      SessionDefaults.minDurationMinutes,
                  label: l.nMinShort(_selectedDuration),
                  onChanged: (v) => setState(() {
                    _selectedDuration = v.round();
                  }),
                ),
                Row(
                  mainAxisAlignment: MainAxisAlignment.spaceBetween,
                  children: [
                    Text(
                      l.nMinShort(SessionDefaults.minDurationMinutes),
                      style: theme.textTheme.bodySmall?.copyWith(
                        color: colorScheme.onSurfaceVariant,
                      ),
                    ),
                    Text(
                      l.nMinShort(SessionDefaults.maxDurationMinutes),
                      style: theme.textTheme.bodySmall?.copyWith(
                        color: colorScheme.onSurfaceVariant,
                      ),
                    ),
                  ],
                ),
                const SizedBox(height: SpacingTokens.xl),
                Card(
                  child: Padding(
                    padding: const EdgeInsets.all(SpacingTokens.md),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Row(
                          children: [
                            Icon(Icons.info_outline_rounded,
                                size: 20, color: colorScheme.primary),
                            const SizedBox(width: SpacingTokens.sm),
                            Text(l.sessionDetails,
                                style: theme.textTheme.titleMedium),
                          ],
                        ),
                        const SizedBox(height: SpacingTokens.sm),
                        _InfoRow(
                            label: l.maxQuestions,
                            value: '${SessionDefaults.maxQuestionsPerSession}'),
                        _InfoRow(
                            label: l.masteryThreshold,
                            value:
                                '${(SessionDefaults.masteryThreshold * 100).toInt()}%'),
                        _InfoRow(
                            label: l.studyEnergy,
                            value: l.remaining(LlmBudget.dailyCap)),
                      ],
                    ),
                  ),
                ),
                const SizedBox(height: SpacingTokens.xl),
                FilledButton.icon(
                  onPressed: _startSession,
                  icon: const Icon(Icons.play_arrow_rounded),
                  label: Text(l.startLesson),
                  style: FilledButton.styleFrom(
                    minimumSize: const Size(double.infinity, 48),
                  ),
                ),
              ],
            ),
    );
  }

  static List<({String name, IconData icon, Color color})> _getSubjects(
      AppLocalizations l) {
    return [
      (
        name: l.math,
        icon: Icons.functions_rounded,
        color: SubjectColorTokens.mathPrimary,
      ),
      (
        name: l.physics,
        icon: Icons.speed_rounded,
        color: SubjectColorTokens.physicsPrimary,
      ),
      (
        name: l.chemistry,
        icon: Icons.science_rounded,
        color: SubjectColorTokens.chemistryPrimary,
      ),
      (
        name: l.biology,
        icon: Icons.biotech_rounded,
        color: SubjectColorTokens.biologyPrimary,
      ),
      (
        name: l.computerScience,
        icon: Icons.computer_rounded,
        color: SubjectColorTokens.csPrimary,
      ),
    ];
  }

  void _startSession() {
    final notifier = ref.read(sessionProvider.notifier);
    Subject? subject;
    if (_selectedSubjectIndex != null) {
      subject = Subject.values[_selectedSubjectIndex!];
    }
    notifier.startSession(
      subject: subject,
      durationMinutes: _selectedDuration,
    );

    // Log session start to analytics
    final analytics = ref.read(analyticsServiceProvider);
    final sessionState = ref.read(sessionProvider);
    final sessionId = sessionState.currentSession?.id ?? 'unknown';
    final subjectName = subject?.name ?? 'unspecified';
    analytics.logSessionStart(sessionId, subjectName);
  }

  // ---------------------------------------------------------------------------
  // Active session screen
  // ---------------------------------------------------------------------------

  Widget _buildActiveSession(BuildContext context, SessionState state) {
    final exercise = state.currentExercise;
    final isFlowState = state.isInFlowState;

    return Scaffold(
      body: Stack(
        children: [
          SafeArea(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                // Progress header — ambient-only in flow state
                ProgressBar(
                  questionsAttempted: state.questionsAttempted,
                  accuracy: state.accuracy,
                  fatigueScore: state.fatigueScore,
                  elapsed: state.elapsed,
                  targetDurationMinutes:
                      state.currentSession?.targetDurationMinutes ??
                          _selectedDuration,
                  isImmersive: isFlowState,
                  onPause: _confirmEndSession,
                ),

                // Methodology indicator — hidden in flow state
                if (state.methodology != null && !isFlowState)
                  _MethodologyBadge(methodology: state.methodology!),

                // Question + answer area
                Expanded(
                  child: exercise == null
                      ? const Center(child: CircularProgressIndicator())
                      : _buildQuestionArea(context, state, exercise),
                ),
              ],
            ),
          ),

          // Feedback overlay — rendered above everything else
          if (_pendingFeedback != null)
            FeedbackOverlay(
              result: _pendingFeedback!,
              onDismiss: _dismissFeedback,
            ),

          // Tiered celebration overlay — above feedback
          CelebrationOverlay(controller: _celebrationController),
        ],
      ),
    );
  }

  Widget _buildQuestionArea(
      BuildContext context, SessionState state, Exercise exercise) {
    final theme = Theme.of(context);
    final l = AppLocalizations.of(context);
    final isFlowState = state.isInFlowState;
    return CustomScrollView(
      slivers: [
        SliverPadding(
          padding: const EdgeInsets.all(SpacingTokens.md),
          sliver: SliverList(
            delegate: SliverChildListDelegate([
              // Question number + end/pause — simplified in flow state
              Row(
                children: [
                  Text(
                    l.questionN(state.questionsAttempted + 1),
                    style: theme.textTheme.titleMedium?.copyWith(
                      color: Theme.of(context).colorScheme.onSurfaceVariant,
                    ),
                  ),
                  const Spacer(),
                  // Flow state: show subtle pause icon only; normal: full button
                  if (isFlowState)
                    IconButton(
                      onPressed: _confirmEndSession,
                      icon: const Icon(Icons.pause_rounded, size: 18),
                      tooltip: l.endSession,
                      color: Theme.of(context).colorScheme.onSurfaceVariant,
                      padding: EdgeInsets.zero,
                      constraints: const BoxConstraints(
                        minWidth: 32,
                        minHeight: 32,
                      ),
                    )
                  else
                    TextButton.icon(
                      onPressed: _confirmEndSession,
                      icon: const Icon(Icons.stop_rounded, size: 16),
                      label: Text(l.endSession),
                      style: TextButton.styleFrom(
                        foregroundColor: Theme.of(context).colorScheme.error,
                      ),
                    ),
                ],
              ),

              const SizedBox(height: SpacingTokens.sm),

              // Question card
              QuestionCard(
                exercise: exercise,
                selectedOption: _selectedOptionIndex,
                onOptionSelected: state.isLoading
                    ? null
                    : (i) => setState(() => _selectedOptionIndex = i),
                isSubmitting: state.isLoading,
              ),

              const SizedBox(height: SpacingTokens.md),

              // Answer input + submit/skip
              AnswerInput(
                questionType: exercise.questionType,
                selectedOptionIndex: _selectedOptionIndex,
                isSubmitting: state.isLoading,
                onSubmit: _submitAnswer,
                onSkip: _skipQuestion,
              ),

              const SizedBox(height: SpacingTokens.md),

              // Hint / approach actions
              ActionButtons(
                isDisabled: state.isLoading,
                questionInteracted: _selectedOptionIndex != null,
              ),

              // Hint text display
              if (state.hintsUsed > 0 && exercise.hints.isNotEmpty)
                _HintDisplay(
                  hints: exercise.hints,
                  revealedCount: state.hintsUsed,
                ),

              const SizedBox(height: SpacingTokens.xl),
            ]),
          ),
        ),
      ],
    );
  }

  // AnswerInput passes its own elapsed time, but we use the screen-level
  // _questionDisplayedAt so the measurement survives widget rebuilds.
  void _submitAnswer(String answer, int _) {
    final timeSpentMs =
        DateTime.now().difference(_questionDisplayedAt).inMilliseconds;
    final state = ref.read(sessionProvider);
    ref.read(sessionProvider.notifier).submitAnswer(answer, timeSpentMs);

    // Log question attempt to analytics after submission
    final exercise = state.currentExercise;
    if (exercise != null) {
      final analytics = ref.read(analyticsServiceProvider);
      // We check the latest history entry after submission for correctness.
      // Since submitAnswer is async internally, we use a post-frame callback
      // to read the updated state.
      WidgetsBinding.instance.addPostFrameCallback((_) {
        if (!mounted) return;
        final updated = ref.read(sessionProvider);
        final lastResult = updated.sessionHistory.isNotEmpty
            ? updated.sessionHistory.last
            : null;
        analytics.logQuestionAttempt(
          exercise.id,
          correct: lastResult?.isCorrect ?? false,
          methodology: state.methodology?.name ?? 'unknown',
        );
      });
    }
  }

  void _skipQuestion(String? reason) {
    ref.read(sessionProvider.notifier).skipQuestion(reason: reason);
    setState(() {
      _selectedOptionIndex = null;
    });
  }

  void _dismissFeedback() {
    setState(() => _pendingFeedback = null);
  }

  Future<void> _confirmEndSession() async {
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (_) => const _EndSessionDialog(),
    );
    if (confirmed == true && mounted) {
      await ref.read(sessionProvider.notifier).endSession();
    }
  }

  // ---------------------------------------------------------------------------
  // Cognitive load break screen
  // ---------------------------------------------------------------------------

  Widget _buildBreakScreen(SessionState state) {
    return CognitiveLoadBreak(
      suggestedMinutes: SessionDefaults.defaultBreakMinutes,
      onContinue: () {
        ref.read(sessionProvider.notifier).dismissBreakSuggestion();
      },
      onEndSession: () {
        ref.read(sessionProvider.notifier).endSession(reason: 'fatigue');
      },
    );
  }

  // ---------------------------------------------------------------------------
  // Session ended view
  // ---------------------------------------------------------------------------

  Widget _buildSessionEndedView(BuildContext context, SessionState state) {
    final theme = Theme.of(context);
    final l = AppLocalizations.of(context);
    final accuracyPct = (state.accuracy * 100).toInt();

    return Scaffold(
      body: SafeArea(
        child: Padding(
          padding: const EdgeInsets.all(SpacingTokens.xl),
          child: Column(
            mainAxisAlignment: MainAxisAlignment.center,
            children: [
              const Icon(Icons.emoji_events_rounded,
                  size: 80, color: Color(0xFFFFB300)),
              const SizedBox(height: SpacingTokens.lg),
              Text(
                l.lessonComplete,
                style: theme.textTheme.headlineLarge?.copyWith(
                  fontWeight: FontWeight.w800,
                ),
                textAlign: TextAlign.center,
              ),
              const SizedBox(height: SpacingTokens.xl),
              _SummaryTile(
                icon: Icons.help_outline_rounded,
                label: l.questions,
                value: '${state.questionsAttempted}',
              ),
              _SummaryTile(
                icon: Icons.check_circle_outline_rounded,
                label: l.accuracy,
                value: '$accuracyPct%',
              ),
              _SummaryTile(
                icon: Icons.timer_outlined,
                label: l.time,
                value: _formatDuration(state.elapsed),
              ),
              const SizedBox(height: SpacingTokens.xxl),
              FilledButton.icon(
                onPressed: () => context.go(CenaRoutes.home),
                icon: const Icon(Icons.home_rounded),
                label: Text(l.backToHome),
                style: FilledButton.styleFrom(
                  minimumSize: const Size(double.infinity, 48),
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }

  String _formatDuration(Duration d) {
    final m = d.inMinutes.remainder(60).toString().padLeft(2, '0');
    final s = d.inSeconds.remainder(60).toString().padLeft(2, '0');
    return '$m:$s';
  }
}

// =============================================================================
// Supporting widgets
// =============================================================================

class _MethodologyBadge extends StatelessWidget {
  const _MethodologyBadge({required this.methodology});

  final Methodology methodology;

  String _label(Methodology m, AppLocalizations l) {
    switch (m) {
      case Methodology.spacedRepetition:
        return l.spacedRepetition;
      case Methodology.interleaved:
        return l.interleaved;
      case Methodology.blocked:
        return l.blocked;
      case Methodology.adaptiveDifficulty:
        return l.adaptiveDifficulty;
      case Methodology.socratic:
        return l.socratic;
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final l = AppLocalizations.of(context);
    final color = theme.colorScheme.tertiary;
    return Container(
      padding: const EdgeInsets.symmetric(
          horizontal: SpacingTokens.md, vertical: SpacingTokens.xxs),
      color: color.withValues(alpha: 0.08),
      child: Row(
        mainAxisAlignment: MainAxisAlignment.center,
        children: [
          Icon(Icons.psychology_rounded, size: 14, color: color),
          const SizedBox(width: SpacingTokens.xs),
          Text(
            _label(methodology, l),
            style: theme.textTheme.labelSmall?.copyWith(color: color),
          ),
        ],
      ),
    );
  }
}

/// Displays the hints revealed so far for the current exercise.
class _HintDisplay extends StatelessWidget {
  const _HintDisplay({
    required this.hints,
    required this.revealedCount,
  });

  final List<String> hints;
  final int revealedCount;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;
    final toShow = hints.take(revealedCount).toList();

    return Container(
      margin: const EdgeInsets.only(top: SpacingTokens.sm),
      padding: const EdgeInsets.all(SpacingTokens.md),
      decoration: BoxDecoration(
        color: const Color(0xFFFFF8E1),
        borderRadius: BorderRadius.circular(RadiusTokens.lg),
        border:
            Border.all(color: const Color(0xFFFF9800).withValues(alpha: 0.4)),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              const Icon(Icons.lightbulb_rounded,
                  size: 16, color: Color(0xFFFF9800)),
              const SizedBox(width: SpacingTokens.xs),
              Text(
                AppLocalizations.of(context).hints,
                style: theme.textTheme.labelLarge?.copyWith(
                  color: const Color(0xFFE65100),
                  fontWeight: FontWeight.w700,
                ),
              ),
            ],
          ),
          const SizedBox(height: SpacingTokens.sm),
          ...toShow.asMap().entries.map((e) => Padding(
                padding: const EdgeInsets.only(bottom: SpacingTokens.xs),
                child: Row(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      '${e.key + 1}. ',
                      style: theme.textTheme.bodySmall?.copyWith(
                        color: const Color(0xFFE65100),
                        fontWeight: FontWeight.w700,
                      ),
                    ),
                    Expanded(
                      child: Text(
                        e.value,
                        style: theme.textTheme.bodySmall?.copyWith(
                          color: colorScheme.onSurface,
                        ),
                      ),
                    ),
                  ],
                ),
              )),
        ],
      ),
    );
  }
}

class _InfoRow extends StatelessWidget {
  const _InfoRow({required this.label, required this.value});

  final String label;
  final String value;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: SpacingTokens.xs),
      child: Row(
        mainAxisAlignment: MainAxisAlignment.spaceBetween,
        children: [
          Text(label,
              style: theme.textTheme.bodyMedium
                  ?.copyWith(color: theme.colorScheme.onSurfaceVariant)),
          Text(value,
              style: theme.textTheme.bodyMedium
                  ?.copyWith(fontWeight: FontWeight.w600)),
        ],
      ),
    );
  }
}

class _ErrorBanner extends StatelessWidget {
  const _ErrorBanner({required this.message});

  final String message;

  @override
  Widget build(BuildContext context) {
    final colorScheme = Theme.of(context).colorScheme;
    return Container(
      margin: const EdgeInsets.only(bottom: SpacingTokens.md),
      padding: const EdgeInsets.all(SpacingTokens.sm),
      decoration: BoxDecoration(
        color: colorScheme.errorContainer,
        borderRadius: BorderRadius.circular(RadiusTokens.md),
      ),
      child: Row(
        children: [
          Icon(Icons.error_outline_rounded,
              color: colorScheme.onErrorContainer, size: 18),
          const SizedBox(width: SpacingTokens.sm),
          Expanded(
            child: Text(message,
                style: TextStyle(color: colorScheme.onErrorContainer)),
          ),
        ],
      ),
    );
  }
}

class _SummaryTile extends StatelessWidget {
  const _SummaryTile({
    required this.icon,
    required this.label,
    required this.value,
  });

  final IconData icon;
  final String label;
  final String value;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: SpacingTokens.xs),
      child: Row(
        children: [
          Icon(icon, size: 20, color: theme.colorScheme.primary),
          const SizedBox(width: SpacingTokens.md),
          Text(label, style: theme.textTheme.bodyLarge),
          const Spacer(),
          Text(value,
              style: theme.textTheme.bodyLarge
                  ?.copyWith(fontWeight: FontWeight.w700)),
        ],
      ),
    );
  }
}

class _EndSessionDialog extends StatelessWidget {
  const _EndSessionDialog();

  @override
  Widget build(BuildContext context) {
    final colorScheme = Theme.of(context).colorScheme;
    final l = AppLocalizations.of(context);
    return AlertDialog(
      title: Text(l.endSessionTitle),
      content: Text(l.endSessionBody),
      actions: [
        TextButton(
          onPressed: () => Navigator.of(context).pop(false),
          child: Text(l.continueLesson),
        ),
        FilledButton(
          style: FilledButton.styleFrom(backgroundColor: colorScheme.error),
          onPressed: () => Navigator.of(context).pop(true),
          child: Text(l.endLessonConfirm),
        ),
      ],
    );
  }
}
