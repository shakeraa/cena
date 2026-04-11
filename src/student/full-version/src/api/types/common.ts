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

export interface AnalyticsSummaryDto {
  studentId: string
  minutesThisWeek: number
  questionsAnswered: number
  accuracyPercent: number
  streakDays: number
  xp: number
  updatedAt: string
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

export interface SessionQuestionDto {
  questionId: string
  questionIndex: number
  totalQuestions: number
  prompt: string
  questionType: 'multiple-choice' | 'short-answer' | 'numeric'
  choices: string[]
  subject: string
  expectedTimeSeconds: number
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
