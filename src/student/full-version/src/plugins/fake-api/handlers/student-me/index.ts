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
]
