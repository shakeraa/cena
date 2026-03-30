// =============================================================================
// Cena Adaptive Learning Platform — Session Notifier
// Manages the active learning session lifecycle via WebSocket events.
// =============================================================================

import 'dart:async';
import 'dart:convert';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:uuid/uuid.dart';

import '../models/domain_models.dart';
import '../services/offline_sync_service.dart';
import '../services/websocket_service.dart';
import 'derived_providers.dart' show webSocketServiceProvider, syncManagerProvider;

// ---------------------------------------------------------------------------
// State
// ---------------------------------------------------------------------------

/// Immutable snapshot of the active learning session.
class SessionState {
  const SessionState({
    this.currentSession,
    this.currentExercise,
    this.methodology,
    this.fatigueScore = 0.0,
    this.questionsAttempted = 0,
    this.questionsCorrect = 0,
    this.isLoading = false,
    this.error,
    this.isBreakSuggested = false,
    this.hintsUsed = 0,
    this.sessionHistory = const [],
  });

  final Session? currentSession;
  final Exercise? currentExercise;
  final Methodology? methodology;
  final double fatigueScore;
  final int questionsAttempted;
  final int questionsCorrect;
  final bool isLoading;
  final String? error;
  final bool isBreakSuggested;
  final int hintsUsed;

  /// Ordered list of answer results for the in-session progress display.
  final List<AnswerResult> sessionHistory;

  /// True while a session is running (started and not yet ended).
  bool get isActive =>
      currentSession != null && currentSession!.endedAt == null;

  /// Correct answer rate for the session so far.
  double get accuracy =>
      questionsAttempted > 0 ? questionsCorrect / questionsAttempted : 0.0;

  /// Wall-clock time since the session started.
  Duration get elapsed => currentSession != null
      ? DateTime.now().difference(currentSession!.startedAt)
      : Duration.zero;

  SessionState copyWith({
    Session? currentSession,
    Exercise? currentExercise,
    Methodology? methodology,
    double? fatigueScore,
    int? questionsAttempted,
    int? questionsCorrect,
    bool? isLoading,
    String? error,
    bool? isBreakSuggested,
    int? hintsUsed,
    List<AnswerResult>? sessionHistory,
    bool clearError = false,
    bool clearCurrentExercise = false,
  }) {
    return SessionState(
      currentSession: currentSession ?? this.currentSession,
      currentExercise:
          clearCurrentExercise ? null : (currentExercise ?? this.currentExercise),
      methodology: methodology ?? this.methodology,
      fatigueScore: fatigueScore ?? this.fatigueScore,
      questionsAttempted: questionsAttempted ?? this.questionsAttempted,
      questionsCorrect: questionsCorrect ?? this.questionsCorrect,
      isLoading: isLoading ?? this.isLoading,
      error: clearError ? null : (error ?? this.error),
      isBreakSuggested: isBreakSuggested ?? this.isBreakSuggested,
      hintsUsed: hintsUsed ?? this.hintsUsed,
      sessionHistory: sessionHistory ?? this.sessionHistory,
    );
  }
}

// ---------------------------------------------------------------------------
// Notifier
// ---------------------------------------------------------------------------

/// Manages the active learning session.
///
/// Routes inbound WebSocket events:
/// - `QuestionPresented` → delivers next exercise
/// - `AnswerEvaluated`   → records result, updates accuracy
/// - `MethodologySwitched` → updates active methodology
/// - `CognitiveLoadWarning` → raises break suggestion when fatigue threshold hit
/// - `SessionSummary`    → marks session as ended
class SessionNotifier extends StateNotifier<SessionState> {
  SessionNotifier({
    required this.webSocketService,
    required this.syncManager,
  }) : super(const SessionState()) {
    _subscribeToEvents();
  }

  final WebSocketService webSocketService;
  final SyncManager syncManager;
  final List<StreamSubscription<dynamic>> _subscriptions = [];
  final _uuid = const Uuid();

  // ---- Public API ----

  /// Start a new learning session.
  Future<void> startSession({Subject? subject, int durationMinutes = 25}) async {
    state = state.copyWith(isLoading: true, clearError: true);
    try {
      await webSocketService.startSession(
        StartSession(
          studentId: '', // UserNotifier provides this at the call site
          subject: subject,
          durationMinutes: durationMinutes,
        ),
      );
      // SessionState is populated by the incoming QuestionPresented event.
    } catch (e) {
      state = state.copyWith(isLoading: false, error: e.toString());
    }
  }

  /// Submit a student answer for the current exercise.
  ///
  /// When the WebSocket is disconnected the answer is queued via [SyncManager]
  /// so it is never lost. The queue is flushed automatically when connectivity
  /// is restored (see [OfflineNotifier]).
  Future<void> submitAnswer(String answer, int timeSpentMs) async {
    final exercise = state.currentExercise;
    final session = state.currentSession;
    if (exercise == null || session == null) return;

    final idempotencyKey = _uuid.v4();
    final isConnected = webSocketService.currentConnectionState ==
        ConnectionState.connected;

    if (isConnected) {
      await webSocketService.attemptConcept(
        AttemptConcept(
          sessionId: session.id,
          exerciseId: exercise.id,
          conceptId: exercise.conceptId,
          answer: answer,
          timeSpentMs: timeSpentMs,
          idempotencyKey: idempotencyKey,
        ),
      );
    } else {
      // Offline path: queue the event for later sync.
      await syncManager.enqueue(
        OfflineEvent(
          idempotencyKey: idempotencyKey,
          clientTimestamp: DateTime.now(),
          eventType: 'AttemptConcept',
          payload: _encodeAttempt(
            session: session,
            exercise: exercise,
            answer: answer,
            timeSpentMs: timeSpentMs,
            idempotencyKey: idempotencyKey,
          ),
          classification: EventClassification.conditional,
          sequenceNumber: 0, // assigned by the queue
        ),
      );
    }
  }

  static String _encodeAttempt({
    required Session session,
    required Exercise exercise,
    required String answer,
    required int timeSpentMs,
    required String idempotencyKey,
  }) {
    return jsonEncode({
      'sessionId': session.id,
      'exerciseId': exercise.id,
      'conceptId': exercise.conceptId,
      'answer': answer,
      'timeSpentMs': timeSpentMs,
      'idempotencyKey': idempotencyKey,
    });
  }

  /// Request the next hint for the current exercise.
  Future<void> requestHint() async {
    final exercise = state.currentExercise;
    final session = state.currentSession;
    if (exercise == null || session == null) return;

    await webSocketService.requestHint(
      RequestHint(
        sessionId: session.id,
        exerciseId: exercise.id,
        hintLevel: state.hintsUsed,
      ),
    );
    state = state.copyWith(hintsUsed: state.hintsUsed + 1);
  }

  /// Skip the current question.
  Future<void> skipQuestion({String? reason}) async {
    final exercise = state.currentExercise;
    final session = state.currentSession;
    if (exercise == null || session == null) return;

    await webSocketService.skipQuestion(
      SkipQuestion(
        sessionId: session.id,
        exerciseId: exercise.id,
        reason: reason,
      ),
    );
    state = state.copyWith(questionsAttempted: state.questionsAttempted + 1);
  }

  /// Request a different pedagogical approach mid-session.
  Future<void> switchApproach(String preferenceHint) async {
    final session = state.currentSession;
    if (session == null) return;

    await webSocketService.switchApproach(
      SwitchApproach(
        sessionId: session.id,
        preferenceHint: preferenceHint,
      ),
    );
  }

  /// End the current session.
  Future<void> endSession({String reason = 'manual'}) async {
    final session = state.currentSession;
    if (session == null) return;

    await webSocketService.endSession(
      EndSession(sessionId: session.id, reason: reason),
    );
  }

  /// Acknowledge the break suggestion — student chose to continue or pause.
  void dismissBreakSuggestion() {
    state = state.copyWith(isBreakSuggested: false);
  }

  // ---- WebSocket event routing ----

  void _subscribeToEvents() {
    _subscriptions.add(
      webSocketService.messageStream.listen(_handleMessage),
    );
  }

  void _handleMessage(MessageEnvelope envelope) {
    switch (envelope.type) {
      case 'QuestionPresented':
        _onQuestionPresented(envelope.payload);
      case 'AnswerEvaluated':
        _onAnswerEvaluated(envelope.payload);
      case 'MethodologySwitched':
        _onMethodologySwitched(envelope.payload);
      case 'CognitiveLoadWarning':
        _onCognitiveLoadWarning(envelope.payload);
      case 'SessionSummary':
        _onSessionSummary(envelope.payload);
      case 'SessionStarted':
        _onSessionStarted(envelope.payload);
    }
  }

  void _onSessionStarted(Map<String, dynamic> payload) {
    final session = Session.fromJson(payload['session'] as Map<String, dynamic>);
    state = state.copyWith(
      currentSession: session,
      isLoading: false,
      questionsAttempted: 0,
      questionsCorrect: 0,
      hintsUsed: 0,
      fatigueScore: 0.0,
      sessionHistory: [],
    );
  }

  void _onQuestionPresented(Map<String, dynamic> payload) {
    final exercise =
        Exercise.fromJson(payload['exercise'] as Map<String, dynamic>);
    state = state.copyWith(
      currentExercise: exercise,
      isLoading: false,
      isBreakSuggested: false,
    );
  }

  void _onAnswerEvaluated(Map<String, dynamic> payload) {
    final result =
        AnswerResult.fromJson(payload['result'] as Map<String, dynamic>);
    final newHistory = [...state.sessionHistory, result];
    state = state.copyWith(
      questionsAttempted: state.questionsAttempted + 1,
      questionsCorrect:
          result.isCorrect ? state.questionsCorrect + 1 : state.questionsCorrect,
      sessionHistory: newHistory,
    );
  }

  void _onMethodologySwitched(Map<String, dynamic> payload) {
    final methodologyName = payload['methodology'] as String?;
    if (methodologyName == null) return;

    final methodology = Methodology.values.firstWhere(
      (m) => m.name == methodologyName,
      orElse: () => Methodology.adaptiveDifficulty,
    );
    state = state.copyWith(methodology: methodology);
  }

  void _onCognitiveLoadWarning(Map<String, dynamic> payload) {
    final fatigue = (payload['fatigueScore'] as num?)?.toDouble() ?? 0.0;
    state = state.copyWith(
      fatigueScore: fatigue,
      isBreakSuggested: fatigue >= 0.7,
    );
  }

  void _onSessionSummary(Map<String, dynamic> payload) {
    final session = state.currentSession;
    if (session == null) return;

    // Mark session as ended by setting endedAt.
    final endedSession = session.copyWith(endedAt: DateTime.now());
    state = state.copyWith(
      currentSession: endedSession,
      isLoading: false,
      clearCurrentExercise: true,
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

/// Session state provider — auto-disposed when no UI is listening.
///
/// Depends on [webSocketServiceProvider] and [syncManagerProvider] which must
/// be overridden in the root ProviderScope at app startup.
final sessionProvider =
    StateNotifierProvider.autoDispose<SessionNotifier, SessionState>((ref) {
  return SessionNotifier(
    webSocketService: ref.watch(webSocketServiceProvider),
    syncManager: ref.watch(syncManagerProvider) as SyncManager,
  );
});
