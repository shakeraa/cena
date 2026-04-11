/**
 * Shared helper for MSW handlers to read the currently-authenticated
 * mock student identity from `localStorage['cena-mock-auth']`.
 *
 * FIND-ux-013: previously every handler hardcoded `u-dev-student` /
 * `Dev Student`, which caused the leaderboard, profile card, and other
 * surfaces to greet the wrong user regardless of which email they'd
 * signed in with. Handlers now read this helper so `/api/me`,
 * `/api/gamification/leaderboard`, etc. all see the SAME identity the
 * `authStore` wrote on sign-in.
 *
 * Since the MSW worker runs on the same origin as the student SPA, it
 * can read localStorage directly — we don't need to parse the
 * `Authorization: Bearer mock-token-${uid}` header. The cookie-mock
 * approach would also work but requires forwarding cookies through
 * `$api.ts`, which is more ceremony for no win.
 */

export interface MockSession {

  /**
   * Student id used for correlating leaderboard rows, /api/me responses,
   * and everything else that needs a stable per-user key. Falls back to
   * `'u-dev-student'` when no session is present so the dev loop still
   * works in an unauthenticated tab (e.g. the public `/login` page).
   */
  studentId: string

  /** Display name shown next to avatars, in the sidebar, on leaderboards. */
  displayName: string

  /** Email shown in account settings and referenced by send-feedback forms. */
  email: string
}

const MOCK_AUTH_STORAGE_KEY = 'cena-mock-auth'

const FALLBACK_SESSION: MockSession = {
  studentId: 'u-dev-student',
  displayName: 'Dev Student',
  email: 'dev-student@example.com',
}

/**
 * Read the currently-active mock session from localStorage. Never
 * throws — returns the fallback identity when no session is present or
 * when the stored payload is malformed.
 */
export function readMockSession(): MockSession {
  if (typeof window === 'undefined')
    return FALLBACK_SESSION

  let raw: string | null = null
  try {
    raw = window.localStorage.getItem(MOCK_AUTH_STORAGE_KEY)
  }
  catch {
    return FALLBACK_SESSION
  }

  if (!raw)
    return FALLBACK_SESSION

  try {
    const parsed = JSON.parse(raw) as {
      uid?: unknown
      email?: unknown
      displayName?: unknown
    }

    if (typeof parsed?.uid !== 'string' || parsed.uid.length === 0)
      return FALLBACK_SESSION

    const email = typeof parsed.email === 'string' && parsed.email.length > 0
      ? parsed.email
      : `${parsed.uid}@example.com`

    const displayName = typeof parsed.displayName === 'string' && parsed.displayName.length > 0
      ? parsed.displayName
      : email

    return {
      studentId: parsed.uid,
      displayName,
      email,
    }
  }
  catch {
    return FALLBACK_SESSION
  }
}
