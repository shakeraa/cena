// =============================================================================
// Cena Adaptive Learning Platform — Configuration & Environment
// =============================================================================

import 'package:flutter/material.dart';

import '../models/domain_models.dart';

// ---------------------------------------------------------------------------
// Environment
// ---------------------------------------------------------------------------

/// Deployment environment.
enum Environment {
  dev,
  staging,
  prod,
}

// ---------------------------------------------------------------------------
// API Endpoints
// ---------------------------------------------------------------------------

/// Backend endpoint configuration per environment.
class ApiEndpoints {
  const ApiEndpoints({
    required this.webSocketUrl,
    required this.restBaseUrl,
    required this.graphqlEndpoint,
  });

  /// SignalR WebSocket hub URL.
  final String webSocketUrl;

  /// REST API base URL for auth, sync, and admin operations.
  final String restBaseUrl;

  /// GraphQL endpoint for complex queries (knowledge graph, analytics).
  final String graphqlEndpoint;

  /// Development environment endpoints.
  static const dev = ApiEndpoints(
    webSocketUrl: 'wss://dev-api.cena.education/hub/learning',
    restBaseUrl: 'https://dev-api.cena.education/api/v1',
    graphqlEndpoint: 'https://dev-api.cena.education/graphql',
  );

  /// Staging environment endpoints.
  static const staging = ApiEndpoints(
    webSocketUrl: 'wss://staging-api.cena.education/hub/learning',
    restBaseUrl: 'https://staging-api.cena.education/api/v1',
    graphqlEndpoint: 'https://staging-api.cena.education/graphql',
  );

  /// Production environment endpoints.
  static const prod = ApiEndpoints(
    webSocketUrl: 'wss://api.cena.education/hub/learning',
    restBaseUrl: 'https://api.cena.education/api/v1',
    graphqlEndpoint: 'https://api.cena.education/graphql',
  );

  /// Get endpoints for a specific environment.
  static ApiEndpoints forEnvironment(Environment env) {
    switch (env) {
      case Environment.dev:
        return dev;
      case Environment.staging:
        return staging;
      case Environment.prod:
        return prod;
    }
  }
}

// ---------------------------------------------------------------------------
// Feature Flags
// ---------------------------------------------------------------------------

/// Runtime feature flags for A/B testing and gradual rollout.
class FeatureFlags {
  const FeatureFlags({
    this.gamificationIntensity = GamificationIntensity.standard,
    this.offlineModeEnabled = true,
    this.knowledgeGraphEnabled = true,
    this.leaderboardEnabled = false,
    this.cognitiveLoadBreaksEnabled = true,
    this.streakNotificationsEnabled = true,
    this.llmHintsEnabled = true,
    this.diagramQuestionsEnabled = false,
    this.proofBuilderEnabled = false,
    this.cohortOverride,
    this.debugOverlayEnabled = false,
  });

  /// How aggressively to show gamification elements.
  final GamificationIntensity gamificationIntensity;

  /// Whether offline mode and sync are enabled.
  final bool offlineModeEnabled;

  /// Whether the interactive knowledge graph is shown.
  final bool knowledgeGraphEnabled;

  /// Whether the class leaderboard is available.
  final bool leaderboardEnabled;

  /// Whether cognitive load break suggestions are active.
  final bool cognitiveLoadBreaksEnabled;

  /// Whether push notifications for streak expiry are sent.
  final bool streakNotificationsEnabled;

  /// Whether LLM-generated hints are available.
  final bool llmHintsEnabled;

  /// Whether diagram-type questions are served.
  final bool diagramQuestionsEnabled;

  /// Whether the structured proof builder is available.
  final bool proofBuilderEnabled;

  /// Override the student's experiment cohort (dev/testing only).
  final ExperimentCohort? cohortOverride;

  /// Show debug overlay with frame rate, sync status, etc.
  final bool debugOverlayEnabled;

  /// Default flags for each environment.
  static const dev = FeatureFlags(
    debugOverlayEnabled: true,
    diagramQuestionsEnabled: true,
    proofBuilderEnabled: true,
    leaderboardEnabled: true,
  );

  static const staging = FeatureFlags(
    diagramQuestionsEnabled: true,
  );

  static const prod = FeatureFlags();
}

/// Gamification intensity levels for A/B testing.
enum GamificationIntensity {
  /// Minimal: only streak counter, no XP bar or badges.
  minimal,

  /// Standard: streak, XP bar, daily goal, badges.
  standard,

  /// Full: everything including leaderboard and achievements.
  full,
}

// ---------------------------------------------------------------------------
// Session Defaults
// ---------------------------------------------------------------------------

/// Default configuration for learning sessions.
abstract class SessionDefaults {
  /// Minimum session duration in minutes.
  static const int minDurationMinutes = 12;

  /// Maximum session duration in minutes.
  static const int maxDurationMinutes = 30;

  /// Default session duration in minutes.
  static const int defaultDurationMinutes = 25;

  /// Fatigue threshold that triggers a break suggestion [0.0, 1.0].
  static const double fatigueBreakThreshold = 0.7;

  /// Default break duration in minutes.
  static const int defaultBreakMinutes = 5;

  /// Maximum questions per session before forced break.
  static const int maxQuestionsPerSession = 40;

  /// Mastery threshold for BKT: P(Known) >= this → mastered.
  static const double masteryThreshold = 0.85;
}

// ---------------------------------------------------------------------------
// LLM Interaction Budget
// ---------------------------------------------------------------------------

/// Daily LLM interaction cap configuration.
///
/// Shown to the student as "Study Energy" to make the concept
/// intuitive and gamified rather than technical.
abstract class LlmBudget {
  /// Maximum LLM interactions per student per day.
  static const int dailyCap = 50;

  /// Student-facing label (English).
  static const String label = 'Study Energy';

  /// Student-facing label (Hebrew).
  static const String labelHe = 'אנרגיית למידה';

  /// Warning threshold: show "low energy" warning when remaining <= this.
  static const int lowEnergyThreshold = 10;

  /// Critical threshold: show "almost out" warning.
  static const int criticalEnergyThreshold = 3;
}

// ---------------------------------------------------------------------------
// Localization
// ---------------------------------------------------------------------------

/// Supported locales.
abstract class AppLocales {
  /// Primary locale: Hebrew (Israel).
  static const Locale primary = Locale('he', 'IL');

  /// Fallback locale: English (US).
  static const Locale fallback = Locale('en', 'US');

  /// All supported locales.
  static const List<Locale> supported = [primary, fallback];

  /// Default text direction for primary locale.
  static const TextDirection primaryDirection = TextDirection.rtl;
}

// ---------------------------------------------------------------------------
// Design Tokens
// ---------------------------------------------------------------------------

/// Subject color palette design tokens.
abstract class SubjectColorTokens {
  static const Color mathPrimary = Color(0xFF0097A7);
  static const Color mathSecondary = Color(0xFF4DD0E1);
  static const Color mathBackground = Color(0xFFE0F7FA);

  static const Color physicsPrimary = Color(0xFFFF8F00);
  static const Color physicsSecondary = Color(0xFFFFCA28);
  static const Color physicsBackground = Color(0xFFFFF8E1);

  static const Color chemistryPrimary = Color(0xFF388E3C);
  static const Color chemistrySecondary = Color(0xFF81C784);
  static const Color chemistryBackground = Color(0xFFE8F5E9);

  static const Color biologyPrimary = Color(0xFF7B1FA2);
  static const Color biologySecondary = Color(0xFFCE93D8);
  static const Color biologyBackground = Color(0xFFF3E5F5);

  static const Color csPrimary = Color(0xFF616161);
  static const Color csSecondary = Color(0xFFBDBDBD);
  static const Color csBackground = Color(0xFFF5F5F5);
}

/// Typography scale design tokens.
abstract class TypographyTokens {
  static const double displayLarge = 32.0;
  static const double displayMedium = 28.0;
  static const double headlineLarge = 24.0;
  static const double headlineMedium = 20.0;
  static const double titleLarge = 18.0;
  static const double titleMedium = 16.0;
  static const double bodyLarge = 16.0;
  static const double bodyMedium = 14.0;
  static const double bodySmall = 12.0;
  static const double labelLarge = 14.0;
  static const double labelMedium = 12.0;
  static const double labelSmall = 10.0;

  /// Primary font family for Hebrew text.
  static const String hebrewFontFamily = 'Heebo';

  /// Primary font family for English/Latin text.
  static const String latinFontFamily = 'Inter';

  /// Monospace font for math/code content.
  static const String monoFontFamily = 'JetBrains Mono';
}

/// Spacing scale design tokens (8px grid).
abstract class SpacingTokens {
  static const double xxs = 2.0;
  static const double xs = 4.0;
  static const double sm = 8.0;
  static const double md = 16.0;
  static const double lg = 24.0;
  static const double xl = 32.0;
  static const double xxl = 48.0;
  static const double xxxl = 64.0;
}

/// Border radius design tokens.
abstract class RadiusTokens {
  static const double sm = 4.0;
  static const double md = 8.0;
  static const double lg = 12.0;
  static const double xl = 16.0;
  static const double full = 999.0;
}

/// Animation duration design tokens.
abstract class AnimationTokens {
  static const Duration fast = Duration(milliseconds: 150);
  static const Duration normal = Duration(milliseconds: 300);
  static const Duration slow = Duration(milliseconds: 600);
  static const Duration celebration = Duration(milliseconds: 1000);
}

// ---------------------------------------------------------------------------
// App Configuration — Composite
// ---------------------------------------------------------------------------

/// Top-level application configuration, assembled from environment.
class AppConfig {
  const AppConfig({
    required this.environment,
    required this.endpoints,
    required this.featureFlags,
  });

  final Environment environment;
  final ApiEndpoints endpoints;
  final FeatureFlags featureFlags;

  /// Create config for a specific environment with defaults.
  factory AppConfig.forEnvironment(Environment env) {
    return AppConfig(
      environment: env,
      endpoints: ApiEndpoints.forEnvironment(env),
      featureFlags: _flagsForEnvironment(env),
    );
  }

  static FeatureFlags _flagsForEnvironment(Environment env) {
    switch (env) {
      case Environment.dev:
        return FeatureFlags.dev;
      case Environment.staging:
        return FeatureFlags.staging;
      case Environment.prod:
        return FeatureFlags.prod;
    }
  }

  bool get isDev => environment == Environment.dev;
  bool get isStaging => environment == Environment.staging;
  bool get isProd => environment == Environment.prod;
}
