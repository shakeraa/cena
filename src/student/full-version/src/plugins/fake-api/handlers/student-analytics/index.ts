import { HttpResponse, http } from 'msw'

/**
 * MSW handlers for the student `/api/analytics/*` endpoint group.
 *
 * These mock responses let the student web dev loop work against a
 * deterministic backend. In production the MSW worker is NOT registered,
 * so real requests pass through to the hardened analytics endpoints on
 * `Cena.Student.Api.Host`. The shapes here mirror the real C# records in
 * `Cena.Api.Contracts.Analytics.*`.
 */

/**
 * Mirrors the real `AnalyticsSummaryDto` record. `overallAccuracy` is a
 * 0..1 decimal (the real endpoint `Math.Round`s to 3 decimal places).
 * Values chosen to resemble a returning student mid-way through a
 * learning plan so the dev loop renders plausibly without looking staged.
 */
function makeAnalyticsSummary() {
  return {
    totalSessions: 14,
    totalQuestionsAttempted: 172,
    overallAccuracy: 0.762,
    currentStreak: 5,
    longestStreak: 12,
    totalXp: 1680,
    level: 7,
  }
}

function makeTimeBreakdown() {
  const items = Array.from({ length: 30 }).map((_, i) => {
    const date = new Date(Date.now() - (29 - i) * 86400_000)

    // Weekday bias: more minutes on weekdays, fewer on weekends
    const dayOfWeek = date.getUTCDay()
    const isWeekend = dayOfWeek === 0 || dayOfWeek === 6
    const base = isWeekend ? 12 : 28
    const variance = Math.floor(Math.random() * 15)

    return {
      date: date.toISOString(),
      minutes: base + variance,
    }
  })

  return { items }
}

function makeFlowAccuracy() {
  const points = Array.from({ length: 7 }).map((_, i) => ({
    timestamp: new Date(Date.now() - (6 - i) * 86400_000).toISOString(),
    flowScore: 55 + Math.floor(Math.random() * 35),
    accuracyPercent: 70 + Math.floor(Math.random() * 25),
  }))

  return { points }
}

export const handlerStudentAnalytics = [
  http.get('/api/analytics/summary', () => {
    return HttpResponse.json(makeAnalyticsSummary())
  }),

  http.get('/api/analytics/time-breakdown', () => {
    return HttpResponse.json(makeTimeBreakdown())
  }),

  http.get('/api/analytics/flow-vs-accuracy', () => {
    return HttpResponse.json(makeFlowAccuracy())
  }),
]
