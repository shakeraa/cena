// =============================================================================
// Cena Adaptive Learning Platform — Growth Mindset Service (PAR-003)
// Provides growth mindset messaging for the AI tutor and session feedback.
// =============================================================================
//
// Based on competitive research: Synthesis shows warm, named AI personality
// with effort-based praise drives emotional connection and retention.
//
// Usage:
//   final msg = GrowthMindsetService.wrongAnswerEncouragement();
//   final prompt = GrowthMindsetService.tutorSystemPrompt(studentName: 'Noor');

import 'dart:math';

/// Provides randomized growth mindset messages and AI tutor system prompts.
///
/// All messages follow three principles from learning science research:
/// 1. Celebrate effort, not just correctness
/// 2. Frame mistakes as learning opportunities
/// 3. Use "not yet" language instead of "wrong"
class GrowthMindsetService {
  GrowthMindsetService._();

  static final _random = Random();

  // ---------------------------------------------------------------------------
  // AI Tutor System Prompt
  // ---------------------------------------------------------------------------

  /// Returns the system prompt that shapes the AI tutor's personality.
  ///
  /// [studentName] personalizes responses. [tone] adjusts intensity:
  /// - `encouraging` (default): warm, celebratory, patient
  /// - `neutral`: clear, supportive but less effusive
  /// - `challenging`: direct, pushes the student harder
  static String tutorSystemPrompt({
    required String studentName,
    String tone = 'encouraging',
  }) {
    return '''
You are Cena, an AI tutor helping $studentName learn STEM subjects.

PERSONALITY:
- Address the student by name ("$studentName") naturally in conversation.
- Be warm, patient, and genuinely curious about their thinking.
- Show enthusiasm when they make progress, even small steps.
- Use "not yet" instead of "wrong" — e.g., "You haven't got it yet, but you're close."

PEDAGOGY (Socratic method):
- Ask guiding questions before revealing answers.
- When the student is wrong, ask "What made you think that?" to understand their reasoning.
- Praise the PROCESS: "I like how you broke that into steps" over "Good job."
- After a wrong answer, say something like: "Interesting approach! Let's look at it from another angle."
- Connect new concepts to what they already know.

MISTAKES:
- Normalize mistakes: "Mistakes are how your brain builds stronger connections."
- Reframe errors: "That's a common misconception — here's why it's tricky..."
- Never say "wrong," "incorrect," or "no." Use: "not quite," "almost," "let's revisit."

TONE (${tone.toUpperCase()}):
${_toneInstructions(tone)}

EFFORT CELEBRATION:
- After hard problems: "You really pushed through that one — that takes real persistence."
- After streaks: "Your consistency is building deep understanding."
- After returning from absence: "Welcome back! Your brain has been consolidating what you learned."
- End of session: summarize what they learned and praise specific effort.

LANGUAGE:
- Keep explanations concise and clear.
- Use LaTeX for math notation: \$x^2 + 3x + 2 = 0\$.
- Support Hebrew, Arabic, and English based on student's locale.
''';
  }

  static String _toneInstructions(String tone) {
    switch (tone) {
      case 'neutral':
        return '- Be supportive but measured. State facts clearly.\n'
            '- Use encouragement sparingly — when it is truly earned.\n'
            '- Focus on clarity over warmth.';
      case 'challenging':
        return '- Push the student to think deeper. Ask "Are you sure?"\n'
            '- Set high expectations: "I know you can figure this out."\n'
            '- Use productive struggle: give less help, more probing questions.';
      default: // encouraging
        return '- Be enthusiastically supportive. Celebrate every win.\n'
            '- Use exclamation marks naturally. Show genuine excitement.\n'
            '- Default to warmth — the student should feel safe to be wrong.';
    }
  }

  // ---------------------------------------------------------------------------
  // Wrong Answer Encouragement
  // ---------------------------------------------------------------------------

  /// Returns a random growth mindset message after a wrong answer.
  static String wrongAnswerEncouragement() {
    return _wrongAnswerMessages[_random.nextInt(_wrongAnswerMessages.length)];
  }

  static const _wrongAnswerMessages = [
    'Not quite yet — but your thinking is on the right track!',
    "Interesting approach! Let's look at this from another angle.",
    "Mistakes help your brain build stronger connections. Let's try again.",
    "Almost there! You've got the right idea, just one piece is off.",
    "That's a common tricky spot — here's a hint to get past it.",
    'Good effort! The fact that you tried means you\'re learning.',
    'You\'re closer than you think. What if we break it into smaller steps?',
    "That's a creative approach! Let me show you why it doesn't quite work here.",
    "Don't worry — this concept trips up a lot of students at first.",
    'Nice try! Remember, every expert was once a beginner.',
    "You haven't got it yet, but I can see you're thinking hard about it.",
    "Let's slow down and look at this step by step. You've got this.",
  ];

  // ---------------------------------------------------------------------------
  // Correct Answer Celebration
  // ---------------------------------------------------------------------------

  /// Returns a random effort-based praise message after a correct answer.
  static String correctAnswerPraise() {
    return _correctAnswerMessages[_random.nextInt(_correctAnswerMessages.length)];
  }

  static const _correctAnswerMessages = [
    'Excellent! Your persistence is paying off.',
    'You worked through that really well!',
    'Great thinking! I like how you approached that.',
    "That's right! The way you broke it down was smart.",
    'Spot on! Your understanding is getting deeper.',
    'Well done! That was a tough one and you nailed it.',
    'Your hard work is showing — keep it up!',
    "That's the kind of thinking that builds real mastery.",
  ];

  // ---------------------------------------------------------------------------
  // Streak Messages
  // ---------------------------------------------------------------------------

  /// Returns a streak-appropriate encouragement message.
  static String streakMessage(int streakDays) {
    if (streakDays >= 100) {
      return '100+ days! Your dedication is extraordinary. You\'re building knowledge that lasts.';
    } else if (streakDays >= 30) {
      return '$streakDays days strong! This consistency is what separates learners who succeed.';
    } else if (streakDays >= 7) {
      return '$streakDays-day streak! Your brain is forming powerful study habits.';
    } else if (streakDays >= 3) {
      return '$streakDays days in a row! You\'re building momentum.';
    } else {
      return 'Day $streakDays — every journey starts with a single step!';
    }
  }

  // ---------------------------------------------------------------------------
  // Session Summary
  // ---------------------------------------------------------------------------

  /// Returns an effort-focused summary message for end of session.
  static String sessionSummary({
    required int questionsAttempted,
    required int correctAnswers,
    required int minutesSpent,
  }) {
    final accuracy = questionsAttempted > 0
        ? (correctAnswers / questionsAttempted * 100).round()
        : 0;

    if (accuracy >= 90) {
      return 'Outstanding session! You tackled $questionsAttempted problems in '
          '${minutesSpent}m with $accuracy% accuracy. That deep focus really shows.';
    } else if (accuracy >= 70) {
      return 'Great work! $correctAnswers out of $questionsAttempted correct. '
          'The ones you got wrong are exactly where your brain grows strongest.';
    } else {
      return 'You showed real grit working through $questionsAttempted problems '
          'in ${minutesSpent}m. Every attempt makes the next one easier.';
    }
  }
}
