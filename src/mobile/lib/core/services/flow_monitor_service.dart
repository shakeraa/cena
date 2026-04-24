// =============================================================================
// Cena Adaptive Learning Platform — Flow Monitor Service (MOB-031)
// =============================================================================
//
// Tracks the student's flow state during a learning session using a weighted
// multi-signal formula. Flow episodes are recorded for session summaries.
//
// Flow score = 0.30 * focus
//            + 0.25 * challengeSkillBalance
//            + 0.20 * consistency
//            + 0.15 * inverseFatigue
//            + 0.10 * voluntaryEngagement
//
// State transitions:
//   >= 0.70 for 3+ consecutive readings -> InFlow
//   >= 0.50                             -> Approaching
//   <  0.30                             -> Fatigued
//   else                                -> Warming or Disrupted
// =============================================================================

/// The student's current flow state during a learning session.
enum FlowState {
  /// Initial warm-up phase — student is settling in.
  warming,

  /// Score rising toward flow threshold.
  approaching,

  /// Sustained high engagement — the student is in flow.
  inFlow,

  /// Flow was reached but score dropped significantly.
  disrupted,

  /// Extended fatigue or disengagement detected.
  fatigued,
}

/// A recorded episode of flow during a session.
class FlowEpisode {
  FlowEpisode({
    required this.startQuestionIndex,
    required this.durationSeconds,
    required this.peakScore,
    this.endQuestionIndex,
  });

  /// Index of the question where flow began.
  final int startQuestionIndex;

  /// Index of the question where flow ended (null if still active).
  int? endQuestionIndex;

  /// Duration of the flow episode in seconds.
  int durationSeconds;

  /// Highest flow score achieved during this episode.
  double peakScore;
}

/// Computes and tracks the student's flow state throughout a session.
///
/// Maintains a history of per-question flow scores so that consecutive-high
/// detection can promote the state to [FlowState.inFlow]. Episodes are
/// recorded when the student enters and exits flow for session summaries.
class FlowMonitorService {
  FlowMonitorService();

  // ---------------------------------------------------------------------------
  // Score weights
  // ---------------------------------------------------------------------------

  static const double _wFocus = 0.30;
  static const double _wChallenge = 0.25;
  static const double _wConsistency = 0.20;
  static const double _wInverseFatigue = 0.15;
  static const double _wVoluntary = 0.10;

  /// Number of consecutive high-score readings required to enter flow.
  static const int _consecutiveThreshold = 3;

  /// Score threshold for flow entry.
  static const double _flowThreshold = 0.70;

  /// Score threshold for approaching flow.
  static const double _approachingThreshold = 0.50;

  /// Score at or below which the student is considered fatigued.
  static const double _fatigueThreshold = 0.30;

  // ---------------------------------------------------------------------------
  // Internal state
  // ---------------------------------------------------------------------------

  final List<double> _scoreHistory = [];
  FlowState _currentState = FlowState.warming;
  bool _wasInFlow = false;
  final List<FlowEpisode> _episodes = [];
  DateTime? _flowEntryTime;
  int _flowEntryQuestionIndex = 0;

  // ---------------------------------------------------------------------------
  // Public API
  // ---------------------------------------------------------------------------

  /// The current flow state.
  FlowState get currentState => _currentState;

  /// All flow scores recorded so far (one per question).
  List<double> get scoreHistory => List.unmodifiable(_scoreHistory);

  /// All completed and active flow episodes.
  List<FlowEpisode> get episodes => List.unmodifiable(_episodes);

  /// The most recent flow score, or 0.0 if no scores recorded yet.
  double get latestScore =>
      _scoreHistory.isNotEmpty ? _scoreHistory.last : 0.0;

  /// Percentage of session time spent in flow [0.0, 1.0].
  ///
  /// Calculated from the ratio of flow episode durations to total session
  /// duration. Returns 0.0 if [totalSessionSeconds] is 0 or negative.
  double flowTimePercentage(int totalSessionSeconds) {
    if (totalSessionSeconds <= 0) return 0.0;
    final totalFlowSeconds =
        _episodes.fold<int>(0, (sum, ep) => sum + ep.durationSeconds);
    return (totalFlowSeconds / totalSessionSeconds).clamp(0.0, 1.0);
  }

  /// Compute the flow score from raw signal values.
  ///
  /// All parameters are expected in the [0.0, 1.0] range.
  /// - [focusLevel]: attention/engagement signal (e.g. answer speed consistency)
  /// - [accuracy]: correctness rate as challenge/skill balance proxy
  /// - [consistency]: how uniform the student's performance is across questions
  /// - [fatigueScore]: current fatigue level (0 = fresh, 1 = exhausted)
  /// - [isVoluntary]: whether the student chose to continue (1.0) or was prompted (0.0)
  double computeFlowScore({
    required double focusLevel,
    required double accuracy,
    required double consistency,
    required double fatigueScore,
    required bool isVoluntary,
  }) {
    final clampedFocus = focusLevel.clamp(0.0, 1.0);
    final clampedAccuracy = accuracy.clamp(0.0, 1.0);
    final clampedConsistency = consistency.clamp(0.0, 1.0);
    final inverseFatigue = 1.0 - fatigueScore.clamp(0.0, 1.0);
    final voluntaryEngagement = isVoluntary ? 1.0 : 0.0;

    final raw = _wFocus * clampedFocus +
        _wChallenge * clampedAccuracy +
        _wConsistency * clampedConsistency +
        _wInverseFatigue * inverseFatigue +
        _wVoluntary * voluntaryEngagement;
    return raw.clamp(0.0, 1.0);
  }

  /// Determine the [FlowState] for a given [score], taking consecutive
  /// score history into account.
  ///
  /// This also records the score in history, manages flow episodes, and
  /// updates [currentState].
  FlowState recordScore(double score, {int? questionIndex}) {
    final clampedScore = score.clamp(0.0, 1.0);
    _scoreHistory.add(clampedScore);

    final newState = _resolveState(clampedScore);

    // Flow episode management
    if (newState == FlowState.inFlow && _currentState != FlowState.inFlow) {
      // Entering flow
      _flowEntryTime = DateTime.now();
      _flowEntryQuestionIndex = questionIndex ?? _scoreHistory.length - 1;
      _episodes.add(FlowEpisode(
        startQuestionIndex: _flowEntryQuestionIndex,
        durationSeconds: 0,
        peakScore: clampedScore,
      ));
    } else if (newState == FlowState.inFlow && _episodes.isNotEmpty) {
      // Continuing flow — update peak score and duration
      final episode = _episodes.last;
      if (clampedScore > episode.peakScore) {
        episode.peakScore = clampedScore;
      }
      if (_flowEntryTime != null) {
        episode.durationSeconds =
            DateTime.now().difference(_flowEntryTime!).inSeconds;
      }
      episode.endQuestionIndex = questionIndex ?? _scoreHistory.length - 1;
    } else if (_currentState == FlowState.inFlow &&
        newState != FlowState.inFlow) {
      // Exiting flow — finalize the episode
      if (_episodes.isNotEmpty) {
        final episode = _episodes.last;
        episode.endQuestionIndex = questionIndex ?? _scoreHistory.length - 1;
        if (_flowEntryTime != null) {
          episode.durationSeconds =
              DateTime.now().difference(_flowEntryTime!).inSeconds;
        }
      }
      _wasInFlow = true;
      _flowEntryTime = null;
    }

    _currentState = newState;
    return newState;
  }

  /// Get the flow state for a score without recording it.
  ///
  /// This is a stateless lookup — it does not consider consecutive history.
  /// Use [recordScore] for the full state machine.
  FlowState getFlowState(double score) {
    if (score >= _flowThreshold) return FlowState.inFlow;
    if (score < _fatigueThreshold) return FlowState.fatigued;
    if (score >= _approachingThreshold) return FlowState.approaching;
    return FlowState.warming;
  }

  /// Reset all tracking state. Call when starting a new session.
  void reset() {
    _scoreHistory.clear();
    _currentState = FlowState.warming;
    _wasInFlow = false;
    _episodes.clear();
    _flowEntryTime = null;
    _flowEntryQuestionIndex = 0;
  }

  // ---------------------------------------------------------------------------
  // Private helpers
  // ---------------------------------------------------------------------------

  FlowState _resolveState(double score) {
    if (score < _fatigueThreshold) {
      return FlowState.fatigued;
    }

    if (score >= _flowThreshold) {
      // Check if we have enough consecutive high scores
      final recentCount = _recentConsecutiveHighCount();
      if (recentCount >= _consecutiveThreshold) {
        return FlowState.inFlow;
      }
      return FlowState.approaching;
    }

    if (score >= _approachingThreshold) {
      // If we were previously in flow but dropped, that's disrupted
      if (_wasInFlow || _currentState == FlowState.inFlow) {
        return FlowState.disrupted;
      }
      return FlowState.approaching;
    }

    // score between fatigueThreshold and approachingThreshold
    if (_wasInFlow || _currentState == FlowState.inFlow) {
      return FlowState.disrupted;
    }
    return FlowState.warming;
  }

  /// Count how many of the most recent scores are >= flow threshold.
  int _recentConsecutiveHighCount() {
    int count = 0;
    for (int i = _scoreHistory.length - 1; i >= 0; i--) {
      if (_scoreHistory[i] >= _flowThreshold) {
        count++;
      } else {
        break;
      }
    }
    return count;
  }
}
