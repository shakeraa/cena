// =============================================================================
// Cena Adaptive Learning Platform — Boss Battle Models
// =============================================================================
//
// Boss battles are triggered when a student has mastered 80%+ of a module's
// concepts. They serve as capstone assessments with game-like mechanics:
//   - Boss HP decreases with correct answers
//   - Student has 3 lives (wrong answers cost a life)
//   - No hints available (pure assessment mode)
//   - Victory triggers Tier 5 (epic) celebration + unique badge
//   - Defeat shows encouraging retry message
// =============================================================================

// ---------------------------------------------------------------------------
// Module
// ---------------------------------------------------------------------------

/// A learning module containing a group of related concepts.
class Module {
  const Module({
    required this.id,
    required this.name,
    this.nameHe,
    required this.concepts,
    required this.masteredCount,
  });

  /// Unique module identifier.
  final String id;

  /// English module name.
  final String name;

  /// Hebrew module name.
  final String? nameHe;

  /// Total concept IDs in this module.
  final List<String> concepts;

  /// Number of concepts the student has mastered in this module.
  final int masteredCount;

  /// Total concepts in the module.
  int get totalConcepts => concepts.length;

  /// Mastery fraction [0.0, 1.0].
  double get masteryFraction =>
      totalConcepts > 0 ? masteredCount / totalConcepts : 0.0;
}

// ---------------------------------------------------------------------------
// Power-Up
// ---------------------------------------------------------------------------

/// Power-ups that the student can earn from daily quests and use in boss
/// battles.
enum PowerUp {
  /// Grants an additional life (from 3 to 4, or restores a lost life).
  extraLife,

  /// Eliminates 2 of 4 wrong MCQ options, leaving 1 correct + 1 wrong.
  fiftyFiftyEliminator,

  /// Pauses the timer for the current question (if timer is enabled).
  timeFreeze,
}

// ---------------------------------------------------------------------------
// Boss Battle Result
// ---------------------------------------------------------------------------

/// Outcome of a completed boss battle.
enum BossBattleOutcome {
  /// Student defeated the boss. Triggers Tier 5 celebration.
  victory,

  /// Boss defeated the student. Encouraging retry message shown.
  defeat,
}

/// Full result of a boss battle including stats.
class BossBattleResult {
  const BossBattleResult({
    required this.outcome,
    required this.questionsAnswered,
    required this.correctAnswers,
    required this.livesRemaining,
    required this.powerUpsUsed,
    required this.durationMs,
  });

  final BossBattleOutcome outcome;
  final int questionsAnswered;
  final int correctAnswers;
  final int livesRemaining;
  final List<PowerUp> powerUpsUsed;
  final int durationMs;

  /// Accuracy fraction [0.0, 1.0].
  double get accuracy =>
      questionsAnswered > 0 ? correctAnswers / questionsAnswered : 0.0;
}

// ---------------------------------------------------------------------------
// Boss Battle State
// ---------------------------------------------------------------------------

/// Mutable state of an in-progress boss battle.
class BossBattle {
  BossBattle({
    required this.moduleId,
    required this.moduleName,
    this.moduleNameHe,
    required this.totalQuestions,
    this.studentLives = 3,
    this.timerEnabled = false,
    this.timerSecondsPerQuestion = 60,
    List<PowerUp>? availablePowerUps,
  })  : bossHp = totalQuestions,
        currentQuestion = 0,
        correctAnswers = 0,
        wrongAnswers = 0,
        _usedPowerUps = [],
        _availablePowerUps = availablePowerUps ?? [];

  /// Module being challenged.
  final String moduleId;

  /// English module name for display.
  final String moduleName;

  /// Hebrew module name for display.
  final String? moduleNameHe;

  /// Total questions in the boss battle (= bossHp at start).
  final int totalQuestions;

  /// Boss HP — decreases with each correct answer.
  int bossHp;

  /// Student lives — decreases with each wrong answer.
  int studentLives;

  /// Current question index (0-based).
  int currentQuestion;

  /// Count of correct answers so far.
  int correctAnswers;

  /// Count of wrong answers so far.
  int wrongAnswers;

  /// Whether a per-question timer is active.
  final bool timerEnabled;

  /// Seconds per question when timer is enabled.
  final int timerSecondsPerQuestion;

  final List<PowerUp> _usedPowerUps;
  final List<PowerUp> _availablePowerUps;

  /// Power-ups the student has available to use.
  List<PowerUp> get availablePowerUps =>
      List.unmodifiable(_availablePowerUps);

  /// Power-ups used so far in this battle.
  List<PowerUp> get usedPowerUps => List.unmodifiable(_usedPowerUps);

  /// Whether the battle is still in progress.
  bool get isActive => bossHp > 0 && studentLives > 0;

  /// Progress fraction [0.0, 1.0] based on boss HP damage dealt.
  double get bossHealthFraction =>
      totalQuestions > 0 ? bossHp / totalQuestions : 0.0;

  /// Process a correct answer: reduce boss HP, advance question.
  void recordCorrectAnswer() {
    if (!isActive) return;
    bossHp--;
    correctAnswers++;
    currentQuestion++;
  }

  /// Process a wrong answer: reduce student lives, advance question.
  void recordWrongAnswer() {
    if (!isActive) return;
    studentLives--;
    wrongAnswers++;
    currentQuestion++;
  }

  /// Use a power-up if available.
  bool usePowerUp(PowerUp powerUp) {
    if (!_availablePowerUps.contains(powerUp)) return false;
    _availablePowerUps.remove(powerUp);
    _usedPowerUps.add(powerUp);

    if (powerUp == PowerUp.extraLife) {
      studentLives++;
    }

    return true;
  }

  /// Get the final result when the battle ends.
  BossBattleResult getResult(int durationMs) {
    return BossBattleResult(
      outcome: bossHp <= 0
          ? BossBattleOutcome.victory
          : BossBattleOutcome.defeat,
      questionsAnswered: correctAnswers + wrongAnswers,
      correctAnswers: correctAnswers,
      livesRemaining: studentLives,
      powerUpsUsed: _usedPowerUps,
      durationMs: durationMs,
    );
  }

  /// Encouraging defeat message — no shame, only motivation.
  static String defeatMessage() {
    final messages = [
      'You gave it a great fight! Review a few more concepts and try again.',
      'Almost there! A little more practice and this boss won\'t stand a chance.',
      'Boss battles are tough by design. You\'re closer than you think!',
      'Every attempt teaches your brain something new. Ready for round two?',
      'The boss got lucky this time. You\'ll crush it next attempt!',
    ];
    return messages[DateTime.now().millisecond % messages.length];
  }

  /// Victory message for the epic celebration.
  static String victoryMessage(String moduleName) {
    return 'You conquered the $moduleName Challenge!';
  }
}

// ---------------------------------------------------------------------------
// Boss Battle Readiness Check
// ---------------------------------------------------------------------------

/// Determines whether a boss battle is available for a module.
///
/// The student must have mastered at least 80% of the module's concepts.
bool isBossBattleReady({
  required int masteredCount,
  required int totalConcepts,
}) {
  if (totalConcepts <= 0) return false;
  return (masteredCount / totalConcepts) >= 0.80;
}
