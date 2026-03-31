// ignore: unused_import
import 'package:intl/intl.dart' as intl;
import 'app_localizations.dart';

// ignore_for_file: type=lint

/// The translations for Arabic (`ar`).
class AppLocalizationsAr extends AppLocalizations {
  AppLocalizationsAr([String locale = 'ar']) : super(locale);

  @override
  String get appTitle => 'سينا';

  @override
  String get goodMorning => 'صباح الخير';

  @override
  String get goodAfternoon => 'مساء الخير';

  @override
  String get goodEvening => 'مساء الخير';

  @override
  String get readyToLearn => 'مستعد للتعلم؟';

  @override
  String get tabHome => 'الرئيسية';

  @override
  String get tabLearn => 'تعلم';

  @override
  String get tabMap => 'خريطة';

  @override
  String get tabProgress => 'التقدم';

  @override
  String get tabProfile => 'الملف';

  @override
  String get quickPractice => 'تمرين سريع';

  @override
  String get start => 'ابدأ';

  @override
  String get continueLearning => 'تابع التعلم';

  @override
  String get pickUpWhereYouLeftOff => 'أكمل من حيث توقفت';

  @override
  String get badges => 'شارات';

  @override
  String get completeSessions => 'أكمل الجلسات للحصول على شارات';

  @override
  String get aiTutor => 'معلم AI';

  @override
  String get chat => 'محادثة';

  @override
  String get subjects => 'المواد';

  @override
  String levelN(int level) {
    return 'المستوى $level';
  }

  @override
  String percentToNext(int percent) {
    return '$percent% للمستوى التالي';
  }

  @override
  String dayStreak(int days) {
    return 'سلسلة $days أيام';
  }

  @override
  String get newLabel => 'جديد';

  @override
  String get noSessionsYet => 'لا توجد جلسات بعد';

  @override
  String get startFirstSession => 'ابدأ أول جلسة تعلم لرؤية سجلك هنا.';

  @override
  String get startSession => 'ابدأ الجلسة';

  @override
  String get startNewSession => 'ابدأ جلسة جديدة';

  @override
  String get practiceSession => 'جلسة تمرين';

  @override
  String accuracyStats(int accuracy, int questions, int minutes) {
    return '$accuracy% دقة · $questions أسئلة · $minutes دق';
  }

  @override
  String minutesAgo(int minutes) {
    return 'قبل $minutes دق';
  }

  @override
  String hoursAgo(int hours) {
    return 'قبل $hours ساعات';
  }

  @override
  String get yesterday => 'أمس';

  @override
  String daysAgo(int days) {
    return 'قبل $days أيام';
  }

  @override
  String get knowledgeMapBuilding => 'خريطة المعرفة قيد البناء';

  @override
  String get knowledgeMapBuildingDesc =>
      'كلما حللت المزيد من الأسئلة، سترى روابط بين المواضيع هنا.';

  @override
  String get knowledgeMap => 'خريطة المعرفة';

  @override
  String get knowledgeGraphPlaceholder =>
      'ستظهر خريطة المفاهيم التفاعلية هنا مع تقدمك في جلسات التعلم.';

  @override
  String get allSubjects => 'جميع المواد';

  @override
  String get language => 'اللغة';

  @override
  String get learningStyle => 'أسلوب التعلم';

  @override
  String get useMomentumMeter => 'استخدم مقياس الزخم';

  @override
  String get momentumDesc => 'تقدم 7 أيام (بدون ضغط إعادة تعيين السلسلة)';

  @override
  String get streakDesc => 'وضع السلسلة اليومية';

  @override
  String get feedback => 'التعليقات';

  @override
  String get haptics => 'الاهتزاز';

  @override
  String get hapticsDesc => 'ملاحظات اهتزازية عند اللمس والنجاح';

  @override
  String get soundEffects => 'المؤثرات الصوتية';

  @override
  String get soundsOffByDefault => 'مغلق افتراضياً';

  @override
  String get account => 'الحساب';

  @override
  String get profile => 'الملف الشخصي';

  @override
  String get signOut => 'تسجيل الخروج';

  @override
  String get signOutConfirm => 'هل أنت متأكد أنك تريد تسجيل الخروج؟';

  @override
  String get cancel => 'إلغاء';

  @override
  String get newStreakUnlocked => 'جديد: تم فتح سلسلة التعلم!';

  @override
  String get newStudyGroups => 'جديد: مجموعات الدراسة متاحة لك';

  @override
  String get yourPersonalMentor => 'مدربك الشخصي للتعلم بالذكاء الاصطناعي';

  @override
  String get continueWithGoogle => 'تابع مع Google';

  @override
  String get continueWithApple => 'تابع مع Apple';

  @override
  String get or => 'أو';

  @override
  String get continueWithPhone => 'تابع بالهاتف';

  @override
  String get phoneNumber => 'رقم الهاتف';

  @override
  String get phoneHint => '05X-XXX-XXXX';

  @override
  String get sendCode => 'إرسال الرمز';

  @override
  String enterCodeSent(String phone) {
    return 'أدخل الرمز المكون من 6 أرقام المرسل إلى $phone';
  }

  @override
  String get verificationCode => 'رمز التحقق';

  @override
  String get verify => 'تحقق';

  @override
  String get changePhoneNumber => 'تغيير رقم الهاتف';

  @override
  String get otherSignInOptions => 'خيارات تسجيل دخول أخرى';

  @override
  String get invalidPhone => 'يرجى إدخال رقم هاتف إسرائيلي صالح';

  @override
  String get enterSixDigitCode => 'يرجى إدخال الرمز المكون من 6 أرقام';

  @override
  String get tryCenaTitle => 'جرب Cena — أجب على سؤال واحد';

  @override
  String get noAccountNeeded => 'لا حاجة لحساب — فقط اضغط على إجابة';

  @override
  String get correct => 'صحيح!';

  @override
  String get notQuiteHereIsHow => 'ليس تماماً — إليك الطريقة:';

  @override
  String get cenaAdaptsToYou => 'Cena يتكيف مع مستواك بالذكاء الاصطناعي.';

  @override
  String get createAccountContinue => 'أنشئ حساباً وتابع';

  @override
  String get alreadyHaveAccount => 'لديك حساب بالفعل؟ سجل الدخول';

  @override
  String get selectSubject => 'اختر الموضوع';

  @override
  String get sessionDuration => 'مدة الجلسة';

  @override
  String nMinutes(int n) {
    return '$n دقائق';
  }

  @override
  String nMinShort(int n) {
    return '$n دق';
  }

  @override
  String get sessionDetails => 'تفاصيل الجلسة';

  @override
  String get maxQuestions => 'الحد الأقصى للأسئلة';

  @override
  String get masteryThreshold => 'عتبة الإتقان';

  @override
  String get studyEnergy => 'طاقة الدراسة';

  @override
  String remaining(int n) {
    return '$n متبقية';
  }

  @override
  String get startLesson => 'ابدأ الدرس';

  @override
  String get newLesson => 'درس جديد';

  @override
  String questionN(int n) {
    return 'سؤال $n';
  }

  @override
  String get endSession => 'إنهاء';

  @override
  String get endSessionTitle => 'إنهاء الدرس؟';

  @override
  String get endSessionBody => 'سيتم حفظ تقدمك. يمكنك المتابعة لاحقاً.';

  @override
  String get continueLesson => 'تابع الدرس';

  @override
  String get endLessonConfirm => 'إنهاء';

  @override
  String get lessonComplete => 'اكتمل الدرس!';

  @override
  String get questions => 'الأسئلة';

  @override
  String get accuracy => 'الدقة';

  @override
  String get time => 'الوقت';

  @override
  String get backToHome => 'العودة للرئيسية';

  @override
  String get hints => 'تلميحات';

  @override
  String get spacedRepetition => 'التكرار المتباعد';

  @override
  String get interleaved => 'التعلم المتداخل';

  @override
  String get blocked => 'التعلم المركز';

  @override
  String get adaptiveDifficulty => 'الصعوبة التكيفية';

  @override
  String get socratic => 'الطريقة السقراطية';

  @override
  String get easy => 'سهل';

  @override
  String get medium => 'متوسط';

  @override
  String get hard => 'صعب';

  @override
  String get multipleChoice => 'اختيار متعدد';

  @override
  String get freeText => 'نص حر';

  @override
  String get numeric => 'رقمي';

  @override
  String get proof => 'إثبات';

  @override
  String get diagram => 'رسم بياني';

  @override
  String get wellDone => 'أحسنت!';

  @override
  String get partiallyCorrect => 'صحيح جزئياً';

  @override
  String get incorrect => 'غير صحيح';

  @override
  String get tapToContinue => 'اضغط للمتابعة';

  @override
  String get workedSolution => 'الحل التفصيلي';

  @override
  String get conceptualError => 'خطأ مفاهيمي';

  @override
  String get proceduralError => 'خطأ إجرائي';

  @override
  String get carelessError => 'خطأ بسبب عدم الانتباه';

  @override
  String get notationError => 'خطأ في الترميز';

  @override
  String get incompleteAnswer => 'إجابة غير كاملة';

  @override
  String get welcomeToCena => 'مرحباً بك في Cena';

  @override
  String get yourPersonalLearningCoach => 'مدربك الشخصي للتعلم';

  @override
  String get selectLanguage => 'اختر اللغة';

  @override
  String get getStarted => 'ابدأ';

  @override
  String get selectStudySubjects => 'اختر مواد الدراسة';

  @override
  String get upTo3Subjects => 'يمكنك اختيار حتى 3 مواد';

  @override
  String get comingSoon => 'قريباً';

  @override
  String get next => 'التالي';

  @override
  String get back => 'رجوع';

  @override
  String get gradeAndTrack => 'الصف والمسار';

  @override
  String get weAdaptToYourLevel => 'سنكيّف التعلم حسب مستواك';

  @override
  String get grade => 'الصف';

  @override
  String get examLevel => 'مستوى الامتحان';

  @override
  String get testYourLevel => 'اختبر مستواك';

  @override
  String get diagnosticDesc =>
      'أجب على 5 أسئلة قصيرة حتى نتمكن من مطابقة\nالمحتوى مع مستواك الدقيق.';

  @override
  String get takeTheQuiz => 'خذني للاختبار';

  @override
  String get skipForNow => 'تخطي الآن';

  @override
  String questionNOfTotal(int n, int total) {
    return 'سؤال $n من $total';
  }

  @override
  String get skip => 'تخطي';

  @override
  String get allSet => 'كل شيء جاهز!';

  @override
  String get hereSummary => 'إليك ملخص إعداداتك';

  @override
  String get subjectsLabel => 'المواد';

  @override
  String get gradeLabel => 'الصف';

  @override
  String get examLevelLabel => 'مستوى الامتحان';

  @override
  String get diagnosticLabel => 'التشخيص';

  @override
  String get skipped => 'تم التخطي';

  @override
  String nAnswersRecorded(int n) {
    return 'تم تسجيل $n إجابات';
  }

  @override
  String get startLearning => 'ابدأ التعلم';

  @override
  String get math => 'الرياضيات';

  @override
  String get physics => 'الفيزياء';

  @override
  String get chemistry => 'الكيمياء';

  @override
  String get biology => 'الأحياء';

  @override
  String get computerScience => 'علوم الحاسوب';

  @override
  String get cs => 'حاسوب';

  @override
  String get tutorName => 'معلم CENA';

  @override
  String get typing => 'يكتب...';

  @override
  String get tutorWelcome => 'مرحباً! أنا معلمك الخاص في CENA';

  @override
  String get tutorWelcomeDesc =>
      'اسألني أي شيء عن الرياضيات، أو اضغط على اقتراح أدناه للبدء.';

  @override
  String get askCenaAnything => 'اسأل CENA أي شيء...';

  @override
  String get explainConcept => 'اشرح هذا المفهوم';

  @override
  String get simplerExample => 'أعطني مثالاً أبسط';

  @override
  String get stepByStep => 'أرني خطوة بخطوة';

  @override
  String get whatDidIGetWrong => 'ما الذي أخطأت فيه؟';

  @override
  String get differentApproach => 'جرب نهجاً مختلفاً';

  @override
  String progressToLevel(int level) {
    return 'التقدم للمستوى $level';
  }

  @override
  String xpTotal(int xp) {
    return '$xp XP إجمالي';
  }

  @override
  String xpOfNeeded(int current, int needed) {
    return '$current / $needed XP';
  }

  @override
  String plusXpToday(int xp) {
    return '+$xp اليوم';
  }

  @override
  String get recentActivity => 'النشاط الأخير';

  @override
  String get completeSessionForAchievements => 'أكمل جلسة لرؤية إنجازاتك هنا.';

  @override
  String get justNow => 'الآن';

  @override
  String get momentumSuggestion =>
      'يبدو أن السلاسل اليومية تضيف ضغطاً. التبديل للزخم؟';

  @override
  String get switchToMomentum => 'نعم، التبديل لوضع الزخم';

  @override
  String get academicInfo => 'المعلومات الأكاديمية';

  @override
  String get streak => 'السلسلة';

  @override
  String get xp => 'XP';

  @override
  String get level => 'المستوى';

  @override
  String get userId => 'معرف المستخدم';

  @override
  String get editDisplayName => 'تعديل اسم العرض';

  @override
  String get displayName => 'اسم العرض';

  @override
  String get enterYourName => 'أدخل اسمك';

  @override
  String get save => 'حفظ';

  @override
  String get appVersion => 'Cena v0.1.0';

  @override
  String nDays(int n) {
    return '$n أيام';
  }

  @override
  String get pageNotFound => 'الصفحة غير موجودة';

  @override
  String routeNotFound(String uri) {
    return 'المسار غير موجود: $uri';
  }

  @override
  String get goHome => 'العودة للرئيسية';

  @override
  String get skipQuestion => 'تخطي';

  @override
  String get submitAnswer => 'إرسال الإجابة';

  @override
  String get checking => 'جاري التحقق...';

  @override
  String get unit => 'الوحدة';

  @override
  String get noUnit => 'بدون';

  @override
  String get writeProofHere => 'اكتب إثباتك هنا...';

  @override
  String get writeAnswerHere => 'اكتب إجابتك هنا...';

  @override
  String get skipQuestionTitle => 'تخطي هذا السؤال؟';

  @override
  String get skipQuestionBody =>
      'التخطي لن يُحتسب كخطأ، لكنه لن يحسّن إتقانك أيضاً.';

  @override
  String get tapToReveal => 'اضغط للكشف';

  @override
  String get dragHere => 'اسحب هنا';

  @override
  String insightAt(String x, String y) {
    return 'القيمة عند $x: $y';
  }

  @override
  String get resetDiagram => 'إعادة تعيين';

  @override
  String get correctPlacement => 'صحيح!';

  @override
  String get tryAgain => 'حاول مرة أخرى';

  @override
  String get zoomIn => 'كبّر لرؤية التفاصيل';

  @override
  String get iUnderstand => 'فهمت، هيا نتدرب';

  @override
  String get challenges => 'التحديات';

  @override
  String get allTopics => 'جميع المواضيع';

  @override
  String nOfTotal(int n, int total) {
    return '$n من $total مكتملة';
  }

  @override
  String get completePreviousFirst => 'أكمل البطاقات السابقة أولاً';

  @override
  String get locked => 'مقفل';

  @override
  String get completed => 'مكتمل';
}
