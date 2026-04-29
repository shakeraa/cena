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
  /** Phase 2B — accommodation extra-time as a percentage of the canonical
   * exam time. Server clamps to [0, 100]. Default 0 = standard time. */
  extraTimePercent?: number
}

export interface MockExamRunStartedResponse {
  runId: string
  examCode: string
  paperCode: string | null
  timeLimitMinutes: number
  extraTimeMinutes: number
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
  extraTimeMinutes: number
  startedAt: string
  deadline: string
  isExpired: boolean
  isSubmitted: boolean
  partAQuestionIds: string[]
  partBQuestionIds: string[]
  partBSelectedIds: string[]
  /** Multi-part subparts use composite "{qid}:{subpartId}" keys here. */
  answeredIds: string[]
  calculatorPolicy: 'Allowed' | 'Restricted' | 'Prohibited'
  formulaSheetMode: 'None' | 'MathBasic' | 'MathAdvanced' | 'PhysicsStandard'
}

export interface SelectPartBRequest {
  selectedQuestionIds: string[]
}

export interface SubmitAnswerRequest {
  questionId: string
  answer: string
  /** Phase 2A — non-null only for multi-part questions ("a"/"b"/"c"). */
  subpartId?: string
}

export interface MockExamSubpartResult {
  subpartId: string
  attempted: boolean
  correct: boolean | null
  studentAnswer: string | null
  canonicalAnswer: string | null
  gradingEngine: string
  points: number
  pointsAwarded: number
}

export interface MockExamPerQuestionResult {
  questionId: string
  section: 'A' | 'B'
  attempted: boolean
  correct: boolean | null
  studentAnswer: string | null
  canonicalAnswer: string | null
  gradingEngine: string
  points: number
  pointsAwarded: number
  /** Phase 2A — non-null when the question is multi-part. */
  subparts?: MockExamSubpartResult[]
}

export interface MockExamSectionResult {
  sectionLabel: string
  attempted: number
  correct: number
  pointsAwarded: number
  totalPoints: number
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
  pointsAwarded: number
  totalPoints: number
  perSection: MockExamSectionResult[]
}

/** Phase 1G — feature-flag probe shape. */
export interface ExamPrepFeatureFlags {
  runnerEnabled: boolean
  tenantOverride?: boolean
}

/** PRR-294 — recent-run summary for the result-page trend card. */
export interface MockExamRunSummary {
  runId: string
  examCode: string
  paperCode: string | null
  startedAt: string
  submittedAt: string
  pointsAwarded: number
  totalPoints: number
  scorePercent: number
}

/** Phase 1D — per-question read shape (for Part-B preview before lock). */
export interface ExamPrepQuestionPreview {
  questionId: string
  prompt: string
  topic: string | null
  bloomsLevel: number
  /** Phase 2A — non-null for multi-part Q's. The runner renders one
   * input per subpart; the parent prompt becomes the shared question stem. */
  subparts?: ExamPrepSubpartPreview[]
}

export interface ExamPrepSubpartPreview {
  partId: string
  prompt: string
  points: number
}
