/**
 * SignalR hub envelopes and event payloads. Mirrors the shapes
 * defined in `src/api/Cena.Api.Host/Hubs/HubContracts.cs` (and
 * post-DB-05 in `src/api/Cena.Api.Contracts/Hub/HubContracts.cs`).
 *
 * Hand-written for STU-W-03; STB-10 will introduce TS codegen from
 * the .NET contracts assembly and replace this file.
 */

export interface BusEnvelope<T> {
  type: string
  payload: T
  correlationId: string
  tenantId?: string
  studentId?: string
  timestamp: string
}

// Session lifecycle events
export interface SessionStartedEvent {
  sessionId: string
  studentId: string
  startedAt: string
}

export interface SessionEndedEvent {
  sessionId: string
  endedAt: string
  durationMs: number
  questionsAnswered: number
  accuracyPercent: number
}

// Learning events
export interface AnswerEvaluatedEvent {
  sessionId: string
  questionId: string
  correct: boolean
  partialCreditPercent: number
  confidence: number | null
}

export interface MasteryUpdatedEvent {
  studentId: string
  conceptId: string
  previousLevel: string
  newLevel: string
  deltaPercent: number
}

export interface HintDeliveredEvent {
  sessionId: string
  questionId: string
  hintIndex: number
  hintText: string
}

// Gamification events
export interface XpAwardedEvent {
  studentId: string
  reason: string
  amount: number
  newTotal: number
}

export interface StreakUpdatedEvent {
  studentId: string
  days: number
  isNewBest: boolean
}

export interface BadgeEarnedEvent {
  studentId: string
  badgeId: string
  badgeName: string
  earnedAt: string
}

// Tutoring events
export interface TutorMessageEvent {
  threadId: string
  messageId: string
  role: 'user' | 'assistant' | 'system'
  content: string
  createdAt: string
}

// System
export interface HubErrorEvent {
  code: string
  message: string
  correlationId?: string
}

export interface CommandAckEvent {
  commandId: string
  status: 'accepted' | 'rejected'
  reason?: string
}

/**
 * Map of hub event type strings → payload TypeScript type.
 * Used by `subscribe<K>(...)` to infer payloads from the event name.
 */
export interface HubEventMap {
  SessionStarted: SessionStartedEvent
  SessionEnded: SessionEndedEvent
  AnswerEvaluated: AnswerEvaluatedEvent
  MasteryUpdated: MasteryUpdatedEvent
  HintDelivered: HintDeliveredEvent
  XpAwarded: XpAwardedEvent
  StreakUpdated: StreakUpdatedEvent
  BadgeEarned: BadgeEarnedEvent
  TutorMessage: TutorMessageEvent
  Error: HubErrorEvent
  CommandAck: CommandAckEvent
}

export type HubEventName = keyof HubEventMap
