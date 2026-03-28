/**
 * Cena Adaptive Learning Platform — SignalR Message Contracts
 *
 * Full TypeScript type definitions for all SignalR WebSocket messages between
 * the React Native / React PWA client and the ASP.NET Core SignalR hub backed
 * by the Proto.Actor cluster.
 *
 * Wire format: JSON over WebSocket (MessagePack negotiable for bandwidth-sensitive paths).
 * Versioning: Additive-only on existing types; breaking changes require a new `type` discriminator
 * value with a 90-day sunset on the old one. Clients MUST ignore unknown `type` values gracefully.
 *
 * @see docs/api-contracts.md Section 1 — SignalR WebSocket Message Envelope
 * @see docs/event-schemas.md — Domain event definitions (server-side, Protobuf)
 * @module signalr-messages
 */

// ─────────────────────────────────────────────────────────────────────────────
// 1. Envelope & Discriminated Union
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Discriminated union envelope wrapping every SignalR frame.
 *
 * The `type` field doubles as the SignalR hub method name and serves as the
 * discriminator for exhaustive `switch` handling on the client. `direction`
 * tags whether the message originates from the client ("command") or the
 * server ("event"), enabling a single deserialization path.
 *
 * @typeParam T - String literal for the message type discriminator.
 * @typeParam P - Payload shape specific to this message type.
 */
export interface MessageEnvelope<T extends string, P> {
  /** Discriminator — doubles as the SignalR hub method name. */
  readonly type: T;

  /**
   * Direction tag.
   * - `"command"` = client -> server
   * - `"event"`   = server -> client
   */
  readonly direction: 'command' | 'event';

  /**
   * Client-generated UUIDv7 for commands; the server echoes it on all
   * causally related events so the client can correlate responses.
   */
  readonly correlationId: string;

  /** ISO 8601 UTC timestamp — when the message was created. */
  readonly timestamp: string;

  /** Type-specific payload. */
  readonly payload: P;
}

// ─────────────────────────────────────────────────────────────────────────────
// 2. Shared Domain Types
// ─────────────────────────────────────────────────────────────────────────────

/**
 * All eight teaching methodologies supported by the Pedagogy Context.
 * Maps 1:1 to the `methodology_active` field in domain events.
 *
 * @see docs/architecture-design.md Section 3.2.1 — MCM Graph
 */
export type MethodologyType =
  | 'socratic'
  | 'spaced-repetition'
  | 'project-based'
  | 'blooms-progression'
  | 'feynman'
  | 'worked-example'
  | 'analogy'
  | 'retrieval-practice';

/**
 * Error classification produced by the LLM Anti-Corruption Layer.
 * Drives methodology switching via the MCM graph.
 *
 * @see docs/event-schemas.md — ConceptAttempted_V1.error_type
 */
export type ErrorType =
  | 'conceptual-misunderstanding'
  | 'computational-error'
  | 'notation-error'
  | 'incomplete-reasoning'
  | 'off-topic'
  | 'partial-understanding';

/**
 * Question presentation formats. The client renders different input
 * components based on this discriminator.
 */
export type QuestionFormat =
  | 'free-text'
  | 'multiple-choice'
  | 'numeric'
  | 'proof'
  | 'graph-sketch';

/**
 * Mastery status for a concept in the knowledge graph overlay.
 * Corresponds to the `MasteryStatus` GraphQL enum.
 */
export type MasteryStatus = 'not-started' | 'in-progress' | 'mastered' | 'decaying';

/**
 * Client device context sent with session-initiating commands.
 */
export interface DeviceContext {
  /** Runtime platform. */
  readonly platform: 'ios' | 'android' | 'web';
  /** Viewport width in logical pixels (dp). */
  readonly screenWidth: number;
  /** Viewport height in logical pixels (dp). */
  readonly screenHeight: number;
  /** BCP 47 locale tag (e.g., "he-IL", "ar-IL", "en-US"). */
  readonly locale: string;
}

// ─────────────────────────────────────────────────────────────────────────────
// 3. Client -> Server Commands
// ─────────────────────────────────────────────────────────────────────────────

// ── StartSession ────────────────────────────────────────────────────────────

/** Begin a new learning session for the authenticated student. */
export interface StartSessionPayload {
  /** Target subject identifier (e.g., "math", "physics", "cs"). */
  readonly subjectId: string;
  /** Optional concept to resume; `null` lets the system select optimally. */
  readonly conceptId: string | null;
  /** Client device context for analytics and responsive content. */
  readonly device: DeviceContext;
}

export type StartSession = MessageEnvelope<'StartSession', StartSessionPayload>;

// ── SubmitAnswer (AttemptConcept) ───────────────────────────────────────────

/**
 * Submit an answer to the currently presented question.
 * Named `SubmitAnswer` on the wire; maps to `ConceptAttempted` domain event.
 */
export interface SubmitAnswerPayload {
  readonly sessionId: string;
  readonly questionId: string;
  /** Free-text answer. May contain LaTeX delimited by `$...$`. */
  readonly answer: string;
  /** Wall-clock milliseconds the student spent on this question. */
  readonly responseTimeMs: number;
  /** Optional 1-5 confidence self-report; `null` if the prompt was not shown. */
  readonly confidence: number | null;
  /**
   * Behavioral signals captured by the input component.
   * Used by the Stagnation Detector for uncertainty pattern analysis.
   */
  readonly behavioralSignals: {
    /** Number of backspace / delete key presses. */
    readonly backspaceCount: number;
    /** Number of times the answer was changed before final submit. */
    readonly answerChangeCount: number;
  };
}

export type SubmitAnswer = MessageEnvelope<'SubmitAnswer', SubmitAnswerPayload>;

// ── EndSession ──────────────────────────────────────────────────────────────

/** Gracefully end the current learning session. */
export interface EndSessionPayload {
  readonly sessionId: string;
  /** Client-reported reason for ending. */
  readonly reason: 'completed' | 'tired' | 'out-of-time' | 'app-background' | 'manual';
}

export type EndSession = MessageEnvelope<'EndSession', EndSessionPayload>;

// ── RequestHint ─────────────────────────────────────────────────────────────

/** Ask the system for a progressive hint on the current question. */
export interface RequestHintPayload {
  readonly sessionId: string;
  readonly questionId: string;
  /**
   * Hint escalation level:
   * - `1` = gentle nudge (directional)
   * - `2` = partial reveal (shows approach)
   * - `3` = full walkthrough (step-by-step solution)
   */
  readonly hintLevel: 1 | 2 | 3;
}

export type RequestHint = MessageEnvelope<'RequestHint', RequestHintPayload>;

// ── SkipQuestion ────────────────────────────────────────────────────────────

/** Skip the current question and request the next one. */
export interface SkipQuestionPayload {
  readonly sessionId: string;
  readonly questionId: string;
  /** Optional skip reason — helps the Stagnation Detector. */
  readonly reason: 'too-hard' | 'already-know' | 'not-relevant' | 'other' | null;
}

export type SkipQuestion = MessageEnvelope<'SkipQuestion', SkipQuestionPayload>;

// ── AddAnnotation ───────────────────────────────────────────────────────────

/** Add a free-text annotation (note / reflection) to a concept. */
export interface AddAnnotationPayload {
  readonly sessionId: string;
  readonly conceptId: string;
  /** Free-text note. Supports Markdown. Encrypted at rest (GDPR). */
  readonly text: string;
  /** Annotation type — drives sentiment analysis routing. */
  readonly kind: 'note' | 'question' | 'confusion' | 'insight';
}

export type AddAnnotation = MessageEnvelope<'AddAnnotation', AddAnnotationPayload>;

// ── SwitchApproach ──────────────────────────────────────────────────────────

/**
 * Student explicitly requests a methodology switch.
 * The server validates via the MCM graph before accepting.
 */
export interface SwitchApproachPayload {
  readonly sessionId: string;
  readonly conceptId: string;
  /** Requested methodology. `null` = let the system choose. */
  readonly requestedMethodology: MethodologyType | null;
  /** Optional free-text reason from the student. */
  readonly reason: string | null;
}

export type SwitchApproach = MessageEnvelope<'SwitchApproach', SwitchApproachPayload>;

// ── RequestNextConcept ──────────────────────────────────────────────────────

/** Explicitly navigate to the next concept in the session. */
export interface RequestNextConceptPayload {
  readonly sessionId: string;
  /** Specific concept to navigate to; `null` = system selects optimally. */
  readonly targetConceptId: string | null;
}

export type RequestNextConcept = MessageEnvelope<'RequestNextConcept', RequestNextConceptPayload>;

// ── UpdatePreferences ───────────────────────────────────────────────────────

/** Update real-time session preferences mid-session. */
export interface UpdatePreferencesPayload {
  /** Preferred explanation style. */
  readonly explanationStyle?: 'concise' | 'detailed' | 'visual' | 'step-by-step';
  /** Difficulty bias: -1 = easier, 0 = adaptive (default), 1 = harder. */
  readonly difficultyBias?: -1 | 0 | 1;
  /** Language preference for generated content. */
  readonly contentLanguage?: 'he' | 'ar' | 'en';
  /** Whether camera-based attention detection is active. */
  readonly cameraEnabled?: boolean;
}

export type UpdatePreferences = MessageEnvelope<'UpdatePreferences', UpdatePreferencesPayload>;

// ─────────────────────────────────────────────────────────────────────────────
// 4. Server -> Client Events
// ─────────────────────────────────────────────────────────────────────────────

// ── SessionStarted ──────────────────────────────────────────────────────────

/** Confirms session creation and provides initial state snapshot. */
export interface SessionStartedPayload {
  readonly sessionId: string;
  readonly subjectId: string;
  /** The concept the session begins with. */
  readonly startingConceptId: string;
  readonly startingConceptName: string;
  /** Active methodology for the starting concept. */
  readonly methodology: MethodologyType;
  /** Student's total XP at session start. */
  readonly currentXP: number;
  /** Current streak count in days. */
  readonly streakDays: number;
}

export type SessionStartedEvent = MessageEnvelope<'SessionStarted', SessionStartedPayload>;

// ── QuestionPresented ───────────────────────────────────────────────────────

/** Server pushes the next question for the student to answer. */
export interface QuestionPresentedPayload {
  readonly sessionId: string;
  readonly questionId: string;
  readonly conceptId: string;
  readonly conceptName: string;
  /** Question content. May contain LaTeX (`$...$`) and Markdown. */
  readonly questionText: string;
  /** Optional diagram — inline SVG string or CDN URL. */
  readonly diagram: string | null;
  /** Question format hint for rendering the correct input component. */
  readonly format: QuestionFormat;
  /** Multiple-choice options; `null` unless `format === "multiple-choice"`. */
  readonly options: ReadonlyArray<{ readonly id: string; readonly text: string }> | null;
  /** Difficulty rating (1-10 scale, Bloom's-mapped). */
  readonly difficulty: number;
  /** Active methodology for this question. */
  readonly methodology: MethodologyType;
  /** 0-based position in the session question sequence. */
  readonly questionIndex: number;
  /** `true` if this is a spaced-repetition review item. */
  readonly isReview: boolean;
}

export type QuestionPresentedEvent = MessageEnvelope<'QuestionPresented', QuestionPresentedPayload>;

// ── AnswerEvaluated ─────────────────────────────────────────────────────────

/** Server evaluates the student's submitted answer. */
export interface AnswerEvaluatedPayload {
  readonly sessionId: string;
  readonly questionId: string;
  /** Whether the answer is correct. */
  readonly correct: boolean;
  /** Partial credit score (0.0 - 1.0). */
  readonly score: number;
  /** LLM-generated explanation of the evaluation. Markdown with LaTeX. */
  readonly explanation: string;
  /** Classified error type; `null` if the answer is correct. */
  readonly errorType: ErrorType | null;
  /** Hint for the next pedagogical move. */
  readonly nextAction: 'next-question' | 'retry' | 'hint' | 'switch-methodology' | 'concept-review';
  /** Updated mastery level for this concept (0.0 - 1.0, post-BKT). */
  readonly updatedMastery: number;
  /** XP earned for this answer. */
  readonly xpEarned: number;
}

export type AnswerEvaluatedEvent = MessageEnvelope<'AnswerEvaluated', AnswerEvaluatedPayload>;

// ── MasteryUpdated ──────────────────────────────────────────────────────────

/**
 * Fired when a concept crosses the mastery threshold (P(known) >= 0.85).
 * Server-authoritative — the client's local BKT prediction is advisory only.
 */
export interface MasteryUpdatedPayload {
  readonly conceptId: string;
  readonly conceptName: string;
  /** Final mastery level that triggered the event. */
  readonly masteryLevel: number;
  /** Total attempts across all sessions to reach mastery. */
  readonly attemptsToMastery: number;
  /** XP bonus for mastering the concept. */
  readonly xpBonus: number;
  /** Badge earned with this mastery; `null` if none. */
  readonly badgeEarned: {
    readonly id: string;
    readonly name: string;
    readonly iconUrl: string;
  } | null;
  /** Concepts now unlocked in the knowledge graph (met prerequisites). */
  readonly unlockedConcepts: ReadonlyArray<{
    readonly conceptId: string;
    readonly conceptName: string;
  }>;
}

export type MasteryUpdatedEvent = MessageEnvelope<'MasteryUpdated', MasteryUpdatedPayload>;

// ── MethodologySwitched ─────────────────────────────────────────────────────

/** The system has changed the teaching methodology for the current concept. */
export interface MethodologySwitchedPayload {
  readonly sessionId: string;
  readonly conceptId: string;
  readonly previousMethodology: MethodologyType;
  readonly newMethodology: MethodologyType;
  /** Human-readable explanation of why the switch occurred. Localized. */
  readonly reason: string;
  /** What triggered the switch. */
  readonly trigger: 'stagnation-detected' | 'student-requested' | 'mcm-recommendation' | 'initial-assignment';
}

export type MethodologySwitchedEvent = MessageEnvelope<'MethodologySwitched', MethodologySwitchedPayload>;

// ── SessionSummary ──────────────────────────────────────────────────────────

/** Confirms session termination and provides a summary for the end-of-session screen. */
export interface SessionSummaryPayload {
  readonly sessionId: string;
  /** Session duration in seconds. */
  readonly durationSeconds: number;
  /** Total questions attempted. */
  readonly questionsAttempted: number;
  /** Total correct answers. */
  readonly correctAnswers: number;
  /** Per-concept breakdown. */
  readonly conceptsTouched: ReadonlyArray<{
    readonly conceptId: string;
    readonly conceptName: string;
    /** Mastery delta during this session (can be negative if decay applied). */
    readonly masteryDelta: number;
  }>;
  /** Total XP earned during the session. */
  readonly xpEarned: number;
  /** Whether the streak was maintained by this session. */
  readonly streakMaintained: boolean;
  /** Server-reported end reason. */
  readonly reason:
    | 'completed'
    | 'tired'
    | 'out-of-time'
    | 'app-background'
    | 'manual'
    | 'cognitive-load'
    | 'timeout';
}

export type SessionSummaryEvent = MessageEnvelope<'SessionSummary', SessionSummaryPayload>;

// ── XpAwarded ───────────────────────────────────────────────────────────────

/** Gamification event — student earned XP. */
export interface XpAwardedPayload {
  /** XP earned in this event. */
  readonly amount: number;
  /** Source of the XP award. */
  readonly source:
    | 'correct-answer'
    | 'concept-mastered'
    | 'streak-bonus'
    | 'daily-login'
    | 'annotation'
    | 'speed-bonus';
  /** Total XP after this event. */
  readonly totalXP: number;
  /** Current level number. */
  readonly level: number;
  /** Progress within current level (0.0 - 1.0). */
  readonly levelProgress: number;
}

export type XpAwardedEvent = MessageEnvelope<'XpAwarded', XpAwardedPayload>;

// ── StreakUpdated ────────────────────────────────────────────────────────────

/** Streak state changed — extended, maintained, broken, or restored. */
export interface StreakUpdatedPayload {
  /** Current streak count in days. */
  readonly currentStreak: number;
  /** All-time longest streak. */
  readonly longestStreak: number;
  /** What happened to the streak. */
  readonly action: 'extended' | 'maintained' | 'broken' | 'restored';
  /** Remaining streak freezes (purchased or earned). */
  readonly freezesRemaining: number;
  /** ISO 8601 UTC — deadline to maintain the streak. */
  readonly expiresAt: string;
}

export type StreakUpdatedEvent = MessageEnvelope<'StreakUpdated', StreakUpdatedPayload>;

// ── KnowledgeGraphUpdated ───────────────────────────────────────────────────

/**
 * The student's knowledge graph overlay has changed.
 * Sent as a delta — only changed nodes and edges, not the full graph.
 */
export interface KnowledgeGraphUpdatedPayload {
  /** Changed concept nodes. */
  readonly updatedNodes: ReadonlyArray<{
    readonly conceptId: string;
    readonly conceptName: string;
    /** Updated mastery level (0.0 - 1.0). */
    readonly masteryLevel: number;
    /** Predicted recall from Half-Life Regression (0.0 - 1.0). */
    readonly predictedRecall: number;
    readonly status: MasteryStatus;
  }>;
  /** Changed prerequisite edges. */
  readonly updatedEdges: ReadonlyArray<{
    readonly fromConceptId: string;
    readonly toConceptId: string;
    readonly unlocked: boolean;
  }>;
}

export type KnowledgeGraphUpdatedEvent = MessageEnvelope<'KnowledgeGraphUpdated', KnowledgeGraphUpdatedPayload>;

// ── CognitiveLoadWarning ────────────────────────────────────────────────────

/** The system detects the student may be experiencing cognitive overload. */
export interface CognitiveLoadWarningPayload {
  readonly sessionId: string;
  /** Detected cognitive load level. */
  readonly level: 'elevated' | 'high' | 'critical';
  /** Signals that contributed to the detection. */
  readonly signals: ReadonlyArray<
    'response-time-increase' | 'error-rate-spike' | 'hint-dependency' | 'session-duration'
  >;
  /** Recommended action for the client to surface. */
  readonly recommendation: 'take-break' | 'reduce-difficulty' | 'switch-to-review' | 'end-session';
  /** Human-readable message. Localized. */
  readonly message: string;
  /** Suggested break duration in minutes; `null` if not a break recommendation. */
  readonly suggestedBreakMinutes: number | null;
}

export type CognitiveLoadWarningEvent = MessageEnvelope<'CognitiveLoadWarning', CognitiveLoadWarningPayload>;

// ── HintDelivered ───────────────────────────────────────────────────────────

/** Server delivers a hint in response to a RequestHint command. */
export interface HintDeliveredPayload {
  readonly sessionId: string;
  readonly questionId: string;
  /** Which hint level was delivered. */
  readonly hintLevel: 1 | 2 | 3;
  /** Hint content. Markdown with LaTeX support. */
  readonly hintText: string;
  /** Optional diagram accompanying the hint. */
  readonly diagram: string | null;
  /** Whether more hint levels are available. */
  readonly hasMoreHints: boolean;
}

export type HintDeliveredEvent = MessageEnvelope<'HintDelivered', HintDeliveredPayload>;

// ── StagnationDetected ──────────────────────────────────────────────────────

/** The stagnation detector identified a learning plateau. */
export interface StagnationDetectedPayload {
  readonly sessionId: string;
  readonly conceptId: string;
  /** Which signal triggered the detection. */
  readonly signal:
    | 'accuracy-plateau'
    | 'response-time-drift'
    | 'error-repetition'
    | 'annotation-sentiment'
    | 'abandonment-pattern';
  /** Recommended action for the client to surface. */
  readonly recommendation: 'switch-methodology' | 'take-break' | 'try-easier-concept' | 'watch-video';
  /** Human-readable message for the student. Localized. */
  readonly message: string;
}

export type StagnationDetectedEvent = MessageEnvelope<'StagnationDetected', StagnationDetectedPayload>;

// ── TutoringStarted ───────────────────────────────────────────────────────

/**
 * Server signals that a tutoring conversation has begun for a concept.
 * Sent once per tutoring episode when TutorActor is spawned and initialized.
 *
 * Triggers: AddAnnotation(kind: 'confusion' | 'question'), ConfusionStuck
 * auto-detection, or post-wrong-answer follow-up.
 *
 * @see tasks/student-ai-interaction/07-tutor-actor.md
 */
export interface TutoringStartedPayload {
  readonly sessionId: string;
  /** Concept being tutored. */
  readonly conceptId: string;
  /** Opening message from the tutor. Supports Markdown and LaTeX ($...$). */
  readonly openingMessage: string;
  /** Active tutoring methodology for this episode. */
  readonly methodology: string;
}

export type TutoringStartedEvent = MessageEnvelope<'TutoringStarted', TutoringStartedPayload>;

// ── TutoringResponse ───────────────────────────────────────────────────────

/**
 * Server delivers a tutoring conversation turn in response to an annotation
 * or ongoing tutoring dialogue. Supports Markdown + LaTeX content.
 *
 * Triggers: AddAnnotation(kind: 'confusion' | 'question'), ConfusionStuck
 * auto-detection, post-wrong-answer follow-up, or ongoing TutorMessage.
 *
 * @see tasks/student-ai-interaction/07-tutor-actor.md
 */
export interface TutoringResponsePayload {
  readonly sessionId: string;
  /** Current turn number in the tutoring episode (1-based). */
  readonly turnNumber: number;
  /** Tutor response content. Supports Markdown and LaTeX ($...$). */
  readonly response: string;
  /** True when the tutoring episode has ended (max turns, budget, or student exit). */
  readonly isComplete: boolean;
  /** Number of turns remaining before the hard cap (max 10). */
  readonly remainingTurns: number;
}

export type TutoringResponseEvent = MessageEnvelope<'TutoringResponse', TutoringResponsePayload>;

// ── SendTutoringMessage ────────────────────────────────────────────────────

/** Student sends a message in an active tutoring conversation. */
export interface SendTutoringMessagePayload {
  readonly sessionId: string;
  /** Free-text message from the student. Supports Markdown. */
  readonly message: string;
}

export type SendTutoringMessage = MessageEnvelope<'SendTutoringMessage', SendTutoringMessagePayload>;


// ── TutoringEnded ─────────────────────────────────────────────────────────

/**
 * Server signals that a tutoring conversation has ended.
 * Sent when the episode terminates (max turns, budget exhausted, student exit,
 * or 5-minute inactivity timeout).
 *
 * @see tasks/student-ai-interaction/07-tutor-actor.md
 */
export interface TutoringEndedPayload {
  readonly sessionId: string;
  /** Concept that was being tutored. */
  readonly conceptId: string;
  /** Human-readable summary of what was covered. Localized. */
  readonly summary: string;
  /** Suggested next action for the client. */
  readonly nextAction: 'next-question' | 'review-concept' | 'take-break' | 'end-session';
}

export type TutoringEndedEvent = MessageEnvelope<'TutoringEnded', TutoringEndedPayload>;

// ── Error ───────────────────────────────────────────────────────────────────

/**
 * Error envelope. The `correlationId` maps to the command that caused the error.
 * Machine-readable `code` enables client-side error handling logic.
 */
export interface ErrorPayload {
  /** Machine-readable error code. */
  readonly code: SignalRErrorCode;
  /** Human-readable message. Localized based on student locale. */
  readonly message: string;
  /** Optional structured details for debugging. */
  readonly details: Record<string, unknown> | null;
}

export type SignalRErrorCode =
  | 'SESSION_NOT_FOUND'
  | 'SESSION_ALREADY_ACTIVE'
  | 'QUESTION_EXPIRED'
  | 'RATE_LIMITED'
  | 'UNAUTHORIZED'
  | 'INTERNAL_ERROR'
  | 'METHODOLOGY_UNAVAILABLE'
  | 'CONCEPT_NOT_ACCESSIBLE'
  | 'SYNC_IN_PROGRESS';

export type ErrorEvent = MessageEnvelope<'Error', ErrorPayload>;

// ─────────────────────────────────────────────────────────────────────────────
// 5. Union Types for Exhaustive Switch
// ─────────────────────────────────────────────────────────────────────────────

/** Union of all client -> server command messages. */
export type ClientCommand =
  | StartSession
  | SubmitAnswer
  | EndSession
  | RequestHint
  | SkipQuestion
  | AddAnnotation
  | SwitchApproach
  | RequestNextConcept
  | UpdatePreferences
  | SendTutoringMessage;

/** Union of all server -> client event messages. */
export type ServerEvent =
  | SessionStartedEvent
  | QuestionPresentedEvent
  | AnswerEvaluatedEvent
  | MasteryUpdatedEvent
  | MethodologySwitchedEvent
  | SessionSummaryEvent
  | XpAwardedEvent
  | StreakUpdatedEvent
  | KnowledgeGraphUpdatedEvent
  | CognitiveLoadWarningEvent
  | HintDeliveredEvent
  | StagnationDetectedEvent
  | TutoringStartedEvent
  | TutoringResponseEvent
  | TutoringEndedEvent
  | ErrorEvent;

/** Extract the `type` discriminator from a message union member. */
export type MessageType<M extends MessageEnvelope<string, unknown>> = M['type'];

/** Extract the payload type from a message given its type discriminator. */
export type PayloadOf<
  T extends string,
  M extends MessageEnvelope<string, unknown> = ServerEvent,
> = Extract<M, { type: T }>['payload'];

// ─────────────────────────────────────────────────────────────────────────────
// 6. Connection Lifecycle
// ─────────────────────────────────────────────────────────────────────────────

/**
 * SignalR connection state machine.
 *
 * ```
 * DISCONNECTED ─► CONNECTING ─► CONNECTED ─► RECONNECTING ─► CONNECTED
 *                     │                           │
 *                     └──► DISCONNECTED ◄─────────┘
 * ```
 */
export enum ConnectionState {
  /** No active connection. Initial state and terminal state after explicit disconnect. */
  Disconnected = 'disconnected',
  /** Attempting initial connection or re-connection after explicit connect() call. */
  Connecting = 'connecting',
  /** Fully connected. Hub methods are callable. */
  Connected = 'connected',
  /** Connection lost; automatic reconnection in progress. */
  Reconnecting = 'reconnecting',
}

/**
 * Reconnection strategy configuration.
 * Uses exponential backoff with jitter to avoid thundering herd on server recovery.
 *
 * Default values are tuned for mobile networks with intermittent connectivity.
 */
export interface ReconnectionStrategy {
  /** Maximum number of reconnection attempts before giving up. Default: 8. */
  readonly maxRetries: number;
  /** Base delay in milliseconds before first retry. Default: 1000. */
  readonly baseDelayMs: number;
  /** Maximum delay cap in milliseconds. Default: 30000. */
  readonly maxDelayMs: number;
  /** Backoff multiplier applied to each subsequent retry. Default: 2.0. */
  readonly backoffMultiplier: number;
  /**
   * Jitter factor (0.0 - 1.0). Applied as random +/- percentage to each
   * calculated delay to avoid thundering herd. Default: 0.3.
   */
  readonly jitterFactor: number;
  /**
   * Whether to attempt a full sync (offline queue flush) after reconnection.
   * Should be `true` for the learning session hub. Default: true.
   */
  readonly syncOnReconnect: boolean;
}

/** Default reconnection strategy suitable for mobile learning sessions. */
export const DEFAULT_RECONNECTION_STRATEGY: ReconnectionStrategy = {
  maxRetries: 8,
  baseDelayMs: 1_000,
  maxDelayMs: 30_000,
  backoffMultiplier: 2.0,
  jitterFactor: 0.3,
  syncOnReconnect: true,
} as const;

/**
 * Connection event emitted by the SignalR client wrapper.
 * Components subscribe to these for UI state (e.g., offline banner).
 */
export interface ConnectionEvent {
  readonly state: ConnectionState;
  /** Current retry attempt number (0 when connected). */
  readonly retryAttempt: number;
  /** Estimated next retry time in ms; `null` when connected or giving up. */
  readonly nextRetryMs: number | null;
  /** Error that caused the disconnection; `null` on clean transitions. */
  readonly error: string | null;
}

// ─────────────────────────────────────────────────────────────────────────────
// 7. Hub Interface Contract
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Type-safe interface for the SignalR hub proxy.
 *
 * Implementations (e.g., `@microsoft/signalr` on web, community wrapper on RN)
 * should satisfy this contract. The `invoke` methods are for commands; the `on`
 * methods register handlers for server events.
 *
 * @example
 * ```tsx
 * const hub: CenaHubProxy = createCenaHub(token);
 * hub.on('QuestionPresented', (payload) => {
 *   // payload is QuestionPresentedPayload — fully typed
 *   dispatch(questionReceived(payload));
 * });
 * await hub.invoke('SubmitAnswer', answerPayload);
 * ```
 */
export interface CenaHubProxy {
  /** Current connection state. */
  readonly state: ConnectionState;

  /** Start the connection with the given JWT. */
  start(accessToken: string): Promise<void>;

  /** Gracefully disconnect. */
  stop(): Promise<void>;

  /**
   * Invoke a client -> server command.
   * Returns when the server acknowledges receipt (not processing completion).
   */
  invoke<T extends ClientCommand['type']>(
    type: T,
    payload: PayloadOf<T, ClientCommand>,
  ): Promise<void>;

  /**
   * Register a handler for a server -> client event.
   * Returns an unsubscribe function.
   */
  on<T extends ServerEvent['type']>(
    type: T,
    handler: (payload: PayloadOf<T, ServerEvent>) => void,
  ): () => void;

  /**
   * Register a handler for connection lifecycle events.
   * Returns an unsubscribe function.
   */
  onConnectionChange(handler: (event: ConnectionEvent) => void): () => void;
}
