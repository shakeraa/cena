import { HttpResponse, http } from 'msw'

/**
 * MSW handlers for the student `/api/gamification/*` endpoint group.
 *
 * These mock responses let the student web dev loop work against a
 * deterministic backend without running the student API host. In
 * production the MSW worker is NOT registered, so real requests pass
 * through to the hardened backend.
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

const NAMES_GLOBAL = ['Alex Chen', 'Priya Rao', 'Jordan Smith', 'Sam Park', 'Dev Student', 'Riley Evans', 'Casey Kim', 'Maya Patel', 'Noa Levi', 'Yusef Ali', 'Lina Morales', 'Ethan Clarke', 'Aisha Khan', 'Oliver Bennett', 'Sofia Russo', 'Daniel Lee', 'Zara Hoffman', 'Mateo García', 'Hannah Müller', 'Kenji Watanabe']
const NAMES_CLASS = ['Dev Student', 'Elena Torres', 'Oren Barak', 'Haruki Sato', 'Fatima Al-Said', 'Leo Fischer', 'Maya Patel', 'Noa Levi']
const NAMES_FRIENDS = ['Alex Chen', 'Priya Rao', 'Dev Student', 'Sam Park', 'Casey Kim']

function makeLeaderboard(scope: string) {
  let names: string[]
  let myIndex: number

  if (scope === 'class') {
    names = NAMES_CLASS
    myIndex = 0
  }
  else if (scope === 'friends') {
    names = NAMES_FRIENDS
    myIndex = 2
  }
  else {
    names = NAMES_GLOBAL
    myIndex = 4
  }

  const entries = names.map((name, i) => ({
    rank: i + 1,
    studentId: i === myIndex ? 'u-dev-student' : `u-${scope}-${i}`,
    displayName: name,
    xp: 2400 - i * 150,
    avatarUrl: null,
  }))

  return {
    scope,
    entries,
    currentStudentRank: myIndex + 1,
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
