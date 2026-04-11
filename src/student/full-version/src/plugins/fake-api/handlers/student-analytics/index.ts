import { HttpResponse, http } from 'msw'

/**
 * MSW handlers for the student `/api/analytics/*` endpoint group from STB-09.
 *
 * These mock responses let the student web dev loop work against a
 * deterministic backend. STU-W-09 wires these into the /progress/time
 * and /progress/mastery pages.
 */

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
  http.get('/api/analytics/time-breakdown', () => {
    return HttpResponse.json(makeTimeBreakdown())
  }),

  http.get('/api/analytics/flow-vs-accuracy', () => {
    return HttpResponse.json(makeFlowAccuracy())
  }),
]
