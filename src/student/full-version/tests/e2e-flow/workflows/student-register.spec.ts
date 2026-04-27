// =============================================================================
// TASK-E2E-A-01 — Student registration → Firebase → tenant → home
//
// Journey: /register → age-gate (adult) → credentials form → Firebase emu
// creates account → student-api `POST /api/auth/on-first-sign-in` sets
// custom claims (role=student, tenant_id, school_id) → SPA redirects to
// /onboarding.
//
// This spec is the @auth @p0 guard for the registration load-bearing edge.
// Every downstream flow depends on claims landing on the JWT.
//
// Boundary coverage matrix (see tasks/e2e-flow/TASK-E2E-A-01-student-register.md):
//   * DOM      — /onboarding rendered within 10s post-submit; no login reprompt
//   * Firebase — JWT custom claims (role, tenant_id, school_id) present
//   * DB       — StudentProfile row created under caller's tenant_id
//                (asserted via `/api/me/profile` — the SPA's read-model path.
//                Swaps to `/api/admin/test/probe` when PRR-436 ships, matching
//                the flagship `subscription-happy-path` precedent.)
//   * Bus      — `StudentOnboardedV1` on `cena.events.student.*.onboarded`
//                within 5s, filtered by `tenant_id` header so parallel
//                workers never cross-match each other's events.
//
// Regressions caught: missing claims → 401 loop; wrong tenant → cross-institute
// visibility; duplicate StudentProfile on retry.
// =============================================================================

import { e2eTest as test, expect, tenantDebugString } from '../fixtures/tenant'
import { probeStudentProfile } from '../probes/db-probe'

// Minimal JWT payload decoder — no external dep. Firebase idTokens are
// standard unsigned-from-the-client JWTs; we only read the claims bundle.
function decodeJwtClaims(idToken: string): Record<string, unknown> {
  const [, payload] = idToken.split('.')
  if (!payload)
    throw new Error('Malformed JWT — missing payload segment')

  // base64url → base64
  const b64 = payload.replace(/-/g, '+').replace(/_/g, '/').padEnd(
    payload.length + ((4 - (payload.length % 4)) % 4),
    '=',
  )
  return JSON.parse(Buffer.from(b64, 'base64').toString('utf8')) as Record<string, unknown>
}

// DOB guaranteed to pass the age gate as an adult (skips parental-consent step).
// Using 25 years ago so the test is stable across clock drift and leap days.
function adultDob(): string {
  const now = new Date()
  const y = now.getUTCFullYear() - 25
  return `${y}-06-15` // HTML date input format
}

test.describe('E2E-A-01 student registration', () => {
  test('register → firebase claims → /onboarding @auth @p0', async ({
    page,
    tenant,
    busProbe,
  }, testInfo) => {
    testInfo.annotations.push({
      type: 'isolation',
      description: tenantDebugString(tenant, page),
    })

    // Unique email per spec — tenant.id + test title + timestamp. Keeps
    // Firebase-emu user-table additive without collision across retries.
    const email = `e2e-reg-w${tenant.workerIndex}-${testInfo.title.replace(/\W+/g, '-').toLowerCase()}-${Date.now()}@cena.test`
    const password = `e2e-${Math.random().toString(36).slice(2, 12)}`

    // 1. Fresh browser → /register. No pre-seeded auth; we drive the UI
    //    the same way a real student would.
    //
    // Seed a locked locale so the `FirstRunLanguageChooser` modal does not
    // intercept pointer events on the age-gate. Real students dismiss it
    // by picking a tile; tests can't afford the extra step per spec and
    // the locale choice is not what this flow is guarding.
    //
    // Also seed the per-worker tenant id under `cena-e2e-tenant-id`: the
    // SPA's `useFirebaseAuth.onFirstSignIn` reads this key when calling
    // `POST /api/auth/on-first-sign-in` so the freshly-registered Firebase
    // user gets bound to THIS worker's tenant (no cross-worker bleed).
    // Production builds ignore the key — they resolve tenant from an invite
    // code, and the backend rejects this trusted-mode path unless
    // CENA_E2E_TRUSTED_REGISTRATION=true (set in docker-compose.app.yml for
    // the dev stack).
    await page.addInitScript((tenantId: string) => {
      window.localStorage.setItem(
        'cena-student-locale',
        JSON.stringify({ code: 'en', locked: true, version: 1 }),
      )
      window.localStorage.setItem('cena-e2e-tenant-id', tenantId)
    }, tenant.id)

    await page.goto('/register')
    await expect(page.getByTestId('age-gate-step')).toBeVisible()

    // 2. Age-gate step — fill DOB, click Next to advance to credentials.
    // Vuetify VTextField carries data-testid on the wrapper; the native
    // input is nested inside. `.locator('input')` targets the real control.
    await page.getByTestId('age-gate-dob').locator('input').fill(adultDob())
    await expect(page.getByTestId('age-gate-adult')).toBeVisible()
    await page.getByTestId('age-gate-next').click()

    // 3. Credentials form — display name, email, password, submit.
    //    EmailPasswordForm (register mode) validates display-name as
    //    required before calling useFirebaseAuth.registerWithEmail.
    await expect(page.getByTestId('email-password-form')).toBeVisible()
    await page.getByTestId('auth-display-name').locator('input').fill(`E2E W${tenant.workerIndex}`)
    await page.getByTestId('auth-email').locator('input').fill(email)
    await page.getByTestId('auth-password').locator('input').fill(password)

    // Intercept the on-first-sign-in callback so we can assert the backend
    // contract directly. Missing endpoint → test fails loudly (the whole
    // point of @p0 regression coverage).
    const onFirstSignIn = page.waitForResponse(
      resp => resp.url().includes('/api/auth/on-first-sign-in')
        && resp.request().method() === 'POST',
      { timeout: 15_000 },
    )

    // Subscribe BEFORE the submit — NATS core-sub is fire-and-forget, so
    // the probe must be listening by the time the backend publishes or
    // the message is lost. The `tenant_id` header filter keeps parallel
    // workers from cross-matching each other's onboarded events.
    const onboardedEvent = busProbe.waitFor({
      subject: 'cena.events.student.*.onboarded',
      requireHeader: { tenant_id: tenant.id },
      timeoutMs: 5_000,
    })

    await page.getByTestId('auth-submit').click()

    // ── Boundary 1: DOM ──
    // /onboarding reached within the 10s expect timeout. No login reprompt
    // along the way (a stale-claims redirect loop would surface here).
    await page.waitForURL(url => url.pathname.startsWith('/onboarding'), { timeout: 15_000 })
    await expect(
      page.getByTestId('onboarding-page'),
      `Expected /onboarding after register for ${email} / ${tenant.id}`,
    ).toBeVisible({ timeout: 10_000 })
    expect(page.url()).not.toContain('/login')

    // ── Boundary 2: Backend contract — /api/auth/on-first-sign-in invoked ──
    // The claims-transformer hook runs here; without it, downstream /api/me
    // calls return 401 because the JWT has no role/tenant_id. Asserting
    // the response explicitly pins the contract.
    const firstSignInResp = await onFirstSignIn
    expect(
      firstSignInResp.status(),
      'POST /api/auth/on-first-sign-in must return 2xx on first registration',
    ).toBeLessThan(300)

    // ── Boundary 3: Firebase JWT custom claims ──
    // After the on-first-sign-in callback, the SPA force-refreshes the
    // idToken so the new claims (role/tenant_id/school_id) land client-side.
    // We don't read localStorage — the Firebase JS SDK persists the token
    // through IndexedDB, not a known string key, so the test fetches a
    // fresh token via the Firebase auth API directly and decodes it.
    const idToken = await page.evaluate(async () => {
      // The Firebase JS SDK exposes auth.currentUser globally on `window` only
      // when explicitly wired; safer to call into the same composable the SPA
      // uses. The SPA's plugin attaches `firebase.auth` for debugging — read
      // it here, fall back to async getAuth() import if the symbol is absent.
      type FirebaseUser = { getIdToken: (force?: boolean) => Promise<string> }
      type Win = Window & {
        __firebase_auth?: { currentUser?: FirebaseUser | null }
      }
      const w = window as Win
      const user = w.__firebase_auth?.currentUser
      if (!user)
        return null
      return await user.getIdToken(true)
    })

    // Fall back: if the SPA doesn't expose __firebase_auth, fetch a fresh
    // idToken from the emulator using the email/password we just registered.
    // This is the test-only path; production tests will rely on the SPA hook.
    let resolvedIdToken = idToken
    if (!resolvedIdToken) {
      const fbResp = await page.request.post(
        `http://${process.env.FIREBASE_EMU_HOST ?? 'localhost:9099'}/identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=fake-api-key`,
        { data: { email, password, returnSecureToken: true } },
      )
      expect(fbResp.ok(), 'Firebase emu re-signin must succeed for claims read').toBe(true)
      const body = await fbResp.json() as { idToken: string }
      resolvedIdToken = body.idToken
    }

    expect(resolvedIdToken, 'must obtain a refreshed idToken to decode claims').toBeTruthy()
    const claims = decodeJwtClaims(resolvedIdToken!)
    // Role claim is uppercase ("STUDENT") to match the CenaRole enum that
    // RoleAuthorizationGuard reads. The seeder uses the same casing.
    expect(claims.role, 'JWT must carry role=STUDENT claim').toBe('STUDENT')
    expect(claims.tenant_id, 'JWT must carry tenant_id claim').toBe(tenant.id)
    expect(claims.school_id, 'JWT must carry school_id claim').toBeTruthy()

    // ── Boundary 4a: DB — StudentProfile row under caller's tenant ──
    // PRR-436 admin test probe: read the canonical Marten state directly
    // (AdminUser doc + StudentProfileSnapshot). Replaces the prior
    // /api/me/profile read-model fallback. Tenant verification is enforced
    // server-side: a probe with the wrong tenantId returns found=false.
    const uid = claims.user_id as string ?? claims.sub as string
    expect(uid, 'JWT must carry uid (sub/user_id)').toBeTruthy()
    const probed = await probeStudentProfile({ tenantId: tenant.id, uid })
    expect(probed.found,
      `db-probe must find StudentProfile for uid=${uid} tenant=${tenant.id}`,
    ).toBe(true)
    expect(probed.data?.email).toBe(email)
    expect(probed.data?.tenantId).toBe(tenant.id)
    expect(probed.data?.role).toBe('student')

    // Cross-tenant defence: probing the same uid under a different tenant
    // must come back found=false (no cross-institute bleed).
    const wrongTenantProbe = await probeStudentProfile({
      tenantId: `${tenant.id}-WRONG`,
      uid,
    })
    expect(wrongTenantProbe.found,
      'db-probe must report found=false when tenantId mismatches AdminUser.School',
    ).toBe(false)

    // ── Boundary 4b: Bus — StudentOnboardedV1 on cena.events.student.*.onboarded ──
    // The probe subscribed before the submit, so the event (published by the
    // `on-first-sign-in` endpoint's emitter) is guaranteed to be caught if
    // the backend fires it. Header-filtered by tenant so parallel workers
    // don't cross-match.
    const envelope = await onboardedEvent
    expect(envelope.subject).toMatch(/^cena\.events\.student\.[^.]+\.onboarded$/)
    expect(envelope.headers.tenant_id).toBe(tenant.id)
    expect(envelope.json, 'StudentOnboardedV1 payload must be JSON').not.toBeNull()
    expect(envelope.json?.uid).toBeTruthy()
    expect(envelope.json?.email).toBe(email)
    expect(envelope.json?.tenant_id ?? envelope.json?.tenantId).toBe(tenant.id)
  })
})
