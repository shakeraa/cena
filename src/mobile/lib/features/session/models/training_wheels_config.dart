// =============================================================================
// Cena Adaptive Learning Platform — Training Wheels Config (MOB-035)
// =============================================================================
//
// Configuration for sessions 1-3 (training wheels mode):
//   - Session 1: minimal UI, coach marks, no hints, no skip
//   - Session 2: hints enabled, no skip, no approach selector
//   - Session 3: same as 2 but approach selector available if P(known) > 0.6
//   - Session 4+: full feature surface
//
// CoachMark data class for first-session tooltips.
// =============================================================================

// ---------------------------------------------------------------------------
// Training Wheels Config
// ---------------------------------------------------------------------------

/// Controls which features are visible based on the student's session count.
///
/// During sessions 1-3 ("training wheels"), the interface is simplified
/// to reduce cognitive load. Features unlock progressively as the student
/// gains comfort with the core loop.
class TrainingWheelsConfig {
  TrainingWheelsConfig({
    required this.completedSessions,
    this.currentPKnown,
  });

  /// Total completed sessions (not including current).
  final int completedSessions;

  /// Current P(Known) from BKT for early unlock consideration.
  /// If > 0.6, some features unlock early.
  final double? currentPKnown;

  /// Whether we are in training wheels mode (sessions 0-2 completed = first 3).
  bool get isTrainingWheels => completedSessions < 3;

  /// Whether this is the very first session.
  bool get isFirstSession => completedSessions == 0;

  /// Whether to show the approach selector.
  /// Hidden for sessions 1-3, unless P(known) > 0.6 (early unlock).
  bool get showApproachSelector {
    if (completedSessions >= 3) return true;
    // Early unlock: if the student demonstrates high mastery, unlock early.
    if (currentPKnown != null && currentPKnown! > 0.6) return true;
    return false;
  }

  /// Whether to show hints.
  /// Hidden for session 1, available from session 2 onwards.
  bool get showHints => completedSessions >= 1;

  /// Whether to show the skip button.
  /// Hidden for sessions 1-3 to encourage attempting all questions.
  bool get showSkipButton => completedSessions >= 3;

  /// Whether to show the knowledge graph link.
  /// Hidden for sessions 1-3.
  bool get showKnowledgeGraphLink => completedSessions >= 3;

  /// Whether coach marks should be shown (first session only).
  bool get showCoachMarks => isFirstSession;

  /// Celebration duration multiplier for first session.
  /// Extended to 1.5x for the first correct answer.
  double get celebrationMultiplier => isFirstSession ? 1.5 : 1.0;

  /// First-session completion message (special celebration).
  bool get showFirstSessionComplete => isFirstSession;

  /// Get the list of coach marks for the first session.
  List<CoachMark> get firstSessionCoachMarks {
    if (!isFirstSession) return const [];
    return const [
      CoachMark(
        id: 'read_question',
        targetKey: 'question_card',
        message: 'קראו את השאלה ובחרו את התשובה',
        messageEn: 'Read the question and tap your answer',
        position: CoachMarkPosition.below,
        order: 0,
      ),
      CoachMark(
        id: 'answer_options',
        targetKey: 'answer_options',
        message: 'לחצו על התשובה שנראית לכם נכונה',
        messageEn: 'Tap the answer that seems right to you',
        position: CoachMarkPosition.above,
        order: 1,
      ),
      CoachMark(
        id: 'progress_bar',
        targetKey: 'progress_bar',
        message: 'כאן תראו את ההתקדמות שלכם',
        messageEn: 'Your progress is shown here',
        position: CoachMarkPosition.below,
        order: 2,
      ),
    ];
  }
}

// ---------------------------------------------------------------------------
// Coach Mark
// ---------------------------------------------------------------------------

/// Position of a coach mark relative to its target widget.
enum CoachMarkPosition {
  above,
  below,
  left,
  right,
}

/// Data class representing a first-session tooltip/coach mark.
class CoachMark {
  const CoachMark({
    required this.id,
    required this.targetKey,
    required this.message,
    required this.messageEn,
    required this.position,
    required this.order,
  });

  /// Unique identifier for persistence (so it shows only once).
  final String id;

  /// The key identifying which widget this coach mark targets.
  final String targetKey;

  /// Tooltip message (Hebrew).
  final String message;

  /// Tooltip message (English).
  final String messageEn;

  /// Where to position the tooltip relative to the target.
  final CoachMarkPosition position;

  /// Display order (lower = shown first).
  final int order;

  /// Get the localized message.
  String localizedMessage(String langCode) {
    return (langCode == 'he' || langCode == 'ar') ? message : messageEn;
  }
}
