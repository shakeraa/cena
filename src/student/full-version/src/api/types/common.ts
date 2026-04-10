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
