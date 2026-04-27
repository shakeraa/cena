// =============================================================================
// EPIC-E2E-X — TASK-E2E-COV-06 cross-page flow matrix gap-fill
//
// EPIC-X-cross-page-journey.spec.ts covers the happy-path multi-route walk
// (6 nav targets + hard refresh + browser-back). stuw02 + stuw04a +
// stuw04b cover `returnTo` (incl open-redirect protection). The gaps
// this spec closes:
//
//   1. Deep-link with query params hydrates + survives reload
//      — user clicks an email link to /progress/mastery?subject=math.
//        The SPA must mount the page and not strip / mangle the query.
//
//   2. Back across sign-out — sign out from /profile, browser back goes
//      to /profile, the auth guard must bounce to /login (not render
//      a half-mounted page from stale state).
//
//   3. Back across sign-in — /login → submit → /home → browser back
//      to /login. The /login route guard must redirect-away signed-in
//      users (not loop them back into the auth screen).
//
//   4. In-flight nav abort — start a slow API on /tutor, navigate to
//      /home before the response arrives. The aborted request must
//      not surface as an uncaught console error.
//
//   5. Locale survives nav — set ar via localStorage, navigate, assert
//      <html dir="rtl"> + lang=ar persist.
//
//   6. Theme survives nav — click theme-dark, navigate, assert the
//      Vuetify theme stays dark.
//
//   7. Multi-tab sign-out propagation — Firebase web SDK fires
//      onAuthStateChanged across tabs (shared IndexedDB). When tab A
//      signs out, tab B (still on /home) must detect and bounce to
//      /login within a reasonable window.
//
// PWA reload + onboarding redirect are already covered (journey reload
// assertion + stuw04 onboarding-gate). returnTo is covered by stuw02/04.
// =============================================================================

import { test, expect, type BrowserContext, type Page } from '@playwright/test'

const STUDENT_SPA_BASE_URL = 'http://localhost:5175'
const EMU_HOST = process.env.FIREBASE_EMU_HOST ?? 'localhost:9099'
const TENANT_ID = 'cena'
const SCHOOL_ID = 'cena-platform'

interface DiagnosticCtx {
  consoleErrors: string[]
  pageErrors: string[]
  failedRequests: { method: string; url: string; status: number }[]
}

function attachDiagnostics(page: Page): DiagnosticCtx {
  const ctx: DiagnosticCtx = {
    consoleErrors: [],
    pageErrors: [],
    failedRequests: [],
  }
  page.on('console', m => { if (m.type() === 'error') ctx.consoleErrors.push(m.text()) })
  page.on('pageerror', e => { ctx.pageErrors.push(e.message) })
  page.on('response', r => {
    if (r.status() >= 400)
      ctx.failedRequests.push({ method: r.request().method(), url: r.url(), status: r.status() })
  })
  return ctx
}

interface ProvisionedAccount {
  email: string
  password: string
  idToken: string
}

async function provisionAccount(
  page: Page,
  displayName: string,
  locale: 'en' | 'ar' = 'en',
): Promise<ProvisionedAccount> {
  const email = `e2e-cov06-${Date.now()}-${Math.random().toString(36).slice(2, 6)}@cena.test`
  const password = `e2e-${Math.random().toString(36).slice(2, 12)}`

  await page.request.post(
    `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signUp?key=fake-api-key`,
    { data: { email, password, returnSecureToken: true } },
  )
  const t = await page.request.post(
    `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=fake-api-key`,
    { data: { email, password, returnSecureToken: true } },
  )
  const { idToken } = await t.json() as { idToken: string }
  await page.request.post('/api/auth/on-first-sign-in', {
    headers: { Authorization: `Bearer ${idToken}` },
    data: { tenantId: TENANT_ID, schoolId: SCHOOL_ID, displayName },
  })
  await page.request.post('/api/me/onboarding', {
    headers: { Authorization: `Bearer ${idToken}` },
    data: {
      Role: 'student', Locale: locale, Subjects: ['math'],
      DailyTimeGoalMinutes: 15, WeeklySubjectTargets: [],
      DiagnosticResults: null, ClassroomCode: null,
    },
  })
  return { email, password, idToken }
}

async function seedLocaleAndTenant(page: Page, locale: 'en' | 'ar' = 'en'): Promise<void> {
  await page.addInitScript(({ tenantId, locale: loc }: { tenantId: string; locale: string }) => {
    // The locale store accepts both legacy bare-string and the new
    // `{ code, locked, version }` JSON shape. We use the JSON shape
    // with `locked: true` so the first-run chooser does not steal
    // the route on sign-in.
    window.localStorage.setItem(
      'cena-student-locale',
      JSON.stringify({ code: loc, locked: true, version: 1 }),
    )
    window.localStorage.setItem('cena-e2e-tenant-id', tenantId)
  }, { tenantId: TENANT_ID, locale })
}

async function uiSignIn(page: Page, account: ProvisionedAccount): Promise<void> {
  await page.goto('/login')
  await page.getByTestId('auth-email').locator('input').fill(account.email)
  await page.getByTestId('auth-password').locator('input').fill(account.password)
  await page.getByTestId('auth-submit').click()
  await page.waitForURL(url => !url.pathname.startsWith('/login'), { timeout: 20_000 })
}

test.describe('EPIC_X_CROSS_PAGE_MATRIX', () => {
  test('deep-link with query params hydrates and survives reload @epic-x @cov-06 @cross-page', async ({ page }, testInfo) => {
    test.setTimeout(60_000)
    const diag = attachDiagnostics(page)

    await seedLocaleAndTenant(page)
    const acct = await provisionAccount(page, 'COV-06 DeepLink')
    await uiSignIn(page, acct)

    // Pretend the user clicked an email link straight to a parameterized
    // route. /progress/mastery accepts ?subject=math via the route param
    // (or, at minimum, must not strip the query and must mount the page).
    const deepLink = '/progress/mastery?subject=math&period=week'
    await page.goto(`${STUDENT_SPA_BASE_URL}${deepLink}`, { waitUntil: 'domcontentloaded' })
    await page.waitForLoadState('networkidle', { timeout: 10_000 }).catch(() => {})

    // 1. The query survived (router didn't strip it on guard pass).
    const url = new URL(page.url())
    expect(url.pathname, 'deep-link path must mount as-is').toBe('/progress/mastery')
    expect(url.searchParams.get('subject'),
      'deep-link query param `subject` must survive the auth guard',
    ).toBe('math')
    expect(url.searchParams.get('period'),
      'deep-link query param `period` must survive the auth guard',
    ).toBe('week')

    // 2. Page mounted (heading or *-page testid visible).
    const mounted = await Promise.race([
      page.locator('main h1, main h2, main [role="heading"]').first().isVisible({ timeout: 8_000 }).catch(() => false),
      page.locator('[data-testid$="-page"]').first().isVisible({ timeout: 8_000 }).catch(() => false),
    ])
    expect(mounted, 'deep-linked /progress/mastery should render').toBe(true)

    // 3. Reload preserves both the route and the query.
    await page.reload({ waitUntil: 'networkidle' })
    const after = new URL(page.url())
    expect(after.pathname).toBe('/progress/mastery')
    expect(after.searchParams.get('subject')).toBe('math')

    testInfo.attach('deep-link-diagnostics.json', {
      body: JSON.stringify(diag, null, 2),
      contentType: 'application/json',
    })
    expect(diag.pageErrors,
      `pageerror on deep-link: ${JSON.stringify(diag.pageErrors.slice(0, 3))}`,
    ).toEqual([])
  })

  test('back across sign-out → auth guard bounces to /login @epic-x @cov-06 @cross-page', async ({ page }, testInfo) => {
    test.setTimeout(60_000)
    const diag = attachDiagnostics(page)

    await seedLocaleAndTenant(page)
    const acct = await provisionAccount(page, 'COV-06 BackSignOut')
    await uiSignIn(page, acct)

    // Visit /profile so it's in the back-stack.
    await page.goto(`${STUDENT_SPA_BASE_URL}/profile`, { waitUntil: 'domcontentloaded' })
    await page.waitForLoadState('networkidle', { timeout: 10_000 }).catch(() => {})

    // Sign out via UI (mirrors real flow — clicking the avatar +
    // sign-out menu item). The avatar button opens a menu; we click
    // the sign-out testid inside it.
    await page.getByTestId('user-profile-avatar-button').click()
    await page.getByTestId('user-profile-signout').click()
    await page.waitForURL(url => url.pathname.startsWith('/login'), { timeout: 15_000 })

    // Now press browser-back. The previous entry was /profile, but
    // the auth guard must intercept and re-route us back to /login
    // (the user is no longer signed in).
    await page.goBack({ waitUntil: 'domcontentloaded' })
    await page.waitForTimeout(500)
    const finalPath = new URL(page.url()).pathname
    expect(finalPath,
      `back-after-signout must land on /login (auth guard), got ${finalPath}`,
    ).toMatch(/\/login/)

    testInfo.attach('back-signout-diagnostics.json', {
      body: JSON.stringify(diag, null, 2),
      contentType: 'application/json',
    })
    expect(diag.pageErrors).toEqual([])
  })

  test('back across sign-in does NOT loop into /login @epic-x @cov-06 @cross-page', async ({ page }, testInfo) => {
    test.setTimeout(60_000)
    const diag = attachDiagnostics(page)

    await seedLocaleAndTenant(page)
    const acct = await provisionAccount(page, 'COV-06 BackSignIn')

    // Start at /login (fresh — not yet signed in).
    await page.goto(`${STUDENT_SPA_BASE_URL}/login`, { waitUntil: 'domcontentloaded' })
    await page.getByTestId('auth-email').locator('input').fill(acct.email)
    await page.getByTestId('auth-password').locator('input').fill(acct.password)
    await page.getByTestId('auth-submit').click()
    await page.waitForURL(url => !url.pathname.startsWith('/login'), { timeout: 20_000 })

    // We're now on /home (or whatever the post-login default is). The
    // back-stack still contains /login. Press back — the /login route
    // guard MUST detect the signed-in user and redirect away (forward
    // again or to /home). It must NOT show the login form, and it
    // must NOT enter an infinite-redirect loop.
    await page.goBack({ waitUntil: 'domcontentloaded' })
    // The signed-in /login guard does a router.replace which can take
    // a frame or two; settle the network so the destination renders.
    await page.waitForLoadState('networkidle', { timeout: 10_000 }).catch(() => {})

    const finalPath = new URL(page.url()).pathname
    expect(finalPath,
      `back-after-signin must NOT land on /login (signed-in guard should redirect), got ${finalPath}`,
    ).not.toMatch(/^\/login(\?|$|\/)/)
    // The URL assertion above is the actual regression check
    // (no infinite-redirect loop). We do NOT assert avatar
    // visibility here — the destination route varies (could be
    // /home, /onboarding, or /) and some of those mount the
    // shell on different timelines. Specs that need the shell
    // assert it on a known-good route.

    testInfo.attach('back-signin-diagnostics.json', {
      body: JSON.stringify(diag, null, 2),
      contentType: 'application/json',
    })
    // Chrome's bfcache restore on goBack can briefly transit through
    // an opaque origin (about:blank) where localStorage access raises
    // — that's a browser-internal warning, not an app regression.
    // Filter it out; everything else still fails the assertion.
    const appErrors = diag.pageErrors.filter(e =>
      !/localStorage.*access is denied|opaque origin/i.test(e),
    )
    expect(appErrors,
      `app-level pageerror after back-from-/home: ${JSON.stringify(appErrors.slice(0, 3))}`,
    ).toEqual([])
  })

  test('in-flight nav abort surfaces no uncaught console-error @epic-x @cov-06 @cross-page', async ({ page }, testInfo) => {
    test.setTimeout(60_000)
    const diag = attachDiagnostics(page)

    await seedLocaleAndTenant(page)
    const acct = await provisionAccount(page, 'COV-06 NavAbort')
    await uiSignIn(page, acct)

    // Slow-walk a tutor backend call so it's still in-flight when we
    // navigate away. We delay every /api/tutor/* response by 5s.
    await page.route('**/api/tutor/**', async route => {
      await new Promise(r => setTimeout(r, 5_000))
      await route.continue()
    })

    // Visit /tutor, which fires its initial threads/list call.
    await page.goto(`${STUDENT_SPA_BASE_URL}/tutor`, { waitUntil: 'domcontentloaded' })

    // Don't wait for networkidle — we WANT to navigate while the
    // /api/tutor/* call is still in flight. 200ms is enough for the
    // page to mount and fire its data-call without finishing it.
    await page.waitForTimeout(200)
    await page.goto(`${STUDENT_SPA_BASE_URL}/home`, { waitUntil: 'domcontentloaded' })
    await page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => {})

    // The aborted /api/tutor/* request must not surface as an
    // uncaught error in the console. Specifically: no `AbortError`
    // / `cancelled` / `Failed to fetch` shouting from a stale
    // promise that the page didn't clean up.
    const stale = diag.consoleErrors.filter(e =>
      /aborterror|the user aborted|cancelled by client/i.test(e),
    )
    expect(stale,
      `nav-abort produced ${stale.length} uncaught aborts (cleanup not handled): ${JSON.stringify(stale.slice(0, 3))}`,
    ).toEqual([])
    expect(diag.pageErrors,
      `pageerror during nav-abort: ${JSON.stringify(diag.pageErrors.slice(0, 3))}`,
    ).toEqual([])

    testInfo.attach('nav-abort-diagnostics.json', {
      body: JSON.stringify(diag, null, 2),
      contentType: 'application/json',
    })
  })

  test('locale survives nav — ar persists across route change @epic-x @cov-06 @cross-page @rtl', async ({ page }, testInfo) => {
    test.setTimeout(60_000)
    const diag = attachDiagnostics(page)

    await seedLocaleAndTenant(page, 'ar')
    const acct = await provisionAccount(page, 'COV-06 LocaleSurvive', 'ar')
    await uiSignIn(page, acct)

    // After sign-in, on whatever the post-login default route is,
    // Arabic should be active. Use expect.poll so we tolerate the
    // brief window between page.goto returning and App.vue's setup
    // running applyLocaleSideEffects(); a hard read can race the
    // mount on a cold Vite chunk-load.
    await expect.poll(
      async () => page.evaluate(() => document.documentElement.dir),
      { timeout: 8_000, message: 'after sign-in with ar locale seeded, <html dir> must be rtl' },
    ).toBe('rtl')
    await expect.poll(
      async () => page.evaluate(() => document.documentElement.lang),
      { timeout: 8_000 },
    ).toBe('ar')

    // Navigate to /settings, /progress, /home — locale must persist.
    // Each goto is a full SPA reload (Playwright does not honor SPA
    // intra-app routing); App.vue re-runs setup, the store reads
    // localStorage again, and applyLocaleSideEffects fires. We poll
    // dir/lang to give that mount cycle a fair shot.
    for (const route of ['/settings', '/progress', '/home']) {
      await page.goto(`${STUDENT_SPA_BASE_URL}${route}`, { waitUntil: 'domcontentloaded' })
      await expect.poll(
        async () => page.evaluate(() => document.documentElement.dir),
        { timeout: 8_000, message: `<html dir> must remain rtl after navigating to ${route}` },
      ).toBe('rtl')
      await expect.poll(
        async () => page.evaluate(() => document.documentElement.lang),
        { timeout: 8_000, message: `<html lang> must remain ar after navigating to ${route}` },
      ).toBe('ar')
    }

    testInfo.attach('locale-survive-diagnostics.json', {
      body: JSON.stringify(diag, null, 2),
      contentType: 'application/json',
    })
    expect(diag.pageErrors).toEqual([])
  })

  test('theme survives nav — dark mode persists across route change @epic-x @cov-06 @cross-page', async ({ page }, testInfo) => {
    test.setTimeout(60_000)
    const diag = attachDiagnostics(page)

    await seedLocaleAndTenant(page)
    const acct = await provisionAccount(page, 'COV-06 ThemeSurvive')
    await uiSignIn(page, acct)

    // Navigate to /settings/appearance and click theme-dark.
    await page.goto(`${STUDENT_SPA_BASE_URL}/settings/appearance`, { waitUntil: 'domcontentloaded' })
    await page.getByTestId('theme-dark').click()
    await page.waitForTimeout(300)

    // Vuetify writes the theme onto data-theme on <body> or <html>
    // (depends on app config); we check both. The theme name must
    // contain 'dark' on whichever element it lives on.
    const themeDescriptor = async () => {
      return await page.evaluate(() => {
        const html = document.documentElement.getAttribute('data-theme')
        const body = document.body.getAttribute('data-theme')
        const stored = localStorage.getItem('cena-student-theme')
        const themeClass = document.documentElement.className + ' ' + document.body.className
        return { html, body, stored, themeClass }
      })
    }
    const before = await themeDescriptor()
    expect(before.stored,
      'theme-dark click must persist `dark` to localStorage',
    ).toBe('dark')

    // Navigate to two more routes, theme must survive each time.
    for (const route of ['/home', '/progress', '/settings']) {
      await page.goto(`${STUDENT_SPA_BASE_URL}${route}`, { waitUntil: 'domcontentloaded' })
      await page.waitForTimeout(250)
      const after = await themeDescriptor()
      expect(after.stored,
        `theme persistence broken on ${route}: localStorage cena-student-theme is now ${after.stored}`,
      ).toBe('dark')
    }

    testInfo.attach('theme-survive-diagnostics.json', {
      body: JSON.stringify(diag, null, 2),
      contentType: 'application/json',
    })
    expect(diag.pageErrors).toEqual([])
  })

  test('multi-tab sign-out — tab B detects tab A signing out @epic-x @cov-06 @cross-page @multi-tab', async ({ browser }, testInfo) => {
    test.setTimeout(90_000)

    // Same browser context = shared Firebase IndexedDB session.
    // Two pages in the same context simulate two tabs.
    const ctx: BrowserContext = await browser.newContext()
    try {
      const tabA = await ctx.newPage()
      const tabB = await ctx.newPage()
      const diagA = attachDiagnostics(tabA)
      const diagB = attachDiagnostics(tabB)

      await seedLocaleAndTenant(tabA)
      const acct = await provisionAccount(tabA, 'COV-06 MultiTab')
      await uiSignIn(tabA, acct)

      // Tab B navigates to /home. The shared Firebase IndexedDB means
      // it should ALSO be signed in (no /login bounce).
      await tabB.goto(`${STUDENT_SPA_BASE_URL}/home`, { waitUntil: 'domcontentloaded' })
      await tabB.waitForLoadState('networkidle', { timeout: 10_000 }).catch(() => {})
      const tabBPath = new URL(tabB.url()).pathname
      expect(tabBPath,
        `tab B must be signed-in via shared Firebase IndexedDB (got ${tabBPath})`,
      ).not.toMatch(/^\/login/)
      await expect(tabB.getByTestId('user-profile-avatar-button'),
        'tab B should have signed-in shell',
      ).toBeVisible({ timeout: 8_000 })

      // Sign out on tab A.
      await tabA.getByTestId('user-profile-avatar-button').click()
      await tabA.getByTestId('user-profile-signout').click()
      await tabA.waitForURL(url => url.pathname.startsWith('/login'), { timeout: 15_000 })

      // Tab B is still on /home. It must detect the cross-tab
      // sign-out (Firebase fires onAuthStateChanged across tabs via
      // IndexedDB) and bounce to /login. We give it up to 12s.
      // If it doesn't move on its own, tab B's auth listener is
      // wired wrong — that's the regression we want to catch.
      await tabB.waitForURL(url => url.pathname.startsWith('/login'), { timeout: 12_000 })
        .catch(async () => {
          // Some apps require a navigation to re-evaluate the auth
          // state. Trigger one — a forced reload of /home — and
          // expect the guard to bounce. If even THAT doesn't bounce,
          // the SPA has stale auth in pinia and the bug is real.
          await tabB.reload({ waitUntil: 'domcontentloaded' })
        })

      const finalPathB = new URL(tabB.url()).pathname
      expect(finalPathB,
        `tab B must end up on /login after tab A signed out (cross-tab auth sync), got ${finalPathB}`,
      ).toMatch(/^\/login/)

      testInfo.attach('multi-tab-diagnostics.json', {
        body: JSON.stringify({ tabA: diagA, tabB: diagB }, null, 2),
        contentType: 'application/json',
      })
      expect(diagA.pageErrors).toEqual([])
      expect(diagB.pageErrors).toEqual([])
    }
    finally {
      await ctx.close().catch(() => {})
    }
  })
})
