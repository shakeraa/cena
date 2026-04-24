import { HttpResponse, http } from 'msw'
import { readMockSession } from '../../mockSession'

/**
 * MSW handlers for the student `/api/me/*` endpoint group.
 *
 * These mock responses let the student web dev loop work against a
 * deterministic backend without running the student API host. In
 * production the MSW worker is NOT registered, so real requests pass
 * through to the deployed API host.
 *
 * FIND-ux-013: the handlers previously returned a hardcoded
 * 'u-dev-student' / 'Dev Student' payload regardless of who was signed
 * in, which created two conflicting "who am I?" signals in the same
 * session. They now read the mock session from localStorage (written by
 * authStore.__mockSignIn) and return that identity, falling back to the
 * legacy Dev Student only when no session is present.
 */

interface BootstrapPayload {
  studentId: string
  displayName: string
  role: string
  locale: string
  onboardedAt: string
  subjects: string[]
  level: number
  streakDays: number
  avatarUrl: null
}

interface ProfilePayload {
  studentId: string
  displayName: string
  email: string
  avatarUrl: null
  bio: string
  favoriteSubjects: string[]
  visibility: string
}

function buildBootstrap(): BootstrapPayload {
  const session = readMockSession()

  return {
    studentId: session.studentId,
    displayName: session.displayName,
    role: 'student',
    locale: 'en',
    onboardedAt: '2026-04-10T00:00:00Z',
    subjects: ['math', 'physics', 'chemistry'],
    level: 7,
    streakDays: 12,
    avatarUrl: null,
  }
}

function buildProfile(): ProfilePayload {
  const session = readMockSession()

  return {
    studentId: session.studentId,
    displayName: session.displayName,
    email: session.email,
    avatarUrl: null,
    bio: 'Learning in public.',
    favoriteSubjects: ['math', 'physics'],
    visibility: 'class-only',
  }
}

/**
 * In-memory settings store used by the MSW handlers so that GET returns
 * whatever PATCH last wrote. Seeded with the same ICO-compliant defaults
 * the real backend uses (FIND-privacy-010).
 */
const settingsStore: Record<string, unknown> = {
  appearance: {
    theme: 'system',
    language: 'en',
    reducedMotion: false,
    highContrast: false,
  },
  notifications: {
    emailNotifications: false,
    pushNotifications: false,
    dailyReminder: false,
    dailyReminderTime: null,
    weeklyProgress: false,
    streakAlerts: false,
    newContentAlerts: false,
  },
  privacy: {
    profileVisibility: 'private',
    showProgressToClass: false,
    allowPeerComparison: false,
    shareAnalytics: false,
  },
  learning: {
    autoAdvance: false,
    showHintsByDefault: true,
    soundEffects: true,
    targetSessionMinutes: 15,
    difficultyPreference: 'adaptive',
  },
  homeLayout: {
    widgetOrder: ['streak', 'progress', 'recommended', 'achievements', 'activity'],
    hiddenWidgets: [],
    compactMode: false,
  },
}

export const handlerStudentMe = [
  http.get('/api/me', () => {
    return HttpResponse.json(buildBootstrap())
  }),

  http.get('/api/me/profile', () => {
    return HttpResponse.json(buildProfile())
  }),

  http.patch('/api/me/profile', async ({ request }) => {
    const base = buildProfile()
    const patch = await request.json() as Record<string, unknown>

    return HttpResponse.json({ ...base, ...patch })
  }),

  http.post('/api/me/onboarding', async () => {
    return HttpResponse.json({ success: true, redirectTo: '/home' })
  }),

  // FIND-ux-032: GET /api/me/settings — returns merged settings state
  http.get('/api/me/settings', () => {
    return HttpResponse.json(settingsStore)
  }),

  // FIND-ux-032: PATCH /api/me/settings — merge-patches into the store
  http.patch('/api/me/settings', async ({ request }) => {
    const patch = await request.json() as Record<string, Record<string, unknown>>

    for (const [section, values] of Object.entries(patch)) {
      if (settingsStore[section] && typeof values === 'object' && values !== null)
        Object.assign(settingsStore[section] as Record<string, unknown>, values)
    }

    return new HttpResponse(null, { status: 204 })
  }),
]
