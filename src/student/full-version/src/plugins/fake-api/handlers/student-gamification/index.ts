import { HttpResponse, http } from 'msw'

/**
 * MSW handlers for the student `/api/gamification/*` endpoint group from STB-03.
 *
 * These mock responses let the student web dev loop work against a
 * deterministic backend without running `Cena.Api.Host`. In production
 * the MSW worker is NOT registered, so real requests pass through.
 *
 * STU-W-07 wires these into the /progress dashboard via useApiQuery.
 */

const mockXp = {
  currentLevel: 7,
  currentXp: 180,
  xpToNextLevel: 250,
  totalXpEarned: 1680,
}

const mockStreak = {
  currentDays: 12,
  longestDays: 18,
  lastActivityAt: '2026-04-10T08:30:00Z',
  isAtRisk: false,
}

const mockBadges = {
  earned: [
    {
      badgeId: 'first-steps',
      name: 'First Steps',
      description: 'Completed your very first learning session.',
      iconName: 'tabler-shoe',
      tier: 'bronze',
      earnedAt: '2026-03-28T14:12:00Z',
    },
    {
      badgeId: 'week-streak',
      name: 'Week Streak',
      description: 'Studied for 7 days in a row.',
      iconName: 'tabler-flame',
      tier: 'silver',
      earnedAt: '2026-04-04T09:00:00Z',
    },
    {
      badgeId: 'quiz-master',
      name: 'Quiz Master',
      description: 'Achieved 90% accuracy on 10 quizzes.',
      iconName: 'tabler-brain',
      tier: 'gold',
      earnedAt: '2026-04-09T16:45:00Z',
    },
  ],
  locked: [
    {
      badgeId: 'month-marathoner',
      name: 'Month Marathoner',
      description: 'Study for 30 days in a row.',
      iconName: 'tabler-medal',
      tier: 'gold',
      earnedAt: null,
    },
    {
      badgeId: 'perfectionist',
      name: 'Perfectionist',
      description: 'Get 100% on 5 sessions in a row.',
      iconName: 'tabler-star',
      tier: 'platinum',
      earnedAt: null,
    },
    {
      badgeId: 'night-owl',
      name: 'Night Owl',
      description: 'Complete 10 sessions after 9 PM.',
      iconName: 'tabler-moon',
      tier: 'bronze',
      earnedAt: null,
    },
    {
      badgeId: 'early-bird',
      name: 'Early Bird',
      description: 'Complete 10 sessions before 8 AM.',
      iconName: 'tabler-sun',
      tier: 'bronze',
      earnedAt: null,
    },
    {
      badgeId: 'social-butterfly',
      name: 'Social Butterfly',
      description: 'Help 5 peers in the class feed.',
      iconName: 'tabler-users',
      tier: 'silver',
      earnedAt: null,
    },
  ],
}

function makeLeaderboard(scope: string) {
  const names = ['Alex Chen', 'Priya Rao', 'Jordan Smith', 'Sam Park', 'Dev Student', 'Riley Evans', 'Casey Kim', 'Maya Patel', 'Noa Levi', 'Yusef Ali']
  const entries = names.map((name, i) => ({
    rank: i + 1,
    studentId: i === 4 ? 'u-dev-student' : `u-${i}`,
    displayName: name,
    xp: 2400 - i * 180,
    avatarUrl: null,
  }))

  return {
    scope,
    entries,
    currentStudentRank: 5,
  }
}

export const handlerStudentGamification = [
  http.get('/api/gamification/xp', () => {
    return HttpResponse.json(mockXp)
  }),

  http.get('/api/gamification/streak', () => {
    return HttpResponse.json(mockStreak)
  }),

  http.get('/api/gamification/badges', () => {
    return HttpResponse.json(mockBadges)
  }),

  http.get('/api/gamification/leaderboard', ({ request }) => {
    const url = new URL(request.url)
    const scope = url.searchParams.get('scope') || 'global'

    return HttpResponse.json(makeLeaderboard(scope))
  }),
]
