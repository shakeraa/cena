import { HttpResponse, http } from 'msw'
import { readMockSession } from '../../mockSession'

/**
 * MSW handlers for the student `/api/challenges/*` endpoint group.
 *
 * These mock responses let the student web dev loop work against a
 * deterministic backend without running the student API host. In
 * production the MSW worker is NOT registered, so real requests pass
 * through to the hardened challenges endpoints.
 *
 * FIND-ux-013: the daily-challenge leaderboard previously hardcoded
 * rank 5 as 'Dev Student'. It now injects the signed-in user so every
 * leaderboard the student sees agrees on who "you" is.
 */

function endOfDayIso(): string {
  const d = new Date()

  d.setUTCHours(23, 59, 59, 999)

  return d.toISOString()
}

const mockDaily = {
  challengeId: 'daily-2026-04-10',
  title: 'Mental Math Sprint',
  description: 'Five rapid-fire arithmetic questions. Beat the clock!',
  subject: 'math',
  difficulty: 'medium',
  expiresAt: endOfDayIso(),
  attempted: false,
  bestScore: null,
}

const mockBoss = {
  available: [
    {
      bossBattleId: 'boss-algebra-overlord',
      name: 'Algebra Overlord',
      subject: 'math',
      difficulty: 'hard',
      requiredMasteryLevel: 5,
    },
    {
      bossBattleId: 'boss-cell-crusher',
      name: 'Cell Crusher',
      subject: 'biology',
      difficulty: 'medium',
      requiredMasteryLevel: 4,
    },
    {
      bossBattleId: 'boss-motion-master',
      name: 'Motion Master',
      subject: 'physics',
      difficulty: 'medium',
      requiredMasteryLevel: 4,
    },
  ],
  locked: [
    {
      bossBattleId: 'boss-calculus-king',
      name: 'Calculus King',
      subject: 'math',
      difficulty: 'hard',
      requiredMasteryLevel: 8,
    },
    {
      bossBattleId: 'boss-organic-overlord',
      name: 'Organic Overlord',
      subject: 'chemistry',
      difficulty: 'hard',
      requiredMasteryLevel: 7,
    },
  ],
}

const mockChains = {
  chains: [
    {
      chainId: 'chain-algebra-fundamentals',
      name: 'Algebra Fundamentals',
      cardsUnlocked: 6,
      cardsTotal: 10,
      lastUnlockedAt: new Date(Date.now() - 2 * 86400_000).toISOString(),
    },
    {
      chainId: 'chain-physics-core',
      name: 'Physics Core',
      cardsUnlocked: 3,
      cardsTotal: 12,
      lastUnlockedAt: new Date(Date.now() - 5 * 86400_000).toISOString(),
    },
  ],
}

const mockTournaments = {
  upcoming: [
    {
      tournamentId: 'tourn-spring-2026',
      name: 'Spring Math Masters 2026',
      startsAt: new Date(Date.now() + 3 * 86400_000).toISOString(),
      endsAt: new Date(Date.now() + 10 * 86400_000).toISOString(),
      participantCount: 42,
      isRegistered: false,
    },
  ],
  active: [
    {
      tournamentId: 'tourn-weekly-47',
      name: 'Weekly Sprint #47',
      startsAt: new Date(Date.now() - 2 * 86400_000).toISOString(),
      endsAt: new Date(Date.now() + 5 * 86400_000).toISOString(),
      participantCount: 180,
      isRegistered: true,
    },
  ],
}

export const handlerStudentChallenges = [
  http.get('/api/challenges/daily', () => HttpResponse.json(mockDaily)),

  http.get('/api/challenges/daily/leaderboard', () => {
    const session = readMockSession()

    return HttpResponse.json({
      entries: [
        { rank: 1, studentId: 'u-1', displayName: 'Alex Chen', score: 100, timeSeconds: 42 },
        { rank: 2, studentId: 'u-2', displayName: 'Priya Rao', score: 95, timeSeconds: 48 },
        { rank: 3, studentId: 'u-3', displayName: 'Jordan Smith', score: 92, timeSeconds: 51 },
        { rank: 4, studentId: 'u-4', displayName: 'Sam Park', score: 90, timeSeconds: 55 },
        { rank: 5, studentId: session.studentId, displayName: session.displayName, score: 88, timeSeconds: 60 },
      ],
      currentStudentRank: 5,
    })
  }),

  http.get('/api/challenges/daily/history', () => {
    const entries = Array.from({ length: 7 }).map((_, i) => ({
      date: new Date(Date.now() - i * 86400_000).toISOString(),
      title: i === 0 ? 'Mental Math Sprint' : `Past Challenge ${7 - i}`,
      attempted: i > 0,
      score: i > 0 ? 70 + (i * 3) : null,
    }))

    return HttpResponse.json({ entries })
  }),

  http.get('/api/challenges/boss', () => HttpResponse.json(mockBoss)),

  http.get('/api/challenges/boss/:id', ({ params }) => {
    return HttpResponse.json({
      bossBattleId: params.id,
      name: 'Boss Battle',
      description: 'A tough boss battle to test your mastery.',
      subject: 'math',
      difficulty: 'hard',
      attemptsRemaining: 3,
      attemptsMax: 5,
      rewards: [
        { type: 'xp', amount: 250 },
        { type: 'badge', amount: 1 },
      ],
    })
  }),

  http.get('/api/challenges/chains', () => HttpResponse.json(mockChains)),

  http.get('/api/challenges/tournaments', () => HttpResponse.json(mockTournaments)),
]
