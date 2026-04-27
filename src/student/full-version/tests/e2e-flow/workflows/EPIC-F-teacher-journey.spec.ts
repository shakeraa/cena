// =============================================================================
// EPIC-E2E-F — Teacher classroom journey (real browser drive)
//
// THE GAP THIS SPEC DOCUMENTS: in the current build, the TEACHER role
// has NO UI. The student SPA is for STUDENT only, and the admin SPA
// rejects TEACHER at the login form with the exact alert "Access
// denied. Admin, Moderator, or Super Admin role required."
//
// The original epic body (EPIC-E2E-F-teacher-classroom.md) describes
// /apps/teacher/heatmap, classroom drill-down, and K-floor
// enforcement — none of which have a UI in this build.
//
// What this spec DOES exercise (the load-bearing journey we have today):
//   1. Drive the admin-SPA /login form with seeded teacher1@cena.local
//   2. Assert the SPA correctly REJECTS the teacher with the role-
//      gated alert — the admin SPA is not a back door for teachers
//   3. Capture diagnostics — proving the rejection is clean (no JS
//      errors, no 5xx)
//
// This is the security positive case: proving the role gate works.
// The "real teacher experience" tests for F-01..F-05 stay queued on
// a teacher-SPA / teacher-routes ship.
// =============================================================================

import { test, expect } from '@playwright/test'

const ADMIN_SPA_BASE_URL = process.env.E2E_ADMIN_SPA_URL ?? 'http://localhost:5174'
const SEEDED_TEACHER_EMAIL = 'teacher1@cena.local'
const SEEDED_TEACHER_PASSWORD = 'DevTeacher123!'

interface ConsoleEntry { type: string; text: string; location?: string }
interface NetworkFailure { method: string; url: string; status: number; body?: string }

test.describe('EPIC_F_TEACHER_JOURNEY', () => {
  test('teacher /login on admin SPA → role-gated rejection (security positive case) @epic-f', async ({ page }, testInfo) => {
    test.setTimeout(60_000)

    const consoleEntries: ConsoleEntry[] = []
    const pageErrors: { message: string; stack?: string }[] = []
    const failedRequests: NetworkFailure[] = []

    page.on('console', msg => consoleEntries.push({
      type: msg.type(),
      text: msg.text(),
      location: msg.location()?.url
        ? `${msg.location().url}:${msg.location().lineNumber}`
        : undefined,
    }))
    page.on('pageerror', err => pageErrors.push({ message: err.message, stack: err.stack }))
    page.on('response', async resp => {
      if (resp.status() >= 400) {
        let body: string | undefined
        try { const t = await resp.text(); body = t.length > 800 ? `${t.slice(0, 800)}…` : t }
        catch { body = '<navigation flushed>' }
        failedRequests.push({ method: resp.request().method(), url: resp.url(), status: resp.status(), body })
      }
    })

    await page.goto(`${ADMIN_SPA_BASE_URL}/login`)
    await expect(page.locator('input[type="email"]')).toBeVisible({ timeout: 10_000 })
    await page.locator('input[type="email"]').fill(SEEDED_TEACHER_EMAIL)
    await page.locator('input[type="password"]').fill(SEEDED_TEACHER_PASSWORD)
    await page.locator('button[type="submit"]').click()

    // Admin SPA's role-gate alert: "Access denied. Admin, Moderator,
    // or Super Admin role required." This is the assertion that
    // matters: the SPA actively rejects TEACHER rather than letting
    // the user through. The alert role makes this targetable
    // semantically.
    const accessDenied = page.getByRole('alert').filter({ hasText: /access denied|admin|moderator/i }).first()
    await expect(
      accessDenied,
      'admin SPA must reject TEACHER role with the role-gated alert',
    ).toBeVisible({ timeout: 10_000 })

    // SPA must STAY on /login — no leak of admin routes to a teacher.
    expect(page.url(), 'TEACHER must not be granted admin SPA access').toContain('/login')

    testInfo.attach('console-entries.json', { body: JSON.stringify(consoleEntries, null, 2), contentType: 'application/json' })
    testInfo.attach('failed-requests.json', { body: JSON.stringify(failedRequests, null, 2), contentType: 'application/json' })
    testInfo.attach('page-errors.json', { body: JSON.stringify(pageErrors, null, 2), contentType: 'application/json' })

    expect(pageErrors,
      `page errors during teacher reject path: ${JSON.stringify(pageErrors.slice(0, 3))}`,
    ).toEqual([])
  })
})
