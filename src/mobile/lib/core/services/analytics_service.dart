// =============================================================================
// Cena Adaptive Learning Platform — Firebase Analytics Service
// Thin wrapper around firebase_analytics for structured event logging.
// =============================================================================

import 'package:firebase_analytics/firebase_analytics.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:logger/logger.dart';

/// Riverpod provider for the analytics service.
///
/// Overridden in `main()` after Firebase initializes. Falls back to a default
/// instance when Firebase is available at provider-read time.
final analyticsServiceProvider = Provider<AnalyticsService>((ref) {
  return AnalyticsService();
});

final _log = Logger(printer: PrettyPrinter(methodCount: 0, printTime: true));

/// Singleton-style analytics wrapper.
///
/// All methods are fire-and-forget: analytics failures are logged but never
/// propagate to the caller. This keeps instrumentation from breaking the UX.
class AnalyticsService {
  AnalyticsService._(this._analytics);

  factory AnalyticsService({FirebaseAnalytics? analytics}) {
    return AnalyticsService._(analytics ?? FirebaseAnalytics.instance);
  }

  final FirebaseAnalytics _analytics;

  /// The [FirebaseAnalyticsObserver] to attach to the router for automatic
  /// screen tracking.
  late final FirebaseAnalyticsObserver observer =
      FirebaseAnalyticsObserver(analytics: _analytics);

  // ---------------------------------------------------------------------------
  // Screen tracking
  // ---------------------------------------------------------------------------

  /// Logs a screen view with the given [screenName].
  Future<void> logScreenView(String screenName) async {
    try {
      await _analytics.logScreenView(screenName: screenName);
    } catch (e) {
      _warn('logScreenView', e);
    }
  }

  // ---------------------------------------------------------------------------
  // Generic event
  // ---------------------------------------------------------------------------

  /// Logs a custom event with optional [params].
  Future<void> logEvent(
    String name, {
    Map<String, Object>? params,
  }) async {
    try {
      await _analytics.logEvent(name: name, parameters: params);
    } catch (e) {
      _warn('logEvent($name)', e);
    }
  }

  // ---------------------------------------------------------------------------
  // Domain-specific helpers
  // ---------------------------------------------------------------------------

  /// Logs when a learning session starts.
  Future<void> logSessionStart(String sessionId, String subject) async {
    await logEvent('session_start', params: {
      'session_id': sessionId,
      'subject': subject,
    });
  }

  /// Logs a question attempt with its outcome and methodology.
  Future<void> logQuestionAttempt(
    String questionId, {
    required bool correct,
    required String methodology,
  }) async {
    await logEvent('question_attempt', params: {
      'question_id': questionId,
      'correct': correct.toString(),
      'methodology': methodology,
    });
  }

  /// Logs a tutoring chat message within a session.
  Future<void> logTutoringMessage(String sessionId, int turnNumber) async {
    await logEvent('tutoring_message', params: {
      'session_id': sessionId,
      'turn_number': turnNumber,
    });
  }

  /// Logs when a badge is earned.
  Future<void> logBadgeEarned(String badgeId, String badgeName) async {
    await logEvent('badge_earned', params: {
      'badge_id': badgeId,
      'badge_name': badgeName,
    });
  }

  // ---------------------------------------------------------------------------
  // User properties
  // ---------------------------------------------------------------------------

  /// Sets analytics user properties for segmentation.
  Future<void> setUserProperties({
    required String userId,
    required String grade,
    required String language,
  }) async {
    try {
      await _analytics.setUserId(id: userId);
      await _analytics.setUserProperty(name: 'grade', value: grade);
      await _analytics.setUserProperty(name: 'language', value: language);
    } catch (e) {
      _warn('setUserProperties', e);
    }
  }

  // ---------------------------------------------------------------------------
  // Internal
  // ---------------------------------------------------------------------------

  void _warn(String method, Object error) {
    if (kDebugMode) {
      _log.w('Analytics.$method failed', error: error);
    }
  }
}
