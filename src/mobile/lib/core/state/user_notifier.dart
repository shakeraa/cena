// =============================================================================
// Cena Adaptive Learning Platform — User Notifier
// Manages authenticated student state, XP, streaks, and study energy.
// =============================================================================

import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../models/domain_models.dart';
import '../services/websocket_service.dart';
import 'derived_providers.dart' show webSocketServiceProvider;

// ---------------------------------------------------------------------------
// State
// ---------------------------------------------------------------------------

/// Immutable snapshot of the authenticated student's user state.
class UserState {
  const UserState({
    this.student,
    this.badges = const [],
    this.dailyQuestionsAnswered = 0,
    this.dailyGoal = 20,
    this.llmInteractionsToday = 0,
    this.isLoading = false,
    this.error,
  });

  final Student? student;
  final List<Badge> badges;
  final int dailyQuestionsAnswered;
  final int dailyGoal;

  /// Number of LLM interactions used today (cap: 50 = "study energy").
  final int llmInteractionsToday;
  final bool isLoading;
  final String? error;

  bool get isAuthenticated => student != null;

  /// Remaining "study energy" interactions. Server enforces the 50-cap.
  int get remainingStudyEnergy => 50 - llmInteractionsToday;

  bool get hasStudyEnergy => llmInteractionsToday < 50;

  double get dailyProgress =>
      dailyGoal > 0 ? (dailyQuestionsAnswered / dailyGoal).clamp(0.0, 1.0) : 0.0;

  UserState copyWith({
    Student? student,
    List<Badge>? badges,
    int? dailyQuestionsAnswered,
    int? dailyGoal,
    int? llmInteractionsToday,
    bool? isLoading,
    String? error,
    bool clearStudent = false,
    bool clearError = false,
  }) {
    return UserState(
      student: clearStudent ? null : (student ?? this.student),
      badges: badges ?? this.badges,
      dailyQuestionsAnswered:
          dailyQuestionsAnswered ?? this.dailyQuestionsAnswered,
      dailyGoal: dailyGoal ?? this.dailyGoal,
      llmInteractionsToday:
          llmInteractionsToday ?? this.llmInteractionsToday,
      isLoading: isLoading ?? this.isLoading,
      error: clearError ? null : (error ?? this.error),
    );
  }
}

// ---------------------------------------------------------------------------
// Notifier
// ---------------------------------------------------------------------------

/// Manages authenticated user state, XP, streaks, badges, and study energy.
///
/// Kept alive for the full app lifetime — user is always relevant.
///
/// WebSocket events handled:
/// - `XpAwarded`       → updates student XP and level
/// - `StreakUpdated`   → updates student streak counter
/// - `BadgeEarned`     → appends new badge to list
/// - `DailyReset`      → resets daily counters at midnight
class UserNotifier extends StateNotifier<UserState> {
  UserNotifier({required this.webSocketService})
      : super(const UserState()) {
    _subscribeToEvents();
  }

  final WebSocketService webSocketService;
  final List<StreamSubscription<dynamic>> _subscriptions = [];

  // ---- Public API ----

  /// Set the authenticated student after login.
  void setStudent(Student student) {
    state = state.copyWith(student: student, clearError: true);
  }

  /// Clear all user state on logout.
  void logout() {
    state = const UserState();
  }

  /// Increment the daily question attempt counter.
  void recordQuestionAttempt() {
    state = state.copyWith(
      dailyQuestionsAnswered: state.dailyQuestionsAnswered + 1,
    );
  }

  /// Increment the LLM interaction counter (tracks "study energy").
  /// Each hint request or feedback generation consumes one unit.
  void recordLlmInteraction() {
    state = state.copyWith(
      llmInteractionsToday: state.llmInteractionsToday + 1,
    );
  }

  // ---- WebSocket event routing ----

  void _subscribeToEvents() {
    _subscriptions.add(
      webSocketService.messageStream.listen(_handleMessage),
    );
  }

  void _handleMessage(MessageEnvelope envelope) {
    switch (envelope.type) {
      case 'XpAwarded':
        _onXpAwarded(envelope.payload);
      case 'StreakUpdated':
        _onStreakUpdated(envelope.payload);
      case 'BadgeEarned':
        _onBadgeEarned(envelope.payload);
      case 'DailyReset':
        _onDailyReset();
    }
  }

  void _onXpAwarded(Map<String, dynamic> payload) {
    final student = state.student;
    if (student == null) return;

    final totalXp = (payload['totalXp'] as num?)?.toInt() ?? student.xp;
    final level = (payload['level'] as num?)?.toInt() ?? student.level;

    state = state.copyWith(
      student: student.copyWith(xp: totalXp, level: level),
    );
  }

  void _onStreakUpdated(Map<String, dynamic> payload) {
    final student = state.student;
    if (student == null) return;

    final streak = (payload['streak'] as num?)?.toInt() ?? student.streak;
    state = state.copyWith(
      student: student.copyWith(streak: streak),
    );
  }

  void _onBadgeEarned(Map<String, dynamic> payload) {
    final badge = Badge.fromJson(payload['badge'] as Map<String, dynamic>);
    state = state.copyWith(
      badges: [...state.badges, badge],
    );
  }

  void _onDailyReset() {
    state = state.copyWith(
      dailyQuestionsAnswered: 0,
      llmInteractionsToday: 0,
    );
  }

  @override
  void dispose() {
    for (final sub in _subscriptions) {
      sub.cancel();
    }
    super.dispose();
  }
}

// ---------------------------------------------------------------------------
// Provider
// ---------------------------------------------------------------------------

/// User state provider — kept alive for the full app session.
final userProvider = StateNotifierProvider<UserNotifier, UserState>((ref) {
  return UserNotifier(
    webSocketService: ref.watch(webSocketServiceProvider),
  );
});
