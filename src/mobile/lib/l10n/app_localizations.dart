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
    Locale('he'),
    Locale('ar'),
    Locale('en')
  ];

  /// The application title
  ///
  /// In he, this message translates to:
  /// **'סנה'**
  String get appTitle;

  /// Generic greeting
  ///
  /// In he, this message translates to:
  /// **'שלום'**
  String get greeting;

  /// Morning greeting
  ///
  /// In he, this message translates to:
  /// **'בוקר טוב'**
  String get goodMorning;

  /// Afternoon greeting
  ///
  /// In he, this message translates to:
  /// **'צהריים טובים'**
  String get goodAfternoon;

  /// Evening greeting
  ///
  /// In he, this message translates to:
  /// **'ערב טוב'**
  String get goodEvening;

  /// Start session button label
  ///
  /// In he, this message translates to:
  /// **'התחל שיעור'**
  String get startSession;

  /// Home tab label
  ///
  /// In he, this message translates to:
  /// **'בית'**
  String get home;

  /// Sessions tab label
  ///
  /// In he, this message translates to:
  /// **'שיעורים'**
  String get sessions;

  /// Progress tab label
  ///
  /// In he, this message translates to:
  /// **'התקדמות'**
  String get progress;

  /// Settings tab label
  ///
  /// In he, this message translates to:
  /// **'הגדרות'**
  String get settings;

  /// Login button label
  ///
  /// In he, this message translates to:
  /// **'כניסה'**
  String get login;

  /// Phone number input label
  ///
  /// In he, this message translates to:
  /// **'מספר טלפון'**
  String get phoneNumber;

  /// Send verification code button
  ///
  /// In he, this message translates to:
  /// **'שלח קוד'**
  String get sendCode;

  /// Verification code input label
  ///
  /// In he, this message translates to:
  /// **'קוד אימות'**
  String get verificationCode;

  /// Verify button label
  ///
  /// In he, this message translates to:
  /// **'אמת'**
  String get verify;

  /// LLM budget label shown to students
  ///
  /// In he, this message translates to:
  /// **'אנרגיית למידה'**
  String get studyEnergy;

  /// Mathematics subject name
  ///
  /// In he, this message translates to:
  /// **'מתמטיקה'**
  String get math;

  /// Physics subject name
  ///
  /// In he, this message translates to:
  /// **'פיזיקה'**
  String get physics;

  /// Chemistry subject name
  ///
  /// In he, this message translates to:
  /// **'כימיה'**
  String get chemistry;

  /// Biology subject name
  ///
  /// In he, this message translates to:
  /// **'ביולוגיה'**
  String get biology;

  /// Computer Science subject name
  ///
  /// In he, this message translates to:
  /// **'מדעי המחשב'**
  String get computerScience;
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
