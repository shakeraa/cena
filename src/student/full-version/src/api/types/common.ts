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
