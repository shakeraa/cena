import { HttpResponse, http } from 'msw'

/**
 * MSW handlers for the student `/api/me/*` endpoint group from STB-00.
 *
 * These mock responses let the student web dev loop work against a
 * deterministic backend without running `Cena.Api.Host`. In production
 * the MSW worker is NOT registered, so real requests pass through to
 * the deployed API host.
 *
 * STU-W-05B wires /api/me into the home dashboard via useApiQuery.
 */

const mockBootstrap = {
  studentId: 'u-dev-student',
  displayName: 'Dev Student',
  role: 'student',
  locale: 'en',
  onboardedAt: '2026-04-10T00:00:00Z',
  subjects: ['math', 'physics', 'chemistry'],
  level: 7,
  streakDays: 12,
  avatarUrl: null,
}

const mockProfile = {
  studentId: 'u-dev-student',
  displayName: 'Dev Student',
  email: 'dev-student@example.com',
  avatarUrl: null,
  bio: 'Learning in public.',
  favoriteSubjects: ['math', 'physics'],
  visibility: 'class-only',
}

export const handlerStudentMe = [
  http.get('/api/me', () => {
    return HttpResponse.json(mockBootstrap)
  }),

  http.get('/api/me/profile', () => {
    return HttpResponse.json(mockProfile)
  }),

  http.patch('/api/me/profile', async ({ request }) => {
    const patch = await request.json() as Record<string, unknown>

    return HttpResponse.json({ ...mockProfile, ...patch })
  }),

  http.post('/api/me/onboarding', async () => {
    return HttpResponse.json({ success: true, redirectTo: '/home' })
  }),
]
