// ignore: unused_import
import 'package:intl/intl.dart' as intl;
import 'app_localizations.dart';

// ignore_for_file: type=lint

/// The translations for English (`en`).
class AppLocalizationsEn extends AppLocalizations {
  AppLocalizationsEn([String locale = 'en']) : super(locale);

  @override
  String get appTitle => 'Cena';

  @override
  String get goodMorning => 'Good morning';

  @override
  String get goodAfternoon => 'Good afternoon';

  @override
  String get goodEvening => 'Good evening';

  @override
  String get readyToLearn => 'Ready to learn?';

  @override
  String get tabHome => 'Home';

  @override
  String get tabLearn => 'Learn';

  @override
  String get tabMap => 'Map';

  @override
  String get tabProgress => 'Progress';

  @override
  String get tabProfile => 'Profile';

  @override
  String get quickPractice => 'Quick Practice';

  @override
  String get start => 'Start';

  @override
  String get continueLearning => 'Continue Learning';

  @override
  String get pickUpWhereYouLeftOff => 'Pick up where you left off';

  @override
  String get badges => 'Badges';

  @override
  String get completeSessions => 'Complete sessions to earn badges';

  @override
  String get aiTutor => 'AI Tutor';

  @override
  String get chat => 'Chat';

  @override
  String get subjects => 'Subjects';

  @override
  String levelN(int level) {
    return 'Level $level';
  }

  @override
  String percentToNext(int percent) {
    return '$percent% to next';
  }

  @override
  String dayStreak(int days) {
    return '$days day streak';
  }

  @override
  String get newLabel => 'NEW';

  @override
  String get noSessionsYet => 'No sessions yet';

  @override
  String get startFirstSession =>
      'Start your first learning session to see your history here.';

  @override
  String get startSession => 'Start Session';

  @override
  String get startNewSession => 'Start New Session';

  @override
  String get practiceSession => 'Practice Session';

  @override
  String accuracyStats(int accuracy, int questions, int minutes) {
    return '$accuracy% accuracy · $questions questions · ${minutes}m';
  }

  @override
  String minutesAgo(int minutes) {
    return '${minutes}m ago';
  }

  @override
  String hoursAgo(int hours) {
    return '${hours}h ago';
  }

  @override
  String get yesterday => 'Yesterday';

  @override
  String daysAgo(int days) {
    return '${days}d ago';
  }

  @override
  String get knowledgeMapBuilding => 'Your knowledge map is being built';

  @override
  String get knowledgeMapBuildingDesc =>
      'As you solve more questions, you’ll see connections between topics here.';

  @override
  String get knowledgeMap => 'Knowledge Map';

  @override
  String get knowledgeGraphPlaceholder =>
      'The interactive concept map will appear here as you progress through your learning sessions.';

  @override
  String get allSubjects => 'All Subjects';

  @override
  String get language => 'Language';

  @override
  String get learningStyle => 'Learning Style';

  @override
  String get useMomentumMeter => 'Use Momentum Meter';

  @override
  String get momentumDesc =>
      '7-day rolling progress (no streak reset pressure)';

  @override
  String get streakDesc => 'Daily streak mode';

  @override
  String get feedback => 'Feedback';

  @override
  String get haptics => 'Haptics';

  @override
  String get hapticsDesc => 'Tap and success vibration feedback';

  @override
  String get soundEffects => 'Sound Effects';

  @override
  String get soundsOffByDefault => 'Off by default';

  @override
  String get account => 'Account';

  @override
  String get profile => 'Profile';

  @override
  String get signOut => 'Sign Out';

  @override
  String get signOutConfirm => 'Are you sure you want to sign out?';

  @override
  String get cancel => 'Cancel';

  @override
  String get newStreakUnlocked => 'New: Learning streak unlocked!';

  @override
  String get newStudyGroups => 'New: Study groups available for you';

  @override
  String get yourPersonalMentor => 'Your personal AI learning mentor';

  @override
  String get continueWithGoogle => 'Continue with Google';

  @override
  String get continueWithApple => 'Continue with Apple';

  @override
  String get or => 'or';

  @override
  String get continueWithPhone => 'Continue with Phone';

  @override
  String get phoneNumber => 'Phone Number';

  @override
  String get phoneHint => '05X-XXX-XXXX';

  @override
  String get sendCode => 'Send Code';

  @override
  String enterCodeSent(String phone) {
    return 'Enter the 6-digit code sent to $phone';
  }

  @override
  String get verificationCode => 'Verification Code';

  @override
  String get verify => 'Verify';

  @override
  String get changePhoneNumber => 'Change phone number';

  @override
  String get otherSignInOptions => 'Other sign-in options';

  @override
  String get invalidPhone => 'Please enter a valid Israeli phone number';

  @override
  String get enterSixDigitCode => 'Please enter the 6-digit code';

  @override
  String get tryCenaTitle => 'Try Cena — answer one question';

  @override
  String get noAccountNeeded => 'No account needed — just tap an answer';

  @override
  String get correct => 'Correct!';

  @override
  String get notQuiteHereIsHow => 'Not quite — here’s how:';

  @override
  String get cenaAdaptsToYou =>
      'Cena adapts to your level with AI-powered tutoring.';

  @override
  String get createAccountContinue => 'Create Account & Continue';

  @override
  String get alreadyHaveAccount => 'Already have an account? Sign in';

  @override
  String get selectSubject => 'Select Subject';

  @override
  String get sessionDuration => 'Session Duration';

  @override
  String nMinutes(int n) {
    return '$n minutes';
  }

  @override
  String nMinShort(int n) {
    return '$n min';
  }

  @override
  String get sessionDetails => 'Session Details';

  @override
  String get maxQuestions => 'Max Questions';

  @override
  String get masteryThreshold => 'Mastery Threshold';

  @override
  String get studyEnergy => 'Study Energy';

  @override
  String remaining(int n) {
    return '$n remaining';
  }

  @override
  String get startLesson => 'Start Lesson';

  @override
  String get newLesson => 'New Lesson';

  @override
  String questionN(int n) {
    return 'Question $n';
  }

  @override
  String get endSession => 'End';

  @override
  String get endSessionTitle => 'End the lesson?';

  @override
  String get endSessionBody =>
      'Your progress will be saved. You can continue later.';

  @override
  String get continueLesson => 'Continue Lesson';

  @override
  String get endLessonConfirm => 'End';

  @override
  String get lessonComplete => 'Lesson Complete!';

  @override
  String get questions => 'Questions';

  @override
  String get accuracy => 'Accuracy';

  @override
  String get time => 'Time';

  @override
  String get backToHome => 'Back to Home';

  @override
  String get hints => 'Hints';

  @override
  String get spacedRepetition => 'Spaced Repetition';

  @override
  String get interleaved => 'Interleaved Learning';

  @override
  String get blocked => 'Focused Learning';

  @override
  String get adaptiveDifficulty => 'Adaptive Difficulty';

  @override
  String get socratic => 'Socratic Method';

  @override
  String get easy => 'Easy';

  @override
  String get medium => 'Medium';

  @override
  String get hard => 'Hard';

  @override
  String get multipleChoice => 'Multiple Choice';

  @override
  String get freeText => 'Free Text';

  @override
  String get numeric => 'Numeric';

  @override
  String get proof => 'Proof';

  @override
  String get diagram => 'Diagram';

  @override
  String get wellDone => 'Well done!';

  @override
  String get partiallyCorrect => 'Partially correct';

  @override
  String get incorrect => 'Incorrect';

  @override
  String get tapToContinue => 'Tap to continue';

  @override
  String get workedSolution => 'Worked Solution';

  @override
  String get conceptualError => 'Conceptual Error';

  @override
  String get proceduralError => 'Procedural Error';

  @override
  String get carelessError => 'Careless Error';

  @override
  String get notationError => 'Notation Error';

  @override
  String get incompleteAnswer => 'Incomplete Answer';

  @override
  String get welcomeToCena => 'Welcome to Cena';

  @override
  String get yourPersonalLearningCoach => 'Your personal learning coach';

  @override
  String get selectLanguage => 'Select Language';

  @override
  String get getStarted => 'Get Started';

  @override
  String get selectStudySubjects => 'Select Study Subjects';

  @override
  String get upTo3Subjects => 'You can select up to 3 subjects';

  @override
  String get comingSoon => 'Coming Soon';

  @override
  String get next => 'Next';

  @override
  String get back => 'Back';

  @override
  String get gradeAndTrack => 'Grade & Track';

  @override
  String get weAdaptToYourLevel => 'We’ll adapt learning to your level';

  @override
  String get grade => 'Grade';

  @override
  String get examLevel => 'Exam Level';

  @override
  String get testYourLevel => 'Test Your Level';

  @override
  String get diagnosticDesc =>
      'Answer 5 short questions so we can match\ncontent to your exact level.';

  @override
  String get takeTheQuiz => 'Take the Quiz';

  @override
  String get skipForNow => 'Skip for now';

  @override
  String questionNOfTotal(int n, int total) {
    return 'Question $n of $total';
  }

  @override
  String get skip => 'Skip';

  @override
  String get allSet => 'All Set!';

  @override
  String get hereSummary => 'Here’s a summary of your settings';

  @override
  String get subjectsLabel => 'Subjects';

  @override
  String get gradeLabel => 'Grade';

  @override
  String get examLevelLabel => 'Exam Level';

  @override
  String get diagnosticLabel => 'Diagnostic';

  @override
  String get whoAreYou => 'Who are you?';

  @override
  String get selectYourRole => 'Select your role';

  @override
  String get roleLabel => 'Role';

  @override
  String get whatDoYouWantToAchieve => 'What do you want to achieve?';

  @override
  String get chooseYourGoal => 'Choose your learning goal';

  @override
  String get goalLabel => 'Goal';

  @override
  String get howMuchTimePerDay => 'How much time per day?';

  @override
  String get youCanChangeThisLater => 'You can change this later';

  @override
  String get dailyTimeLabel => 'Daily Time';

  @override
  String get minutesShort => 'min';

  @override
  String get discoveryTour => 'Discovery Tour';

  @override
  String get discoveryTourDesc =>
      'Let\'s discover what you already know.\nAnswer a few quick questions so we can personalize your journey.';

  @override
  String get startDiscovery => 'Start Exploring';

  @override
  String get discoveryLabel => 'Discovery Tour';

  @override
  String get skipped => 'Skipped';

  @override
  String nAnswersRecorded(int n) {
    return '$n answers recorded';
  }

  @override
  String get startLearning => 'Start Learning';

  @override
  String get math => 'Mathematics';

  @override
  String get physics => 'Physics';

  @override
  String get chemistry => 'Chemistry';

  @override
  String get biology => 'Biology';

  @override
  String get computerScience => 'Computer Science';

  @override
  String get cs => 'CS';

  @override
  String get tutorName => 'CENA Tutor';

  @override
  String get typing => 'typing...';

  @override
  String get tutorWelcome => 'Hi! I’m your CENA tutor';

  @override
  String get tutorWelcomeDesc =>
      'Ask me anything about math, or tap a suggestion below to get started.';

  @override
  String get askCenaAnything => 'Ask CENA anything...';

  @override
  String get explainConcept => 'Explain this concept';

  @override
  String get simplerExample => 'Give me a simpler example';

  @override
  String get stepByStep => 'Show me step by step';

  @override
  String get whatDidIGetWrong => 'What did I get wrong?';

  @override
  String get differentApproach => 'Try a different approach';

  @override
  String progressToLevel(int level) {
    return 'Progress to Level $level';
  }

  @override
  String xpTotal(int xp) {
    return '$xp XP total';
  }

  @override
  String xpOfNeeded(int current, int needed) {
    return '$current / $needed XP';
  }

  @override
  String plusXpToday(int xp) {
    return '+$xp today';
  }

  @override
  String get recentActivity => 'Recent Activity';

  @override
  String get completeSessionForAchievements =>
      'Complete a session to see your achievements here.';

  @override
  String get justNow => 'Just now';

  @override
  String get momentumSuggestion =>
      'It looks like daily streaks add pressure. Switch to Momentum?';

  @override
  String get switchToMomentum => 'Yes, switch to Momentum mode';

  @override
  String get academicInfo => 'Academic Info';

  @override
  String get streak => 'Streak';

  @override
  String get xp => 'XP';

  @override
  String get level => 'Level';

  @override
  String get userId => 'User ID';

  @override
  String get editDisplayName => 'Edit Display Name';

  @override
  String get displayName => 'Display Name';

  @override
  String get enterYourName => 'Enter your name';

  @override
  String get save => 'Save';

  @override
  String get appVersion => 'Cena v0.1.0';

  @override
  String nDays(int n) {
    return '$n days';
  }

  @override
  String get pageNotFound => 'Page Not Found';

  @override
  String routeNotFound(String uri) {
    return 'Route not found: $uri';
  }

  @override
  String get goHome => 'Go Home';

  @override
  String get skipQuestion => 'Skip';

  @override
  String get submitAnswer => 'Submit Answer';

  @override
  String get checking => 'Checking...';

  @override
  String get unit => 'Unit';

  @override
  String get noUnit => 'None';

  @override
  String get writeProofHere => 'Write your proof here...';

  @override
  String get writeAnswerHere => 'Write your answer here...';

  @override
  String get skipQuestionTitle => 'Skip this question?';

  @override
  String get skipQuestionBody =>
      'Skipping won’t count as wrong, but it won’t improve your mastery either.';

  @override
  String get tapToReveal => 'Tap to reveal';

  @override
  String get dragHere => 'Drag here';

  @override
  String insightAt(String x, String y) {
    return 'Value at $x: $y';
  }

  @override
  String get resetDiagram => 'Reset';

  @override
  String get correctPlacement => 'Correct!';

  @override
  String get tryAgain => 'Try again';

  @override
  String get zoomIn => 'Zoom in to see details';

  @override
  String get iUnderstand => 'I understand, let’s practice';

  @override
  String get challenges => 'Challenges';

  @override
  String get allTopics => 'All Topics';

  @override
  String nOfTotal(int n, int total) {
    return '$n of $total completed';
  }

  @override
  String get completePreviousFirst => 'Complete previous cards first';

  @override
  String get locked => 'Locked';

  @override
  String get completed => 'Completed';
}
