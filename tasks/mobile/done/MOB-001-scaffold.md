# MOB-001: Flutter Project Scaffold & Configuration

**Priority:** P0 — all other tasks depend on this
**Blocked by:** None (entry point)
**Estimated effort:** 2 days
**Contract:** `contracts/mobile/pubspec.yaml`, `contracts/mobile/lib/core/config/app_config.dart`

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule. `throw UnimplementedError`, `// TODO: implement`, empty bodies, and mock returns are FORBIDDEN in source code. If you cannot implement it fully, file a blocking dependency instead.

## Context
Cena is a Flutter 3.24+ app targeting iOS, Android, and web. The project must be set up with the exact dependency versions from the contract `pubspec.yaml`, configured for three locales (Hebrew primary, Arabic, English fallback), with both Hebrew and Arabic as RTL. The font stack includes Heebo (Hebrew), Noto Sans Arabic (Arabic), Inter (Latin), and JetBrains Mono (code/math). The project uses Riverpod for state, drift for offline SQLite, and Hive for key-value storage. All design tokens (colors, spacing, typography, animation durations) live in `app_config.dart`.

## Subtasks

### MOB-001.1: Project Initialization & Dependency Setup
**Files:**
- `pubspec.yaml`
- `lib/main.dart`
- `lib/app.dart`
- `analysis_options.yaml`

**Acceptance:**
- [ ] `flutter create --org education.cena` produces a compilable project
- [ ] All dependencies from contract `pubspec.yaml` added with exact version constraints: `flutter_riverpod: ^2.6.1`, `riverpod_annotation: ^2.6.1`, `web_socket_channel: ^3.0.1`, `dio: ^5.7.0`, `graphql_flutter: ^5.2.0-beta.7`, `connectivity_plus: ^6.1.0`, `drift: ^2.22.1`, `sqlite3_flutter_libs: ^0.5.28`, `hive_ce: ^2.7.0`, `hive_ce_flutter: ^2.2.0`, `flutter_secure_storage: ^9.2.3`, `freezed_annotation: ^2.4.6`, `json_annotation: ^4.9.0`, `flutter_animate: ^4.5.0`, `rive: ^0.13.17`, `fl_chart: ^0.69.2`, `flutter_svg: ^2.0.16`, `cached_network_image: ^3.4.1`, `shimmer: ^3.0.0`, `flutter_math_fork: ^0.7.2`, `firebase_core: ^3.8.1`, `firebase_auth: ^5.3.4`, `firebase_messaging: ^15.1.6`, `flutter_local_notifications: ^18.0.1`, `intl: ^0.19.0`, `uuid: ^4.5.1`, `crypto: ^3.0.6`, `collection: ^1.19.0`, `equatable: ^2.0.7`, `logger: ^2.5.0`, `package_info_plus: ^8.1.2`, `url_launcher: ^6.3.1`
- [ ] All dev dependencies: `build_runner: ^2.4.13`, `freezed: ^2.5.7`, `json_serializable: ^6.8.0`, `riverpod_generator: ^2.6.2`, `drift_dev: ^2.22.1`, `hive_ce_generator: ^1.7.0`, `mocktail: ^1.0.4`, `patrol: ^3.13.2`, `golden_toolkit: ^0.15.0`, `fake_async: ^1.3.2`, `flutter_lints: ^5.0.0`, `custom_lint: ^0.7.0`, `riverpod_lint: ^2.6.2`
- [ ] `flutter pub get` succeeds with zero errors
- [ ] `analysis_options.yaml` includes `flutter_lints`, `riverpod_lint`, and `custom_lint` configurations
- [ ] `lib/main.dart` bootstraps a `ProviderScope` wrapping `CenaApp`
- [ ] `lib/app.dart` defines `CenaApp` as a `MaterialApp.router` with locale and theme setup
- [ ] Environment detection via `--dart-define=ENV=dev|staging|prod` feeds `AppConfig.forEnvironment()`
- [ ] SDK constraint: `>=3.5.0 <4.0.0`, Flutter constraint: `>=3.24.0`

**Test:**
```dart
test('AppConfig resolves correct endpoints per environment', () {
  final devConfig = AppConfig.forEnvironment(Environment.dev);
  expect(devConfig.endpoints.webSocketUrl, contains('dev-api.cena.education'));
  expect(devConfig.featureFlags.debugOverlayEnabled, isTrue);

  final prodConfig = AppConfig.forEnvironment(Environment.prod);
  expect(prodConfig.endpoints.webSocketUrl, contains('api.cena.education'));
  expect(prodConfig.featureFlags.debugOverlayEnabled, isFalse);
  expect(prodConfig.featureFlags.leaderboardEnabled, isFalse);
});

test('CenaApp builds without errors', () {
  // Verify the widget tree builds
  testWidgets('CenaApp renders', (tester) async {
    await tester.pumpWidget(
      ProviderScope(
        child: CenaApp(config: AppConfig.forEnvironment(Environment.dev)),
      ),
    );
    expect(find.byType(MaterialApp), findsOneWidget);
  });
});
```

**Edge Cases:**
- `flutter pub get` fails on a dependency conflict — pin to exact versions and document resolution
- Firebase initialization fails without `google-services.json` — gate Firebase init behind `try/catch` and log warning in dev mode

---

### MOB-001.2: Font Registration & RTL Configuration
**Files:**
- `pubspec.yaml` (flutter.fonts section)
- `lib/core/config/app_config.dart` (TypographyTokens, AppLocales)
- `lib/core/theme/cena_theme.dart`
- `assets/fonts/` (font file stubs for CI — real files downloaded separately)

**Acceptance:**
- [ ] Four font families registered in `pubspec.yaml`: Heebo (400, 500, 700, 800), Noto Sans Arabic (400, 500, 700, 800), Inter (400, 500, 600, 700), JetBrains Mono (400, 500)
- [ ] `TypographyTokens.fontFamilyForLocale('he')` returns `'Heebo'`
- [ ] `TypographyTokens.fontFamilyForLocale('ar')` returns `'Noto Sans Arabic'`
- [ ] `TypographyTokens.fontFamilyForLocale('en')` returns `'Inter'`
- [ ] `TypographyTokens.monoFontFamily` equals `'JetBrains Mono'`
- [ ] `CenaTheme` class produces a `ThemeData` with locale-aware `TextTheme`: each text style uses the correct font family based on current locale
- [ ] `CenaTheme` sets `textDirection: TextDirection.rtl` for Hebrew and Arabic locales via `AppLocales.isRtl()`
- [ ] `SubjectColorTokens` provides primary, secondary, and background colors for all 5 subjects (math, physics, chemistry, biology, cs) matching contract hex values
- [ ] `SpacingTokens`, `RadiusTokens`, `AnimationTokens` all match contract values exactly
- [ ] Assets declared: `assets/images/`, `assets/icons/`, `assets/animations/`, `assets/fonts/`

**Test:**
```dart
test('TypographyTokens returns correct font per locale', () {
  expect(TypographyTokens.fontFamilyForLocale('he'), equals('Heebo'));
  expect(TypographyTokens.fontFamilyForLocale('ar'), equals('Noto Sans Arabic'));
  expect(TypographyTokens.fontFamilyForLocale('en'), equals('Inter'));
  expect(TypographyTokens.fontFamilyForLocale('fr'), equals('Inter')); // fallback
});

test('AppLocales.isRtl identifies RTL locales', () {
  expect(AppLocales.isRtl(const Locale('he', 'IL')), isTrue);
  expect(AppLocales.isRtl(const Locale('ar')), isTrue);
  expect(AppLocales.isRtl(const Locale('en', 'US')), isFalse);
});

test('SubjectColorTokens match contract hex values', () {
  expect(SubjectColorTokens.mathPrimary.value, equals(0xFF0097A7));
  expect(SubjectColorTokens.physicsPrimary.value, equals(0xFFFF8F00));
  expect(SubjectColorTokens.chemistryPrimary.value, equals(0xFF388E3C));
  expect(SubjectColorTokens.biologyPrimary.value, equals(0xFF7B1FA2));
  expect(SubjectColorTokens.csPrimary.value, equals(0xFF616161));
});

testWidgets('CenaTheme applies RTL for Hebrew locale', (tester) async {
  await tester.pumpWidget(
    MaterialApp(
      locale: const Locale('he', 'IL'),
      theme: CenaTheme.light(const Locale('he', 'IL')),
      home: const Directionality(
        textDirection: TextDirection.rtl,
        child: Text('test'),
      ),
    ),
  );
  final directionality = tester.widget<Directionality>(
    find.byType(Directionality).first,
  );
  expect(directionality.textDirection, equals(TextDirection.rtl));
});
```

**Edge Cases:**
- Font file missing at runtime — register fallback font chain: Heebo -> Noto Sans Arabic -> Inter -> system default
- Arabic diacritics rendering incorrectly — verify Noto Sans Arabic renders tashkeel marks in golden tests

---

### MOB-001.3: Folder Structure & Code Generation Pipeline
**Files:**
- `lib/core/config/` (app_config.dart)
- `lib/core/models/` (placeholder)
- `lib/core/services/` (placeholder)
- `lib/core/state/` (placeholder)
- `lib/core/theme/` (cena_theme.dart)
- `lib/features/session/` (placeholder)
- `lib/features/knowledge_graph/` (placeholder)
- `lib/features/gamification/` (placeholder)
- `lib/l10n/` (placeholder for ARB files)
- `build.yaml`

**Acceptance:**
- [ ] Folder structure created per DDD bounded context: `core/` for shared infrastructure, `features/` for each feature module
- [ ] `build.yaml` configures `build_runner` with `freezed`, `json_serializable`, `riverpod_generator`, `drift_dev`, `hive_ce_generator`
- [ ] `dart run build_runner build --delete-conflicting-outputs` runs without errors (once models exist)
- [ ] `.gitignore` excludes `*.freezed.dart`, `*.g.dart`, `*.mocks.dart` from version control (they are generated)
- [ ] `flutter: generate: true` in `pubspec.yaml` enables `flutter_gen` for l10n
- [ ] `l10n.yaml` file configures ARB directory as `lib/l10n`, template file as `app_he.arb`, output as `app_localizations.dart`
- [ ] `FeatureFlags` class available with all contract flags: `gamificationIntensity`, `offlineModeEnabled`, `knowledgeGraphEnabled`, `leaderboardEnabled`, `cognitiveLoadBreaksEnabled`, `streakNotificationsEnabled`, `llmHintsEnabled`, `diagramQuestionsEnabled`, `proofBuilderEnabled`, `cohortOverride`, `debugOverlayEnabled`
- [ ] `SessionDefaults` class with all contract constants: `minDurationMinutes=12`, `maxDurationMinutes=30`, `defaultDurationMinutes=25`, `fatigueBreakThreshold=0.7`, `defaultBreakMinutes=5`, `maxQuestionsPerSession=40`, `masteryThreshold=0.85`
- [ ] `LlmBudget` class with `dailyCap=50`, `label='Study Energy'`, `labelHe='אנרגיית למידה'`, `lowEnergyThreshold=10`, `criticalEnergyThreshold=3`

**Test:**
```dart
test('FeatureFlags.dev enables debug overlay and all question types', () {
  const flags = FeatureFlags.dev;
  expect(flags.debugOverlayEnabled, isTrue);
  expect(flags.diagramQuestionsEnabled, isTrue);
  expect(flags.proofBuilderEnabled, isTrue);
  expect(flags.leaderboardEnabled, isTrue);
});

test('FeatureFlags.prod is conservative', () {
  const flags = FeatureFlags.prod;
  expect(flags.debugOverlayEnabled, isFalse);
  expect(flags.diagramQuestionsEnabled, isFalse);
  expect(flags.leaderboardEnabled, isFalse);
  expect(flags.offlineModeEnabled, isTrue);
});

test('SessionDefaults match contract values', () {
  expect(SessionDefaults.masteryThreshold, equals(0.85));
  expect(SessionDefaults.fatigueBreakThreshold, equals(0.7));
  expect(SessionDefaults.defaultDurationMinutes, equals(25));
  expect(SessionDefaults.maxQuestionsPerSession, equals(40));
});

test('LlmBudget has correct caps and labels', () {
  expect(LlmBudget.dailyCap, equals(50));
  expect(LlmBudget.labelHe, equals('אנרגיית למידה'));
  expect(LlmBudget.lowEnergyThreshold, equals(10));
});

test('GamificationIntensity has 3 levels', () {
  expect(GamificationIntensity.values.length, equals(3));
  expect(GamificationIntensity.values, contains(GamificationIntensity.minimal));
  expect(GamificationIntensity.values, contains(GamificationIntensity.standard));
  expect(GamificationIntensity.values, contains(GamificationIntensity.full));
});
```

**Edge Cases:**
- `build_runner` conflicts between freezed and json_serializable — use `build.yaml` to set correct builder order
- Generated file size exceeds lint threshold — configure `analysis_options.yaml` to exclude generated files from analysis

---

## Integration Test

```dart
void main() {
  group('MOB-001 Integration: Project scaffold is complete and functional', () {
    test('flutter pub get resolves all dependencies', () async {
      final result = await Process.run('flutter', ['pub', 'get']);
      expect(result.exitCode, equals(0));
    });

    test('build_runner generates without errors', () async {
      final result = await Process.run(
        'dart',
        ['run', 'build_runner', 'build', '--delete-conflicting-outputs'],
      );
      expect(result.exitCode, equals(0));
    });

    test('flutter analyze passes', () async {
      final result = await Process.run('flutter', ['analyze']);
      expect(result.exitCode, equals(0));
    });

    testWidgets('app boots in dev mode with ProviderScope', (tester) async {
      final config = AppConfig.forEnvironment(Environment.dev);
      await tester.pumpWidget(
        ProviderScope(
          child: CenaApp(config: config),
        ),
      );
      await tester.pumpAndSettle();
      expect(find.byType(MaterialApp), findsOneWidget);
    });

    test('all design tokens are non-null and within bounds', () {
      expect(SpacingTokens.sm, equals(8.0));
      expect(SpacingTokens.md, equals(16.0));
      expect(RadiusTokens.md, equals(8.0));
      expect(AnimationTokens.fast.inMilliseconds, equals(150));
      expect(AnimationTokens.celebration.inMilliseconds, equals(1000));
    });
  });
}
```

## Rollback Criteria
- If Flutter 3.24 has breaking changes with a critical dependency: pin Flutter to latest 3.22.x and document the version cap
- If `graphql_flutter: ^5.2.0-beta.7` is too unstable: replace with `graphql: ^5.2.0` (non-Flutter package) + manual widget integration
- If `rive: ^0.13.17` causes build failures on web: conditionally import Rive only on mobile platforms

## Definition of Done
- [ ] All 3 subtasks pass their individual tests
- [ ] `flutter pub get` succeeds cleanly
- [ ] `flutter analyze` reports zero issues
- [ ] `flutter test` passes all scaffold tests
- [ ] `dart run build_runner build` completes without errors
- [ ] App launches on iOS simulator and Android emulator showing a blank MaterialApp shell
- [ ] Hebrew RTL layout verified visually on both platforms
- [ ] Arabic RTL layout verified with Noto Sans Arabic font rendering
- [ ] PR reviewed by mobile lead
