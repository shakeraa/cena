// =============================================================================
// Cena E2E flow — Firebase Auth emulator sign-up/sign-in
//
// The dev stack's firebase-emulator container exposes the standard
// Identity Toolkit REST API on port 9099. This fixture hits that directly
// to create test users + obtain idTokens, then drops the token into the
// browser context so SPA requests authenticate transparently.
//
// Reference: docker/firebase-emulator/seed-dev-users.sh — same shape.
// =============================================================================

import type { Page } from '@playwright/test'

const EMU_HOST = process.env.FIREBASE_EMU_HOST ?? 'localhost:9099'
const PROJECT_ID = process.env.FIREBASE_PROJECT_ID ?? 'cena-platform'
const BEARER = process.env.FIREBASE_EMU_BEARER ?? 'owner'

export interface FirebaseAuthUser {
  email: string
  password: string
  uid: string
  idToken: string
  refreshToken: string
  /** Custom-claims bundle applied via Firebase emulator admin API. */
  claims: Record<string, unknown>
}

export interface CreateUserOptions {
  tenantId: string
  role: 'student' | 'parent' | 'teacher' | 'admin'
  schoolId?: string
  grade?: number
  /** Override the default random password (for reproducible debugging). */
  password?: string
}

/**
 * Create a fresh user in the Firebase Auth emulator with the specified
 * custom claims. Returns the idToken suitable for Authorization: Bearer.
 */
export async function createFirebaseUser(
  email: string,
  options: CreateUserOptions,
): Promise<FirebaseAuthUser> {
  const password = options.password ?? `e2e-${Math.random().toString(36).slice(2, 12)}`

  // 1. Create account + capture idToken + refreshToken
  const createUrl = `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signUp?key=fake-api-key`
  const createResp = await fetch(createUrl, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email, password, returnSecureToken: true }),
  })
  if (!createResp.ok) {
    throw new Error(`Firebase emu signUp failed ${createResp.status}: ${await createResp.text()}`)
  }
  const created = await createResp.json() as {
    localId: string
    idToken: string
    refreshToken: string
  }

  // 2. Set custom claims via emulator admin endpoint
  const claims = {
    role: options.role,
    tenant_id: options.tenantId,
    school_id: options.schoolId ?? 'dev-school',
    ...(options.grade !== undefined && { grade: options.grade }),
  }
  const claimsUrl = `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/projects/${PROJECT_ID}/accounts:update`
  const claimsResp = await fetch(claimsUrl, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${BEARER}`,
    },
    body: JSON.stringify({
      localId: created.localId,
      customAttributes: JSON.stringify(claims),
    }),
  })
  if (!claimsResp.ok) {
    throw new Error(`Firebase emu setClaims failed ${claimsResp.status}: ${await claimsResp.text()}`)
  }

  // 3. Re-sign-in to pick up the new claims into a fresh idToken
  const signInUrl = `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=fake-api-key`
  const signInResp = await fetch(signInUrl, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email, password, returnSecureToken: true }),
  })
  if (!signInResp.ok) {
    throw new Error(`Firebase emu signInWithPassword failed ${signInResp.status}: ${await signInResp.text()}`)
  }
  const signedIn = await signInResp.json() as {
    localId: string
    idToken: string
    refreshToken: string
  }

  return {
    email,
    password,
    uid: signedIn.localId,
    idToken: signedIn.idToken,
    refreshToken: signedIn.refreshToken,
    claims,
  }
}

/**
 * Plant the Firebase session into the browser context. The SPA's auth
 * layer expects a valid idToken in either localStorage (dev fallback) or
 * an httpOnly cookie (production). This helper seeds both paths so the
 * SPA's route guard accepts the user without navigating through /login.
 *
 * For tests that explicitly cover the /login UI, use navigateToLoginAndSignIn
 * instead.
 */
export async function signInFirebaseUser(page: Page, user: FirebaseAuthUser): Promise<void> {
  // Navigate to the app origin first so localStorage scope matches.
  await page.goto('/')

  // Seed the idToken into the locations the SPA's auth guard inspects.
  await page.evaluate(({ idToken, refreshToken, uid, email }) => {
    // Local dev fallback shape. Production flips to httpOnly cookies, but
    // for E2E we drive the dev-fallback branch which student-api accepts
    // when FIREBASE_AUTH_EMULATOR_HOST is set.
    window.localStorage.setItem('cena-auth-token', idToken)
    window.localStorage.setItem('cena-auth-refresh', refreshToken)
    window.localStorage.setItem('cena-auth-uid', uid)
    window.localStorage.setItem('cena-auth-email', email)
  }, user)

  // Force a reload so the SPA picks up the session from localStorage.
  await page.reload()
}

/**
 * Alternative entry for tests that want to exercise the /login form.
 * Fills the form fields + submits + asserts redirect.
 */
export async function navigateToLoginAndSignIn(
  page: Page,
  user: FirebaseAuthUser,
): Promise<void> {
  await page.goto('/login')
  await page.getByTestId('login-email').fill(user.email)
  await page.getByTestId('login-password').fill(user.password)
  await page.getByTestId('login-submit').click()
  await page.waitForURL(url => !url.pathname.startsWith('/login'), { timeout: 10_000 })
}
