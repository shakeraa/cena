// =============================================================================
// E2E-K-01 — Install the PWA (P2)
//
// Verifies the install-PWA chain at the boundaries reachable from a browser
// drive in dev / preview:
//
//   1. Manifest is fetchable, JSON-valid, and declares the M3 minimums
//      (name, start_url, display=standalone, icons[192,512]).
//   2. vite-plugin-pwa's `registerSW.js` (or `sw.js`) asset is served — proves
//      the SW registration surface exists. Full SW activation only fires in a
//      production build (`npm run build && npm run preview`); in dev the
//      virtual:pwa-register/vue module is a no-op stub. We don't assert
//      `navigator.serviceWorker.controller` here because it's null in dev.
//   3. <InstallPrompt> mounts in the default layout for an authenticated
//      student (not in embed mode) and stays inert until the gate opens.
//   4. After 2 visits + a synthetic `beforeinstallprompt` event, the install
//      snackbar surfaces; "Not now" persists a dismissal timestamp; the
//      snackbar hides afterwards.
//   5. Standalone display-mode (`matchMedia('(display-mode: standalone)')`)
//      flips `isInstalled=true` and suppresses the prompt — students who
//      already installed don't get re-prompted.
//
// What's NOT covered (intentional, scoped to a future preview-mode spec):
//   * Real Service Worker activation + cache-first navigation.
//   * Native install dialog acceptance — Chrome's prompt() can't be
//     programmatically clicked.
//   * `appinstalled` event arrival from the browser shell.
//
// Component under test: src/components/InstallPrompt.vue
// Composable under test: src/composables/useInstallPrompt.ts (already has
// vitest unit coverage; this spec proves the browser-driven wiring).
// =============================================================================

import { test, expect } from '@playwright/test'

const TENANT_ID = 'cena'
const SCHOOL_ID = 'cena-platform'
const EMU_HOST = process.env.FIREBASE_EMU_HOST ?? 'localhost:9099'
const STUDENT_API = process.env.E2E_STUDENT_API_URL ?? 'http://localhost:5050'

interface ManifestIcon { src: string; sizes: string; type: string; purpose?: string }
interface WebManifest {
  name?: string
  short_name?: string
  start_url?: string
  display?: string
  scope?: string
  theme_color?: string
  icons?: ManifestIcon[]
}

async function bootstrapStudent(
  page: import('@playwright/test').Page,
  label: string,
): Promise<{ idToken: string; uid: string; email: string; password: string }> {
  const email = `${label}-${Date.now()}-${Math.random().toString(36).slice(2, 6)}@cena.test`
  const password = `e2e-${Math.random().toString(36).slice(2, 12)}`
  const su = await page.request.post(
    `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signUp?key=fake-api-key`,
    { data: { email, password, returnSecureToken: true } },
  )
  expect(su.ok(), 'firebase emulator signUp').toBe(true)
  const { idToken: bootstrapToken } = await su.json() as { idToken: string }

  expect((await page.request.post(`${STUDENT_API}/api/auth/on-first-sign-in`, {
    headers: { Authorization: `Bearer ${bootstrapToken}` },
    data: { tenantId: TENANT_ID, schoolId: SCHOOL_ID, displayName: `K01 ${label}` },
  })).status(), 'on-first-sign-in').toBe(200)

  expect((await page.request.post(`${STUDENT_API}/api/me/onboarding`, {
    headers: { Authorization: `Bearer ${bootstrapToken}` },
    data: {
      role: 'student',
      locale: 'en',
      subjects: ['math'],
      dailyTimeGoalMinutes: 15,
      weeklySubjectTargets: [],
      diagnosticResults: null,
      classroomCode: null,
    },
  })).status(), 'onboarding').toBe(200)

  // Re-sign-in to pick up onboarding claims.
  const reLogin = await page.request.post(
    `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=fake-api-key`,
    { data: { email, password, returnSecureToken: true } },
  )
  const { idToken, localId } = await reLogin.json() as { idToken: string; localId: string }

  return { idToken, uid: localId, email, password }
}

async function loginAndLand(
  page: import('@playwright/test').Page,
  email: string,
  password: string,
): Promise<void> {
  await page.goto('/login')
  await page.getByTestId('auth-email').locator('input').fill(email)
  await page.getByTestId('auth-password').locator('input').fill(password)
  await page.getByTestId('auth-submit').click()
  await page.waitForURL(url => !url.pathname.startsWith('/login'), { timeout: 15_000 })
  await page.goto('/home')
}

test.describe('E2E_K_01_PWA_INSTALL', () => {
  test('manifest.webmanifest is reachable and declares PWA minimums @epic-k @pwa @p2', async ({ page }) => {
    test.setTimeout(30_000)
    const resp = await page.request.get('/manifest.webmanifest')
    expect(resp.status(), 'manifest must be served, not the SPA HTML fallback').toBe(200)
    const ct = (resp.headers()['content-type'] ?? '').toLowerCase()
    expect(ct, 'content-type').toMatch(/manifest|json/)

    const m = await resp.json() as WebManifest
    expect(m.name, 'manifest.name').toBeTruthy()
    expect(m.start_url, 'manifest.start_url').toBeTruthy()
    expect(m.display, 'manifest.display=standalone (PWA-installable)').toBe('standalone')
    expect(Array.isArray(m.icons) && (m.icons?.length ?? 0) > 0, 'manifest.icons[]').toBe(true)
    const has192 = (m.icons ?? []).some(i => i.sizes === '192x192')
    const has512 = (m.icons ?? []).some(i => i.sizes === '512x512')
    expect(has192, 'manifest must declare 192x192 icon').toBe(true)
    expect(has512, 'manifest must declare 512x512 icon').toBe(true)
    const hasMaskable = (m.icons ?? []).some(i => i.purpose === 'maskable')
    expect(hasMaskable, 'manifest must declare a maskable icon for Android home screen').toBe(true)
  })

  test('SW registration asset is served by vite-plugin-pwa @epic-k @pwa @p2', async ({ page }) => {
    test.setTimeout(30_000)
    // vite-plugin-pwa's injectRegister option determines which file is
    // emitted. Default is 'auto' → /registerSW.js. Some configs use
    // injectRegister:'inline' (no separate file) or null (custom register).
    // We accept either /registerSW.js or /sw.js as evidence the plugin is
    // wired.
    const reg = await page.request.get('/registerSW.js')
    const sw = await page.request.get('/sw.js')
    const anyPresent = reg.status() === 200 || sw.status() === 200
    expect(
      anyPresent,
      'one of /registerSW.js or /sw.js must be served (404 for both = vite-plugin-pwa misconfigured)',
    ).toBe(true)
  })

  test('synthetic beforeinstallprompt + 2nd visit surfaces InstallPrompt; "Not now" dismisses @epic-k @pwa @p2', async ({ page }, testInfo) => {
    test.setTimeout(120_000)
    const consoleEntries: { type: string; text: string }[] = []
    const pageErrors: { message: string }[] = []
    page.on('console', m => consoleEntries.push({ type: m.type(), text: m.text() }))
    page.on('pageerror', e => pageErrors.push({ message: e.message }))

    // Pre-load localStorage so we don't have to drive 2 navigations:
    //   * lock locale to 'en' so the snackbar copy matches our regex
    //   * pretend visit count is 1 → useInstallPrompt's onMounted bumps it
    //     to 2, which satisfies the gate
    //   * clear any stale dismissal that would suppress the prompt
    //   * pin matchMedia(display-mode: standalone) to FALSE so isInstalled
    //     stays false (real Playwright browsers in headless mode generally
    //     do not match standalone, but pinning makes this hermetic)
    await page.addInitScript(() => {
      window.localStorage.setItem(
        'cena-student-locale',
        JSON.stringify({ code: 'en', locked: true, version: 1 }),
      )
      window.localStorage.setItem('cena-install-visit-count', '1')
      window.localStorage.removeItem('cena-install-dismissed-at')
      const orig = window.matchMedia
      window.matchMedia = (q: string) => {
        if (q.includes('display-mode: standalone')) {
          return {
            matches: false,
            media: q,
            onchange: null,
            addListener() {},
            removeListener() {},
            addEventListener() {},
            removeEventListener() {},
            dispatchEvent: () => false,
          } as unknown as MediaQueryList
        }
        return orig.call(window, q)
      }
    })

    const a = await bootstrapStudent(page, 'k-01-prompt')
    await loginAndLand(page, a.email, a.password)

    // Sanity: InstallPrompt is mounted somewhere (the layout includes it
    // unless embed-mode). v-if hides the markup but the surrounding
    // VSnackbar mounts conditionally — assert by searching for the
    // tabler-download icon used inside the install banner avatar.
    // We don't expect it visible YET — beforeinstallprompt hasn't fired.
    await page.waitForLoadState('networkidle').catch(() => { /* dev HMR keeps a ws open */ })

    // Dispatch a synthetic beforeinstallprompt with the BeforeInstallPromptEvent
    // shape useInstallPrompt expects. The composable installs its listener on
    // onMounted, so this only works after the layout has mounted.
    await page.evaluate(() => {
      const ev: any = new Event('beforeinstallprompt')
      ev.prompt = async () => { /* no-op; we never accept */ }
      ev.userChoice = Promise.resolve({ outcome: 'dismissed', platform: 'web' })
      window.dispatchEvent(ev)
    })

    // The snackbar should mount with both action buttons. We match by
    // accessible name — InstallPrompt sets aria-label from the i18n key
    // `pwa.install.installButton` (locked locale = en).
    const installBtn = page.getByRole('button', { name: /install/i }).first()
    await expect(installBtn, 'install button visible after beforeinstallprompt').toBeVisible({ timeout: 5_000 })

    const notNowBtn = page.getByRole('button', { name: /not now/i }).first()
    await expect(notNowBtn).toBeVisible()
    await notNowBtn.click()

    // Dismissal must persist a numeric timestamp.
    const dismissedAtRaw = await page.evaluate(
      () => window.localStorage.getItem('cena-install-dismissed-at'),
    )
    const ts = Number.parseInt(dismissedAtRaw ?? '', 10)
    expect(Number.isFinite(ts) && ts > 0, 'dismiss timestamp persisted').toBe(true)

    // After dismiss, canShow must flip false → snackbar hides.
    await expect(installBtn).not.toBeVisible({ timeout: 5_000 })

    testInfo.attach('console-entries.json', {
      body: JSON.stringify(consoleEntries, null, 2),
      contentType: 'application/json',
    })
    testInfo.attach('page-errors.json', {
      body: JSON.stringify(pageErrors, null, 2),
      contentType: 'application/json',
    })
    expect(pageErrors, 'no JS exceptions during install-prompt drive').toHaveLength(0)
  })

  test('standalone display-mode suppresses the install prompt @epic-k @pwa @p2', async ({ page }) => {
    test.setTimeout(60_000)
    await page.addInitScript(() => {
      window.localStorage.setItem(
        'cena-student-locale',
        JSON.stringify({ code: 'en', locked: true, version: 1 }),
      )
      // Visit count high enough that gate would otherwise pass.
      window.localStorage.setItem('cena-install-visit-count', '5')
      window.localStorage.removeItem('cena-install-dismissed-at')
      // Force matchMedia(display-mode: standalone) → matches=true.
      window.matchMedia = ((q: string) => ({
        matches: q.includes('display-mode: standalone'),
        media: q,
        onchange: null,
        addListener() {},
        removeListener() {},
        addEventListener() {},
        removeEventListener() {},
        dispatchEvent: () => false,
      })) as unknown as typeof window.matchMedia
    })

    const a = await bootstrapStudent(page, 'k-01-standalone')
    await loginAndLand(page, a.email, a.password)

    // Even with a synthetic beforeinstallprompt, the prompt MUST NOT surface
    // because checkInstalled() returned true on mount (canShow gate).
    await page.evaluate(() => {
      const ev: any = new Event('beforeinstallprompt')
      ev.prompt = async () => {}
      ev.userChoice = Promise.resolve({ outcome: 'dismissed', platform: 'web' })
      window.dispatchEvent(ev)
    })
    await page.waitForTimeout(1500)

    const installBtn = page.getByRole('button', { name: /install/i }).first()
    await expect(installBtn, 'no install banner when already installed').not.toBeVisible()
  })
})
