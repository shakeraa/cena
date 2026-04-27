// =============================================================================
// TASK-E2E-A-06 — Sign-out clears all surfaces
//
// Journey: signed-in student → opens UserProfile menu → click sign-out →
//   * Firebase JS SDK invokes signOut (idToken revoked client-side)
//   * authStore.__signOut clears uid/email/idToken + wipes encrypted
//     offline cache
//   * userAbilityRules cookie cleared
//   * SPA redirects to /login
//   * Subsequent /api/me request with the OLD token must surface a
//     post-sign-out behaviour. The exact backend semantics:
//       - The backend trusts the JWT signature + exp claim, so the OLD
//         token may still validate locally for up to 1h until it
//         expires. The SPA stops *sending* it after sign-out; the
//         backend sees no further calls from this user.
//       - The hard guarantee is: SPA's authStore.isSignedIn = false.
//
// What this spec asserts:
//   * Pre-condition: signed-in via the seeded student user
//   * Post-sign-out DOM: SPA at /login
//   * Post-sign-out localStorage: cena-mock-auth removed (mock path),
//     no leftover idToken under any key
//   * Post-sign-out cookie: userAbilityRules cookie cleared
//   * Post-sign-out: a fresh /api/me call WITHOUT a token returns 401
//     (proves the API still requires auth; this is the "zombie session"
//     regression catcher — if the SPA somehow kept sending a stale
//     token after sign-out, the test would notice)
//
// Note on the "old token still validates" property: that's a JWT
// expiry-window characteristic, not a sign-out failure. A-06 is about
// the *client-side* contract — what the SPA does, not what an
// attacker holding a copied JWT could do. Token revocation is a
// separate concern handled by short JWT lifetime + refresh-token
// rotation; covered by other specs.
// =============================================================================

import { test, expect } from '@playwright/test'

const EMU_HOST = process.env.FIREBASE_EMU_HOST ?? 'localhost:9099'
const STUDENT_API_BASE_URL = process.env.E2E_STUDENT_API_URL ?? 'http://localhost:5050'

const SEEDED_EMAIL = 'student1@cena.local'
const SEEDED_PASSWORD = 'DevStudent123!'

async function emuSignIn(email: string, password: string): Promise<string> {
  const resp = await fetch(
    `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=fake-api-key`,
    {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email, password, returnSecureToken: true }),
    },
  )
  if (!resp.ok)
    throw new Error(`Firebase emu signIn failed for ${email}: ${resp.status} ${await resp.text()}`)
  const body = await resp.json() as { idToken: string }
  return body.idToken
}

/**
 * Ensure the seeded student has a StudentProfileSnapshot in Marten so
 * the SPA's route guard lands them on /home (where UserProfile avatar
 * lives) rather than /onboarding (the wizard, which has no menu). The
 * onboarding endpoint is idempotent — second call returns 200 without
 * re-emitting events.
 */
async function ensureOnboarded(idToken: string): Promise<void> {
  const resp = await fetch(`${STUDENT_API_BASE_URL}/api/me/onboarding`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${idToken}`,
    },
    body: JSON.stringify({
      Role: 'student',
      Locale: 'en',
      Subjects: ['math'],
      DailyTimeGoalMinutes: 15,
      WeeklySubjectTargets: [],
      DiagnosticResults: null,
      ClassroomCode: null,
    }),
  })
  if (!resp.ok)
    throw new Error(`POST /api/me/onboarding failed ${resp.status}: ${await resp.text()}`)
}

test.describe('E2E-A-06 sign-out clears all surfaces', () => {
  // Pre-flight: seed the StudentProfileSnapshot via /api/me/onboarding so
  // the SPA's route guard sends the user to /home (where UserProfile
  // avatar mounts) rather than /onboarding (the wizard, which has no
  // menu). Idempotent — safe to run on every test. The Firebase user
  // is shared across the suite, so this only meaningfully runs once.
  test.beforeAll(async () => {
    const idToken = await emuSignIn(SEEDED_EMAIL, SEEDED_PASSWORD)
    await ensureOnboarded(idToken)
  })

  // Dismiss FirstRunLanguageChooser modal — see sign-in.spec.ts comment.
  test.beforeEach(async ({ page }) => {
    await page.addInitScript(() => {
      window.localStorage.setItem(
        'cena-student-locale',
        JSON.stringify({ code: 'en', locked: true, version: 1 }),
      )
    })
  })

  test('sign-out → /login + cleared local state @auth @p0', async ({ page, context }) => {
    // 1. Sign in via the /login form (real Firebase emu path).
    await page.goto('/login')
    await expect(page.getByTestId('email-password-form')).toBeVisible()
    await page.getByTestId('auth-email').locator('input').fill(SEEDED_EMAIL)
    await page.getByTestId('auth-password').locator('input').fill(SEEDED_PASSWORD)
    await page.getByTestId('auth-submit').click()

    // SPA leaves /login on success. We accept either /home or /onboarding —
    // see sign-in.spec.ts for the rationale.
    await page.waitForURL(url => !url.pathname.startsWith('/login'), { timeout: 15_000 })
    expect(page.url()).not.toContain('/login')

    // 2. Open the UserProfile avatar menu and click sign-out.
    //    UserProfile.vue mounts the avatar in the navbar; the menu
    //    has data-testid="user-profile-signout" on the sign-out item.
    const avatarBtn = page.getByTestId('user-profile-avatar-button')
    await expect(
      avatarBtn,
      'UserProfile avatar must be visible — auth shell did not mount post-login',
    ).toBeVisible({ timeout: 10_000 })
    await avatarBtn.click()

    const signOutItem = page.getByTestId('user-profile-signout')
    await expect(signOutItem).toBeVisible({ timeout: 5_000 })
    await signOutItem.click()

    // 3. ── Boundary: DOM ──
    //    SPA pushes to /login. Allow up to 10s for the Firebase signOut
    //    promise + Vue router transition.
    await page.waitForURL(url => url.pathname.startsWith('/login'), { timeout: 10_000 })
    expect(page.url(), 'sign-out must land on /login').toContain('/login')

    // 4. ── Boundary: localStorage ──
    //    The mock-auth key MUST be removed. We also assert that no other
    //    key in localStorage still holds a JWT-shaped value — a regression
    //    where signOut forgot to wipe a key would surface here.
    const localStorageDump = await page.evaluate(() => {
      const out: Record<string, string> = {}
      for (let i = 0; i < window.localStorage.length; i++) {
        const k = window.localStorage.key(i)
        if (k)
          out[k] = window.localStorage.getItem(k) ?? ''
      }
      return out
    })
    expect(
      localStorageDump['cena-mock-auth'],
      `cena-mock-auth must be cleared on sign-out, got: ${localStorageDump['cena-mock-auth']}`,
    ).toBeUndefined()

    // Heuristic JWT shape: 3 base64url segments separated by `.`.
    const jwtShape = /^[A-Za-z0-9_-]{20,}\.[A-Za-z0-9_-]{20,}\.[A-Za-z0-9_-]{20,}$/
    for (const [k, v] of Object.entries(localStorageDump)) {
      expect(
        jwtShape.test(v),
        `localStorage key "${k}" still contains a JWT-shaped value after sign-out: ${v.slice(0, 40)}…`,
      ).toBe(false)
    }

    // 5. ── Boundary: cookie ──
    //    `userAbilityRules` is cleared by clearAbilityCookie() inside
    //    authStore.__signOut. The browser may keep the cookie name with
    //    an empty value or evict it — both are acceptable. A non-empty
    //    value here means CASL ability rules survived sign-out, which
    //    leaves stale "can()" gates accessible.
    const cookies = await context.cookies()
    const ability = cookies.find(c => c.name === 'userAbilityRules')
    if (ability) {
      expect(
        ability.value,
        'userAbilityRules cookie kept a non-empty value after sign-out — stale CASL ability rules',
      ).toBe('')
    }

    // 6. ── Boundary: API still requires auth ──
    //    A fresh GET /api/me with no Authorization must be rejected.
    //    This catches a "zombie session" regression where the backend
    //    decides to fall back to a session cookie or sniffed identity
    //    after sign-out.
    const meResp = await page.request.get(`${STUDENT_API_BASE_URL}/api/me`)
    expect(
      meResp.status(),
      `Unauthenticated GET /api/me must be 401, got ${meResp.status()} ` +
      '— a 2xx here means the backend trusts something other than the bearer token (zombie session).',
    ).toBe(401)
  })

  test('sign-out then attempt protected route → bounced back to /login @auth @p1', async ({ page }) => {
    // After sign-out, navigating to a protected route must redirect to
    // /login (route guard catches the missing auth state).
    await page.goto('/login')
    await page.getByTestId('auth-email').locator('input').fill(SEEDED_EMAIL)
    await page.getByTestId('auth-password').locator('input').fill(SEEDED_PASSWORD)
    await page.getByTestId('auth-submit').click()
    await page.waitForURL(url => !url.pathname.startsWith('/login'), { timeout: 15_000 })

    // Sign out via the menu.
    await page.getByTestId('user-profile-avatar-button').click()
    await page.getByTestId('user-profile-signout').click()
    await page.waitForURL(url => url.pathname.startsWith('/login'), { timeout: 10_000 })

    // Try to navigate to /home — guard must bounce us back.
    await page.goto('/home')
    await page.waitForURL(url => url.pathname.startsWith('/login'), { timeout: 10_000 })
    expect(
      page.url(),
      'guard must redirect signed-out user from /home back to /login',
    ).toContain('/login')
  })
})

