// =============================================================================
// Cena Adaptive Learning Platform — AppConfig Unit Tests
// =============================================================================

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:cena/core/config/app_config.dart';

void main() {
  group('AppConfig', () {
    test('resolves correct endpoints per environment', () {
      final devConfig = AppConfig.forEnvironment(Environment.dev);
      expect(
        devConfig.endpoints.webSocketUrl,
        contains('dev-api.cena.education'),
      );
      expect(devConfig.featureFlags.debugOverlayEnabled, isTrue);

      final prodConfig = AppConfig.forEnvironment(Environment.prod);
      expect(
        prodConfig.endpoints.webSocketUrl,
        contains('api.cena.education'),
      );
      expect(prodConfig.featureFlags.debugOverlayEnabled, isFalse);
      expect(prodConfig.featureFlags.leaderboardEnabled, isFalse);
    });

    test('staging config uses staging endpoints', () {
      final config = AppConfig.forEnvironment(Environment.staging);
      expect(
        config.endpoints.restBaseUrl,
        contains('staging-api.cena.education'),
      );
      expect(config.featureFlags.diagramQuestionsEnabled, isTrue);
      expect(config.featureFlags.debugOverlayEnabled, isFalse);
    });

    test('resolveEnvironment handles all valid strings', () {
      expect(
        AppConfig.resolveEnvironment('dev'),
        equals(Environment.dev),
      );
      expect(
        AppConfig.resolveEnvironment('development'),
        equals(Environment.dev),
      );
      expect(
        AppConfig.resolveEnvironment('staging'),
        equals(Environment.staging),
      );
      expect(
        AppConfig.resolveEnvironment('prod'),
        equals(Environment.prod),
      );
      expect(
        AppConfig.resolveEnvironment('production'),
        equals(Environment.prod),
      );
    });

    test('resolveEnvironment defaults to dev for unknown values', () {
      expect(
        AppConfig.resolveEnvironment('unknown'),
        equals(Environment.dev),
      );
      expect(
        AppConfig.resolveEnvironment(null),
        equals(Environment.dev),
      );
      expect(
        AppConfig.resolveEnvironment(''),
        equals(Environment.dev),
      );
    });

    test('convenience getters report correct environment', () {
      final devConfig = AppConfig.forEnvironment(Environment.dev);
      expect(devConfig.isDev, isTrue);
      expect(devConfig.isStaging, isFalse);
      expect(devConfig.isProd, isFalse);

      final prodConfig = AppConfig.forEnvironment(Environment.prod);
      expect(prodConfig.isDev, isFalse);
      expect(prodConfig.isProd, isTrue);
    });
  });

  group('FeatureFlags', () {
    test('dev enables debug overlay and all question types', () {
      const flags = FeatureFlags.dev;
      expect(flags.debugOverlayEnabled, isTrue);
      expect(flags.diagramQuestionsEnabled, isTrue);
      expect(flags.proofBuilderEnabled, isTrue);
      expect(flags.leaderboardEnabled, isTrue);
    });

    test('prod is conservative', () {
      const flags = FeatureFlags.prod;
      expect(flags.debugOverlayEnabled, isFalse);
      expect(flags.diagramQuestionsEnabled, isFalse);
      expect(flags.leaderboardEnabled, isFalse);
      expect(flags.offlineModeEnabled, isTrue);
    });

    test('staging enables diagram questions but not debug overlay', () {
      const flags = FeatureFlags.staging;
      expect(flags.diagramQuestionsEnabled, isTrue);
      expect(flags.debugOverlayEnabled, isFalse);
      expect(flags.proofBuilderEnabled, isFalse);
    });
  });

  group('GamificationIntensity', () {
    test('has 3 levels', () {
      expect(GamificationIntensity.values.length, equals(3));
      expect(
        GamificationIntensity.values,
        contains(GamificationIntensity.minimal),
      );
      expect(
        GamificationIntensity.values,
        contains(GamificationIntensity.standard),
      );
      expect(
        GamificationIntensity.values,
        contains(GamificationIntensity.full),
      );
    });
  });

  group('SessionDefaults', () {
    test('match contract values', () {
      expect(SessionDefaults.masteryThreshold, equals(0.85));
      expect(SessionDefaults.fatigueBreakThreshold, equals(0.7));
      expect(SessionDefaults.defaultDurationMinutes, equals(25));
      expect(SessionDefaults.maxQuestionsPerSession, equals(40));
      expect(SessionDefaults.minDurationMinutes, equals(12));
      expect(SessionDefaults.maxDurationMinutes, equals(30));
      expect(SessionDefaults.defaultBreakMinutes, equals(5));
    });
  });

  group('LlmBudget', () {
    test('has correct caps and labels', () {
      expect(LlmBudget.dailyCap, equals(50));
      expect(LlmBudget.labelHe, equals('אנרגיית למידה'));
      expect(LlmBudget.label, equals('Study Energy'));
      expect(LlmBudget.lowEnergyThreshold, equals(10));
      expect(LlmBudget.criticalEnergyThreshold, equals(3));
    });
  });

  group('TypographyTokens', () {
    test('returns correct font per locale', () {
      expect(TypographyTokens.fontFamilyForLocale('he'), equals('Heebo'));
      expect(
        TypographyTokens.fontFamilyForLocale('ar'),
        equals('Noto Sans Arabic'),
      );
      expect(TypographyTokens.fontFamilyForLocale('en'), equals('Inter'));
      expect(
        TypographyTokens.fontFamilyForLocale('fr'),
        equals('Inter'),
      ); // fallback
    });

    test('monoFontFamily is JetBrains Mono', () {
      expect(TypographyTokens.monoFontFamily, equals('JetBrains Mono'));
    });
  });

  group('AppLocales', () {
    test('isRtl identifies RTL locales', () {
      expect(AppLocales.isRtl(const Locale('he', 'IL')), isTrue);
      expect(AppLocales.isRtl(const Locale('ar')), isTrue);
      expect(AppLocales.isRtl(const Locale('en', 'US')), isFalse);
    });

    test('supported locales are in correct order', () {
      expect(AppLocales.supported.length, equals(3));
      expect(AppLocales.supported[0], equals(const Locale('en', 'US')));
      expect(AppLocales.supported[1], equals(const Locale('ar')));
      expect(AppLocales.supported[2], equals(const Locale('he', 'IL')));
    });

    test('primary locale is English', () {
      expect(AppLocales.primary, equals(const Locale('en', 'US')));
    });

    test('primaryDirection is LTR', () {
      expect(AppLocales.primaryDirection, equals(TextDirection.ltr));
    });
  });

  group('SubjectColorTokens', () {
    test('match contract hex values', () {
      expect(SubjectColorTokens.mathPrimary.value, equals(0xFF0097A7));
      expect(SubjectColorTokens.physicsPrimary.value, equals(0xFFFF8F00));
      expect(SubjectColorTokens.chemistryPrimary.value, equals(0xFF388E3C));
      expect(SubjectColorTokens.biologyPrimary.value, equals(0xFF7B1FA2));
      expect(SubjectColorTokens.csPrimary.value, equals(0xFF616161));
    });

    test('secondary and background colors are defined', () {
      expect(SubjectColorTokens.mathSecondary.value, equals(0xFF4DD0E1));
      expect(SubjectColorTokens.mathBackground.value, equals(0xFFE0F7FA));
      expect(SubjectColorTokens.physicsSecondary.value, equals(0xFFFFCA28));
      expect(SubjectColorTokens.physicsBackground.value, equals(0xFFFFF8E1));
    });
  });

  group('SpacingTokens', () {
    test('match contract values', () {
      expect(SpacingTokens.sm, equals(8.0));
      expect(SpacingTokens.md, equals(16.0));
      expect(SpacingTokens.lg, equals(24.0));
      expect(SpacingTokens.xl, equals(32.0));
    });
  });

  group('RadiusTokens', () {
    test('match contract values', () {
      expect(RadiusTokens.sm, equals(4.0));
      expect(RadiusTokens.md, equals(8.0));
      expect(RadiusTokens.lg, equals(12.0));
      expect(RadiusTokens.xl, equals(16.0));
      expect(RadiusTokens.full, equals(999.0));
    });
  });

  group('AnimationTokens', () {
    test('match contract durations', () {
      expect(AnimationTokens.fast.inMilliseconds, equals(150));
      expect(AnimationTokens.normal.inMilliseconds, equals(300));
      expect(AnimationTokens.slow.inMilliseconds, equals(600));
      expect(AnimationTokens.celebration.inMilliseconds, equals(1000));
    });
  });

  group('ApiEndpoints', () {
    test('dev endpoints contain dev-api prefix', () {
      expect(
        ApiEndpoints.dev.webSocketUrl,
        startsWith('wss://dev-api.cena.education'),
      );
      expect(
        ApiEndpoints.dev.restBaseUrl,
        startsWith('https://dev-api.cena.education'),
      );
    });

    test('prod endpoints use api.cena.education', () {
      expect(
        ApiEndpoints.prod.webSocketUrl,
        equals('wss://api.cena.education/hub/cena'),
      );
      expect(
        ApiEndpoints.prod.restBaseUrl,
        equals('https://api.cena.education/api/v1'),
      );
    });

    test('forEnvironment returns matching endpoints', () {
      expect(
        ApiEndpoints.forEnvironment(Environment.dev),
        same(ApiEndpoints.dev),
      );
      expect(
        ApiEndpoints.forEnvironment(Environment.staging),
        same(ApiEndpoints.staging),
      );
      expect(
        ApiEndpoints.forEnvironment(Environment.prod),
        same(ApiEndpoints.prod),
      );
    });
  });
}
