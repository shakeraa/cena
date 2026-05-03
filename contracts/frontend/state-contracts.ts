/**
 * Cena Adaptive Learning Platform — Client State Contracts
 *
 * Defines the Zustand store shapes, action contracts, and selector signatures
 * for the React Native (iOS + Android) and React PWA clients.
 *
 * Architecture:
 *   - Zustand stores with slice pattern (one slice per domain concern)
 *   - Immer middleware for immutable updates
 *   - Persist middleware (MMKV on RN, localStorage on web) for non-sensitive state
 *   - Offline state persisted to SQLite (see offline-sync-client.ts)
 *
 * @see contracts/frontend/signalr-messages.ts — Wire message types
 * @see contracts/frontend/offline-sync-client.ts — Offline queue implementation
 * @see docs/offline-sync-protocol.md — Server-side reconciliation
 * @module state-contracts
 */

import type {
  MethodologyType,
  MasteryStatus,
  ErrorType,
  QuestionFormat,
  ConnectionState,
  ServerEvent,
  SessionSummaryPayload,
  QuestionPresentedPayload,
  AnswerEvaluatedPayload,
  KnowledgeGraphUpdatedPayload,
  StreakUpdatedPayload,
  XpAwardedPayload,
  MethodologySwitchedPayload,
  CognitiveLoadWarningPayload,
  MasteryUpdatedPayload,
} from './signalr-messages';

// ─────────────────────────────────────────────────────────────────────────────
// 1. Session State
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Active learning session state.
 *
 * This is the "hot" state that changes rapidly during a session.
 * Not persisted across app restarts — the server is authoritative.
 * Ephemeral session data is held in memory only.
 */
export interface SessionState {
  // ── Session lifecycle ──────────────────────────────────────────────────

  /** Active session ID; `null` when no session is running. */
  readonly activeSessionId: string | null;
  /** Session status. */
  readonly status: 'idle' | 'starting' | 'active' | 'ending' | 'ended';
  /** Subject of the active session. */
  readonly subjectId: string | null;
  /** Session start timestamp (ISO 8601). */
  readonly startedAt: string | null;
  /** Elapsed time in seconds (updated by a 1s interval timer). */
  readonly elapsedSeconds: number;

  // ── Current question ───────────────────────────────────────────────────

  /** The currently presented question; `null` between questions. */
  readonly currentQuestion: CurrentQuestion | null;
  /** 0-based index of the current question in the session. */
  readonly questionIndex: number;
  /** Whether we're waiting for the server to evaluate an answer. */
  readonly isEvaluating: boolean;
  /** The most recent answer evaluation; `null` before first answer. */
  readonly lastEvaluation: AnswerEvaluatedPayload | null;

  // ── Methodology ────────────────────────────────────────────────────────

  /** Active methodology for the current concept. */
  readonly activeMethodology: MethodologyType | null;
  /** Concept ID the methodology applies to. */
  readonly activeConceptId: string | null;
  /** Active concept display name. */
  readonly activeConceptName: string | null;

  // ── Cognitive load ─────────────────────────────────────────────────────

  /**
   * Client-side fatigue score estimate (0.0 - 1.0).
   * Computed from session duration, error rate trend, and response time drift.
   * Used for proactive "take a break" prompts before the server detects overload.
   */
  readonly fatigueScore: number;
  /** Last cognitive load warning from the server; `null` if none. */
  readonly cognitiveLoadWarning: CognitiveLoadWarningPayload | null;

  // ── Session statistics (running tallies) ───────────────────────────────

  readonly questionsAttempted: number;
  readonly correctAnswers: number;
  readonly xpEarnedThisSession: number;
  readonly hintsUsedThisSession: number;
  readonly questionsSkippedThisSession: number;

  // ── Session summary (populated on end) ─────────────────────────────────

  /** Session summary from the server; `null` until session ends. */
  readonly summary: SessionSummaryPayload | null;
}

/** Shape of the currently presented question in state. */
export interface CurrentQuestion {
  readonly questionId: string;
  readonly conceptId: string;
  readonly conceptName: string;
  readonly questionText: string;
  readonly diagram: string | null;
  readonly format: QuestionFormat;
  readonly options: ReadonlyArray<{ readonly id: string; readonly text: string }> | null;
  readonly difficulty: number;
  readonly methodology: MethodologyType;
  readonly isReview: boolean;
  /** Client-local timestamp when the question was presented (for response time). */
  readonly presentedAt: number;
}

// ── Session Actions ─────────────────────────────────────────────────────────

/** Actions that mutate SessionState. Each maps to a Zustand action. */
export interface SessionActions {
  /** Initiate a new learning session via SignalR. */
  startSession(subjectId: string, conceptId?: string | null): Promise<void>;
  /** End the current session. */
  endSession(reason: 'completed' | 'tired' | 'out-of-time' | 'app-background' | 'manual'): Promise<void>;
  /** Submit an answer to the current question. */
  submitAnswer(answer: string, confidence?: number | null): Promise<void>;
  /** Request a hint at the specified level. */
  requestHint(level: 1 | 2 | 3): Promise<void>;
  /** Skip the current question. */
  skipQuestion(reason: 'too-hard' | 'already-know' | 'not-relevant' | 'other' | null): Promise<void>;
  /** Request the system to switch methodology. */
  switchApproach(methodology?: MethodologyType | null, reason?: string | null): Promise<void>;
  /** Navigate to the next concept. */
  requestNextConcept(targetConceptId?: string | null): Promise<void>;

  // ── Server event handlers (called by SignalR subscription layer) ────────

  /** Handle QuestionPresented event from server. */
  onQuestionPresented(payload: QuestionPresentedPayload): void;
  /** Handle AnswerEvaluated event from server. */
  onAnswerEvaluated(payload: AnswerEvaluatedPayload): void;
  /** Handle MethodologySwitched event from server. */
  onMethodologySwitched(payload: MethodologySwitchedPayload): void;
  /** Handle CognitiveLoadWarning event from server. */
  onCognitiveLoadWarning(payload: CognitiveLoadWarningPayload): void;
  /** Handle SessionSummary event from server. */
  onSessionSummary(payload: SessionSummaryPayload): void;

  // ── Internal ───────────────────────────────────────────────────────────

  /** Tick the elapsed time counter (called by 1s interval). */
  tick(): void;
  /** Recalculate the client-side fatigue score. */
  recalculateFatigue(): void;
  /** Reset session state to idle. */
  reset(): void;
}

// ─────────────────────────────────────────────────────────────────────────────
// 2. Knowledge Graph State
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Knowledge graph state for the visualization component.
 *
 * Persisted to MMKV/localStorage for instant rendering on app open.
 * Updated incrementally via KnowledgeGraphUpdated server events.
 */
export interface KnowledgeGraphState {
  /** Map of subject ID -> KnowledgeGraphSnapshot. */
  readonly graphs: Readonly<Record<string, KnowledgeGraphSnapshot>>;
  /** Currently selected subject for the graph view. */
  readonly selectedSubjectId: string | null;
  /** Currently focused/selected concept in the graph view. */
  readonly selectedConceptId: string | null;
  /** Whether the graph is currently loading from the server. */
  readonly isLoading: boolean;
  /** Last error from graph queries; `null` if none. */
  readonly error: string | null;
}

/** Snapshot of a student's knowledge graph for a single subject. */
export interface KnowledgeGraphSnapshot {
  readonly subjectId: string;
  readonly subjectName: string;
  /** Map of concept ID -> ConceptNode for O(1) lookups. */
  readonly nodes: Readonly<Record<string, ConceptNode>>;
  /** Directed edges (prerequisite -> dependent). */
  readonly edges: ReadonlyArray<GraphEdgeState>;
  /** Aggregate mastery (0.0 - 1.0). */
  readonly overallMastery: number;
  /** Concept IDs the student is ready to learn. */
  readonly readyToLearn: ReadonlyArray<string>;
  /** Concept IDs due for spaced repetition review. */
  readonly reviewDue: ReadonlyArray<string>;
  /** Knowledge graph version for sync compatibility. */
  readonly graphVersion: string;
  /** Last fetch timestamp (ISO 8601). */
  readonly lastFetchedAt: string;
}

/** A single concept node in the client-side knowledge graph. */
export interface ConceptNode {
  readonly conceptId: string;
  readonly conceptName: string;
  readonly topic: string;
  readonly difficulty: number;
  readonly masteryLevel: number;
  readonly predictedRecall: number;
  readonly status: MasteryStatus;
  readonly attemptsCount: number;
  readonly lastAttemptAt: string | null;
  readonly lastMasteredAt: string | null;
  readonly activeMethodology: MethodologyType | null;
  readonly halfLifeHours: number | null;
  readonly nextReviewDue: string | null;
  /** Prerequisite concept IDs (for client-side graph traversal). */
  readonly prerequisiteIds: ReadonlyArray<string>;
  /** Dependent concept IDs. */
  readonly dependentIds: ReadonlyArray<string>;
}

/** Edge state in the knowledge graph. */
export interface GraphEdgeState {
  readonly fromConceptId: string;
  readonly toConceptId: string;
  readonly unlocked: boolean;
}

// ── Knowledge Graph Actions ─────────────────────────────────────────────────

export interface KnowledgeGraphActions {
  /** Fetch the full knowledge graph for a subject from the server (GraphQL). */
  fetchGraph(subjectId: string): Promise<void>;
  /** Apply an incremental update from a KnowledgeGraphUpdated SignalR event. */
  applyUpdate(subjectId: string, payload: KnowledgeGraphUpdatedPayload): void;
  /** Apply a MasteryUpdated event (concept mastered). */
  applyMasteryUpdate(payload: MasteryUpdatedPayload): void;
  /** Select a subject for the graph view. */
  selectSubject(subjectId: string | null): void;
  /** Select/focus a concept in the graph view. */
  selectConcept(conceptId: string | null): void;
  /** Invalidate and refresh a subject's graph. */
  invalidate(subjectId: string): void;
}

// ─────────────────────────────────────────────────────────────────────────────
// 3. User State
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Authenticated user profile and gamification state.
 *
 * Persisted to MMKV/localStorage for instant app-open rendering.
 * Refreshed from the server on each app foreground transition.
 */
export interface UserState {
  // ── Authentication ─────────────────────────────────────────────────────

  /** Whether the user is authenticated. */
  readonly isAuthenticated: boolean;
  /** JWT access token; `null` when not authenticated. */
  readonly accessToken: string | null;
  /** JWT refresh token; `null` when not authenticated. */
  readonly refreshToken: string | null;
  /** Token expiry timestamp (ISO 8601). */
  readonly tokenExpiresAt: string | null;

  // ── Profile ────────────────────────────────────────────────────────────

  readonly userId: string | null;
  readonly displayName: string | null;
  readonly gradeLevel: number | null;
  readonly locale: string;
  /** User role for authorization. */
  readonly role: 'student' | 'teacher' | 'parent' | 'admin' | null;

  // ── Gamification ───────────────────────────────────────────────────────

  /** Total XP accumulated. */
  readonly totalXP: number;
  /** Current level. */
  readonly level: number;
  /** Progress within current level (0.0 - 1.0). */
  readonly levelProgress: number;

  // ── Streak ─────────────────────────────────────────────────────────────

  /** Current consecutive day streak. */
  readonly currentStreak: number;
  /** All-time longest streak. */
  readonly longestStreak: number;
  /** Remaining streak freezes. */
  readonly freezesRemaining: number;
  /** ISO 8601 UTC — when the streak expires. */
  readonly streakExpiresAt: string | null;

  // ── Experimentation ────────────────────────────────────────────────────

  /**
   * A/B test experiment cohort identifier.
   * Used to gate features and adjust parameters (e.g., streak_window_hours).
   * `null` if the user is not enrolled in any experiment.
   */
  readonly experimentCohort: string | null;
  /** Feature flags derived from the experiment cohort. */
  readonly featureFlags: Readonly<Record<string, boolean | string | number>>;

  // ── Enrolled subjects ──────────────────────────────────────────────────

  readonly enrolledSubjects: ReadonlyArray<{
    readonly subjectId: string;
    readonly subjectName: string;
    readonly displayName: string;
    readonly overallMastery: number;
  }>;
}

// ── User Actions ────────────────────────────────────────────────────────────

export interface UserActions {
  /** Authenticate with email and password. */
  login(email: string, password: string): Promise<void>;
  /** Register a new account. */
  register(params: {
    email: string;
    password: string;
    displayName: string;
    gradeLevel: number;
    locale: string;
  }): Promise<void>;
  /** Refresh the access token using the refresh token. */
  refreshTokens(): Promise<void>;
  /** Log out and clear all persisted state. */
  logout(): void;
  /** Fetch the full profile from the server (GraphQL myProfile). */
  fetchProfile(): Promise<void>;
  /** Update display name or preferences. */
  updateProfile(params: { displayName?: string; locale?: string }): Promise<void>;
  /** Apply an XpAwarded SignalR event. */
  onXpAwarded(payload: XpAwardedPayload): void;
  /** Apply a StreakUpdated SignalR event. */
  onStreakUpdated(payload: StreakUpdatedPayload): void;
  /** Set feature flags (received from server on login/sync). */
  setFeatureFlags(flags: Record<string, boolean | string | number>): void;
}

// ─────────────────────────────────────────────────────────────────────────────
// 4. Offline State
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Offline sync state.
 *
 * Tracks the offline event queue, sync progress, and connectivity status.
 * The actual event queue is in SQLite (see offline-sync-client.ts);
 * this state is a lightweight in-memory mirror for UI rendering.
 *
 * @see docs/offline-sync-protocol.md — Full sync protocol specification
 */
export interface OfflineState {
  // ── Connectivity ───────────────────────────────────────────────────────

  /** SignalR connection state. */
  readonly connectionState: ConnectionState;
  /** Whether the device has network connectivity (NetInfo). */
  readonly isNetworkAvailable: boolean;
  /** Whether the app is in offline learning mode (explicit or detected). */
  readonly isOfflineMode: boolean;

  // ── Event queue ────────────────────────────────────────────────────────

  /** Number of events pending in the offline queue. */
  readonly queuedEventCount: number;
  /** Types of events in the queue (for UI: "12 exercises, 3 annotations"). */
  readonly queuedEventSummary: Readonly<Record<string, number>>;
  /** Oldest event timestamp in the queue (ISO 8601); `null` if empty. */
  readonly oldestQueuedEventAt: string | null;

  // ── Sync progress ──────────────────────────────────────────────────────

  /** Current sync status. */
  readonly syncStatus: 'idle' | 'syncing' | 'resolving' | 'complete' | 'failed';
  /** Active sync session ID; `null` when not syncing. */
  readonly syncSessionId: string | null;
  /** Sync progress (0.0 - 1.0); `null` when not syncing. */
  readonly syncProgress: number | null;
  /** Last successful sync timestamp (ISO 8601). */
  readonly lastSyncAt: string | null;
  /** Last sync error message; `null` if last sync succeeded. */
  readonly lastSyncError: string | null;

  // ── Server sequence tracking ───────────────────────────────────────────

  /**
   * Last known server event sequence number.
   * Used for divergence detection in the sync handshake.
   */
  readonly lastKnownServerSeq: number;

  // ── Clock skew ─────────────────────────────────────────────────────────

  /**
   * Estimated clock offset from server in milliseconds.
   * Positive = client ahead; negative = client behind.
   */
  readonly clockOffsetMs: number;

  // ── Sync result (last completed sync) ──────────────────────────────────

  /** Summary of the last completed sync; `null` if never synced or no conflicts. */
  readonly lastSyncResult: SyncResultSummary | null;
}

/**
 * Client-side summary of a completed sync operation.
 * Used to render the sync summary toast/card (see offline-sync-protocol.md Section 7).
 */
export interface SyncResultSummary {
  /** Total events submitted. */
  readonly totalEvents: number;
  /** Events accepted at full weight. */
  readonly acceptedFullWeight: number;
  /** Events accepted at reduced weight. */
  readonly acceptedReducedWeight: number;
  /** Events accepted as historical record only. */
  readonly acceptedHistoricalOnly: number;
  /** Events where server recalculated (ConceptMastered). */
  readonly serverRecalculated: number;
  /** XP delta from the sync. */
  readonly xpDelta: number;
  /** Whether the streak was maintained by the offline session. */
  readonly streakMaintained: boolean;
  /** Outreach corrections (e.g., "streak restored" messages). */
  readonly outreachCorrections: ReadonlyArray<{
    readonly type: string;
    readonly message: string;
  }>;
  /** Severity level for the sync summary UI. */
  readonly severity: 'silent' | 'minor' | 'significant';
}

// ── Offline Actions ─────────────────────────────────────────────────────────

export interface OfflineActions {
  /** Update network availability (called by NetInfo listener). */
  setNetworkAvailable(available: boolean): void;
  /** Update SignalR connection state (called by connection event handler). */
  setConnectionState(state: ConnectionState): void;
  /** Enter explicit offline mode. */
  enterOfflineMode(): void;
  /** Exit offline mode and trigger sync. */
  exitOfflineMode(): Promise<void>;
  /** Enqueue an event to the offline queue (SQLite write + state update). */
  enqueueEvent(eventType: string, payload: Record<string, unknown>): Promise<void>;
  /** Trigger the sync handshake with the server. */
  startSync(): Promise<void>;
  /** Update sync progress (called by SyncManager during sync). */
  updateSyncProgress(progress: number): void;
  /** Handle sync completion. */
  onSyncComplete(result: SyncResultSummary): void;
  /** Handle sync failure. */
  onSyncFailed(error: string): void;
  /** Update clock offset (called after each server response). */
  updateClockOffset(serverTimestamp: string, clientSendTime: number, clientReceiveTime: number): void;
  /** Update the last known server sequence number. */
  updateServerSeq(seq: number): void;
  /** Refresh the queue summary from SQLite. */
  refreshQueueSummary(): Promise<void>;
}

// ─────────────────────────────────────────────────────────────────────────────
// 5. Connection State (lightweight)
// ─────────────────────────────────────────────────────────────────────────────

/**
 * SignalR connection metadata.
 * Separate from OfflineState for components that only need connection status.
 */
export interface ConnectionMetaState {
  readonly state: ConnectionState;
  readonly retryAttempt: number;
  readonly nextRetryMs: number | null;
  readonly lastError: string | null;
  /** ISO 8601 timestamp of last successful connection. */
  readonly connectedSince: string | null;
  /** Round-trip latency estimate in ms (from periodic pings). */
  readonly latencyMs: number | null;
}

export interface ConnectionMetaActions {
  onConnectionChange(state: ConnectionState, error?: string | null): void;
  onRetryScheduled(attempt: number, nextRetryMs: number): void;
  onLatencyMeasured(latencyMs: number): void;
}

// ─────────────────────────────────────────────────────────────────────────────
// 6. Combined Store Type
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Combined Zustand store type using the slice pattern.
 *
 * @example
 * ```tsx
 * const useStore = create<CenaStore>()(
 *   devtools(
 *     persist(
 *       immer((...a) => ({
 *         ...createSessionSlice(...a),
 *         ...createKnowledgeGraphSlice(...a),
 *         ...createUserSlice(...a),
 *         ...createOfflineSlice(...a),
 *         ...createConnectionMetaSlice(...a),
 *       })),
 *       { name: 'cena-store', storage: createMMKVStorage() }
 *     )
 *   )
 * );
 * ```
 */
export type CenaStore = SessionState &
  SessionActions &
  KnowledgeGraphState &
  KnowledgeGraphActions &
  UserState &
  UserActions &
  OfflineState &
  OfflineActions &
  ConnectionMetaState &
  ConnectionMetaActions;

// ─────────────────────────────────────────────────────────────────────────────
// 7. Selector Signatures (memoization hints)
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Selector signatures for use with Zustand's shallow equality or
 * custom comparators. Group related selectors for memoization.
 *
 * Usage with Zustand:
 * ```tsx
 * const { questionText, format } = useStore(selectCurrentQuestion, shallow);
 * ```
 *
 * These are type contracts — implementations use Zustand selectors.
 */

// ── Session selectors ────────────────────────────────────────────────────────

/** Select whether a session is currently active. */
export type SelectIsSessionActive = (state: CenaStore) => boolean;
// Impl hint: `state.status === 'active'`

/** Select the current question for rendering. Null-safe. */
export type SelectCurrentQuestion = (state: CenaStore) => CurrentQuestion | null;
// Impl hint: `state.currentQuestion`

/** Select running session statistics for the session HUD. */
export type SelectSessionStats = (state: CenaStore) => {
  questionsAttempted: number;
  correctAnswers: number;
  accuracy: number;
  xpEarned: number;
  elapsedSeconds: number;
};
// Impl hint: computed from multiple fields — use `shallow` comparator

/** Select the fatigue score for the cognitive load indicator. */
export type SelectFatigueScore = (state: CenaStore) => number;
// Impl hint: `state.fatigueScore`

// ── Knowledge graph selectors ────────────────────────────────────────────────

/** Select the knowledge graph for the currently selected subject. */
export type SelectActiveGraph = (state: CenaStore) => KnowledgeGraphSnapshot | null;
// Impl hint: `state.graphs[state.selectedSubjectId]` — memoize on selectedSubjectId change

/** Select a single concept node by ID. */
export type SelectConceptNode = (state: CenaStore, conceptId: string) => ConceptNode | null;
// Impl hint: parameterized selector — use `useCallback` or selector factory

/** Select concepts ready to learn for the "up next" component. */
export type SelectReadyToLearn = (state: CenaStore) => ReadonlyArray<ConceptNode>;
// Impl hint: derive from `graphs[selectedSubjectId].readyToLearn` + node lookup

/** Select concepts due for review for the spaced repetition component. */
export type SelectReviewDue = (state: CenaStore) => ReadonlyArray<ConceptNode>;
// Impl hint: derive from `graphs[selectedSubjectId].reviewDue` + node lookup

// ── User selectors ───────────────────────────────────────────────────────────

/** Select the user's gamification state for the profile/header display. */
export type SelectGamification = (state: CenaStore) => {
  totalXP: number;
  level: number;
  levelProgress: number;
  currentStreak: number;
  longestStreak: number;
};
// Impl hint: 5 primitive fields — use `shallow` comparator

/** Select whether a feature flag is enabled. */
export type SelectFeatureFlag = (state: CenaStore, flag: string) => boolean | string | number | undefined;
// Impl hint: `state.featureFlags[flag]`

// ── Offline selectors ────────────────────────────────────────────────────────

/** Select the offline sync banner state. */
export type SelectOfflineBanner = (state: CenaStore) => {
  isOffline: boolean;
  queuedEventCount: number;
  syncStatus: OfflineState['syncStatus'];
  syncProgress: number | null;
};
// Impl hint: 4 fields from OfflineState — use `shallow` comparator

/** Select whether a sync is needed (queued events exist + connected). */
export type SelectSyncNeeded = (state: CenaStore) => boolean;
// Impl hint: `state.queuedEventCount > 0 && state.connectionState === 'connected'`

// ── Connection selectors ─────────────────────────────────────────────────────

/** Select the connection indicator state. */
export type SelectConnectionIndicator = (state: CenaStore) => {
  state: ConnectionState;
  latencyMs: number | null;
  retryAttempt: number;
};

// ─────────────────────────────────────────────────────────────────────────────
// 8. Initial State Factories
// ─────────────────────────────────────────────────────────────────────────────

/** Create initial SessionState. */
export const createInitialSessionState = (): SessionState => ({
  activeSessionId: null,
  status: 'idle',
  subjectId: null,
  startedAt: null,
  elapsedSeconds: 0,
  currentQuestion: null,
  questionIndex: 0,
  isEvaluating: false,
  lastEvaluation: null,
  activeMethodology: null,
  activeConceptId: null,
  activeConceptName: null,
  fatigueScore: 0,
  cognitiveLoadWarning: null,
  questionsAttempted: 0,
  correctAnswers: 0,
  xpEarnedThisSession: 0,
  hintsUsedThisSession: 0,
  questionsSkippedThisSession: 0,
  summary: null,
});

/** Create initial KnowledgeGraphState. */
export const createInitialKnowledgeGraphState = (): KnowledgeGraphState => ({
  graphs: {},
  selectedSubjectId: null,
  selectedConceptId: null,
  isLoading: false,
  error: null,
});

/** Create initial UserState. */
export const createInitialUserState = (): UserState => ({
  isAuthenticated: false,
  accessToken: null,
  refreshToken: null,
  tokenExpiresAt: null,
  userId: null,
  displayName: null,
  gradeLevel: null,
  locale: 'he-IL',
  role: null,
  totalXP: 0,
  level: 1,
  levelProgress: 0,
  currentStreak: 0,
  longestStreak: 0,
  freezesRemaining: 0,
  streakExpiresAt: null,
  experimentCohort: null,
  featureFlags: {},
  enrolledSubjects: [],
});

/** Create initial OfflineState. */
export const createInitialOfflineState = (): OfflineState => ({
  connectionState: 'disconnected' as ConnectionState,
  isNetworkAvailable: true,
  isOfflineMode: false,
  queuedEventCount: 0,
  queuedEventSummary: {},
  oldestQueuedEventAt: null,
  syncStatus: 'idle',
  syncSessionId: null,
  syncProgress: null,
  lastSyncAt: null,
  lastSyncError: null,
  lastKnownServerSeq: 0,
  clockOffsetMs: 0,
  lastSyncResult: null,
});

/** Create initial ConnectionMetaState. */
export const createInitialConnectionMetaState = (): ConnectionMetaState => ({
  state: 'disconnected' as ConnectionState,
  retryAttempt: 0,
  nextRetryMs: null,
  lastError: null,
  connectedSince: null,
  latencyMs: null,
});
