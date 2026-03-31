// ignore: unused_import
import 'package:intl/intl.dart' as intl;
import 'app_localizations.dart';

// ignore_for_file: type=lint

/// The translations for Hebrew (`he`).
class AppLocalizationsHe extends AppLocalizations {
  AppLocalizationsHe([String locale = 'he']) : super(locale);

  @override
  String get appTitle => 'סנה';

  @override
  String get goodMorning => 'בוקר טוב';

  @override
  String get goodAfternoon => 'צהריים טובים';

  @override
  String get goodEvening => 'ערב טוב';

  @override
  String get readyToLearn => 'מוכנים ללמוד?';

  @override
  String get tabHome => 'בית';

  @override
  String get tabLearn => 'למידה';

  @override
  String get tabMap => 'מפה';

  @override
  String get tabProgress => 'התקדמות';

  @override
  String get tabProfile => 'פרופיל';

  @override
  String get quickPractice => 'תרגול מהיר';

  @override
  String get start => 'התחל';

  @override
  String get continueLearning => 'המשך למידה';

  @override
  String get pickUpWhereYouLeftOff => 'המשך מאיפה שהפסקת';

  @override
  String get badges => 'תגים';

  @override
  String get completeSessions => 'השלם שיעורים כדי להרוויח תגים';

  @override
  String get aiTutor => 'מורה AI';

  @override
  String get chat => 'צ׳אט';

  @override
  String get subjects => 'מקצועות';

  @override
  String levelN(int level) {
    return 'רמה $level';
  }

  @override
  String percentToNext(int percent) {
    return '$percent% לרמה הבאה';
  }

  @override
  String dayStreak(int days) {
    return 'רצף של $days ימים';
  }

  @override
  String get newLabel => 'חדש';

  @override
  String get noSessionsYet => 'אין שיעורים עדיין';

  @override
  String get startFirstSession =>
      'התחל את השיעור הראשון שלך כדי לראות את ההיסטוריה כאן.';

  @override
  String get startSession => 'התחל שיעור';

  @override
  String get startNewSession => 'התחל שיעור חדש';

  @override
  String get practiceSession => 'שיעור תרגול';

  @override
  String accuracyStats(int accuracy, int questions, int minutes) {
    return '$accuracy% דיוק · $questions שאלות · $minutes דק׳';
  }

  @override
  String minutesAgo(int minutes) {
    return 'לפני $minutes דק׳';
  }

  @override
  String hoursAgo(int hours) {
    return 'לפני $hours שעות';
  }

  @override
  String get yesterday => 'אתמול';

  @override
  String daysAgo(int days) {
    return 'לפני $days ימים';
  }

  @override
  String get knowledgeMapBuilding => 'מפת הידע שלך נבנית';

  @override
  String get knowledgeMapBuildingDesc =>
      'ככל שתפתור יותר שאלות, נראה כאן חיבורים בין נושאים.';

  @override
  String get knowledgeMap => 'מפת ידע';

  @override
  String get knowledgeGraphPlaceholder =>
      'מפת המושגים האינטראקטיבית תופיע כאן ככל שתתקדם בשיעורים.';

  @override
  String get allSubjects => 'כל המקצועות';

  @override
  String get language => 'שפה';

  @override
  String get learningStyle => 'סגנון למידה';

  @override
  String get useMomentumMeter => 'השתמש במד מומנטום';

  @override
  String get momentumDesc => 'התקדמות 7 ימים (ללא לחץ של איפוס רצף)';

  @override
  String get streakDesc => 'מצב רצף יומי';

  @override
  String get feedback => 'משוב';

  @override
  String get haptics => 'רטט';

  @override
  String get hapticsDesc => 'משוב רטט בלחיצה ובהצלחה';

  @override
  String get soundEffects => 'אפקטי קול';

  @override
  String get soundsOffByDefault => 'כבוי כברירת מחדל';

  @override
  String get account => 'חשבון';

  @override
  String get profile => 'פרופיל';

  @override
  String get signOut => 'יציאה';

  @override
  String get signOutConfirm => 'בטוח שברצונך לצאת?';

  @override
  String get cancel => 'ביטול';

  @override
  String get newStreakUnlocked => 'חדש: רצף לימוד נפתח!';

  @override
  String get newStudyGroups => 'חדש: קבוצות לימוד זמינות עבורך';

  @override
  String get yourPersonalMentor => 'המאמן האישי שלך ללמידה';

  @override
  String get continueWithGoogle => 'המשך עם Google';

  @override
  String get continueWithApple => 'המשך עם Apple';

  @override
  String get or => 'או';

  @override
  String get continueWithPhone => 'המשך עם טלפון';

  @override
  String get phoneNumber => 'מספר טלפון';

  @override
  String get phoneHint => '05X-XXX-XXXX';

  @override
  String get sendCode => 'שלח קוד';

  @override
  String enterCodeSent(String phone) {
    return 'הזן את הקוד בן 6 הספרות שנשלח ל-$phone';
  }

  @override
  String get verificationCode => 'קוד אימות';

  @override
  String get verify => 'אמת';

  @override
  String get changePhoneNumber => 'שנה מספר טלפון';

  @override
  String get otherSignInOptions => 'אפשרויות כניסה אחרות';

  @override
  String get invalidPhone => 'אנא הזן מספר טלפון ישראלי תקין';

  @override
  String get enterSixDigitCode => 'אנא הזן את הקוד בן 6 הספרות';

  @override
  String get tryCenaTitle => 'נסה את Cena — ענה על שאלה אחת';

  @override
  String get noAccountNeeded => 'לא צריך חשבון — פשוט לחץ על תשובה';

  @override
  String get correct => 'נכון!';

  @override
  String get notQuiteHereIsHow => 'לא בדיוק — ככה עושים:';

  @override
  String get cenaAdaptsToYou => 'Cena מתאים את עצמו לרמה שלך עם AI.';

  @override
  String get createAccountContinue => 'צור חשבון והמשך';

  @override
  String get alreadyHaveAccount => 'כבר יש לך חשבון? התחבר';

  @override
  String get selectSubject => 'בחר נושא';

  @override
  String get sessionDuration => 'משך השיעור';

  @override
  String nMinutes(int n) {
    return '$n דקות';
  }

  @override
  String nMinShort(int n) {
    return '$n דק׳';
  }

  @override
  String get sessionDetails => 'פרטי השיעור';

  @override
  String get maxQuestions => 'שאלות מקסימום';

  @override
  String get masteryThreshold => 'סף שליטה';

  @override
  String get studyEnergy => 'אנרגיית למידה';

  @override
  String remaining(int n) {
    return '$n נותרו';
  }

  @override
  String get startLesson => 'התחל שיעור';

  @override
  String get newLesson => 'שיעור חדש';

  @override
  String questionN(int n) {
    return 'שאלה $n';
  }

  @override
  String get endSession => 'סיים';

  @override
  String get endSessionTitle => 'לסיים את השיעור?';

  @override
  String get endSessionBody => 'ההתקדמות שלך תישמר. תוכל להמשיך מאוחר יותר.';

  @override
  String get continueLesson => 'המשך שיעור';

  @override
  String get endLessonConfirm => 'סיים';

  @override
  String get lessonComplete => 'השיעור הסתיים!';

  @override
  String get questions => 'שאלות';

  @override
  String get accuracy => 'דיוק';

  @override
  String get time => 'זמן';

  @override
  String get backToHome => 'חזור לדף הבית';

  @override
  String get hints => 'רמזים';

  @override
  String get spacedRepetition => 'חזרה מרווחת';

  @override
  String get interleaved => 'למידה מעורבת';

  @override
  String get blocked => 'למידה ממוקדת';

  @override
  String get adaptiveDifficulty => 'קושי מותאם';

  @override
  String get socratic => 'שיטה סוקרטית';

  @override
  String get easy => 'קל';

  @override
  String get medium => 'בינוני';

  @override
  String get hard => 'קשה';

  @override
  String get multipleChoice => 'בחירה מרובה';

  @override
  String get freeText => 'תשובה חופשית';

  @override
  String get numeric => 'מספרי';

  @override
  String get proof => 'הוכחה';

  @override
  String get diagram => 'דיאגרמה';

  @override
  String get wellDone => 'כל הכבוד!';

  @override
  String get partiallyCorrect => 'חלקית נכון';

  @override
  String get incorrect => 'לא נכון';

  @override
  String get tapToContinue => 'הקש להמשך';

  @override
  String get workedSolution => 'פתרון מודגם';

  @override
  String get conceptualError => 'שגיאה מושגית';

  @override
  String get proceduralError => 'שגיאה בהליך';

  @override
  String get carelessError => 'שגיאת רשלנות';

  @override
  String get notationError => 'שגיאת סימון';

  @override
  String get incompleteAnswer => 'תשובה חלקית';

  @override
  String get welcomeToCena => 'ברוכים הבאים ל-Cena';

  @override
  String get yourPersonalLearningCoach => 'המאמן האישי שלך ללמידה';

  @override
  String get selectLanguage => 'בחר שפה';

  @override
  String get getStarted => 'התחל';

  @override
  String get selectStudySubjects => 'בחר מקצועות לימוד';

  @override
  String get upTo3Subjects => 'ניתן לבחור עד 3 מקצועות';

  @override
  String get comingSoon => 'בקרוב';

  @override
  String get next => 'המשך';

  @override
  String get back => 'חזרה';

  @override
  String get gradeAndTrack => 'כיתה ורמת בגרות';

  @override
  String get weAdaptToYourLevel => 'נתאים את הלמידה לרמה שלך';

  @override
  String get grade => 'כיתה';

  @override
  String get examLevel => 'רמת בגרות';

  @override
  String get testYourLevel => 'בחן את רמתך';

  @override
  String get diagnosticDesc =>
      'ענה על 5 שאלות קצרות כדי שנוכל להתאים את\nהתוכן לרמה המדויקת שלך.';

  @override
  String get takeTheQuiz => 'קח אותי לחידון';

  @override
  String get skipForNow => 'דלג בינתיים';

  @override
  String questionNOfTotal(int n, int total) {
    return 'שאלה $n מתוך $total';
  }

  @override
  String get skip => 'דלג';

  @override
  String get allSet => 'הכל מוכן!';

  @override
  String get hereSummary => 'הנה סיכום ההגדרות שלך';

  @override
  String get subjectsLabel => 'מקצועות';

  @override
  String get gradeLabel => 'כיתה';

  @override
  String get examLevelLabel => 'רמת בגרות';

  @override
  String get diagnosticLabel => 'אבחון';

  @override
  String get skipped => 'דולג';

  @override
  String nAnswersRecorded(int n) {
    return '$n תשובות נרשמו';
  }

  @override
  String get startLearning => 'התחל ללמוד';

  @override
  String get math => 'מתמטיקה';

  @override
  String get physics => 'פיזיקה';

  @override
  String get chemistry => 'כימיה';

  @override
  String get biology => 'ביולוגיה';

  @override
  String get computerScience => 'מדעי המחשב';

  @override
  String get cs => 'מד״מ';

  @override
  String get tutorName => 'מורה CENA';

  @override
  String get typing => 'מקליד...';

  @override
  String get tutorWelcome => 'שלום! אני המורה הפרטי שלך ב-CENA';

  @override
  String get tutorWelcomeDesc =>
      'שאל אותי כל דבר על מתמטיקה, או לחץ על הצעה למטה כדי להתחיל.';

  @override
  String get askCenaAnything => 'שאל את CENA כל דבר...';

  @override
  String get explainConcept => 'הסבר את המושג';

  @override
  String get simplerExample => 'תן לי דוגמה פשוטה יותר';

  @override
  String get stepByStep => 'הראה לי צעד אחר צעד';

  @override
  String get whatDidIGetWrong => 'מה טעיתי?';

  @override
  String get differentApproach => 'נסה גישה אחרת';

  @override
  String progressToLevel(int level) {
    return 'התקדמות לרמה $level';
  }

  @override
  String xpTotal(int xp) {
    return '$xp XP סה״כ';
  }

  @override
  String xpOfNeeded(int current, int needed) {
    return '$current / $needed XP';
  }

  @override
  String plusXpToday(int xp) {
    return '+$xp היום';
  }

  @override
  String get recentActivity => 'פעילות אחרונה';

  @override
  String get completeSessionForAchievements =>
      'השלם שיעור כדי לראות את ההישגים שלך כאן.';

  @override
  String get justNow => 'עכשיו';

  @override
  String get momentumSuggestion => 'נראה שרצף יומי מוסיף לחץ. לעבור למומנטום?';

  @override
  String get switchToMomentum => 'כן, לעבור למצב מומנטום';

  @override
  String get academicInfo => 'מידע אקדמי';

  @override
  String get streak => 'רצף';

  @override
  String get xp => 'XP';

  @override
  String get level => 'רמה';

  @override
  String get userId => 'מזהה משתמש';

  @override
  String get editDisplayName => 'ערוך שם תצוגה';

  @override
  String get displayName => 'שם תצוגה';

  @override
  String get enterYourName => 'הזן את שמך';

  @override
  String get save => 'שמור';

  @override
  String get appVersion => 'Cena v0.1.0';

  @override
  String nDays(int n) {
    return '$n ימים';
  }

  @override
  String get pageNotFound => 'הדף לא נמצא';

  @override
  String routeNotFound(String uri) {
    return 'הנתיב לא נמצא: $uri';
  }

  @override
  String get goHome => 'חזרה לבית';

  @override
  String get skipQuestion => 'דלג';

  @override
  String get submitAnswer => 'שלח תשובה';

  @override
  String get checking => 'בודק...';

  @override
  String get unit => 'יחידה';

  @override
  String get noUnit => 'ללא';

  @override
  String get writeProofHere => 'כתוב את הוכחתך כאן...';

  @override
  String get writeAnswerHere => 'כתוב את תשובתך כאן...';

  @override
  String get skipQuestionTitle => 'דלג על שאלה?';

  @override
  String get skipQuestionBody =>
      'דילוג על שאלה לא ייחשב כטעות, אך גם לא יתרום לשיפור השליטה שלך.';

  @override
  String get tapToReveal => 'לחץ לחשיפה';

  @override
  String get dragHere => 'גרור לכאן';

  @override
  String insightAt(String x, String y) {
    return 'ערך ב-$x: $y';
  }

  @override
  String get resetDiagram => 'איפוס';

  @override
  String get correctPlacement => 'נכון!';

  @override
  String get tryAgain => 'נסה שוב';

  @override
  String get zoomIn => 'הגדל כדי לראות פרטים';

  @override
  String get iUnderstand => 'הבנתי, בואו נתרגל';

  @override
  String get challenges => 'אתגרים';

  @override
  String get allTopics => 'כל הנושאים';

  @override
  String nOfTotal(int n, int total) {
    return '$n מתוך $total הושלמו';
  }

  @override
  String get completePreviousFirst => 'השלם קודם את הכרטיסים הקודמים';

  @override
  String get locked => 'נעול';

  @override
  String get completed => 'הושלם';
}
