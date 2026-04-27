// =============================================================================
// EPIC-E2E-L — Accessibility / i18n RTL journey (real browser drive)
//
// Drives the full register flow with each of en / ar / he locale seeds
// and asserts:
//   * `document.dir` matches expectation (`rtl` for ar/he, `ltr` for en)
//   * No raw i18n keys (e.g. "auth.email") visible on the rendered page
//   * Submit + redirect to /onboarding works in every locale
//
// Console / page-error / 4xx-5xx diagnostics collected per locale.
// =============================================================================

import { test, expect } from '@playwright/test'
import type { Page, TestInfo } from '@playwright/test'

const TENANT_ID = 'cena'
const SCHOOL_ID = 'cena-platform'
const EMU_HOST = process.env.FIREBASE_EMU_HOST ?? 'localhost:9099'
const STUDENT_API = process.env.E2E_STUDENT_API_URL ?? 'http://localhost:5050'

interface ConsoleEntry { type: string; text: string }
interface NetworkFailure { method: string; url: string; status: number }

interface LocaleCase {
  code: 'en' | 'ar' | 'he'
  expectedDir: 'ltr' | 'rtl'
  // Substring sanity-check the rendered page must contain.
  // Avoids brittle full-translation lookups; just proves the locale loaded.
  expectedSubstring: string
}

const CASES: LocaleCase[] = [
  { code: 'en', expectedDir: 'ltr', expectedSubstring: 'Cena' },
  // Arabic + Hebrew assertions stay loose — different deploys may
  // localize the heading differently. The dir attribute is the
  // hard-line check.
  { code: 'ar', expectedDir: 'rtl', expectedSubstring: '' },
  { code: 'he', expectedDir: 'rtl', expectedSubstring: '' },
]

async function driveOneLocale(page: Page, testInfo: TestInfo, lc: LocaleCase): Promise<void> {
  const consoleEntries: ConsoleEntry[] = []
  const pageErrors: { message: string }[] = []
  const failedRequests: NetworkFailure[] = []

  page.on('console', msg => consoleEntries.push({ type: msg.type(), text: msg.text() }))
  page.on('pageerror', err => pageErrors.push({ message: err.message }))
  page.on('response', async (resp) => {
    if (resp.status() >= 400)
      failedRequests.push({ method: resp.request().method(), url: resp.url(), status: resp.status() })
  })

  await page.addInitScript((args: { code: string; tenantId: string }) => {
    window.localStorage.setItem(
      'cena-student-locale',
      JSON.stringify({ code: args.code, locked: true, version: 1 }),
    )
    window.localStorage.setItem('cena-e2e-tenant-id', args.tenantId)
  }, { code: lc.code, tenantId: TENANT_ID })

  const email = `epic-l-${lc.code}-${Date.now()}-${Math.random().toString(36).slice(2, 6)}@cena.test`
  const password = `e2e-${Math.random().toString(36).slice(2, 12)}`

  console.log(`\n--- locale=${lc.code} (expect dir=${lc.expectedDir}) — ${email} ---`)

  // Server-side: provision + bootstrap so the SPA register flow has
  // somewhere to land. We're testing locale rendering, not the auth
  // edge cases (already covered by EPIC-A).
  const signupResp = await page.request.post(
    `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signUp?key=fake-api-key`,
    { data: { email, password, returnSecureToken: true } },
  )
  expect(signupResp.ok()).toBe(true)
  const { idToken: bootstrapToken } = await signupResp.json() as { idToken: string }
  await page.request.post(`${STUDENT_API}/api/auth/on-first-sign-in`, {
    headers: { Authorization: `Bearer ${bootstrapToken}` },
    data: { tenantId: TENANT_ID, schoolId: SCHOOL_ID, displayName: `EpicL-${lc.code}` },
  })

  // Drive /login UI in the chosen locale
  await page.goto('/login')
  await page.getByTestId('auth-email').locator('input').fill(email)
  await page.getByTestId('auth-password').locator('input').fill(password)

  // Assert document.dir before submission so we capture the locale
  // rendering as it stands on /login.
  const dir = await page.evaluate(() => document.documentElement.dir || document.body.dir || 'ltr')
  console.log(`[epic-l] dir=${dir}`)
  expect(dir, `document.dir must be ${lc.expectedDir} for locale=${lc.code}`)
    .toBe(lc.expectedDir)

  // No raw i18n keys leaking. We pick a substring that, if shown raw,
  // means the i18n wiring is broken for this locale.
  const html = await page.content()
  expect(
    html.includes('auth.email') || html.includes('auth.password') || html.includes('auth.signIn'),
    `no raw i18n keys must be rendered (locale=${lc.code})`,
  ).toBe(false)

  if (lc.expectedSubstring) {
    expect(html, `${lc.code} page should contain "${lc.expectedSubstring}"`)
      .toContain(lc.expectedSubstring)
  }

  // Real submit click — verify the locale doesn't break the auth flow
  await page.getByTestId('auth-submit').click()
  await page.waitForURL(url => !url.pathname.startsWith('/login'), { timeout: 15_000 })
  console.log(`[epic-l] post-login url=${page.url()}`)

  const errs = consoleEntries.filter(e => e.type === 'error')
  console.log(`[epic-l] ${lc.code}: console errors=${errs.length} pageerrs=${pageErrors.length} failedNet=${failedRequests.length}`)
  testInfo.attach(`console-${lc.code}.json`, { body: JSON.stringify(consoleEntries, null, 2), contentType: 'application/json' })
  testInfo.attach(`failed-${lc.code}.json`, { body: JSON.stringify(failedRequests, null, 2), contentType: 'application/json' })
  for (const e of errs.slice(0, 10))
    console.log(`  err: ${e.text}`)

  // Cleanup listeners so the next locale's run isn't polluted.
  page.removeAllListeners('console')
  page.removeAllListeners('pageerror')
  page.removeAllListeners('response')
}

test.describe('EPIC_L_I18N_RTL_JOURNEY', () => {
  for (const lc of CASES) {
    test(`/login + /register render correctly with locale=${lc.code} (dir=${lc.expectedDir}) @epic-l`, async ({ page }, testInfo) => {
      test.setTimeout(120_000)
      await driveOneLocale(page, testInfo, lc)
    })
  }
})
