/**
 * Cena Platform — Mock-exam (Bagrut שאלון playbook) API types.
 *
 * Mirror of the C# DTOs in
 * src/actors/Cena.Actors/Assessment/MockExamRunDtos.cs. Shape parity
 * is load-bearing — the runner UI deserializes these directly from
 * the wire.
 */

export type ExamCode = '806' | '807' | '036'

/** Request to start a new mock-exam run. */
export interface StartMockExamRunRequest {
  examCode: ExamCode
  /** Optional Ministry שאלון code (e.g. "035582"). Display-only today. */
  paperCode?: string
}

export interface MockExamRunStartedResponse {
  runId: string
  examCode: string
  paperCode: string | null
  timeLimitMinutes: number
  partAQuestionCount: number
  partBQuestionCount: number
  partBRequiredCount: number
  partAQuestionIds: string[]
  partBQuestionIds: string[]
  startedAt: string
  deadline: string
}

export interface MockExamRunStateResponse {
  runId: string
  examCode: string
  paperCode: string | null
  timeLimitMinutes: number
  startedAt: string
  deadline: string
  isExpired: boolean
  isSubmitted: boolean
  partAQuestionIds: string[]
  partBQuestionIds: string[]
  partBSelectedIds: string[]
  answeredIds: string[]
}

export interface SelectPartBRequest {
  selectedQuestionIds: string[]
}

export interface SubmitAnswerRequest {
  questionId: string
  answer: string
}

export interface MockExamPerQuestionResult {
  questionId: string
  section: 'A' | 'B'
  attempted: boolean
  correct: boolean | null
  studentAnswer: string | null
  canonicalAnswer: string | null
  gradingEngine: string
}

export interface MockExamResultResponse {
  runId: string
  examCode: string
  paperCode: string | null
  totalQuestions: number
  questionsAttempted: number
  questionsCorrect: number
  scorePercent: number
  timeTaken: string
  timeLimit: string
  visibilityWarnings: number
  perQuestion: MockExamPerQuestionResult[]
}
