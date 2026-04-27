// =============================================================================
// EPIC-E2E-G — Admin operations journey (real browser drive, admin SPA :5174)
//
// Drives a SUPER_ADMIN through the platform-operator surfaces:
//   1. /login form fill + submit (real Firebase emu user, role=SUPER_ADMIN)
//   2. /dashboards/admin                 — overview widgets, charts
//   3. /apps/user/list                   — user management table
//   4. /apps/system/health               — service-status cards
//   5. /apps/audit-log (in /apps/system) — audit trail surface
//
// What we verify:
//   * Real router transitions through the admin SPA layout
//   * Each landing page reaches a terminal render state without a JS
//     throw (data | empty | tier-gate | skeleton-then-resolve)
//   * Console errors / page errors / 4xx-5xx network are captured.
//     Known backlog 404s (a few admin endpoints still in flight) are
//     filtered to avoid noise; everything else is reported.
//
// We deliberately avoid mutation paths (POST/DELETE) — those are exam
// territory of the unit/integration suite. EPIC-G is a *render and
// permission* journey, not a CRUD test.
// =============================================================================

import { test, expect } from '@playwright/test'
import type { Page } from '@playwright/test'

const ADMIN_SPA_URL = process.env.E2E_ADMIN_SPA_URL ?? 'http://localhost:5174'
const EMU_HOST = process.env.FIREBASE_EMU_HOST ?? 'localhost:9099'
const FIREBASE_PROJECT_ID = process.env.FIREBASE_PROJECT_ID ?? 'cena-platform'
const EMU_BEARER = process.env.FIREBASE_EMU_BEARER ?? 'owner'
const SCHOOL_ID = 'cena-platform'

interface ConsoleEntry { type: string; text: string; location?: string }
interface NetworkFailure { method: string; url: string; status: number; body?: string }

interface Diag {
  consoleEntries: ConsoleEntry[]
  pageErrors: { message: string; stack?: string }[]
  failedRequests: NetworkFailure[]
}

function attachListeners(page: Page): Diag {
  const d: Diag = { consoleEntries: [], pageErrors: [], failedRequests: [] }
  page.on('console', msg => d.consoleEntries.push({
    type: msg.type(),
    text: msg.text(),
    location: msg.location()?.url
      ? `${msg.location().url}:${msg.location().lineNumber}`
      : undefined,
  }))
  page.on('pageerror', err => d.pageErrors.push({ message: err.message, stack: err.stack }))
  page.on('response', async (resp) => {
    if (resp.status() >= 400) {
      let body: string | undefined
      try { const t = await resp.text(); body = t.length > 800 ? `${t.slice(0, 800)}…` : t }
      catch { body = '<navigation flushed>' }
      d.failedRequests.push({ method: resp.request().method(), url: resp.url(), status: resp.status(), body })
    }
  })
  return d
}

test.describe('EPIC_G_ADMIN_OPERATIONS_JOURNEY', () => {
  test('super-admin /login → dashboard → users → system → audit-log @epic-g', async ({ page }, testInfo) => {
    test.setTimeout(180_000)

    const diag = attachListeners(page)

    // ── 1. Provision SUPER_ADMIN via Firebase emu ──
    const email = `epic-g-${Date.now()}-${Math.random().toString(36).slice(2, 6)}@cena.test`
    const password = `e2e-${Math.random().toString(36).slice(2, 12)}`
    console.log(`\n=== EPIC_G_ADMIN_OPERATIONS_JOURNEY for ${email} ===\n`)

    const signUpResp = await page.request.post(
      `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signUp?key=fake-api-key`,
      { data: { email, password, returnSecureToken: true } },
    )
    expect(signUpResp.ok()).toBe(true)
    const { localId } = await signUpResp.json() as { localId: string }

    const claims = { role: 'SUPER_ADMIN', school_id: SCHOOL_ID, locale: 'en', plan: 'free' }
    const claimsResp = await page.request.post(
      `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/projects/${FIREBASE_PROJECT_ID}/accounts:update`,
      {
        headers: { Authorization: `Bearer ${EMU_BEARER}` },
        data: { localId, customAttributes: JSON.stringify(claims) },
      },
    )
    expect(claimsResp.ok()).toBe(true)
    console.log('[epic-g] SUPER_ADMIN provisioned')

    // ── 2. /login form click ──
    await page.goto(`${ADMIN_SPA_URL}/login`)
    await page.getByPlaceholder('admin@cena.edu').fill(email)
    await page.locator('input[type="password"]').fill(password)
    await page.getByRole('button', { name: /sign in/i }).first().click()
    await page.waitForURL(url => !url.pathname.startsWith('/login'), { timeout: 20_000 })
    console.log(`[epic-g] post-login url: ${page.url()}`)

    // ── 3. /dashboards/admin — overview widgets ──
    // The admin home page is /dashboards/admin (post-login `/` resolves
    // here). It awaits useApi('/admin/dashboard/overview') in <script setup>
    // — the page won't render until that resolves OR errors. We assert
    // the heading and at least one widget label render.
    await page.goto(`${ADMIN_SPA_URL}/dashboards/admin`)
    const dashboardSettled = await Promise.race([
      page.getByText(/active users/i).first().waitFor({ state: 'visible', timeout: 20_000 }).then(() => 'data'),
      page.getByText(/total students/i).first().waitFor({ state: 'visible', timeout: 20_000 }).then(() => 'data'),
    ]).catch(() => 'timeout')
    console.log(`[epic-g] /dashboards/admin: ${dashboardSettled}`)
    expect(dashboardSettled, '/dashboards/admin must render at least one widget label').toBe('data')

    // ── 4. /apps/user/list — Users management ──
    await page.goto(`${ADMIN_SPA_URL}/apps/user/list`)
    // The page registers definePage({ action: 'read', subject: 'Users' })
    // — SUPER_ADMIN's manage:all unblocks it. Wait for either the data
    // table rows or its empty-state.
    const usersSettled = await Promise.race([
      page.locator('table').first().waitFor({ state: 'visible', timeout: 15_000 }).then(() => 'data'),
      page.getByText(/no data available|no users/i).first().waitFor({ state: 'visible', timeout: 15_000 }).then(() => 'empty'),
    ]).catch(() => 'timeout')
    console.log(`[epic-g] /apps/user/list: ${usersSettled}`)
    expect(usersSettled, '/apps/user/list must reach a terminal state').not.toBe('timeout')

    // ── 5. /apps/system/health — Service status ──
    await page.goto(`${ADMIN_SPA_URL}/apps/system/health`)
    const healthSettled = await Promise.race([
      page.getByText(/service|health|status/i).first().waitFor({ state: 'visible', timeout: 15_000 }).then(() => 'data'),
    ]).catch(() => 'timeout')
    console.log(`[epic-g] /apps/system/health: ${healthSettled}`)
    expect(healthSettled, '/apps/system/health must render at least the heading').toBe('data')

    // ── 6. /apps/system/audit-log — Audit trail ──
    await page.goto(`${ADMIN_SPA_URL}/apps/system/audit-log`)
    const auditSettled = await Promise.race([
      page.getByText(/audit/i).first().waitFor({ state: 'visible', timeout: 15_000 }).then(() => 'data'),
    ]).catch(() => 'timeout')
    console.log(`[epic-g] /apps/system/audit-log: ${auditSettled}`)
    expect(auditSettled, '/apps/system/audit-log must render at least the heading').toBe('data')

    // ── 7. Diagnostics ──
    testInfo.attach('console-entries.json', { body: JSON.stringify(diag.consoleEntries, null, 2), contentType: 'application/json' })
    testInfo.attach('failed-requests.json', { body: JSON.stringify(diag.failedRequests, null, 2), contentType: 'application/json' })
    testInfo.attach('page-errors.json', { body: JSON.stringify(diag.pageErrors, null, 2), contentType: 'application/json' })

    const errs = diag.consoleEntries.filter(e => e.type === 'error')
    // Backlog endpoints — known not-yet-built, filter so they don't drown
    // the signal. Anything else is flagged.
    const knownBacklog404s = [
      '/api/instructor/classrooms',
      '/api/mentor/',
      '/api/admin/embeddings/corpus-stats',
      '/api/admin/experiments',
    ]
    const unexpectedFailedRequests = diag.failedRequests.filter(f =>
      !knownBacklog404s.some(p => f.url.includes(p) && f.status === 404),
    )

    console.log('\n=== EPIC_G DIAGNOSTICS SUMMARY ===')
    console.log(`Console: ${diag.consoleEntries.length} | errors=${errs.length}`)
    console.log(`Page errors: ${diag.pageErrors.length}`)
    console.log(`Failed network: ${diag.failedRequests.length} (backlog 404s ignored: ${diag.failedRequests.length - unexpectedFailedRequests.length})`)
    console.log(`Unexpected failed requests: ${unexpectedFailedRequests.length}`)
    if (errs.length) {
      console.log('— console errors —')
      for (const e of errs.slice(0, 10))
        console.log(`  ${e.text}${e.location ? ` @ ${e.location}` : ''}`)
    }
    if (unexpectedFailedRequests.length) {
      console.log('— unexpected failed requests —')
      for (const f of unexpectedFailedRequests.slice(0, 15))
        console.log(`  ${f.status} ${f.method} ${f.url}`)
    }

    expect(diag.pageErrors, 'No JS exceptions across the admin journey').toHaveLength(0)
  })
})
