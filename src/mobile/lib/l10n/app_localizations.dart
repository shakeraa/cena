import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:flutter/widgets.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:intl/intl.dart' as intl;

import 'app_localizations_ar.dart';
import 'app_localizations_en.dart';
import 'app_localizations_he.dart';

// ignore_for_file: type=lint

/// Callers can lookup localized strings with an instance of AppLocalizations
/// returned by `AppLocalizations.of(context)`.
///
/// Applications need to include `AppLocalizations.delegate()` in their app's
/// `localizationDelegates` list, and the locales they support in the app's
/// `supportedLocales` list. For example:
///
/// ```dart
/// import 'l10n/app_localizations.dart';
///
/// return MaterialApp(
///   localizationsDelegates: AppLocalizations.localizationsDelegates,
///   supportedLocales: AppLocalizations.supportedLocales,
///   home: MyApplicationHome(),
/// );
/// ```
///
/// ## Update pubspec.yaml
///
/// Please make sure to update your pubspec.yaml to include the following
/// packages:
///
/// ```yaml
/// dependencies:
///   # Internationalization support.
///   flutter_localizations:
///     sdk: flutter
///   intl: any # Use the pinned version from flutter_localizations
///
///   # Rest of dependencies
/// ```
///
/// ## iOS Applications
///
/// iOS applications define key application metadata, including supported
/// locales, in an Info.plist file that is built into the application bundle.
/// To configure the locales supported by your app, you’ll need to edit this
/// file.
///
/// First, open your project’s ios/Runner.xcworkspace Xcode workspace file.
/// Then, in the Project Navigator, open the Info.plist file under the Runner
/// project’s Runner folder.
///
/// Next, select the Information Property List item, select Add Item from the
/// Editor menu, then select Localizations from the pop-up menu.
///
/// Select and expand the newly-created Localizations item then, for each
/// locale your application supports, add a new item and select the locale
/// you wish to add from the pop-up menu in the Value field. This list should
/// be consistent with the languages listed in the AppLocalizations.supportedLocales
/// property.
abstract class AppLocalizations {
  AppLocalizations(String locale)
      : localeName = intl.Intl.canonicalizedLocale(locale.toString());

  final String localeName;

  static AppLocalizations of(BuildContext context) {
    return Localizations.of<AppLocalizations>(context, AppLocalizations)!;
  }

  static const LocalizationsDelegate<AppLocalizations> delegate =
      _AppLocalizationsDelegate();

  /// A list of this localizations delegate along with the default localizations
  /// delegates.
  ///
  /// Returns a list of localizations delegates containing this delegate along with
  /// GlobalMaterialLocalizations.delegate, GlobalCupertinoLocalizations.delegate,
  /// and GlobalWidgetsLocalizations.delegate.
  ///
  /// Additional delegates can be added by appending to this list in
  /// MaterialApp. This list does not have to be used at all if a custom list
  /// of delegates is preferred or required.
  static const List<LocalizationsDelegate<dynamic>> localizationsDelegates =
      <LocalizationsDelegate<dynamic>>[
    delegate,
    GlobalMaterialLocalizations.delegate,
    GlobalCupertinoLocalizations.delegate,
    GlobalWidgetsLocalizations.delegate,
  ];

  /// A list of this localizations delegate's supported locales.
  static const List<Locale> supportedLocales = <Locale>[
    Locale('en'),
    Locale('ar'),
    Locale('he')
  ];

  /// The application title
  ///
  /// In en, this message translates to:
  /// **'Cena'**
  String get appTitle;

  /// Morning greeting (before 12pm)
  ///
  /// In en, this message translates to:
  /// **'Good morning'**
  String get goodMorning;

  /// Afternoon greeting (12pm-5pm)
  ///
  /// In en, this message translates to:
  /// **'Good afternoon'**
  String get goodAfternoon;

  /// Evening greeting (after 5pm)
  ///
  /// In en, this message translates to:
  /// **'Good evening'**
  String get goodEvening;

  /// Subtitle under greeting on home screen
  ///
  /// In en, this message translates to:
  /// **'Ready to learn?'**
  String get readyToLearn;

  /// Home tab label
  ///
  /// In en, this message translates to:
  /// **'Home'**
  String get tabHome;

  /// Learn tab label
  ///
  /// In en, this message translates to:
  /// **'Learn'**
  String get tabLearn;

  /// Map/Knowledge Graph tab label
  ///
  /// In en, this message translates to:
  /// **'Map'**
  String get tabMap;

  /// Progress tab label
  ///
  /// In en, this message translates to:
  /// **'Progress'**
  String get tabProgress;

  /// Profile tab label
  ///
  /// In en, this message translates to:
  /// **'Profile'**
  String get tabProfile;

  /// Quick practice card title on home
  ///
  /// In en, this message translates to:
  /// **'Quick Practice'**
  String get quickPractice;

  /// Generic start button
  ///
  /// In en, this message translates to:
  /// **'Start'**
  String get start;

  /// Continue learning CTA title
  ///
  /// In en, this message translates to:
  /// **'Continue Learning'**
  String get continueLearning;

  /// Continue learning CTA subtitle
  ///
  /// In en, this message translates to:
  /// **'Pick up where you left off'**
  String get pickUpWhereYouLeftOff;

  /// Badges section title
  ///
  /// In en, this message translates to:
  /// **'Badges'**
  String get badges;

  /// Empty badges placeholder text
  ///
  /// In en, this message translates to:
  /// **'Complete sessions to earn badges'**
  String get completeSessions;

  /// AI Tutor card title
  ///
  /// In en, this message translates to:
  /// **'AI Tutor'**
  String get aiTutor;

  /// Chat button label
  ///
  /// In en, this message translates to:
  /// **'Chat'**
  String get chat;

  /// Subjects section header
  ///
  /// In en, this message translates to:
  /// **'Subjects'**
  String get subjects;

  /// Level display label
  ///
  /// In en, this message translates to:
  /// **'Level {level}'**
  String levelN(int level);

  /// XP progress subtitle
  ///
  /// In en, this message translates to:
  /// **'{percent}% to next'**
  String percentToNext(int percent);

  /// Streak chip label
  ///
  /// In en, this message translates to:
  /// **'{days} day streak'**
  String dayStreak(int days);

  /// Badge shown on new features
  ///
  /// In en, this message translates to:
  /// **'NEW'**
  String get newLabel;

  /// Empty sessions title
  ///
  /// In en, this message translates to:
  /// **'No sessions yet'**
  String get noSessionsYet;

  /// Empty sessions description
  ///
  /// In en, this message translates to:
  /// **'Start your first learning session to see your history here.'**
  String get startFirstSession;

  /// Start session button
  ///
  /// In en, this message translates to:
  /// **'Start Session'**
  String get startSession;

  /// Start new session button in history
  ///
  /// In en, this message translates to:
  /// **'Start New Session'**
  String get startNewSession;

  /// Default session name when no subject
  ///
  /// In en, this message translates to:
  /// **'Practice Session'**
  String get practiceSession;

  /// Session history card subtitle
  ///
  /// In en, this message translates to:
  /// **'{accuracy}% accuracy · {questions} questions · {minutes}m'**
  String accuracyStats(int accuracy, int questions, int minutes);

  /// Relative time
  ///
  /// In en, this message translates to:
  /// **'{minutes}m ago'**
  String minutesAgo(int minutes);

  /// Relative time
  ///
  /// In en, this message translates to:
  /// **'{hours}h ago'**
  String hoursAgo(int hours);

  /// Relative time
  ///
  /// In en, this message translates to:
  /// **'Yesterday'**
  String get yesterday;

  /// Relative time
  ///
  /// In en, this message translates to:
  /// **'{days}d ago'**
  String daysAgo(int days);

  /// Knowledge graph locked state title
  ///
  /// In en, this message translates to:
  /// **'Your knowledge map is being built'**
  String get knowledgeMapBuilding;

  /// Knowledge graph locked state description
  ///
  /// In en, this message translates to:
  /// **'As you solve more questions, you’ll see connections between topics here.'**
  String get knowledgeMapBuildingDesc;

  /// Knowledge graph screen title
  ///
  /// In en, this message translates to:
  /// **'Knowledge Map'**
  String get knowledgeMap;

  /// Knowledge graph placeholder text
  ///
  /// In en, this message translates to:
  /// **'The interactive concept map will appear here as you progress through your learning sessions.'**
  String get knowledgeGraphPlaceholder;

  /// Subject filter option
  ///
  /// In en, this message translates to:
  /// **'All Subjects'**
  String get allSubjects;

  /// Language settings card title
  ///
  /// In en, this message translates to:
  /// **'Language'**
  String get language;

  /// Learning style settings card title
  ///
  /// In en, this message translates to:
  /// **'Learning Style'**
  String get learningStyle;

  /// Momentum meter toggle title
  ///
  /// In en, this message translates to:
  /// **'Use Momentum Meter'**
  String get useMomentumMeter;

  /// Momentum meter on description
  ///
  /// In en, this message translates to:
  /// **'7-day rolling progress (no streak reset pressure)'**
  String get momentumDesc;

  /// Momentum meter off description
  ///
  /// In en, this message translates to:
  /// **'Daily streak mode'**
  String get streakDesc;

  /// Feedback settings card title
  ///
  /// In en, this message translates to:
  /// **'Feedback'**
  String get feedback;

  /// Haptics toggle title
  ///
  /// In en, this message translates to:
  /// **'Haptics'**
  String get haptics;

  /// Haptics toggle description
  ///
  /// In en, this message translates to:
  /// **'Tap and success vibration feedback'**
  String get hapticsDesc;

  /// Sound effects toggle title
  ///
  /// In en, this message translates to:
  /// **'Sound Effects'**
  String get soundEffects;

  /// Sound effects toggle description
  ///
  /// In en, this message translates to:
  /// **'Off by default'**
  String get soundsOffByDefault;

  /// Account settings card title
  ///
  /// In en, this message translates to:
  /// **'Account'**
  String get account;

  /// Profile menu item
  ///
  /// In en, this message translates to:
  /// **'Profile'**
  String get profile;

  /// Sign out button
  ///
  /// In en, this message translates to:
  /// **'Sign Out'**
  String get signOut;

  /// Sign out confirmation message
  ///
  /// In en, this message translates to:
  /// **'Are you sure you want to sign out?'**
  String get signOutConfirm;

  /// Generic cancel button
  ///
  /// In en, this message translates to:
  /// **'Cancel'**
  String get cancel;

  /// Snackbar when streak feature unlocks
  ///
  /// In en, this message translates to:
  /// **'New: Learning streak unlocked!'**
  String get newStreakUnlocked;

  /// Snackbar when study groups unlock
  ///
  /// In en, this message translates to:
  /// **'New: Study groups available for you'**
  String get newStudyGroups;

  /// Auth screen tagline
  ///
  /// In en, this message translates to:
  /// **'Your personal AI learning mentor'**
  String get yourPersonalMentor;

  /// Google sign-in button
  ///
  /// In en, this message translates to:
  /// **'Continue with Google'**
  String get continueWithGoogle;

  /// Apple sign-in button
  ///
  /// In en, this message translates to:
  /// **'Continue with Apple'**
  String get continueWithApple;

  /// Divider text between auth options
  ///
  /// In en, this message translates to:
  /// **'or'**
  String get or;

  /// Phone auth button
  ///
  /// In en, this message translates to:
  /// **'Continue with Phone'**
  String get continueWithPhone;

  /// Phone input label
  ///
  /// In en, this message translates to:
  /// **'Phone Number'**
  String get phoneNumber;

  /// Phone input hint text
  ///
  /// In en, this message translates to:
  /// **'05X-XXX-XXXX'**
  String get phoneHint;

  /// Send SMS code button
  ///
  /// In en, this message translates to:
  /// **'Send Code'**
  String get sendCode;

  /// Code entry instruction
  ///
  /// In en, this message translates to:
  /// **'Enter the 6-digit code sent to {phone}'**
  String enterCodeSent(String phone);

  /// Code input label
  ///
  /// In en, this message translates to:
  /// **'Verification Code'**
  String get verificationCode;

  /// Verify code button
  ///
  /// In en, this message translates to:
  /// **'Verify'**
  String get verify;

  /// Change phone number link
  ///
  /// In en, this message translates to:
  /// **'Change phone number'**
  String get changePhoneNumber;

  /// Back to social auth link
  ///
  /// In en, this message translates to:
  /// **'Other sign-in options'**
  String get otherSignInOptions;

  /// Invalid phone validation message
  ///
  /// In en, this message translates to:
  /// **'Please enter a valid Israeli phone number'**
  String get invalidPhone;

  /// Code validation message
  ///
  /// In en, this message translates to:
  /// **'Please enter the 6-digit code'**
  String get enterSixDigitCode;

  /// Try question screen title
  ///
  /// In en, this message translates to:
  /// **'Try Cena — answer one question'**
  String get tryCenaTitle;

  /// Try question hint text
  ///
  /// In en, this message translates to:
  /// **'No account needed — just tap an answer'**
  String get noAccountNeeded;

  /// Correct answer feedback
  ///
  /// In en, this message translates to:
  /// **'Correct!'**
  String get correct;

  /// Wrong answer feedback title
  ///
  /// In en, this message translates to:
  /// **'Not quite — here’s how:'**
  String get notQuiteHereIsHow;

  /// Post-answer CTA text
  ///
  /// In en, this message translates to:
  /// **'Cena adapts to your level with AI-powered tutoring.'**
  String get cenaAdaptsToYou;

  /// Post-answer signup CTA
  ///
  /// In en, this message translates to:
  /// **'Create Account & Continue'**
  String get createAccountContinue;

  /// Existing user sign-in link
  ///
  /// In en, this message translates to:
  /// **'Already have an account? Sign in'**
  String get alreadyHaveAccount;

  /// Session config subject header
  ///
  /// In en, this message translates to:
  /// **'Select Subject'**
  String get selectSubject;

  /// Session config duration header
  ///
  /// In en, this message translates to:
  /// **'Session Duration'**
  String get sessionDuration;

  /// Duration display
  ///
  /// In en, this message translates to:
  /// **'{n} minutes'**
  String nMinutes(int n);

  /// Duration short label
  ///
  /// In en, this message translates to:
  /// **'{n} min'**
  String nMinShort(int n);

  /// Session info card title
  ///
  /// In en, this message translates to:
  /// **'Session Details'**
  String get sessionDetails;

  /// Session info row label
  ///
  /// In en, this message translates to:
  /// **'Max Questions'**
  String get maxQuestions;

  /// Session info row label
  ///
  /// In en, this message translates to:
  /// **'Mastery Threshold'**
  String get masteryThreshold;

  /// LLM budget label
  ///
  /// In en, this message translates to:
  /// **'Study Energy'**
  String get studyEnergy;

  /// Remaining budget
  ///
  /// In en, this message translates to:
  /// **'{n} remaining'**
  String remaining(int n);

  /// Start lesson button
  ///
  /// In en, this message translates to:
  /// **'Start Lesson'**
  String get startLesson;

  /// New lesson AppBar title
  ///
  /// In en, this message translates to:
  /// **'New Lesson'**
  String get newLesson;

  /// Question number label
  ///
  /// In en, this message translates to:
  /// **'Question {n}'**
  String questionN(int n);

  /// End session button
  ///
  /// In en, this message translates to:
  /// **'End'**
  String get endSession;

  /// End session dialog title
  ///
  /// In en, this message translates to:
  /// **'End the lesson?'**
  String get endSessionTitle;

  /// End session dialog body
  ///
  /// In en, this message translates to:
  /// **'Your progress will be saved. You can continue later.'**
  String get endSessionBody;

  /// Continue lesson dialog button
  ///
  /// In en, this message translates to:
  /// **'Continue Lesson'**
  String get continueLesson;

  /// End lesson dialog confirm button
  ///
  /// In en, this message translates to:
  /// **'End'**
  String get endLessonConfirm;

  /// Session ended title
  ///
  /// In en, this message translates to:
  /// **'Lesson Complete!'**
  String get lessonComplete;

  /// Summary label
  ///
  /// In en, this message translates to:
  /// **'Questions'**
  String get questions;

  /// Summary label
  ///
  /// In en, this message translates to:
  /// **'Accuracy'**
  String get accuracy;

  /// Summary label
  ///
  /// In en, this message translates to:
  /// **'Time'**
  String get time;

  /// Session ended CTA
  ///
  /// In en, this message translates to:
  /// **'Back to Home'**
  String get backToHome;

  /// Hints section title
  ///
  /// In en, this message translates to:
  /// **'Hints'**
  String get hints;

  /// Methodology name
  ///
  /// In en, this message translates to:
  /// **'Spaced Repetition'**
  String get spacedRepetition;

  /// Methodology name
  ///
  /// In en, this message translates to:
  /// **'Interleaved Learning'**
  String get interleaved;

  /// Methodology name
  ///
  /// In en, this message translates to:
  /// **'Focused Learning'**
  String get blocked;

  /// Methodology name
  ///
  /// In en, this message translates to:
  /// **'Adaptive Difficulty'**
  String get adaptiveDifficulty;

  /// Methodology name
  ///
  /// In en, this message translates to:
  /// **'Socratic Method'**
  String get socratic;

  /// Difficulty label (1-3)
  ///
  /// In en, this message translates to:
  /// **'Easy'**
  String get easy;

  /// Difficulty label (4-6)
  ///
  /// In en, this message translates to:
  /// **'Medium'**
  String get medium;

  /// Difficulty label (7-10)
  ///
  /// In en, this message translates to:
  /// **'Hard'**
  String get hard;

  /// Question type
  ///
  /// In en, this message translates to:
  /// **'Multiple Choice'**
  String get multipleChoice;

  /// Question type
  ///
  /// In en, this message translates to:
  /// **'Free Text'**
  String get freeText;

  /// Question type
  ///
  /// In en, this message translates to:
  /// **'Numeric'**
  String get numeric;

  /// Question type
  ///
  /// In en, this message translates to:
  /// **'Proof'**
  String get proof;

  /// Question type
  ///
  /// In en, this message translates to:
  /// **'Diagram'**
  String get diagram;

  /// Correct answer overlay title
  ///
  /// In en, this message translates to:
  /// **'Well done!'**
  String get wellDone;

  /// Partial answer overlay title
  ///
  /// In en, this message translates to:
  /// **'Partially correct'**
  String get partiallyCorrect;

  /// Wrong answer overlay title
  ///
  /// In en, this message translates to:
  /// **'Incorrect'**
  String get incorrect;

  /// Overlay dismiss hint
  ///
  /// In en, this message translates to:
  /// **'Tap to continue'**
  String get tapToContinue;

  /// Worked solution card title
  ///
  /// In en, this message translates to:
  /// **'Worked Solution'**
  String get workedSolution;

  /// Error type
  ///
  /// In en, this message translates to:
  /// **'Conceptual Error'**
  String get conceptualError;

  /// Error type
  ///
  /// In en, this message translates to:
  /// **'Procedural Error'**
  String get proceduralError;

  /// Error type
  ///
  /// In en, this message translates to:
  /// **'Careless Error'**
  String get carelessError;

  /// Error type
  ///
  /// In en, this message translates to:
  /// **'Notation Error'**
  String get notationError;

  /// Error type
  ///
  /// In en, this message translates to:
  /// **'Incomplete Answer'**
  String get incompleteAnswer;

  /// Onboarding page 1 title
  ///
  /// In en, this message translates to:
  /// **'Welcome to Cena'**
  String get welcomeToCena;

  /// Onboarding page 1 subtitle
  ///
  /// In en, this message translates to:
  /// **'Your personal learning coach'**
  String get yourPersonalLearningCoach;

  /// Onboarding language selector label
  ///
  /// In en, this message translates to:
  /// **'Select Language'**
  String get selectLanguage;

  /// Onboarding page 1 CTA
  ///
  /// In en, this message translates to:
  /// **'Get Started'**
  String get getStarted;

  /// Onboarding page 2 title
  ///
  /// In en, this message translates to:
  /// **'Select Study Subjects'**
  String get selectStudySubjects;

  /// Onboarding page 2 subtitle
  ///
  /// In en, this message translates to:
  /// **'You can select up to 3 subjects'**
  String get upTo3Subjects;

  /// Unavailable subject badge
  ///
  /// In en, this message translates to:
  /// **'Coming Soon'**
  String get comingSoon;

  /// Next button
  ///
  /// In en, this message translates to:
  /// **'Next'**
  String get next;

  /// Back button
  ///
  /// In en, this message translates to:
  /// **'Back'**
  String get back;

  /// Onboarding page 3 title
  ///
  /// In en, this message translates to:
  /// **'Grade & Track'**
  String get gradeAndTrack;

  /// Onboarding page 3 subtitle
  ///
  /// In en, this message translates to:
  /// **'We’ll adapt learning to your level'**
  String get weAdaptToYourLevel;

  /// Grade selector label
  ///
  /// In en, this message translates to:
  /// **'Grade'**
  String get grade;

  /// Bagrut units selector label
  ///
  /// In en, this message translates to:
  /// **'Exam Level'**
  String get examLevel;

  /// Onboarding diagnostic page title
  ///
  /// In en, this message translates to:
  /// **'Test Your Level'**
  String get testYourLevel;

  /// Diagnostic page description
  ///
  /// In en, this message translates to:
  /// **'Answer 5 short questions so we can match\ncontent to your exact level.'**
  String get diagnosticDesc;

  /// Start diagnostic button
  ///
  /// In en, this message translates to:
  /// **'Take the Quiz'**
  String get takeTheQuiz;

  /// Skip diagnostic button
  ///
  /// In en, this message translates to:
  /// **'Skip for now'**
  String get skipForNow;

  /// Diagnostic progress
  ///
  /// In en, this message translates to:
  /// **'Question {n} of {total}'**
  String questionNOfTotal(int n, int total);

  /// Skip button
  ///
  /// In en, this message translates to:
  /// **'Skip'**
  String get skip;

  /// Onboarding ready page title
  ///
  /// In en, this message translates to:
  /// **'All Set!'**
  String get allSet;

  /// Onboarding ready page subtitle
  ///
  /// In en, this message translates to:
  /// **'Here’s a summary of your settings'**
  String get hereSummary;

  /// Summary row label
  ///
  /// In en, this message translates to:
  /// **'Subjects'**
  String get subjectsLabel;

  /// Summary row label
  ///
  /// In en, this message translates to:
  /// **'Grade'**
  String get gradeLabel;

  /// Summary row label
  ///
  /// In en, this message translates to:
  /// **'Exam Level'**
  String get examLevelLabel;

  /// Summary row label
  ///
  /// In en, this message translates to:
  /// **'Diagnostic'**
  String get diagnosticLabel;

  /// Diagnostic skipped value
  ///
  /// In en, this message translates to:
  /// **'Skipped'**
  String get skipped;

  /// Diagnostic completed value
  ///
  /// In en, this message translates to:
  /// **'{n} answers recorded'**
  String nAnswersRecorded(int n);

  /// Onboarding final CTA
  ///
  /// In en, this message translates to:
  /// **'Start Learning'**
  String get startLearning;

  /// Math subject name
  ///
  /// In en, this message translates to:
  /// **'Mathematics'**
  String get math;

  /// Physics subject name
  ///
  /// In en, this message translates to:
  /// **'Physics'**
  String get physics;

  /// Chemistry subject name
  ///
  /// In en, this message translates to:
  /// **'Chemistry'**
  String get chemistry;

  /// Biology subject name
  ///
  /// In en, this message translates to:
  /// **'Biology'**
  String get biology;

  /// CS subject name
  ///
  /// In en, this message translates to:
  /// **'Computer Science'**
  String get computerScience;

  /// CS subject abbreviation
  ///
  /// In en, this message translates to:
  /// **'CS'**
  String get cs;

  /// AI tutor display name
  ///
  /// In en, this message translates to:
  /// **'CENA Tutor'**
  String get tutorName;

  /// Typing indicator
  ///
  /// In en, this message translates to:
  /// **'typing...'**
  String get typing;

  /// Tutor welcome title
  ///
  /// In en, this message translates to:
  /// **'Hi! I’m your CENA tutor'**
  String get tutorWelcome;

  /// Tutor welcome description
  ///
  /// In en, this message translates to:
  /// **'Ask me anything about math, or tap a suggestion below to get started.'**
  String get tutorWelcomeDesc;

  /// Chat input placeholder
  ///
  /// In en, this message translates to:
  /// **'Ask CENA anything...'**
  String get askCenaAnything;

  /// Quick reply chip
  ///
  /// In en, this message translates to:
  /// **'Explain this concept'**
  String get explainConcept;

  /// Quick reply chip
  ///
  /// In en, this message translates to:
  /// **'Give me a simpler example'**
  String get simplerExample;

  /// Quick reply chip
  ///
  /// In en, this message translates to:
  /// **'Show me step by step'**
  String get stepByStep;

  /// Quick reply chip
  ///
  /// In en, this message translates to:
  /// **'What did I get wrong?'**
  String get whatDidIGetWrong;

  /// Quick reply chip
  ///
  /// In en, this message translates to:
  /// **'Try a different approach'**
  String get differentApproach;

  /// XP progress label
  ///
  /// In en, this message translates to:
  /// **'Progress to Level {level}'**
  String progressToLevel(int level);

  /// Total XP display
  ///
  /// In en, this message translates to:
  /// **'{xp} XP total'**
  String xpTotal(int xp);

  /// XP progress fraction
  ///
  /// In en, this message translates to:
  /// **'{current} / {needed} XP'**
  String xpOfNeeded(int current, int needed);

  /// Daily XP badge
  ///
  /// In en, this message translates to:
  /// **'+{xp} today'**
  String plusXpToday(int xp);

  /// Achievement section title
  ///
  /// In en, this message translates to:
  /// **'Recent Activity'**
  String get recentActivity;

  /// Empty achievements text
  ///
  /// In en, this message translates to:
  /// **'Complete a session to see your achievements here.'**
  String get completeSessionForAchievements;

  /// Relative time
  ///
  /// In en, this message translates to:
  /// **'Just now'**
  String get justNow;

  /// Momentum suggestion text
  ///
  /// In en, this message translates to:
  /// **'It looks like daily streaks add pressure. Switch to Momentum?'**
  String get momentumSuggestion;

  /// Momentum switch button
  ///
  /// In en, this message translates to:
  /// **'Yes, switch to Momentum mode'**
  String get switchToMomentum;

  /// Profile card title
  ///
  /// In en, this message translates to:
  /// **'Academic Info'**
  String get academicInfo;

  /// Profile info label
  ///
  /// In en, this message translates to:
  /// **'Streak'**
  String get streak;

  /// Profile info label
  ///
  /// In en, this message translates to:
  /// **'XP'**
  String get xp;

  /// Profile info label
  ///
  /// In en, this message translates to:
  /// **'Level'**
  String get level;

  /// Profile info label
  ///
  /// In en, this message translates to:
  /// **'User ID'**
  String get userId;

  /// Profile action
  ///
  /// In en, this message translates to:
  /// **'Edit Display Name'**
  String get editDisplayName;

  /// Display name input label
  ///
  /// In en, this message translates to:
  /// **'Display Name'**
  String get displayName;

  /// Display name input hint
  ///
  /// In en, this message translates to:
  /// **'Enter your name'**
  String get enterYourName;

  /// Save button
  ///
  /// In en, this message translates to:
  /// **'Save'**
  String get save;

  /// App version display
  ///
  /// In en, this message translates to:
  /// **'Cena v0.1.0'**
  String get appVersion;

  /// Days count
  ///
  /// In en, this message translates to:
  /// **'{n} days'**
  String nDays(int n);

  /// 404 screen title
  ///
  /// In en, this message translates to:
  /// **'Page Not Found'**
  String get pageNotFound;

  /// 404 screen message
  ///
  /// In en, this message translates to:
  /// **'Route not found: {uri}'**
  String routeNotFound(String uri);

  /// 404 home button
  ///
  /// In en, this message translates to:
  /// **'Go Home'**
  String get goHome;

  /// Skip button on answer input
  ///
  /// In en, this message translates to:
  /// **'Skip'**
  String get skipQuestion;

  /// Submit answer button
  ///
  /// In en, this message translates to:
  /// **'Submit Answer'**
  String get submitAnswer;

  /// Submit button while evaluating
  ///
  /// In en, this message translates to:
  /// **'Checking...'**
  String get checking;

  /// Unit selector label for numeric input
  ///
  /// In en, this message translates to:
  /// **'Unit'**
  String get unit;

  /// No unit option in dropdown
  ///
  /// In en, this message translates to:
  /// **'None'**
  String get noUnit;

  /// Proof text input hint
  ///
  /// In en, this message translates to:
  /// **'Write your proof here...'**
  String get writeProofHere;

  /// Free text input hint
  ///
  /// In en, this message translates to:
  /// **'Write your answer here...'**
  String get writeAnswerHere;

  /// Skip confirmation dialog title
  ///
  /// In en, this message translates to:
  /// **'Skip this question?'**
  String get skipQuestionTitle;

  /// Skip confirmation dialog body
  ///
  /// In en, this message translates to:
  /// **'Skipping won’t count as wrong, but it won’t improve your mastery either.'**
  String get skipQuestionBody;

  /// Hotspot tooltip for hidden hotspots
  ///
  /// In en, this message translates to:
  /// **'Tap to reveal'**
  String get tapToReveal;

  /// Drag target placeholder
  ///
  /// In en, this message translates to:
  /// **'Drag here'**
  String get dragHere;

  /// Graph insight tooltip
  ///
  /// In en, this message translates to:
  /// **'Value at {x}: {y}'**
  String insightAt(String x, String y);

  /// Reset diagram to original state
  ///
  /// In en, this message translates to:
  /// **'Reset'**
  String get resetDiagram;

  /// Drag label placed correctly
  ///
  /// In en, this message translates to:
  /// **'Correct!'**
  String get correctPlacement;

  /// Wrong drag placement
  ///
  /// In en, this message translates to:
  /// **'Try again'**
  String get tryAgain;

  /// Diagram zoom hint
  ///
  /// In en, this message translates to:
  /// **'Zoom in to see details'**
  String get zoomIn;

  /// Concept summary card CTA
  ///
  /// In en, this message translates to:
  /// **'I understand, let’s practice'**
  String get iUnderstand;

  /// Challenges screen title
  ///
  /// In en, this message translates to:
  /// **'Challenges'**
  String get challenges;

  /// Filter chip for all topics
  ///
  /// In en, this message translates to:
  /// **'All Topics'**
  String get allTopics;

  /// Card chain progress label
  ///
  /// In en, this message translates to:
  /// **'{n} of {total} completed'**
  String nOfTotal(int n, int total);

  /// Locked card tooltip
  ///
  /// In en, this message translates to:
  /// **'Complete previous cards first'**
  String get completePreviousFirst;

  /// Locked card state
  ///
  /// In en, this message translates to:
  /// **'Locked'**
  String get locked;

  /// Completed card state
  ///
  /// In en, this message translates to:
  /// **'Completed'**
  String get completed;
}

class _AppLocalizationsDelegate
    extends LocalizationsDelegate<AppLocalizations> {
  const _AppLocalizationsDelegate();

  @override
  Future<AppLocalizations> load(Locale locale) {
    return SynchronousFuture<AppLocalizations>(lookupAppLocalizations(locale));
  }

  @override
  bool isSupported(Locale locale) =>
      <String>['ar', 'en', 'he'].contains(locale.languageCode);

  @override
  bool shouldReload(_AppLocalizationsDelegate old) => false;
}

AppLocalizations lookupAppLocalizations(Locale locale) {
  // Lookup logic when only language code is specified.
  switch (locale.languageCode) {
    case 'ar':
      return AppLocalizationsAr();
    case 'en':
      return AppLocalizationsEn();
    case 'he':
      return AppLocalizationsHe();
  }

  throw FlutterError(
      'AppLocalizations.delegate failed to load unsupported locale "$locale". This is likely '
      'an issue with the localizations generation tool. Please file an issue '
      'on GitHub with a reproducible sample app and the gen-l10n configuration '
      'that was used.');
}
