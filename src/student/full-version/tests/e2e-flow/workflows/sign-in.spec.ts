// =============================================================================
// TASK-E2E-A-02 — Sign-in path (existing account)
//
// Journey: /login → email+password → Firebase emu issues idToken → SPA
// stores token → fetches `/api/me` → lands on `/home` (if onboarded) or
// `/onboarding` (if not). Refresh persists the session.
//
// This spec exercises the load-bearing /login form against a real
// pre-seeded Firebase emu user (student1@cena.local from
// docker/firebase-emulator/seed-dev-users.sh). Unlike A-01, no fresh
// account is created — the point is to prove the existing-account path.
//
// Boundary coverage matrix:
//   * DOM      — landed on a signed-in route (/home or /onboarding); no
//                stay on /login; no /login re-prompt after a hard refresh
//   * Firebase — JWT carries the seeded role/tenant_id/school_id claims
//   * API      — /api/me returns 200 (onboarded) or 404 (not yet); 401
//                proves auth never reached the backend, which is a fail
//
// Why no DB or bus boundary: A-02 is the *re-entry* path — the DB row
// and onboarding event were created by A-01 (or the seed). Re-asserting
// them here would be redundant and would couple this spec to A-01's
// state. We pin the Firebase boundary because that's what A-02 actually
// exercises that A-01 doesn't.
// =============================================================================

import { test, expect } from '@playwright/test'

const EMU_HOST = process.env.FIREBASE_EMU_HOST ?? 'localhost:9099'
const STUDENT_API_BASE_URL = process.env.E2E_STUDENT_API_URL ?? 'http://localhost:5050'

const SEEDED_EMAIL = 'student1@cena.local'
const SEEDED_PASSWORD = 'DevStudent123!'
const SEEDED_TENANT_ID = 'cena'
const SEEDED_ROLE = 'STUDENT'

function decodeJwtClaims(idToken: string): Record<string, unknown> {
  const [, payload] = idToken.split('.')
  if (!payload)
    throw new Error('Malformed JWT — missing payload segment')
  const b64 = payload.replace(/-/g, '+').replace(/_/g, '/').padEnd(
    payload.length + ((4 - (payload.length % 4)) % 4),
    '=',
  )
  return JSON.parse(Buffer.from(b64, 'base64').toString('utf8')) as Record<string, unknown>
}

test.describe('E2E-A-02 sign-in (existing account)', () => {
  // Seed a locked locale before every nav so the FirstRunLanguageChooser
  // modal doesn't intercept pointer events on the auth form. Centralizing
  // it in beforeEach ensures all tests in this describe block are immune.
  test.beforeEach(async ({ page }) => {
    await page.addInitScript(() => {
      window.localStorage.setItem(
        'cena-student-locale',
        JSON.stringify({ code: 'en', locked: true, version: 1 }),
      )
    })
  })

  test('login → seeded student → signed-in route + persistent claims @auth @p0', async ({ page }) => {
    // 0. Sanity-fetch an idToken directly from the emu so we can pin the
    //    backend's view of the user is consistent with the SPA's. If the
    //    emu has no such user, the seed wasn't run — fail loudly with the
    //    operator-visible recipe.
    const signInResp = await fetch(
      `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=fake-api-key`,
      {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          email: SEEDED_EMAIL,
          password: SEEDED_PASSWORD,
          returnSecureToken: true,
        }),
      },
    )
    expect(
      signInResp.ok,
      `Firebase emu signInWithPassword failed for ${SEEDED_EMAIL}. ` +
      'Run: docker exec cena-firebase-emulator /seed/seed-dev-users.sh',
    ).toBe(true)
    const seeded = await signInResp.json() as { idToken: string }

    // ── Boundary: Firebase JWT ──
    // The seed sets role/tenant_id/school_id as customAttributes; assert
    // the issued idToken carries them. If it doesn't, the seed shape has
    // drifted from the claims-transformer expectations.
    const directClaims = decodeJwtClaims(seeded.idToken)
    expect(directClaims.role, 'seeded user must carry role=STUDENT').toBe(SEEDED_ROLE)
    expect(directClaims.tenant_id, 'seeded user must carry tenant_id=cena').toBe(SEEDED_TENANT_ID)
    expect(directClaims.school_id, 'seeded user must carry school_id').toBeTruthy()

    // 1. Drive the /login form. The SPA's firebase plugin connects to the
    //    same emu (VITE_FIREBASE_AUTH_EMULATOR_HOST) so the in-browser
    //    sign-in goes against the same identity store as step 0. The
    //    `beforeEach` hook above seeds the locale so the
    //    FirstRunLanguageChooser modal does not intercept clicks.
    await page.goto('/login')
    await expect(page.getByTestId('email-password-form')).toBeVisible()

    await page.getByTestId('auth-email').locator('input').fill(SEEDED_EMAIL)
    await page.getByTestId('auth-password').locator('input').fill(SEEDED_PASSWORD)
    await page.getByTestId('auth-submit').click()

    // 2. The SPA navigates away from /login. Either /home (onboarded) or
    //    /onboarding (signed-in but no StudentProfileSnapshot yet — the
    //    seeded user is in this state until A-01 / on-first-sign-in
    //    populates the projection). Both are acceptable proof of sign-in.
    await page.waitForURL(
      url => !url.pathname.startsWith('/login'),
      { timeout: 15_000 },
    )
    expect(page.url(), 'must leave /login after successful sign-in').not.toContain('/login')

    const landedAtHome = page.url().includes('/home')
    const landedAtOnboarding = page.url().includes('/onboarding')
    expect(
      landedAtHome || landedAtOnboarding,
      `Expected /home or /onboarding, got ${page.url()}`,
    ).toBe(true)

    if (landedAtHome)
      await expect(page.getByTestId('home-page')).toBeVisible({ timeout: 10_000 })
    else
      await expect(page.getByTestId('onboarding-page')).toBeVisible({ timeout: 10_000 })

    // ── Boundary: API ──
    // /api/me returns 200 if a snapshot exists, 404 if not, 401 if auth
    // didn't reach the backend. 401 is the failure mode this spec must
    // catch — it means the SPA is "signed in" but the backend rejected
    // the token (the classic claims-transformer regression). Pin against
    // the directly-issued token so we don't depend on SPA token mirroring.
    const meResp = await page.request.get(`${STUDENT_API_BASE_URL}/api/me`, {
      headers: { Authorization: `Bearer ${seeded.idToken}` },
    })
    expect(
      [200, 404].includes(meResp.status()),
      `GET /api/me must be 200 (onboarded) or 404 (snapshot absent), got ${meResp.status()}`,
    ).toBe(true)

    // 3. Hard refresh — session must persist via the Firebase JS SDK's
    //    IndexedDB-persisted user. A regression here means the emu user
    //    isn't being re-hydrated on app boot; SPA would bounce to /login.
    await page.reload({ waitUntil: 'networkidle' })
    expect(page.url(), 'session lost across reload — bounced to /login').not.toContain('/login')
  })

  test('login with wrong password → error visible, stays on /login @auth @p1', async ({ page }) => {
    await page.goto('/login')
    await expect(page.getByTestId('email-password-form')).toBeVisible()

    await page.getByTestId('auth-email').locator('input').fill(SEEDED_EMAIL)
    await page.getByTestId('auth-password').locator('input').fill('definitely-not-the-password')
    await page.getByTestId('auth-submit').click()

    // Error surfaces inline; no redirect away from /login.
    await expect(page.getByTestId('auth-error')).toBeVisible({ timeout: 10_000 })
    expect(page.url(), 'wrong password must keep us on /login').toContain('/login')
  })
})
