/**
 * Shared API types for the student web app.
 * STB-10 will replace with codegen from `Cena.Api.Contracts`.
 */

export interface ApiError {
  code: string
  message: string
  correlationId?: string
  details?: unknown
}

export interface PaginatedResponse<T> {
  items: T[]
  total: number
  page: number
  pageSize: number
}

/**
 * `GET /api/analytics/summary` response. Mirrors the real C# record
 * `Cena.Api.Contracts.Analytics.AnalyticsSummaryDto`. All fields are
 * overall-to-date, computed server-side from the student's event stream
 * and `StudentProfileSnapshot`. There is no per-"today" summary in this
 * payload — for today-specific values the front end queries
 * `GET /api/analytics/time-breakdown` (returns the last 30 days with
 * today as the final entry).
 *
 * `overallAccuracy` is a normalized 0..1 value. UI code must multiply by
 * 100 to render a percentage.
 */
export interface AnalyticsSummaryDto {
  totalSessions: number
  totalQuestionsAttempted: number
  overallAccuracy: number
  currentStreak: number
  longestStreak: number
  totalXp: number
  level: number
}

export interface SessionSummary {
  sessionId: string
  startedAt: string
  endedAt: string | null
  durationMs: number
  subject: string
  questionsAnswered: number
  accuracyPercent: number
}

export interface MeProfile {
  uid: string
  displayName: string
  email: string
  locale: 'en' | 'ar' | 'he'
  onboardedAt: string | null
  role: 'student' | 'self-learner' | 'test-prep' | 'homeschool'
}

/**
 * `GET /api/me` bootstrap payload from STB-00.
 * Mirrors `src/api/Cena.Api.Contracts/Me/MeDtos.cs:MeBootstrapDto`.
 * STB-10 will replace this with TypeScript codegen.
 */
export interface MeBootstrapDto {
  studentId: string
  displayName: string
  role: string
  locale: 'en' | 'ar' | 'he'
  onboardedAt: string | null
  subjects: string[]
  level: number
  streakDays: number
  avatarUrl: string | null
}

/**
 * Gamification DTOs from STB-03 Phase 1. Mirrors
 * `src/api/Cena.Api.Contracts/Gamification/GamificationDtos.cs`.
 * Consumed by STU-W-07 progress dashboard.
 */
export type BadgeTier = 'bronze' | 'silver' | 'gold' | 'platinum'

export interface Badge {
  badgeId: string
  name: string
  description: string
  iconName: string
  tier: BadgeTier
  earnedAt: string | null
}

export interface BadgeListResponse {
  earned: Badge[]
  locked: Badge[]
}

export interface XpStatusDto {
  currentLevel: number
  currentXp: number
  xpToNextLevel: number
  totalXpEarned: number
}

export interface StreakStatusDto {
  currentDays: number
  longestDays: number
  lastActivityAt: string | null
  isAtRisk: boolean
}

export interface LeaderboardEntry {
  rank: number
  studentId: string
  displayName: string
  xp: number
  avatarUrl: string | null
}

export interface LeaderboardDto {
  scope: string
  entries: LeaderboardEntry[]
  currentStudentRank: number
}

/**
 * Tutor DTOs from STB-04 Phase 1. Mirrors
 * `src/api/Cena.Api.Contracts/Tutor/TutorDtos.cs`. Consumed by STU-W-08.
 */
export type TutorMessageRole = 'user' | 'assistant' | 'system'

export interface TutorThreadDto {
  threadId: string
  title: string
  subject: string | null
  topic: string | null
  createdAt: string
  updatedAt: string
  messageCount: number
  isArchived: boolean
}

export interface TutorThreadListDto {
  items: TutorThreadDto[]
  totalCount: number
}

export interface TutorMessageDto {
  messageId: string
  role: TutorMessageRole
  content: string
  createdAt: string
  model: string | null
}

export interface TutorMessageListDto {
  threadId: string
  messages: TutorMessageDto[]
  hasMore: boolean
}

export interface SendMessageResponse {
  messageId: string
  role: TutorMessageRole
  content: string
  createdAt: string
  status: 'complete' | 'streaming'
}

export interface CreateThreadResponse {
  threadId: string
  title: string
  createdAt: string
}

/**
 * Challenges DTOs from STB-05 Phase 1. Mirrors
 * `src/api/Cena.Api.Contracts/Challenges/ChallengesDtos.cs`.
 * Consumed by STU-W-11 challenges hub.
 */
export interface DailyChallengeDto {
  challengeId: string
  title: string
  description: string
  subject: string
  difficulty: 'easy' | 'medium' | 'hard'
  expiresAt: string
  attempted: boolean
  bestScore: number | null
}

export interface BossBattleSummary {
  bossBattleId: string
  name: string
  subject: string
  difficulty: string
  requiredMasteryLevel: number
}

export interface BossBattleListDto {
  available: BossBattleSummary[]
  locked: BossBattleSummary[]
}

export interface CardChainSummary {
  chainId: string
  name: string
  cardsUnlocked: number
  cardsTotal: number
  lastUnlockedAt: string | null
}

export interface CardChainListDto {
  chains: CardChainSummary[]
}

export interface TournamentSummary {
  tournamentId: string
  name: string
  startsAt: string
  endsAt: string
  participantCount: number
  isRegistered: boolean
}

export interface TournamentListDto {
  upcoming: TournamentSummary[]
  active: TournamentSummary[]
}

/**
 * Session DTOs from STB-01 + STB-01b. Mirrors
 * `src/api/Cena.Api.Contracts/Sessions/SessionDtos.cs`. Consumed by STU-W-06.
 */
export type SessionMode = 'practice' | 'challenge' | 'review' | 'diagnostic'

export interface SessionStartRequest {
  subjects: string[]
  durationMinutes: number
  mode: SessionMode
}

export interface SessionStartResponse {
  sessionId: string
  hubGroupName: string
  firstQuestionId: string | null
}

export interface ActiveSessionDto {
  sessionId: string
  subjects: string[]
  mode: SessionMode
  startedAt: string
  durationMinutes: number
  progressPercent: number
  currentQuestionId: string | null
}

/**
 * FIND-pedagogy-006 — Scaffolding metadata surfaced to the Vue session
 * flow so novices get worked examples, intermediates get hints, and
 * experts get independent practice. Mirrors
 * `Cena.Actors.Mastery.ScaffoldingLevel`.
 *
 * Citations:
 *   - Sweller et al. (1998) Cognitive Architecture and Instructional
 *     Design, Educational Psychology Review 10(3), 251-296.
 *     DOI: 10.1023/A:1022193728205 (worked example effect)
 *   - Renkl & Atkinson (2003) Educational Psychologist 38(1), 15-22.
 *     DOI: 10.1207/S15326985EP3801_3 (faded examples)
 *   - Kalyuga et al. (2003) Educational Psychologist 38(1), 23-31.
 *     DOI: 10.1207/S15326985EP3801_4 (expertise reversal effect)
 */
export type ScaffoldingLevelDto = 'Full' | 'Partial' | 'HintsOnly' | 'None'

/**
 * RDY-013 — A single step in a worked or faded example.
 */
export interface WorkedExampleStepDto {
  /** Human-readable description of the step. */
  description: string
  /** KaTeX math expression (rendered with KaTeX). */
  math?: string | null
  /** Short prose explanation of the step. */
  explanation?: string | null
}

/**
 * RDY-013 — Structured worked example with step-by-step breakdown.
 * At Full scaffolding all steps are shown; at Partial later steps are
 * faded (blanked) for the student to fill in.
 */
export interface WorkedExampleDto {
  /** Steps of the worked example. */
  steps: WorkedExampleStepDto[]
}

/**
 * prr-206 — Step-solver step shape. Mirrors server-side
 * `StepSolverQuestion.Step`. Delivered when `questionType === 'step_solver'`.
 */
export interface StepSolverStepDto {
  stepNumber: number
  /** Instruction shown to the student (e.g. "isolate the variable"). */
  instruction?: string
  /** Human-friendly faded-example scaffold, when scaffoldingLevel='Partial'. */
  fadedExample?: string
  /** Expected canonical expression. Not shown to student directly. */
  expectedExpression?: string
  /** Per-step hint templates, available to the per-step hint ladder. */
  hints?: string[]
  /** BKT mastery discount for scaffolded attempts. Server-driven. */
  scaffoldingPenalty?: number
}

/**
 * prr-208 — Physics figure spec subset consumed by the runner to
 * decide whether to render an interactive FBD Construct.
 */
export interface PhysicsFigureSpec {
  type: 'PhysicsDiagramSpec' | string
  diagramMode?: 'Display' | 'Construct'
  sceneSvg?: string
  bodyCenter?: { x: number; y: number }
  expectedForces?: { label: string; magnitude: number; angleDeg: number }[]
  methodology?: string
  ariaLabel?: string
}

export interface SessionQuestionDto {
  questionId: string
  questionIndex: number
  totalQuestions: number
  prompt: string
  /** prr-206 adds 'step_solver' to the discriminator set. */
  questionType: 'multiple-choice' | 'short-answer' | 'numeric' | 'step_solver'
  choices: string[]
  subject: string
  expectedTimeSeconds: number

  /**
   * FIND-pedagogy-006 — scaffolding level computed from the student's
   * current mastery on this question's concept. Drives whether the UI
   * renders a worked example, a hint button, or nothing.
   * Omitted by backend only on session-completion synthetic responses.
   */
  scaffoldingLevel?: ScaffoldingLevelDto

  /**
   * RDY-013 — Worked example data. May be a legacy plain string or a
   * structured object with steps[]. Only populated when
   * scaffoldingLevel is 'Full' or 'Partial'.
   */
  workedExample?: string | WorkedExampleDto | null

  /** Total hint budget granted for this scaffolding level. */
  hintsAvailable?: number

  /** Remaining hints the student can still request (mirrors hintsAvailable on first load). */
  hintsRemaining?: number

  /** prr-206 — populated for step_solver questions only. */
  steps?: StepSolverStepDto[] | null

  /** prr-206 — canonical final answer expression (server CAS reference). */
  finalAnswer?: string | null

  /**
   * prr-208 — optional figure spec; physics + Construct => FBD renderer.
   * Loosely typed to stay compatible with QuestionFigure's internal
   * FigureSpec interface. Runtime guards in the runner page + children
   * narrow it to PhysicsFigureSpec when the methodology tag allows.
   */
  figureSpec?: any

  /** prr-208 — physics methodology tag ("mechanics.fbd"); drives FBD routing. */
  methodology?: string | null

  /**
   * prr-205 — BKT mastery bucket ('low' | 'mid' | 'high' | 'unknown')
   * used client-side for expertise-reversal gating. Server authoritative;
   * when 'high' (>0.60 mastery) the HintLadder stays collapsed by default.
   */
  bktMasteryBucket?: 'low' | 'mid' | 'high' | 'unknown'
}

// =============================================================================
// prr-203 — Hint ladder DTO (mirrors C# HintLadderResponseDto)
// =============================================================================

export interface HintLadderResponseDto {
  rung: number
  body: string
  rungSource: 'template' | 'haiku' | 'sonnet' | 'template-fallback' | string
  maxRungReached: number
  nextRungAvailable: boolean
}

// =============================================================================
// prr-206 — Step-solver submit DTOs
// =============================================================================

export interface StepSolverSubmitRequest {
  stepNumber: number
  /** Student-submitted LaTeX or plain expression. */
  expression: string
  timeSpentMs: number
  /** Hints consumed on this step (BKT scaffolding adjustment). */
  hintsConsumed?: number
}

export interface StepSolverSubmitResponseDto {
  correct: boolean
  /** AST-diff feedback string; null on correct. */
  astDiff?: string | null
  /** Optional misconception tag (session-scoped, ADR-0003). */
  misconceptionTag?: string | null
  /** True when CAS decided this wrong path is pedagogically productive. */
  isProductiveFailurePath?: boolean
  /** Whether a next step is now unlocked. */
  nextStepUnlocked?: boolean
  /** CAS sidecar rest-state ("checking later" → queued verification). */
  queuedForLaterVerification?: boolean
}

// =============================================================================
// prr-208 — FBD submission DTOs
// =============================================================================

export interface FbdForceDto {
  label: string
  magnitude: number
  angleDeg: number
}

export interface FbdSubmissionRequest {
  forces: FbdForceDto[]
  timeSpentMs: number
}

export interface FbdForceVerdictDto {
  label: string
  directionCorrect: boolean
  magnitudeCorrect: boolean
  componentsCorrect: boolean
  feedback?: string | null
}

export interface FbdSubmissionResponseDto {
  correct: boolean
  perForceVerdicts: FbdForceVerdictDto[]
  /** Σ F = 0 or Σ F = ma consistency. */
  netForceValid: boolean
  feedback?: string | null
}

// =============================================================================
// prr-204 — Tutor Context DTO (mirrors C# TutorContextResponseDto)
// =============================================================================

export interface TutorContextAccommodationDto {
  ldAnxiousFriendly: boolean
  extendedTimeMultiplier: number
  distractionReducedLayout: boolean
  ttsForProblemStatements: boolean
}

export interface TutorContextResponseDto {
  sessionId: string
  currentQuestionId: string | null
  answeredCount: number
  correctCount: number
  currentRung: number
  lastMisconceptionTag: string | null
  attemptPhase: 'first_try' | 'retry' | 'post_solution' | string
  elapsedMinutes: number
  dailyMinutesRemaining: number
  bktMasteryBucket: 'low' | 'mid' | 'high' | 'unknown' | string
  accommodationFlags: TutorContextAccommodationDto
  builtAtUtc: string
}

// =============================================================================
// prr-207 — Tutor turn request/response (non-streaming)
// =============================================================================

export type SidekickIntent =
  | 'explain_question'
  | 'explain_step'
  | 'explain_concept'
  | 'free_form'

export interface TutorTurnRequest {
  intent: SidekickIntent
  userMessage?: string
  stepIndex?: number
}

export interface TutorTurnResponseDto {
  /** Final assembled assistant message (non-streaming fallback). */
  content: string
  model: string | null
  cached: boolean
}

export interface SessionAnswerRequest {
  questionId: string
  answer: string
  timeSpentMs: number
}

export interface SessionAnswerResponseDto {
  correct: boolean
  feedback: string
  xpAwarded: number
  masteryDelta: number
  nextQuestionId: string | null

  /**
   * FIND-pedagogy-001 — authored worked explanation for the question
   * (QuestionDocument.Explanation). Rendered in a dedicated block so
   * students can read the rationale at their own pace.
   */
  explanation?: string | null

  /**
   * FIND-pedagogy-001 — authored rationale for the SPECIFIC wrong option
   * the student chose. Null on correct answers and when no per-option
   * rationale exists.
   */
  distractorRationale?: string | null
}

/**
 * FIND-pedagogy-006 — Response for POST /api/sessions/{id}/question/{qid}/hint.
 * Progressive hints generated by the backend HintGenerator service.
 */
export interface SessionHintResponseDto {
  hintLevel: number
  hintText: string
  hasMoreHints: boolean
  hintsRemaining: number
}

export interface SessionCompletedDto {
  sessionId: string
  totalCorrect: number
  totalWrong: number
  totalXpAwarded: number
  accuracyPercent: number
  durationSeconds: number
}

/**
 * Analytics DTOs from STB-09 Phase 1. Mirrors
 * `src/api/Cena.Api.Contracts/Analytics/AnalyticsDtos.cs`.
 * Consumed by STU-W-09 progress subpages.
 */
export interface TimeBreakdownItem {
  date: string
  minutes: number
}

export interface TimeBreakdownDto {
  items: TimeBreakdownItem[]
}

export interface FlowAccuracyPoint {
  timestamp: string
  flowScore: number
  accuracyPercent: number
}

export interface FlowAccuracyDto {
  points: FlowAccuracyPoint[]
}

/**
 * Knowledge/content DTOs from STB-08 Phase 1. Mirrors
 * `src/api/Cena.Api.Contracts/Content/KnowledgeDtos.cs`.
 * Consumed by STU-W-10 knowledge graph page.
 */
export type ConceptDifficulty = 'beginner' | 'intermediate' | 'advanced'
export type ConceptStatus = 'locked' | 'available' | 'in-progress' | 'mastered'

export interface ConceptSummary {
  conceptId: string
  name: string
  subject: string
  topic: string | null
  difficulty: ConceptDifficulty
  status: ConceptStatus
}

export interface ConceptListDto {
  items: ConceptSummary[]
}

export interface ConceptDetailDto {
  conceptId: string
  name: string
  description: string
  subject: string
  topic: string | null
  difficulty: ConceptDifficulty
  status: ConceptStatus
  currentMastery: number | null
  prerequisites: string[]
  dependencies: string[]
  estimatedMinutes: number
  questionCount: number
}

export interface PathNode {
  conceptId: string
  name: string
  stepNumber: number
  status: ConceptStatus
}

export interface PathEdge {
  fromConceptId: string
  toConceptId: string
  relationship: 'prerequisite' | 'dependency'
}

export interface PathDto {
  fromConceptId: string
  toConceptId: string
  nodes: PathNode[]
  edges: PathEdge[]
  totalSteps: number
  estimatedMinutes: number
}

/**
 * Social DTOs from STB-06 Phase 1 + STB-06b Phase 1b. Mirrors
 * `src/api/Cena.Api.Contracts/Social/SocialDtos.cs`. Consumed by STU-W-12.
 */
export type FeedItemKind = 'achievement' | 'milestone' | 'question' | 'announcement'

export interface ClassFeedItem {
  itemId: string
  kind: FeedItemKind
  authorStudentId: string
  authorDisplayName: string
  authorAvatarUrl: string | null
  title: string
  body: string | null
  postedAt: string
  reactionCount: number
  commentCount: number
}

export interface ClassFeedDto {
  items: ClassFeedItem[]
  page: number
  pageSize: number
  hasMore: boolean
}

export interface PeerSolution {
  solutionId: string
  questionId: string
  authorStudentId: string
  authorDisplayName: string
  content: string
  upvoteCount: number
  downvoteCount: number
  postedAt: string
}

export interface PeerSolutionListDto {
  solutions: PeerSolution[]
}

export interface Friend {
  studentId: string
  displayName: string
  avatarUrl: string | null
  level: number
  streakDays: number
  isOnline: boolean
}

export interface FriendRequest {
  requestId: string
  fromStudentId: string
  fromDisplayName: string
  fromAvatarUrl: string | null
  requestedAt: string
}

export interface FriendsListDto {
  friends: Friend[]
  pendingRequests: FriendRequest[]
}

/**
 * Notification DTOs from STB-07 Phase 1. Mirrors
 * `src/api/Cena.Api.Contracts/Notifications/NotificationsDtos.cs`.
 * Consumed by STU-W-14 notifications center.
 */
export type NotificationKind = 'xp' | 'badge' | 'streak' | 'friend-request' | 'review-due' | 'system'
export type NotificationPriority = 'low' | 'normal' | 'high'

export interface NotificationItem {
  notificationId: string
  kind: NotificationKind
  priority: NotificationPriority
  title: string
  body: string
  iconName: string | null
  deepLinkUrl: string | null
  read: boolean
  createdAt: string
}

export interface NotificationListDto {
  items: NotificationItem[]
  page: number
  pageSize: number
  total: number
  hasMore: boolean
  unreadCount: number
}

export interface UnreadCountDto {
  count: number
}

/**
 * Profile DTOs from STB-00. Mirrors
 * `src/api/Cena.Api.Contracts/Me/MeDtos.cs:ProfileDto + ProfilePatchDto`.
 */
export interface ProfileDto {
  studentId: string
  displayName: string
  email: string
  avatarUrl: string | null
  bio: string | null
  favoriteSubjects: string[]
  visibility: 'public' | 'class-only' | 'private'
}

export interface ProfilePatchDto {
  displayName?: string
  bio?: string
  favoriteSubjects?: string[]
  visibility?: 'public' | 'class-only' | 'private'
}

/**
 * FIND-privacy-018: Content Reporting & User Blocking DTOs.
 * ICO Children's Code Std 11 — safeguarding tools for minors.
 */
export type ReportContentType = 'feed-item' | 'comment' | 'peer-solution' | 'friend-request' | 'study-room'
export type ReportCategory = 'bullying' | 'inappropriate' | 'spam' | 'self-harm-risk' | 'other'

export interface SubmitReportRequest {
  contentType: ReportContentType
  contentId: string
  category: ReportCategory
  reason?: string
}

export interface SubmitReportResponse {
  reportId: string
  severity: string
  reportedAt: string
}

export interface BlockUserRequest {
  targetStudentId: string
}

export interface BlockUserResponse {
  ok: boolean
  targetStudentId: string
  blockedAt: string
}

export interface UnblockUserResponse {
  ok: boolean
  targetStudentId: string
}
